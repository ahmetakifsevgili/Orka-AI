using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class TeachingEvidenceRouter : ITeachingEvidenceRouter
{
    public IReadOnlyList<TutorToolPlanDto> Route(TutorTurnStateDto turnState, string normalizedUserMessage)
    {
        var text = FoldTurkish($"{normalizedUserMessage} {turnState.ActiveConceptKey} {turnState.ActiveConceptLabel}");
        var plans = new List<TutorToolPlanDto>();

        if (ContainsAny(text, "tarih", "selcuk", "seljuk", "osmanli", "devlet", "kavram", "kimdir", "nedir", "nerede", "olay", "donem"))
            plans.Add(new("knowledge_entity", "Kavram/olay/yer icin ansiklopedik ve entity zemini alinacak.", false, "low"));

        if (ContainsAny(text, "cografya", "cografi", "iklim", "harita", "enlem", "boylam", "yukselti", "ulke", "bolge", "nufus", "konum"))
            plans.Add(new("geo_context", "Konum, enlem, yukselti, iklim ve ulke baglami kontrol edilecek.", false, "low"));

        if (ContainsAny(text, "ekonomi", "nufus", "gdp", "gelir", "issizlik", "istatistik", "kalkinma", "ulke karsilastirma"))
            plans.Add(new("socioeconomic_context", "Gercek ulke ve kalkinma verisiyle pekistirme yapilacak.", false, "medium"));

        if (ContainsAny(text, "kimya", "molekul", "bilesik", "biyoloji", "species", "canli turu", "deprem", "fay", "fizik", "astronomi", "nasa", "gezegen", "ekoloji"))
            plans.Add(new("science_context", "Bilimsel/public veri ile somut ornek aranacak.", false, "medium"));

        if (ContainsAny(text, "makale", "arastirma", "paper", "akademik", "kitap", "kaynak oner", "ileri okuma"))
            plans.Add(new("research_context", "Akademik ve kitap kaynaklari pekistirme karti icin aranacak.", false, "medium"));

        if (ContainsAny(text, "forum", "insanlar", "hata", "bug", "exception", "stackoverflow", "pratikte", "gercek hayatta", "nerede takiliyor"))
            plans.Add(new("forum_signal", "Forum sinyali yaygin hata oruntusu olarak kullanilacak.", false, "medium"));

        if (plans.Count == 0 && (turnState.StyleMode == "example_first" || turnState.StyleMode == "visual" || turnState.MasteryProbability < 0.55m))
            plans.Add(new("knowledge_entity", "Dusuk kanit/ornek ihtiyaci icin guvenli kavram zemini alinacak.", false, "low"));

        return plans
            .GroupBy(p => p.ToolId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(4)
            .ToList();
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string FoldTurkish(string value) =>
        (value ?? string.Empty).ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');
}

public sealed class RealWorldEvidenceService : IRealWorldEvidenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<RealWorldEvidenceService> _logger;

    public RealWorldEvidenceService(
        IHttpClientFactory httpClientFactory,
        OrkaDbContext db,
        ILogger<RealWorldEvidenceService> logger,
        IRedisMemoryService? redis = null)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
        _redis = redis;
    }

    public async Task<TeachingEvidenceResultDto> GetEvidenceAsync(TeachingEvidenceRequestDto request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var evidenceType = NormalizeEvidenceType(request.EvidenceType);
        var query = NormalizeQuery(request.Query, request.UserMessage);
        if (string.IsNullOrWhiteSpace(query))
        {
            return await FinishAsync(request, evidenceType, evidenceType, "needs_input", [], "Evidence query is missing; ask for a clearer concept or place.", "missing_query", sw.ElapsedMilliseconds, true, ct);
        }

        try
        {
            var cacheKey = $"orka:v3:evidence:{evidenceType}:{Hash($"{query}|{request.ConceptKey}")}";
            if (_redis != null)
            {
                var cached = await _redis.GetJsonAsync(cacheKey);
                if (!string.IsNullOrWhiteSpace(cached) && TryDeserializeCards(cached, out var cachedCards))
                {
                    var cards = CloneForTurn(cachedCards, request, query, evidenceType);
                    await PersistCardsAsync(cards, request, CacheTtl(evidenceType), ct);
                    return await FinishAsync(request, evidenceType, "redis_cache", "ready", cards, "Cached real-world teaching evidence is available.", null, sw.ElapsedMilliseconds, false, ct);
                }
            }

            var built = evidenceType switch
            {
                "knowledge_entity" => await BuildKnowledgeEntityAsync(request, query, ct),
                "geo_context" => await BuildGeoContextAsync(request, query, ct),
                "socioeconomic_context" => await BuildSocioeconomicContextAsync(request, query, ct),
                "science_context" => await BuildScienceContextAsync(request, query, ct),
                "research_context" => await BuildResearchContextAsync(request, query, ct),
                "forum_signal" => await BuildForumSignalAsync(request, query, ct),
                _ => []
            };

            if (built.Count == 0)
            {
                return await FinishAsync(request, evidenceType, evidenceType, "degraded", [], "No safe public evidence result was found; do not invent real-world data.", "empty_result", sw.ElapsedMilliseconds, true, ct);
            }

            await PersistCardsAsync(built, request, CacheTtl(evidenceType), ct);
            if (_redis != null)
            {
                await _redis.SetJsonAsync(cacheKey, JsonSerializer.Serialize(StripTurnFields(built), JsonOptions), CacheTtl(evidenceType));
                if (request.SessionId.HasValue)
                {
                    await _redis.AddStreamEventAsync($"orka:v3:tutor-events:{request.SessionId.Value}", new Dictionary<string, string>
                    {
                        ["eventType"] = "tutor.evidence.ready",
                        ["evidenceType"] = evidenceType,
                        ["cardIds"] = string.Join(",", built.Select(c => c.Id)),
                        ["provider"] = string.Join(",", built.Select(c => c.Provider).Distinct(StringComparer.OrdinalIgnoreCase))
                    }, TimeSpan.FromDays(2));
                }
            }

            return await FinishAsync(request, evidenceType, string.Join(",", built.Select(c => c.Provider).Distinct(StringComparer.OrdinalIgnoreCase)), "ready", built, "Real-world teaching evidence is available.", null, sw.ElapsedMilliseconds, false, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await FinishAsync(request, evidenceType, evidenceType, "timeout", [], "Public evidence provider timed out; do not invent live data.", "provider_timeout", sw.ElapsedMilliseconds, true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[RealWorldEvidence] Provider failed safely. Type={EvidenceType} ErrorType={ErrorType}",
                evidenceType,
                LogPrivacyGuard.SafeExceptionType(ex));
            return await FinishAsync(request, evidenceType, evidenceType, "degraded", [], "Public evidence provider failed safely; use a general analogy instead.", "provider_error", sw.ElapsedMilliseconds, true, ct);
        }
    }

    public async Task<IReadOnlyList<TeachingEvidenceCardDto>> GetRecentCardsAsync(
        Guid userId,
        Guid? topicId,
        Guid? tutorActionTraceId = null,
        int take = 8,
        CancellationToken ct = default)
    {
        var query = _db.TeachingEvidenceItems.AsNoTracking().Where(e => e.UserId == userId);
        if (topicId.HasValue) query = query.Where(e => e.TopicId == topicId.Value);
        if (tutorActionTraceId.HasValue) query = query.Where(e => e.TutorActionTraceId == tutorActionTraceId.Value);

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(take, 1, 30))
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildKnowledgeEntityAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var cards = new List<TeachingEvidenceCardDto>();
        var title = Uri.EscapeDataString(query.Trim().Replace(' ', '_'));
        var wiki = await TryGetJsonAsync($"https://tr.wikipedia.org/api/rest_v1/page/summary/{title}", ct)
                   ?? await TryGetJsonAsync($"https://en.wikipedia.org/api/rest_v1/page/summary/{title}", ct);
        if (wiki.HasValue)
        {
            var root = wiki.Value;
            var pageTitle = GetString(root, "title") ?? query;
            var summary = GetString(root, "extract") ?? string.Empty;
            var url = root.TryGetProperty("content_urls", out var urls) &&
                      urls.TryGetProperty("desktop", out var desktop) &&
                      desktop.TryGetProperty("page", out var page)
                ? page.GetString()
                : null;
            cards.Add(Card(request, "wikipedia", "knowledge_entity", query, pageTitle, Trim(summary, 480),
                Trim(summary, 360),
                $"{pageTitle} icin ansiklopedik zemini dersin girisinde kisa tanim olarak kullan.",
                "Stable definition/background card; cite if using as factual grounding.",
                url, "Wikipedia summary", 0.78, "wiki_cached", "low", root));
        }

        var wd = await TryGetJsonAsync($"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={Uri.EscapeDataString(query)}&language=en&format=json&limit=1", ct);
        if (wd.HasValue && wd.Value.TryGetProperty("search", out var search) && search.ValueKind == JsonValueKind.Array && search.GetArrayLength() > 0)
        {
            var first = search[0];
            var label = GetString(first, "label") ?? query;
            var description = GetString(first, "description") ?? string.Empty;
            var id = GetString(first, "id");
            cards.Add(Card(request, "wikidata", "knowledge_entity", query, label, description,
                string.IsNullOrWhiteSpace(description) ? label : $"{label}: {description}",
                "Entity bilgisini isim/yer/olay karisikligini azaltmak icin kullan.",
                "Entity disambiguation only; pair with a richer source for long explanations.",
                string.IsNullOrWhiteSpace(id) ? null : $"https://www.wikidata.org/wiki/{id}",
                "Wikidata entity", 0.74, "static", "low", first));
        }

        return cards.Take(2).ToList();
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildGeoContextAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var geo = await TryGetJsonAsync($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=tr&format=json", ct);
        if (!geo.HasValue || !geo.Value.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return [];
        }

        var first = results[0];
        var name = GetString(first, "name") ?? query;
        var country = GetString(first, "country");
        var lat = first.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() : double.NaN;
        var lon = first.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() : double.NaN;
        if (double.IsNaN(lat) || double.IsNaN(lon)) return [];

        var label = string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";
        var elevation = await TryGetJsonAsync($"https://api.opentopodata.org/v1/aster30m?locations={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}", ct);
        var elevationMeters = elevation.HasValue &&
                              elevation.Value.TryGetProperty("results", out var elevationResults) &&
                              elevationResults.ValueKind == JsonValueKind.Array &&
                              elevationResults.GetArrayLength() > 0 &&
                              elevationResults[0].TryGetProperty("elevation", out var elev)
            ? elev.GetDouble()
            : (double?)null;

        var band = Math.Abs(lat) switch
        {
            < 23.5 => "tropical latitude band",
            < 35 => "subtropical latitude band",
            < 55 => "mid-latitude band",
            < 66.5 => "subpolar latitude band",
            _ => "polar latitude band"
        };
        var elevationText = elevationMeters.HasValue ? $" Elevation signal: about {elevationMeters.Value:0} m." : string.Empty;
        var summary = $"{label} is around {Math.Abs(lat):0.##} degrees {(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):0.##} degrees {(lon >= 0 ? "E" : "W")}. Latitude band: {band}.{elevationText}";

        return [Card(request, "open_meteo+opentopodata", "geo_context", query, $"{label} geography context", summary, summary,
            "Konumu bir haritada dusun: enlem iklimi, boylam bolgesel baglami, yukselti ise iklim/yerlesme kosullarini etkiler.",
            "Use for map reasoning, latitude-climate links, region comparison, and place-based analogies.",
            "https://open-meteo.com/", "Open-Meteo geocoding + OpenTopodata elevation", 0.80, "live_context", "low", new { first, elevationMeters })];
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildSocioeconomicContextAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var rest = await TryGetJsonAsync($"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(query)}?fields=name,cca3,capital,region,subregion,population,latlng,area", ct);
        if (!rest.HasValue || rest.Value.ValueKind != JsonValueKind.Array || rest.Value.GetArrayLength() == 0) return [];

        var country = rest.Value[0];
        var common = country.TryGetProperty("name", out var name) && name.TryGetProperty("common", out var commonProp) ? commonProp.GetString() ?? query : query;
        var cca3 = GetString(country, "cca3") ?? string.Empty;
        var population = country.TryGetProperty("population", out var pop) ? pop.GetInt64() : 0;
        var area = country.TryGetProperty("area", out var areaProp) ? areaProp.GetDouble() : 0d;
        var region = GetString(country, "region") ?? "unknown region";

        string worldBankText = string.Empty;
        if (!string.IsNullOrWhiteSpace(cca3))
        {
            var wb = await TryGetJsonAsync($"https://api.worldbank.org/v2/country/{Uri.EscapeDataString(cca3)}/indicator/SP.POP.TOTL?format=json&per_page=1&MRV=1", ct);
            if (wb.HasValue && wb.Value.ValueKind == JsonValueKind.Array && wb.Value.GetArrayLength() > 1 && wb.Value[1].ValueKind == JsonValueKind.Array && wb.Value[1].GetArrayLength() > 0)
            {
                var item = wb.Value[1][0];
                var year = GetString(item, "date");
                var value = item.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number ? val.GetInt64() : (long?)null;
                if (value.HasValue) worldBankText = $" World Bank latest population point: {value.Value:n0} ({year}).";
            }
        }

        var density = area > 0 ? population / area : 0;
        var summary = $"{common} is in {region}; population about {population:n0}, area about {area:n0} km2, rough density {density:0.#}/km2.{worldBankText}";
        return [Card(request, "restcountries+worldbank", "socioeconomic_context", query, $"{common} socioeconomic context", summary, summary,
            "Nufus/alan yogunlugunu oran ve grafik anlatiminda somut veri olarak kullan.",
            "Use as real data for statistics, geography, economics, citizenship, and historical comparison examples.",
            "https://data.worldbank.org/", "REST Countries + World Bank", 0.82, "public_dataset", "medium", new { country, worldBankText })];
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildScienceContextAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var folded = FoldTurkish(query);
        if (ContainsAny(folded, "deprem", "earthquake", "fay", "sismik"))
        {
            var usgs = await TryGetJsonAsync("https://earthquake.usgs.gov/fdsnws/event/1/query?format=geojson&limit=1&orderby=time", ct);
            if (usgs.HasValue && usgs.Value.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array && features.GetArrayLength() > 0)
            {
                var props = features[0].GetProperty("properties");
                var place = GetString(props, "place") ?? "recent earthquake";
                var mag = props.TryGetProperty("mag", out var magProp) && magProp.ValueKind == JsonValueKind.Number ? magProp.GetDouble() : (double?)null;
                var summary = $"Recent USGS earthquake feed example: {place}, magnitude {(mag.HasValue ? mag.Value.ToString("0.0") : "unknown")}.";
                return [Card(request, "usgs", "science_context", query, "USGS earthquake example", summary, summary,
                    "Deprem dalgasi, magnitud veya enerji farkini gercek olayla orneklendir.",
                    "Use as live scientific event data; avoid panic language and cite USGS.",
                    "https://earthquake.usgs.gov/", "USGS Earthquake Catalog", 0.84, "live_feed", "medium", features[0])];
            }
        }

        if (ContainsAny(folded, "astronomi", "nasa", "uzay", "gezegen", "yildiz", "galaksi"))
        {
            var nasa = await TryGetJsonAsync("https://api.nasa.gov/planetary/apod?api_key=DEMO_KEY", ct);
            if (nasa.HasValue)
            {
                var title = GetString(nasa.Value, "title") ?? "NASA astronomy picture";
                var explanation = GetString(nasa.Value, "explanation") ?? string.Empty;
                var url = GetString(nasa.Value, "url");
                return [Card(request, "nasa_apod", "science_context", query, title, Trim(explanation, 460), Trim(explanation, 320),
                    "Uzay/astronomi konusunu gercek bir NASA gorseli veya olayi uzerinden somutlastir.",
                    "Use as illustrative astronomy evidence; APOD demo key can be rate-limited.",
                    url, "NASA APOD", 0.78, "daily", "low", nasa.Value)];
            }
        }

        if (ContainsAny(folded, "biyoloji", "species", "canli turu", "hayvan", "bitki", "ekoloji"))
        {
            var cards = new List<TeachingEvidenceCardDto>();
            var gbif = await TryGetJsonAsync($"https://api.gbif.org/v1/species/search?q={Uri.EscapeDataString(query)}&limit=1", ct);
            if (gbif.HasValue && gbif.Value.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
            {
                var first = results[0];
                var name = GetString(first, "scientificName") ?? GetString(first, "canonicalName") ?? query;
                var rank = GetString(first, "rank") ?? "taxon";
                var summary = $"{name} appears in GBIF species data as {rank}.";
                cards.Add(Card(request, "gbif", "science_context", query, name, summary, summary,
                    "Canli siniflandirma ve gozlem verisi mantigini somutlastirmak icin kullan.",
                    "Use as biodiversity/taxonomy signal; not a full biology explanation alone.",
                    "https://www.gbif.org/", "GBIF species API", 0.74, "public_dataset", "low", first));
            }

            var inat = await TryGetJsonAsync($"https://api.inaturalist.org/v1/observations?taxon_name={Uri.EscapeDataString(query)}&per_page=1", ct);
            if (inat.HasValue && inat.Value.TryGetProperty("results", out var observations) && observations.ValueKind == JsonValueKind.Array && observations.GetArrayLength() > 0)
            {
                var first = observations[0];
                var observedOn = GetString(first, "observed_on") ?? "recent observation";
                var url = GetString(first, "uri");
                var taxonName = first.TryGetProperty("taxon", out var taxon)
                    ? GetString(taxon, "preferred_common_name") ?? GetString(taxon, "name") ?? query
                    : query;
                cards.Add(Card(request, "inaturalist", "science_context", query, $"{taxonName} observation signal", $"iNaturalist has an observation signal for {taxonName}; observed on {observedOn}.",
                    $"A real-world biodiversity observation exists for {taxonName}.",
                    "Tur gozlemi ve ekosistem baglamini gercek gozlem mantigiyla aciklamak icin kullan.",
                    "Community observation signal; verify before using as definitive range evidence.",
                    url, "iNaturalist observations API", 0.66, "community_dataset", "medium", first));
            }

            if (cards.Count > 0) return cards.Take(2).ToList();
        }

        var pubchem = await TryGetJsonAsync($"https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/name/{Uri.EscapeDataString(query)}/property/MolecularFormula,MolecularWeight/JSON", ct);
        if (pubchem.HasValue &&
            pubchem.Value.TryGetProperty("PropertyTable", out var table) &&
            table.TryGetProperty("Properties", out var compoundProps) &&
            compoundProps.ValueKind == JsonValueKind.Array &&
            compoundProps.GetArrayLength() > 0)
        {
            var first = compoundProps[0];
            var formula = GetString(first, "MolecularFormula") ?? "unknown formula";
            var weight = first.TryGetProperty("MolecularWeight", out var mw) ? mw.ToString() : "unknown weight";
            var summary = $"{query}: molecular formula {formula}, molecular weight {weight}.";
            return [Card(request, "pubchem", "science_context", query, $"{query} chemistry fact", summary, summary,
                "Formul ve mol kutlesini gercek veriyle baglayarak kimya hesaplarini somutlastir.",
                "Use as chemistry data card; cite PubChem for molecular properties.",
                "https://pubchem.ncbi.nlm.nih.gov/", "PubChem PUG REST", 0.86, "static_dataset", "low", first)];
        }

        return [];
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildResearchContextAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var cards = new List<TeachingEvidenceCardDto>();
        var openAlex = await TryGetJsonAsync($"https://api.openalex.org/works?search={Uri.EscapeDataString(query)}&per-page=1", ct);
        if (openAlex.HasValue && openAlex.Value.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array && results.GetArrayLength() > 0)
        {
            var first = results[0];
            var title = GetString(first, "title") ?? query;
            var citedBy = first.TryGetProperty("cited_by_count", out var cited) && cited.ValueKind == JsonValueKind.Number ? cited.GetInt32() : 0;
            var url = GetString(first, "doi") ?? GetString(first, "id");
            cards.Add(Card(request, "openalex", "research_context", query, title, $"OpenAlex related work; cited by count: {citedBy}.",
                $"{title} is a related research record indexed by OpenAlex.",
                "Ileri okuma veya akademik baglam karti olarak kullan.",
                "Research context only; do not overstate as consensus.",
                url, "OpenAlex work search", 0.76, "research_index", "medium", first));
        }

        var arxiv = await TryGetTextAsync($"https://export.arxiv.org/api/query?search_query=all:{Uri.EscapeDataString(query)}&start=0&max_results=1", ct);
        if (!string.IsNullOrWhiteSpace(arxiv))
        {
            var title = ExtractXmlTag(arxiv, "title", skipFirst: true) ?? $"arXiv result for {query}";
            var summary = ExtractXmlTag(arxiv, "summary") ?? string.Empty;
            var id = ExtractXmlTag(arxiv, "id", skipFirst: true);
            cards.Add(Card(request, "arxiv", "research_context", query, Trim(title, 180), Trim(summary, 420),
                Trim(summary, 300),
                "Konu ileri seviyedeyse makale okuma karti veya kavram baglami olarak kullan.",
                "Preprint index; not peer-review guarantee.",
                id, "arXiv API", 0.70, "research_index", "medium", new { title, summary, id }));
        }

        var openLibrary = await TryGetJsonAsync($"https://openlibrary.org/search.json?q={Uri.EscapeDataString(query)}&limit=1", ct);
        if (openLibrary.HasValue && openLibrary.Value.TryGetProperty("docs", out var docs) && docs.ValueKind == JsonValueKind.Array && docs.GetArrayLength() > 0)
        {
            var first = docs[0];
            var title = GetString(first, "title") ?? query;
            var key = GetString(first, "key");
            var author = first.TryGetProperty("author_name", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0
                ? authors[0].GetString()
                : null;
            cards.Add(Card(request, "openlibrary", "research_context", query, title, string.IsNullOrWhiteSpace(author) ? "OpenLibrary reading result." : $"OpenLibrary reading result by {author}.",
                $"{title} can be used as a reading/reference suggestion.",
                "Kitap/okuma onerisi olarak kullan; kavram kaniti icin asil kaynakla destekle.",
                "Bibliographic signal only; availability and edition quality can vary.",
                string.IsNullOrWhiteSpace(key) ? "https://openlibrary.org/" : $"https://openlibrary.org{key}",
                "OpenLibrary search API", 0.64, "library_index", "low", first));
        }

        return cards.Take(3).ToList();
    }

    private async Task<List<TeachingEvidenceCardDto>> BuildForumSignalAsync(TeachingEvidenceRequestDto request, string query, CancellationToken ct)
    {
        var cards = new List<TeachingEvidenceCardDto>();
        var stack = await TryGetJsonAsync($"https://api.stackexchange.com/2.3/search/advanced?order=desc&sort=votes&site=stackoverflow&q={Uri.EscapeDataString(query)}&pagesize=1", ct);
        if (stack.HasValue && stack.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
        {
            var first = items[0];
            var title = WebUtility.HtmlDecode(GetString(first, "title") ?? query);
            var url = GetString(first, "link");
            var score = first.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number ? scoreProp.GetInt32() : 0;
            cards.Add(Card(request, "stackexchange", "forum_signal", query, title, $"Stack Overflow high-vote question signal; score: {score}.",
                "Developers commonly ask about this pattern; treat it as a confusion signal, not proof.",
                "Yaygin hata/yanlis anlama oruntusunu anlatimda kullan; dogru bilgi icin resmi kaynak veya test sonucu gerekir.",
                "Forum signal only; never use as factual authority.",
                url, "Stack Exchange API", 0.64, "forum_recent_cache", "medium", first));
        }

        var hn = await TryGetJsonAsync($"https://hn.algolia.com/api/v1/search?query={Uri.EscapeDataString(query)}&tags=story&hitsPerPage=1", ct);
        if (hn.HasValue && hn.Value.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array && hits.GetArrayLength() > 0)
        {
            var first = hits[0];
            var title = GetString(first, "title") ?? GetString(first, "story_title") ?? query;
            var url = GetString(first, "url");
            cards.Add(Card(request, "hn_algolia", "forum_signal", query, title, "Hacker News discussion signal found.",
                "Practitioners discuss this topic; use only as interest/friction signal.",
                "Gercek hayatta bu konunun nasil tartisildigini gostermek icin kullan.",
                "Discussion signal only; not a truth source.",
                url, "HN Algolia search", 0.58, "forum_recent_cache", "medium", first));
        }

        return cards.Take(2).ToList();
    }

    private async Task<JsonElement?> TryGetJsonAsync(string url, CancellationToken ct)
    {
        var text = await TryGetTextAsync(url, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private async Task<string?> TryGetTextAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var client = _httpClientFactory.CreateClient("RealWorldEvidence");
        using var response = await client.GetAsync(url, timeout.Token);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    private async Task PersistCardsAsync(IReadOnlyList<TeachingEvidenceCardDto> cards, TeachingEvidenceRequestDto request, TimeSpan ttl, CancellationToken ct)
    {
        if (cards.Count == 0) return;
        var expiresAt = DateTime.UtcNow.Add(ttl);
        foreach (var card in cards)
        {
            _db.TeachingEvidenceItems.Add(new TeachingEvidenceItem
            {
                Id = card.Id,
                UserId = request.UserId,
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                TutorTurnStateId = request.TutorTurnStateId,
                TutorActionTraceId = request.TutorActionTraceId,
                TutorToolCallId = card.TutorToolCallId,
                EvidenceType = card.EvidenceType,
                Provider = card.Provider,
                ConceptKey = card.ConceptKey,
                Query = card.Query,
                Title = card.Title,
                Summary = card.Summary,
                FactualClaim = card.FactualClaim,
                AnalogyCandidate = card.AnalogyCandidate,
                ClassroomUse = card.ClassroomUse,
                CitationUrl = card.CitationUrl,
                CitationLabel = card.CitationLabel,
                Confidence = (decimal)Math.Clamp(card.Confidence, 0, 1),
                Freshness = card.Freshness,
                RiskLevel = card.RiskLevel,
                RawPayloadHash = card.RawPayloadHash,
                RawPayloadJson = JsonSerializer.Serialize(card, JsonOptions),
                Status = card.Status,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<TeachingEvidenceResultDto> FinishAsync(
        TeachingEvidenceRequestDto request,
        string evidenceType,
        string provider,
        string status,
        IReadOnlyList<TeachingEvidenceCardDto> cards,
        string safeMessage,
        string? errorCode,
        long latencyMs,
        bool fallbackUsed,
        CancellationToken ct)
    {
        _db.TeachingEvidenceProviderHealth.Add(new TeachingEvidenceProviderHealth
        {
            Id = Guid.NewGuid(),
            Provider = string.IsNullOrWhiteSpace(provider) ? evidenceType : provider,
            EvidenceType = evidenceType,
            Status = status,
            Success = cards.Count > 0 && string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase),
            LastErrorCode = errorCode,
            LatencyMs = latencyMs,
            Notes = safeMessage,
            CheckedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var success = cards.Count > 0 && string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase);
        return new TeachingEvidenceResultDto(
            success,
            evidenceType,
            provider,
            status,
            cards,
            safeMessage,
            errorCode,
            cards.Count == 0 ? null : cards.Average(c => c.Confidence),
            cards.Count,
            latencyMs,
            fallbackUsed);
    }

    private static TeachingEvidenceCardDto Card(
        TeachingEvidenceRequestDto request,
        string provider,
        string evidenceType,
        string query,
        string title,
        string summary,
        string factualClaim,
        string analogyCandidate,
        string classroomUse,
        string? citationUrl,
        string citationLabel,
        double confidence,
        string freshness,
        string riskLevel,
        object rawPayload)
    {
        var rawJson = JsonSerializer.Serialize(rawPayload, JsonOptions);
        return new TeachingEvidenceCardDto
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TutorTurnStateId = request.TutorTurnStateId,
            TutorActionTraceId = request.TutorActionTraceId,
            Provider = provider,
            EvidenceType = evidenceType,
            ConceptKey = request.ConceptKey ?? string.Empty,
            Query = query,
            Title = title,
            Summary = summary,
            FactualClaim = factualClaim,
            AnalogyCandidate = analogyCandidate,
            ClassroomUse = classroomUse,
            CitationUrl = citationUrl,
            CitationLabel = citationLabel,
            Confidence = Math.Clamp(confidence, 0, 1),
            Freshness = freshness,
            RiskLevel = riskLevel,
            RawPayloadHash = Hash(rawJson),
            Status = string.IsNullOrWhiteSpace(citationLabel) ? "degraded" : "ready",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<TeachingEvidenceCardDto> CloneForTurn(IReadOnlyList<TeachingEvidenceCardDto> cards, TeachingEvidenceRequestDto request, string query, string evidenceType) =>
        cards.Select(card => new TeachingEvidenceCardDto
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TutorTurnStateId = request.TutorTurnStateId,
            TutorActionTraceId = request.TutorActionTraceId,
            Provider = card.Provider,
            EvidenceType = evidenceType,
            ConceptKey = request.ConceptKey ?? card.ConceptKey,
            Query = query,
            Title = card.Title,
            Summary = card.Summary,
            FactualClaim = card.FactualClaim,
            AnalogyCandidate = card.AnalogyCandidate,
            ClassroomUse = card.ClassroomUse,
            CitationUrl = card.CitationUrl,
            CitationLabel = card.CitationLabel,
            Confidence = card.Confidence,
            Freshness = card.Freshness,
            RiskLevel = card.RiskLevel,
            RawPayloadHash = card.RawPayloadHash,
            Status = card.Status,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();

    private static IReadOnlyList<TeachingEvidenceCardDto> StripTurnFields(IReadOnlyList<TeachingEvidenceCardDto> cards) =>
        cards.Select(card => new TeachingEvidenceCardDto
        {
            Id = Guid.NewGuid(),
            UserId = Guid.Empty,
            TopicId = null,
            SessionId = null,
            TutorTurnStateId = null,
            TutorActionTraceId = null,
            TutorToolCallId = null,
            Provider = card.Provider,
            EvidenceType = card.EvidenceType,
            ConceptKey = card.ConceptKey,
            Query = card.Query,
            Title = card.Title,
            Summary = card.Summary,
            FactualClaim = card.FactualClaim,
            AnalogyCandidate = card.AnalogyCandidate,
            ClassroomUse = card.ClassroomUse,
            CitationUrl = card.CitationUrl,
            CitationLabel = card.CitationLabel,
            Confidence = card.Confidence,
            Freshness = card.Freshness,
            RiskLevel = card.RiskLevel,
            RawPayloadHash = card.RawPayloadHash,
            Status = card.Status,
            CreatedAt = card.CreatedAt
        }).ToList();

    private static TeachingEvidenceCardDto ToDto(TeachingEvidenceItem item) => new()
    {
        Id = item.Id,
        UserId = item.UserId,
        TopicId = item.TopicId,
        SessionId = item.SessionId,
        TutorTurnStateId = item.TutorTurnStateId,
        TutorActionTraceId = item.TutorActionTraceId,
        TutorToolCallId = item.TutorToolCallId,
        Provider = item.Provider,
        EvidenceType = item.EvidenceType,
        ConceptKey = item.ConceptKey,
        Query = item.Query,
        Title = item.Title,
        Summary = item.Summary,
        FactualClaim = item.FactualClaim,
        AnalogyCandidate = item.AnalogyCandidate,
        ClassroomUse = item.ClassroomUse,
        CitationUrl = item.CitationUrl,
        CitationLabel = item.CitationLabel,
        Confidence = (double)item.Confidence,
        Freshness = item.Freshness,
        RiskLevel = item.RiskLevel,
        RawPayloadHash = item.RawPayloadHash,
        Status = item.Status,
        CreatedAt = item.CreatedAt
    };

    private static TimeSpan CacheTtl(string evidenceType) => evidenceType switch
    {
        "forum_signal" => TimeSpan.FromHours(6),
        "news" => TimeSpan.FromHours(6),
        "geo_context" => TimeSpan.FromMinutes(45),
        "socioeconomic_context" => TimeSpan.FromDays(7),
        "science_context" => TimeSpan.FromDays(14),
        "research_context" => TimeSpan.FromDays(7),
        "knowledge_entity" => TimeSpan.FromDays(3),
        _ => TimeSpan.FromHours(12)
    };

    private static bool TryDeserializeCards(string json, out IReadOnlyList<TeachingEvidenceCardDto> cards)
    {
        try
        {
            cards = JsonSerializer.Deserialize<List<TeachingEvidenceCardDto>>(json, JsonOptions) ?? [];
            return cards.Count > 0;
        }
        catch
        {
            cards = [];
            return false;
        }
    }

    private static string NormalizeEvidenceType(string value)
    {
        var clean = (value ?? string.Empty).Trim().ToLowerInvariant();
        return clean is "knowledge_entity" or "geo_context" or "socioeconomic_context" or "science_context" or "research_context" or "forum_signal"
            ? clean
            : "knowledge_entity";
    }

    private static string NormalizeQuery(string query, string userMessage)
    {
        var raw = string.IsNullOrWhiteSpace(query) ? userMessage : query;
        raw = (raw ?? string.Empty).Trim();
        if (raw.Length > 120) raw = raw[..120];
        return raw.Trim(' ', '?', '.', ',', ':', ';', '"', '\'');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? property.ToString()
            : null;

    private static string Trim(string value, int max) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= max ? value ?? string.Empty : value[..max].Trim() + "...";

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string FoldTurkish(string value) =>
        (value ?? string.Empty).ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');

    private static string? ExtractXmlTag(string xml, string tag, bool skipFirst = false)
    {
        var open = $"<{tag}>";
        var close = $"</{tag}>";
        var start = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (skipFirst && start >= 0)
        {
            start = xml.IndexOf(open, start + open.Length, StringComparison.OrdinalIgnoreCase);
        }
        if (start < 0) return null;
        start += open.Length;
        var end = xml.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : WebUtility.HtmlDecode(xml[start..end].Trim());
    }
}
