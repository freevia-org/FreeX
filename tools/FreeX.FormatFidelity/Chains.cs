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
internal sealed record Hop(string ProfileKey, string Extension, string? FormatName = null)
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
            // SpreadsheetML full round-trip — merges/widths/numfmt/formulas Full; styles None.
            // The trailing ->xlsx hop is back now that (a) R1C1<->A1 conversion makes formulas round-trip
            // faithfully (xml Formulas promoted to Full) and (b) the xml adapter no longer emits null-URI
            // hyperlinks, so re-serializing to xlsx no longer hits ClosedXML AddHyperlinkRelationship(null).
            new Chain
            {
                Name = "xlsx -> xml -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("xml", ".xml"), new Hop("xlsx-rebuilt", x) },
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
    /// Phase 2 — easy net-new formats (audit §4 Phase 2). Each new adapter ships with its chain here so
    /// it is gated from day one. The encoding variants test the BOM-bearing CSV/TXT writers; slk/dif/xltx
    /// test the new line-based and template adapters.
    /// </summary>
    public static List<Chain> Phase2(string sourcePath)
    {
        var x = ".xlsx";
        return new List<Chain>
        {
            // CSV UTF-8 (BOM) round-trip — same value/formula ceiling as plain csv; verifies the BOM
            // encoding path reads back losslessly.
            new Chain
            {
                Name = "xlsx -> csv-utf8 -> xlsx",
                SourcePath = sourcePath,
                Hops = new[]
                {
                    new Hop("csv-utf8", ".csv", "CSV UTF-8 (Comma delimited)"),
                    new Hop("xlsx-rebuilt", x),
                },
                PromoteDataSheet = true,
            },
            // Unicode Text (UTF-16LE BOM) round-trip — same ceiling as tab-delimited txt.
            new Chain
            {
                Name = "xlsx -> txt-unicode -> xlsx",
                SourcePath = sourcePath,
                Hops = new[]
                {
                    new Hop("txt-unicode", ".txt", "Unicode Text"),
                    new Hop("xlsx-rebuilt", x),
                },
                PromoteDataSheet = true,
            },
            // XLTX save — xlsx writer with template content-type. Same engine as a full rebuild, so all
            // modeled dimensions must round-trip exactly (it IS the xlsx writer).
            new Chain
            {
                Name = "xltx -> xltx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("xltx", ".xltx") },
            },
            // SLK round-trip — values + R1C1 formulas + coarse number formats survive a single sheet.
            new Chain
            {
                Name = "xlsx -> slk -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("slk", ".slk"), new Hop("xlsx-rebuilt", x) },
                PromoteDataSheet = true,
            },
            // DIF round-trip — values only, single sheet.
            new Chain
            {
                Name = "xlsx -> dif -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("dif", ".dif"), new Hop("xlsx-rebuilt", x) },
                PromoteDataSheet = true,
            },
        };
    }

    /// <summary>
    /// Phase 3 — ODS (audit §4 Phase 3, the highest-ROI net-new format). The single-hop gate asserts that
    /// styles/merges/structure/number-formats survive an xlsx->ods->xlsx round-trip exactly (all Full in
    /// the ods profile). The multi-hop chains verify the intersection logic composes: once csv collapses
    /// styling to None it stays None, and an ods hop after an xml hop cannot resurrect dropped styling.
    /// </summary>
    public static List<Chain> Phase3(string sourcePath)
    {
        var x = ".xlsx";
        return new List<Chain>
        {
            // The ODS regression gate — styles/merges/structure/number-formats are Full and must survive.
            new Chain
            {
                Name = "xlsx -> ods -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("ods", ".ods"), new Hop("xlsx-rebuilt", x) },
            },
            // ODS then CSV — after the csv hop, values + formula-text are the only surviving dims.
            new Chain
            {
                Name = "xlsx -> ods -> xlsx -> csv -> xlsx",
                SourcePath = sourcePath,
                Hops = new[]
                {
                    new Hop("ods", ".ods"),
                    new Hop("xlsx-rebuilt", x),
                    new Hop("csv", ".csv"),
                    new Hop("xlsx-rebuilt", x),
                },
                PromoteDataSheet = true,
            },
            // SpreadsheetML then ODS — styling is None (xml can't hold it), but values/formulas/merges/
            // widths/numfmt remain Full across both hops and must round-trip.
            new Chain
            {
                Name = "xlsx -> xml -> xlsx -> ods -> xlsx",
                SourcePath = sourcePath,
                Hops = new[]
                {
                    new Hop("xml", ".xml"),
                    new Hop("xlsx-rebuilt", x),
                    new Hop("ods", ".ods"),
                    new Hop("xlsx-rebuilt", x),
                },
            },
            // ODS then SpreadsheetML — the reverse order; styling collapses at the xml hop.
            new Chain
            {
                Name = "xlsx -> ods -> xlsx -> xml -> xlsx",
                SourcePath = sourcePath,
                Hops = new[]
                {
                    new Hop("ods", ".ods"),
                    new Hop("xlsx-rebuilt", x),
                    new Hop("xml", ".xml"),
                    new Hop("xlsx-rebuilt", x),
                },
            },
        };
    }

    /// <summary>
    /// Phase 4 — Tier-B heavy lifts (audit §4 Phase 4). HTML is a read+write table format: the
    /// xlsx->html->xlsx chain proves values + merge geometry round-trip (both Lossy), while styles/formulas/
    /// number-formats/multi-sheet drop as the documented HTML-table ceiling. (DBF is read-only and therefore
    /// has no round-trip chain — it is gated by a focused unit test loading a .dbf fixture instead.)
    /// </summary>
    public static List<Chain> Phase4(string sourcePath)
    {
        var x = ".xlsx";
        return new List<Chain>
        {
            // HTML round-trip — values + colspan/rowspan merge geometry survive; styling/structure is the
            // single-table ceiling (None). Gate: BUGS:0.
            new Chain
            {
                Name = "xlsx -> html -> xlsx",
                SourcePath = sourcePath,
                Hops = new[] { new Hop("html", ".html"), new Hop("xlsx-rebuilt", x) },
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
