using System;
using System.Collections.Generic;
using System.Linq;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

/// <summary>
/// One write→read stage in a conversion chain. <see cref="ProfileKey"/> selects the
/// <see cref="CapabilityProfile"/> row (e.g. "xlsx-rebuilt" vs "xlsx-preserved"), which may differ
/// from a bare extension. <see cref="Extension"/> is what resolves the adapter.
/// </summary>
internal sealed record Hop(string ProfileKey, string Extension)
{
    public CapabilityProfile Profile => CapabilityProfile.All[ProfileKey];
}

/// <summary>
/// A named conversion chain. The source format is loaded once into memory (F0); every hop is a
/// write→read ceiling. <see cref="MutateBeforeSnapshot"/> forces the xlsx full-rebuild path by
/// dirtying a cell so source-package patch-save cannot apply — applied to the in-memory workbook
/// BEFORE the reference snapshot is taken, so the mutation itself is never flagged.
/// </summary>
internal sealed class Chain
{
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public required IReadOnlyList<Hop> Hops { get; init; }
    public bool MutateBeforeSnapshot { get; init; }

    /// <summary>
    /// For single-sheet formats (csv/txt) that write only Sheets[0]: if Sheets[0] is empty, promote the
    /// largest data-bearing sheet to index 0 first, so the value/formula assertion is meaningful (models a
    /// user exporting their data sheet rather than a blank cover sheet).
    /// </summary>
    public bool PromoteDataSheet { get; init; }

    public string HopDescription =>
        "xlsx(source)" + string.Concat(Hops.Select(h => $" -> {h.ProfileKey}"));

    /// <summary>Profiles for the hops only (F0 excluded), used for chain-cap min.</summary>
    public IReadOnlyList<CapabilityProfile> HopProfiles => Hops.Select(h => h.Profile).ToList();

    /// <summary>
    /// The first format in the chain whose cap for this dimension is Full yet which could have changed
    /// the value — i.e. the offending hop for a BUG cluster. Falls back to the min-cap hop's key.
    /// </summary>
    public string OffendingFormatFor(Dim d)
    {
        // The first hop whose profile cap equals the chain cap is where the loss is introduced.
        var chainCap = ChainCapability.Min(HopProfiles, d);
        var hop = Hops.FirstOrDefault(h => h.Profile[d] == chainCap);
        return hop?.ProfileKey ?? (Hops.Count > 0 ? Hops[^1].ProfileKey : "?");
    }
}

internal static class Chains
{
    public static List<Chain> Phase0(string sourcePath)
    {
        var x = ".xlsx";
        return new List<Chain>
        {
            // Native idempotence — strictest, every dimension Full. Must be BUGS:0.
            new Chain
            {
                Name = "fxl -> fxl",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("fxl", ".fxl"), new Hop("fxl", ".fxl") },
            },
            // Source-package preservation — patch/verbatim path, no mutation. Must be BUGS:0.
            new Chain
            {
                Name = "xlsx -> xlsx (patch)",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("xlsx-preserved", x) },
            },
            // Full ClosedXML rebuild — mutate a cell so patch-save can't apply. VBA/chartEx expected-loss.
            new Chain
            {
                Name = "xlsx -> xlsx (rebuilt)",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("xlsx-rebuilt", x) },
                MutateBeforeSnapshot = true,
            },
            // SpreadsheetML round-trip — merges/widths/numfmt Full; styles None; R1C1/comment bugs surface.
            // Terminates at the xml reload (single hop): the chain capability intersection is identical to
            // adding a trailing ->xlsx hop (xml already collapses styling to None, so a later xlsx hop can't
            // resurrect it), and stopping at xml avoids a ClosedXML AddHyperlinkRelationship(null) crash that
            // the xml adapter triggers when its null-URI hyperlinks are re-serialized to xlsx. The compared
            // surface is exactly what the spec asks for: styles EXPECTED-LOSS, structure Full, formulas Lossy.
            new Chain
            {
                Name = "xlsx -> xml (reload)",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("xml", ".xml") },
            },
            // CSV round-trip — values + formula-text only; styles/sheets EXPECTED-LOSS.
            new Chain
            {
                Name = "xlsx -> csv -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("csv", ".csv"), new Hop("xlsx-rebuilt", x) },
                PromoteDataSheet = true,
            },
            // Tab-delimited text round-trip — same ceiling as csv.
            new Chain
            {
                Name = "xlsx -> txt -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("txt", ".txt"), new Hop("xlsx-rebuilt", x) },
                PromoteDataSheet = true,
            },
        };
    }

    /// <summary>
    /// Dirties the workbook so the xlsx source-package patch path cannot apply (forcing a full rebuild):
    /// writes a sentinel value to a far-off, guaranteed-empty cell on the first sheet. Done before the
    /// reference snapshot so the sentinel is present in both ref and got and is never itself a BUG.
    /// </summary>
    public static void ForceRebuildMutation(Workbook wb)
    {
        if (wb.Sheets.Count == 0) return;
        var sheet = wb.Sheets[0];
        // Pick a cell well outside any realistic used range.
        var addr = new CellAddress(sheet.Id, 1048500, 16380);
        sheet.SetCell(addr, new NumberValue(424242));
    }

    /// <summary>Move the most-populated sheet to index 0 when the current Sheets[0] is empty.</summary>
    public static void PromoteLargestDataSheet(Workbook wb)
    {
        if (wb.Sheets.Count < 2) return;
        if (wb.Sheets[0].GetOccupiedCellMap().Count > 0) return;

        int bestIdx = 0; int bestCount = -1;
        for (int i = 0; i < wb.Sheets.Count; i++)
        {
            int c = wb.Sheets[i].GetOccupiedCellMap().Count;
            if (c > bestCount) { bestCount = c; bestIdx = i; }
        }
        if (bestIdx != 0) wb.MoveSheet(bestIdx, 0);
    }
}
