using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

internal enum ResultKind
{
    Ok,
    Bug,
    ExpectedLoss,
    PreservedAnyway,
}

/// <summary>Outcome of comparing one dimension of one chain.</summary>
internal sealed class DimensionResult
{
    public required Dim Dimension { get; init; }
    public required Cap ChainCap { get; init; }
    public required ResultKind Kind { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>Sample {sheet}!{col}{row} addresses for a BUG (or PreservedAnyway), up to a few.</summary>
    public List<string> SampleAddresses { get; } = new();
    public int Matched { get; init; }
    public int Total { get; init; }
}

/// <summary>
/// Compares a reference snapshot (F0, in-memory source) against a reloaded snapshot (Fn), one
/// dimension at a time, gated by the chain cap (§3d). The core loss-vs-bug rule:
///   None  -&gt; any change is EXPECTED-LOSS (never a failure); no change is PRESERVED-ANYWAY.
///   Lossy -&gt; tolerant/display comparison; beyond tolerance is a BUG.
///   Full  -&gt; exact comparison; any change is a BUG.
/// </summary>
internal static class DimensionComparer
{
    public static List<DimensionResult> Compare(
        WorkbookSnapshot reference,
        WorkbookSnapshot got,
        IReadOnlyList<CapabilityProfile> hopProfiles)
    {
        var results = new List<DimensionResult>();
        foreach (Dim d in Enum.GetValues<Dim>())
        {
            var cap = ChainCapability.Min(hopProfiles, d);
            results.Add(CompareDimension(d, cap, reference, got));
        }
        return results;
    }

    private static DimensionResult CompareDimension(Dim d, Cap cap, WorkbookSnapshot refSnap, WorkbookSnapshot gotSnap)
    {
        // Pair sheets. When MultiSheet collapses to None (csv/txt), only the first sheet survives;
        // compare reference sheet[0] against got sheet[0] positionally.
        var sheetPairs = PairSheets(refSnap, gotSnap, cap);

        return d switch
        {
            Dim.CellValues => CompareCells(d, cap, sheetPairs, valuesOnly: true),
            Dim.Formulas => CompareFormulas(d, cap, sheetPairs),
            Dim.NumberFormats => CompareCellStyle(d, cap, sheetPairs,
                (a, b) => string.Equals(Canonical(a.NumberFormat), Canonical(b.NumberFormat), StringComparison.Ordinal)),
            Dim.Fonts => CompareCellStyle(d, cap, sheetPairs, FontsEqual),
            Dim.Fills => CompareCellStyle(d, cap, sheetPairs, FillsEqual),
            Dim.Borders => CompareCellStyle(d, cap, sheetPairs, BordersEqual),
            Dim.Alignment => CompareCellStyle(d, cap, sheetPairs, AlignmentEqual),
            Dim.MultiSheet => CompareScalar(d, cap, refSnap.Sheets.Count, gotSnap.Sheets.Count,
                $"sheet count {refSnap.Sheets.Count}->{gotSnap.Sheets.Count}"),
            Dim.SheetNames => CompareSheetNames(d, cap, refSnap, gotSnap),
            Dim.MergedCells => CompareMerges(d, cap, sheetPairs),
            Dim.ColumnWidths => CompareWidths(d, cap, sheetPairs, widths: true),
            Dim.RowHeights => CompareWidths(d, cap, sheetPairs, widths: false),
            Dim.FreezePanes => CompareFreeze(d, cap, sheetPairs),
            Dim.Hyperlinks => CompareCount(d, cap, sheetPairs, s => s.HyperlinkCount, "hyperlinks"),
            Dim.Comments => CompareCount(d, cap, sheetPairs, s => s.CommentCount, "comments"),
            Dim.DefinedNames => CompareNamedRanges(d, cap, refSnap, gotSnap),
            Dim.DataValidation => CompareCount(d, cap, sheetPairs, s => s.DataValidationCount, "data-validations"),
            Dim.ConditionalFormat => CompareCount(d, cap, sheetPairs, s => s.ConditionalFormatCount, "conditional-formats"),
            Dim.Charts => CompareCount(d, cap, sheetPairs, s => s.ChartCount, "charts"),
            Dim.Images => CompareCount(d, cap, sheetPairs, s => s.ImageCount, "images"),
            Dim.Vba => CompareScalar(d, cap, refSnap.HasVba ? 1 : 0, gotSnap.HasVba ? 1 : 0, "vba presence"),
            _ => new DimensionResult { Dimension = d, ChainCap = cap, Kind = ResultKind.Ok },
        };
    }

    // ---- sheet pairing ----------------------------------------------------------------------

    private sealed record SheetPair(WorkbookSnapshot.SheetSnapshot Ref, WorkbookSnapshot.SheetSnapshot Got);

    private static List<SheetPair> PairSheets(WorkbookSnapshot refSnap, WorkbookSnapshot gotSnap, Cap cap)
    {
        var pairs = new List<SheetPair>();
        if (cap == Cap.None)
        {
            // Format dropped multi-sheet: only sheet[0] is meaningful, paired positionally.
            if (refSnap.Sheets.Count > 0 && gotSnap.Sheets.Count > 0)
                pairs.Add(new SheetPair(refSnap.Sheets[0], gotSnap.Sheets[0]));
            return pairs;
        }

        // Prefer name match; fall back to positional for renamed/sanitized sheets.
        var gotByName = gotSnap.Sheets
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        for (int i = 0; i < refSnap.Sheets.Count; i++)
        {
            var r = refSnap.Sheets[i];
            if (gotByName.TryGetValue(r.Name, out var byName))
                pairs.Add(new SheetPair(r, byName));
            else if (i < gotSnap.Sheets.Count)
                pairs.Add(new SheetPair(r, gotSnap.Sheets[i]));
        }
        return pairs;
    }

    // ---- cell-value / formula comparison ----------------------------------------------------

    private static DimensionResult CompareCells(Dim d, Cap cap, List<SheetPair> pairs, bool valuesOnly)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: pairs.Count > 0);

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            // For value comparison across single-sheet formats, csv re-keys cells identically (row/col
            // preserved), so a direct (row,col) join works for all chains.
            foreach (var ((row, col), refCell) in pair.Ref.Cells)
            {
                // Skip pure style-only blanks (no value, no formula) for the VALUE dimension.
                if (refCell.Value is BlankValue && !refCell.HasFormula) continue;
                total++;
                pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                bool ok = cap == Cap.Lossy
                    ? ValuesMatchLossy(refCell.Value, gotCell?.Value ?? BlankValue.Instance)
                    : FidelityCompare.ValuesMatch(refCell.Value, gotCell?.Value ?? BlankValue.Instance);
                if (ok) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row} "
                        + $"[{FidelityCompare.ScalarStr(refCell.Value)} -> {FidelityCompare.ScalarStr(gotCell?.Value ?? BlankValue.Instance)}]");
            }
        }
        return Classify(d, cap, matched, total, samples, "cells");
    }

    private static bool ValuesMatchLossy(ScalarValue a, ScalarValue b)
    {
        if (FidelityCompare.ValuesMatch(a, b)) return true;
        // CSV coercion: compare by display string (a typed value written as text may reload typed-but-equal,
        // or a number formatted differently). Numbers already handled by NumbersMatch in ValuesMatch.
        return string.Equals(FidelityCompare.DisplayString(a), FidelityCompare.DisplayString(b), StringComparison.Ordinal);
    }

    private static DimensionResult CompareFormulas(Dim d, Cap cap, List<SheetPair> pairs)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: pairs.Any(p => p.Ref.Cells.Values.Any(c => c.HasFormula)));

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            foreach (var ((row, col), refCell) in pair.Ref.Cells)
            {
                if (!refCell.HasFormula || refCell.FormulaText is null) continue;
                total++;
                pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                bool ok;
                if (cap == Cap.Full)
                {
                    ok = gotCell is not null && gotCell.HasFormula &&
                         string.Equals(NormalizeFormula(refCell.FormulaText), NormalizeFormula(gotCell.FormulaText),
                             StringComparison.OrdinalIgnoreCase);
                }
                else // Lossy (csv writes text; xml may store R1C1) — require formula text to survive recoverably.
                {
                    ok = gotCell is not null && gotCell.HasFormula && !string.IsNullOrEmpty(gotCell.FormulaText)
                         && string.Equals(NormalizeFormula(refCell.FormulaText), NormalizeFormula(gotCell.FormulaText),
                             StringComparison.OrdinalIgnoreCase);
                }
                if (ok) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row} "
                        + $"[={refCell.FormulaText} -> {(gotCell?.HasFormula == true ? "=" + gotCell.FormulaText : FidelityCompare.ScalarStr(gotCell?.Value ?? BlankValue.Instance))}]");
            }
        }
        return Classify(d, cap, matched, total, samples, "formulas");
    }

    private static string NormalizeFormula(string? f)
    {
        if (string.IsNullOrEmpty(f)) return "";
        var s = f.TrimStart('=').Trim();
        return s.Replace(" ", "");
    }

    // ---- style comparison -------------------------------------------------------------------

    private static DimensionResult CompareCellStyle(Dim d, Cap cap, List<SheetPair> pairs, Func<CellStyle, CellStyle, bool> eq)
    {
        if (cap == Cap.None)
        {
            // Did anything in this dimension differ? Approximate by checking whether any styled cell exists.
            bool anyChange = false;
            foreach (var pair in pairs)
                foreach (var ((row, col), refCell) in pair.Ref.Cells)
                {
                    pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                    if (gotCell is null || !eq(refCell.Style, gotCell.Style)) { anyChange = true; break; }
                }
            return MakeNoneResult(d, cap, anyChange);
        }

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            foreach (var ((row, col), refCell) in pair.Ref.Cells)
            {
                total++;
                pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                bool ok = gotCell is not null && eq(refCell.Style, gotCell.Style);
                if (ok) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row}");
            }
        }
        return Classify(d, cap, matched, total, samples, "styled cells");
    }

    private static bool FontsEqual(CellStyle a, CellStyle b) =>
        a.FontName == b.FontName && Math.Abs(a.FontSize - b.FontSize) < 1e-6 &&
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline &&
        a.Strikethrough == b.Strikethrough && a.FontColor == b.FontColor && a.FontScheme == b.FontScheme;

    private static bool FillsEqual(CellStyle a, CellStyle b) =>
        Nullable.Equals(a.FillColor, b.FillColor) && a.FillPatternStyle == b.FillPatternStyle &&
        Nullable.Equals(a.FillPatternColor, b.FillPatternColor);

    private static bool BordersEqual(CellStyle a, CellStyle b) =>
        a.BorderTop == b.BorderTop && a.BorderRight == b.BorderRight &&
        a.BorderBottom == b.BorderBottom && a.BorderLeft == b.BorderLeft;

    private static bool AlignmentEqual(CellStyle a, CellStyle b) =>
        a.HorizontalAlignment == b.HorizontalAlignment && a.VerticalAlignment == b.VerticalAlignment &&
        a.WrapText == b.WrapText && a.TextRotation == b.TextRotation && a.IndentLevel == b.IndentLevel;

    // ---- structure --------------------------------------------------------------------------

    private static DimensionResult CompareSheetNames(Dim d, Cap cap, WorkbookSnapshot refSnap, WorkbookSnapshot gotSnap)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: true);

        // xml sanitizes/truncates to <=31 — compare reference names truncated to 31 against got.
        var refNames = refSnap.Sheets.Select(s => Trunc31(s.Name)).ToList();
        var gotNames = gotSnap.Sheets.Select(s => s.Name).ToList();
        int n = Math.Min(refNames.Count, gotNames.Count);
        int matched = 0;
        var samples = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (string.Equals(refNames[i], gotNames[i], StringComparison.Ordinal)) matched++;
            else if (samples.Count < 6) samples.Add($"[{refNames[i]} -> {gotNames[i]}]");
        }
        return Classify(d, cap, matched, refNames.Count, samples, "sheet names");
    }

    private static string Trunc31(string s) => s.Length <= 31 ? s : s[..31];

    private static DimensionResult CompareMerges(Dim d, Cap cap, List<SheetPair> pairs)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: pairs.Any(p => p.Ref.MergedRanges.Count > 0));

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            var gotSet = new HashSet<((uint, uint), (uint, uint))>(pair.Got.MergedRanges);
            foreach (var m in pair.Ref.MergedRanges)
            {
                total++;
                if (gotSet.Contains(m)) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(m.Start.Item2)}{m.Start.Item1}");
            }
        }
        return Classify(d, cap, matched, total, samples, "merges");
    }

    private static DimensionResult CompareWidths(Dim d, Cap cap, List<SheetPair> pairs, bool widths)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: pairs.Any(p => (widths ? p.Ref.ColumnWidths : p.Ref.RowHeights).Count > 0));

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            var refMap = widths ? pair.Ref.ColumnWidths : pair.Ref.RowHeights;
            var gotMap = widths ? pair.Got.ColumnWidths : pair.Got.RowHeights;
            foreach (var (k, v) in refMap)
            {
                total++;
                if (gotMap.TryGetValue(k, out var gv) && Math.Abs(v - gv) < 1e-3) matched++;
                else if (samples.Count < 6) samples.Add($"{pair.Ref.Name} idx {k} ({v:F2})");
            }
        }
        return Classify(d, cap, matched, total, samples, widths ? "column widths" : "row heights");
    }

    private static DimensionResult CompareFreeze(Dim d, Cap cap, List<SheetPair> pairs)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: pairs.Any(p => p.Ref.FrozenRows > 0 || p.Ref.FrozenCols > 0));

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            total++;
            if (pair.Ref.FrozenRows == pair.Got.FrozenRows && pair.Ref.FrozenCols == pair.Got.FrozenCols) matched++;
            else if (samples.Count < 6)
                samples.Add($"{pair.Ref.Name} froze {pair.Ref.FrozenRows}x{pair.Ref.FrozenCols} -> {pair.Got.FrozenRows}x{pair.Got.FrozenCols}");
        }
        return Classify(d, cap, matched, total, samples, "panes");
    }

    private static DimensionResult CompareNamedRanges(Dim d, Cap cap, WorkbookSnapshot refSnap, WorkbookSnapshot gotSnap)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: refSnap.NamedRanges.Count > 0);

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var (name, refersTo) in refSnap.NamedRanges)
        {
            total++;
            if (gotSnap.NamedRanges.TryGetValue(name, out var got) &&
                string.Equals(got, refersTo, StringComparison.Ordinal)) matched++;
            else if (samples.Count < 6) samples.Add(name);
        }
        return Classify(d, cap, matched, total, samples, "defined names");
    }

    private static DimensionResult CompareCount(Dim d, Cap cap, List<SheetPair> pairs, Func<WorkbookSnapshot.SheetSnapshot, int> count, string label)
    {
        int refTotal = pairs.Sum(p => count(p.Ref));
        int gotTotal = pairs.Sum(p => count(p.Got));
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: refTotal != gotTotal);
        return CompareScalar(d, cap, refTotal, gotTotal, $"{label} {refTotal}->{gotTotal}");
    }

    private static DimensionResult CompareScalar(Dim d, Cap cap, int refVal, int gotVal, string detail)
    {
        if (cap == Cap.None)
            return MakeNoneResult(d, cap, anyChange: refVal != gotVal);

        bool ok = refVal == gotVal;
        // Lossy counts (e.g. xlsx-rebuilt CF/charts): the modeled subset should survive, so a DROP below
        // reference is tolerated as lossy, an INCREASE is unexpected. Treat <= as within-tolerance.
        if (!ok && cap == Cap.Lossy && gotVal <= refVal) ok = true;
        return new DimensionResult
        {
            Dimension = d,
            ChainCap = cap,
            Kind = ok ? ResultKind.Ok : ResultKind.Bug,
            Detail = detail,
            Matched = ok ? 1 : 0,
            Total = 1,
        };
    }

    // ---- classification helpers -------------------------------------------------------------

    private static DimensionResult Classify(Dim d, Cap cap, int matched, int total, List<string> samples, string unit)
    {
        if (total == 0)
            return new DimensionResult { Dimension = d, ChainCap = cap, Kind = ResultKind.Ok, Detail = $"no {unit}", Matched = 0, Total = 0 };

        bool clean = matched == total;
        var r = new DimensionResult
        {
            Dimension = d,
            ChainCap = cap,
            Kind = clean ? ResultKind.Ok : ResultKind.Bug,
            Detail = $"{matched}/{total} {unit} match",
            Matched = matched,
            Total = total,
        };
        if (!clean) r.SampleAddresses.AddRange(samples);
        return r;
    }

    private static DimensionResult MakeNoneResult(Dim d, Cap cap, bool anyChange) => new()
    {
        Dimension = d,
        ChainCap = cap,
        Kind = anyChange ? ResultKind.ExpectedLoss : ResultKind.PreservedAnyway,
        Detail = anyChange ? "dropped — format ceiling" : "preserved despite ceiling",
    };

    private static string Canonical(string numFmt) =>
        string.IsNullOrEmpty(numFmt) || numFmt.Equals("General", StringComparison.OrdinalIgnoreCase) ? "General" : numFmt;
}
