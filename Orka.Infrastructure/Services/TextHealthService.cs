using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TextHealthService : ITextHealthService
{
    private const int MaxFindings = 200;
    private static readonly Regex MojibakeRegex = new(
        @"(?:[\u00c2-\u00c5](?:[\u0080-\u00bf]|\u20ac|\u201a|\u0192|\u201e|\u2026|\u2020|\u2021|\u02c6|\u2030|\u0160|\u2039|\u0152|\u017d|\u2018|\u2019|\u201c|\u201d|\u2022|\u2013|\u2014|\u02dc|\u2122|\u0161|\u203a|\u0153|\u017e|\u0178)|\u00e2(?:[\u0080-\u00bf]|\u20ac|\u201a|\u201c|\u201d|\u2022|\u2013|\u2014){1,2}|\u011f\u0178|\u00ef\u00b8|\ufffd)",
        RegexOptions.Compiled);

    private readonly OrkaDbContext _db;

    public TextHealthService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<TextHealthReportDto> DryRunAsync(CancellationToken ct = default)
    {
        var findings = new List<TextHealthFindingDto>();
        var scanned = await ScanAsync(findings, repair: false, ct);

        return new TextHealthReportDto
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            HasIssues = findings.Count > 0,
            ScannedValueCount = scanned,
            Findings = findings,
            Mode = "dry-run"
        };
    }

    public async Task<TextHealthRepairResultDto> RepairAsync(CancellationToken ct = default)
    {
        var before = new List<TextHealthFindingDto>();
        var scanned = await ScanAsync(before, repair: true, ct);
        await _db.SaveChangesAsync(ct);

        var after = new List<TextHealthFindingDto>();
        await ScanAsync(after, repair: false, ct);

        return new TextHealthRepairResultDto
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ScannedValueCount = scanned,
            RepairedValueCount = before.Count(f => f.Repairable),
            RemainingFindings = after,
            RepairEnabled = true
        };
    }

    private async Task<int> ScanAsync(List<TextHealthFindingDto> findings, bool repair, CancellationToken ct)
    {
        var scanned = 0;

        foreach (var topic in await _db.Topics.ToListAsync(ct))
        {
            scanned += Check(findings, "Topics", "Title", topic.Id, topic.Title, value => topic.Title = value, repair);
            scanned += Check(findings, "Topics", "Category", topic.Id, topic.Category, value => topic.Category = value, repair);
            scanned += Check(findings, "Topics", "PlanIntent", topic.Id, topic.PlanIntent, value => topic.PlanIntent = value, repair);
            scanned += Check(findings, "Topics", "LastStudySnapshot", topic.Id, topic.LastStudySnapshot, value => topic.LastStudySnapshot = value, repair);
        }

        foreach (var page in await _db.WikiPages.ToListAsync(ct))
        {
            scanned += Check(findings, "WikiPages", "Title", page.Id, page.Title, value => page.Title = value, repair);
            scanned += Check(findings, "WikiPages", "Content", page.Id, page.Content, value => page.Content = value, repair);
        }

        foreach (var block in await _db.WikiBlocks.ToListAsync(ct))
        {
            scanned += Check(findings, "WikiBlocks", "Title", block.Id, block.Title, value => block.Title = value, repair);
            scanned += Check(findings, "WikiBlocks", "Content", block.Id, block.Content, value => block.Content = value, repair);
        }

        foreach (var source in await _db.LearningSources.ToListAsync(ct))
        {
            scanned += Check(findings, "LearningSources", "Title", source.Id, source.Title, value => source.Title = value, repair);
            scanned += Check(findings, "LearningSources", "FileName", source.Id, source.FileName, value => source.FileName = value, repair);
            scanned += Check(findings, "LearningSources", "ErrorMessage", source.Id, source.ErrorMessage, value => source.ErrorMessage = value, repair);
        }

        foreach (var chunk in await _db.SourceChunks.Where(c => !c.IsDeleted).Take(500).ToListAsync(ct))
        {
            scanned += Check(findings, "SourceChunks", "Text", chunk.Id, chunk.Text, value => chunk.Text = value, repair);
            scanned += Check(findings, "SourceChunks", "HighlightHint", chunk.Id, chunk.HighlightHint, value => chunk.HighlightHint = value, repair);
        }

        foreach (var card in await _db.Flashcards.ToListAsync(ct))
        {
            scanned += Check(findings, "Flashcards", "Front", card.Id, card.Front, value => card.Front = value, repair);
            scanned += Check(findings, "Flashcards", "Back", card.Id, card.Back, value => card.Back = value, repair);
            scanned += Check(findings, "Flashcards", "Hint", card.Id, card.Hint, value => card.Hint = value, repair);
        }

        return scanned;
    }

    private static int Check(
        ICollection<TextHealthFindingDto> findings,
        string table,
        string column,
        Guid id,
        string? value,
        Action<string> set,
        bool repair)
    {
        if (string.IsNullOrEmpty(value)) return 1;

        var matches = MojibakeRegex.Matches(value);
        if (matches.Count == 0) return 1;

        var repaired = RepairLikelyMojibake(value);
        var repairable = !string.Equals(repaired, value, StringComparison.Ordinal);
        if (repair && repairable)
        {
            set(repaired);
            return 1;
        }

        if (findings.Count < MaxFindings)
        {
            findings.Add(new TextHealthFindingDto
            {
                Table = table,
                Column = column,
                EntityId = id,
                Marker = matches[0].Value,
                Sample = Trim(value),
                OccurrenceCount = matches.Count,
                Repairable = repairable
            });
        }

        return 1;
    }

    private static string RepairLikelyMojibake(string value)
    {
        var repaired = value;
        foreach (var (dirty, clean) in ReplacementPairs)
            repaired = repaired.Replace(dirty, clean, StringComparison.Ordinal);
        return repaired;
    }

    private static readonly IReadOnlyList<(string Dirty, string Clean)> ReplacementPairs =
    [
        (Dirty(0x00c3, 0x00a7), "ç"),
        (Dirty(0x00c3, 0x0087), "Ç"),
        (Dirty(0x00c3, 0x00bc), "ü"),
        (Dirty(0x00c3, 0x009c), "Ü"),
        (Dirty(0x00c3, 0x00b6), "ö"),
        (Dirty(0x00c3, 0x0096), "Ö"),
        (Dirty(0x00c4, 0x00b1), "ı"),
        (Dirty(0x00c4, 0x00b0), "İ"),
        (Dirty(0x00c4, 0x009f), "ğ"),
        (Dirty(0x00c4, 0x009e), "Ğ"),
        (Dirty(0x00c5, 0x009f), "ş"),
        (Dirty(0x00c5, 0x009e), "Ş"),
        (Dirty(0x00e2, 0x20ac, 0x201d), "-"),
        (Dirty(0x00e2, 0x20ac, 0x00a2), "-"),
        (Dirty(0x00e2, 0x20ac, 0x2122), "'"),
        (Dirty(0x00e2, 0x20ac, 0x0153), "\""),
        (Dirty(0x00e2, 0x20ac, 0x009d), "\""),
        (Dirty(0x00c2, 0x00b7), "·"),
        (Dirty(0x00c2, 0x00a0), " ")
    ];

    private static string Dirty(params int[] codepoints) =>
        string.Concat(codepoints.Select(char.ConvertFromUtf32));

    private static string Trim(string value)
    {
        var clean = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length <= 180 ? clean : clean[..180] + "...";
    }
}
