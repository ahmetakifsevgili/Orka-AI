using Orka.Core.DTOs.Chat;

namespace Orka.Core.Services;

public static class EvidenceQualityEvaluator
{
    public static EvidenceQualityDto Build(
        int sourceCount,
        int readySourceCount,
        int retrievedEvidenceCount,
        decimal citationCoverage,
        int unsupportedCitationCount,
        int citationMissingCount,
        string? retrievalHealthStatus = null,
        string? citationCoverageStatus = null)
    {
        var reasons = new List<string>();
        var retrieval = Normalize(retrievalHealthStatus);
        var citation = Normalize(citationCoverageStatus);

        if (sourceCount <= 0 || readySourceCount <= 0)
            reasons.Add("no_ready_sources");
        if (retrievedEvidenceCount <= 0 || retrieval is "source_retrieval_empty" or "empty" or "no_source")
            reasons.Add("retrieval_empty");
        if (retrieval is "low_confidence")
            reasons.Add("low_confidence_retrieval");
        if (retrieval is "degraded" or "unverified" or "unknown")
            reasons.Add("unverified_retrieval");
        if (unsupportedCitationCount > 0 || citation is "citation_unsupported")
            reasons.Add("citation_unsupported");
        if (citationMissingCount > 0 || citation is "citation_missing")
            reasons.Add("citation_missing");
        if (citationCoverage > 0m && citationCoverage < 0.50m)
            reasons.Add("low_citation_coverage");
        else if (citationCoverage > 0m && citationCoverage < 0.85m)
            reasons.Add("limited_citation_coverage");
        if (citation is "unverified" or "unknown")
            reasons.Add("unverified_citation_coverage");

        var status =
            sourceCount <= 0 || readySourceCount <= 0 || retrievedEvidenceCount <= 0 ||
            retrieval is "source_retrieval_empty" or "empty" or "no_source"
                ? "missing"
                : unsupportedCitationCount > 0 ||
                  citationMissingCount > 0 ||
                  retrieval is "low_confidence" ||
                  citationCoverage is > 0m and < 0.50m
                    ? "weak"
                    : retrieval is "degraded" or "unverified" or "unknown" ||
                      citation is "unverified" or "unknown" ||
                      citationCoverage is > 0m and < 0.85m
                        ? "partial"
                        : retrieval is "healthy" &&
                          (citation is "healthy" or "supported") &&
                          citationCoverage >= 0.85m &&
                          unsupportedCitationCount == 0 &&
                          citationMissingCount == 0
                            ? "strong"
                            : "partial";

        if (status == "strong" && reasons.Count == 0)
            reasons.Add("healthy_retrieval_and_citations");

        return new EvidenceQualityDto
        {
            Status = status,
            UserSafeLabel = Label(status),
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            SourceCount = Math.Max(0, sourceCount),
            ReadySourceCount = Math.Max(0, readySourceCount),
            RetrievedEvidenceCount = Math.Max(0, retrievedEvidenceCount),
            CitationCoverage = Math.Clamp(citationCoverage, 0m, 1m),
            UnsupportedCitationCount = Math.Max(0, unsupportedCitationCount),
            CitationMissingCount = Math.Max(0, citationMissingCount)
        };
    }

    public static EvidenceQualityDto Unknown() => new()
    {
        Status = "unknown",
        UserSafeLabel = Label("unknown"),
        Reasons = new[] { "metadata_unavailable" }
    };

    private static string Normalize(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim().ToLowerInvariant();

    private static string Label(string status) => status switch
    {
        "strong" => "Kaynak güveni güçlü",
        "partial" => "Kaynak güveni sınırlı",
        "weak" => "Kaynak zayıf",
        "missing" => "Kaynak bulunamadı",
        _ => "Kaynak durumu bilinmiyor"
    };
}
