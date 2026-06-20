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
        // Sheet pairing is governed by the MULTI-SHEET chain cap, not each dimension's own cap: csv/txt
        // collapse to a single sheet (pair positionally), every other format keeps all sheets (pair by name).
        var multiSheetCap = ChainCapability.Min(hopProfiles, Dim.MultiSheet);

        var results = new List<DimensionResult>();
        foreach (Dim d in Enum.GetValues<Dim>())
        {
            var cap = ChainCapability.Min(hopProfiles, d);
            results.Add(CompareDimension(d, cap, multiSheetCap, reference, got));
        }
        return results;
    }

    private static DimensionResult CompareDimension(Dim d, Cap cap, Cap multiSheetCap, WorkbookSnapshot refSnap, WorkbookSnapshot gotSnap)
    {
        var sheetPairs = PairSheets(refSnap, gotSnap, multiSheetCap);

        return d switch
        {
            Dim.CellValues => CompareCells(d, cap, sheetPairs, valuesOnly: true),
            Dim.Formulas => CompareFormulas(d, cap, sheetPairs),
            Dim.NumberFormats => CompareNumberFormats(d, cap, sheetPairs),
            Dim.Fonts => CompareFonts(d, cap, sheetPairs),
            Dim.Fills => CompareCellStyle(d, cap, sheetPairs, (a, b, c) => c == Cap.Lossy ? FillsEqualLossy(a, b) : FillsEqual(a.Style, b.Style)),
            Dim.Borders => CompareCellStyle(d, cap, sheetPairs, (a, b, c) => c == Cap.Lossy ? BordersEqualLossy(a.Style, b.Style) : BordersEqual(a.Style, b.Style)),
            Dim.Alignment => CompareCellStyle(d, cap, sheetPairs, (a, b, c) => c == Cap.Lossy ? AlignmentEqualLossy(a.Style, b.Style) : AlignmentEqual(a.Style, b.Style)),
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
                // A Lossy (csv/txt) hop writes a formula's TEXT, not its cached value (footnote 3). The
                // value of such a cell is asserted by the Formulas dimension, not here — counting it as a
                // value mismatch would double-penalize an expected, documented behavior.
                if (cap == Cap.Lossy && refCell.HasFormula) continue;
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
                else // Lossy: require the formula TEXT to survive recoverably. csv/txt reload it as a text
                {    // value ("=IF(...)"); xml may keep it as a formula (possibly R1C1). Accept either form.
                    var refNorm = NormalizeFormula(refCell.FormulaText);
                    string? gotText =
                        gotCell?.HasFormula == true ? gotCell.FormulaText
                        : gotCell?.Value is TextValue tv ? tv.Value
                        : null;
                    ok = gotText is not null &&
                         string.Equals(refNorm, NormalizeFormula(gotText), StringComparison.OrdinalIgnoreCase);
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
        // Strip a leading formula-injection guard apostrophe (csv writer prepends ' to =,+,-,@) and the
        // leading '=', then collapse whitespace so a text-reloaded formula matches the source formula text.
        var s = f.Trim();
        if (s.StartsWith('\'')) s = s[1..];
        s = s.TrimStart('=').Trim();
        return s.Replace(" ", "");
    }

    // ---- style comparison -------------------------------------------------------------------

    private static DimensionResult CompareCellStyle(Dim d, Cap cap, List<SheetPair> pairs,
        Func<WorkbookSnapshot.CellEntry, WorkbookSnapshot.CellEntry, Cap, bool> eq)
    {
        if (cap == Cap.None)
        {
            // Did anything in this dimension differ? Approximate by checking whether any styled cell exists.
            bool anyChange = false;
            foreach (var pair in pairs)
                foreach (var ((row, col), refCell) in pair.Ref.Cells)
                {
                    pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                    if (gotCell is null || !eq(refCell, gotCell, cap)) { anyChange = true; break; }
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
                bool ok = gotCell is not null && eq(refCell, gotCell, cap);
                if (ok) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row}");
            }
        }
        return Classify(d, cap, matched, total, samples, "styled cells");
    }

    // Fonts compare by the EFFECTIVE rendered font (theme-resolved name) plus weight/style/color, not by
    // the raw FontName/FontScheme representation: a cell stored as scheme=Minor and one stored as an
    // explicit name render identically when the theme resolves to that same name, so that is NOT a loss.
    // A genuine substitution (e.g. effective "Aptos Narrow" -> "Calibri") still fails.
    private static DimensionResult CompareFonts(Dim d, Cap cap, List<SheetPair> pairs)
    {
        // Full: assert the raw stored font (effective name + weight/style/underline/strike + raw color).
        // Lossy (HTML): assert only what inline CSS carries — the EFFECTIVE family/size, bold/italic, the
        // resolved font color, and underline treated as a single bucket (HTML cannot distinguish single vs
        // double underline). Strikethrough is NOT carried by the writer, so it is a tolerated approximation
        // and not asserted at Lossy.
        Func<WorkbookSnapshot.CellEntry, WorkbookSnapshot.CellEntry, bool> eq = cap == Cap.Lossy
            ? (a, b) =>
                string.Equals(a.EffectiveFontName, b.EffectiveFontName, StringComparison.Ordinal) &&
                Math.Abs(a.Style.FontSize - b.Style.FontSize) < 1e-6 &&
                a.Style.Bold == b.Style.Bold && a.Style.Italic == b.Style.Italic &&
                (a.Style.Underline || a.Style.DoubleUnderline) == (b.Style.Underline || b.Style.DoubleUnderline) &&
                a.ResolvedFontColor == b.ResolvedFontColor
            : (a, b) =>
                string.Equals(a.EffectiveFontName, b.EffectiveFontName, StringComparison.Ordinal) &&
                Math.Abs(a.Style.FontSize - b.Style.FontSize) < 1e-6 &&
                a.Style.Bold == b.Style.Bold && a.Style.Italic == b.Style.Italic &&
                a.Style.Underline == b.Style.Underline && a.Style.Strikethrough == b.Style.Strikethrough &&
                a.Style.FontColor == b.Style.FontColor;

        if (cap == Cap.None)
        {
            bool anyChange = false;
            foreach (var pair in pairs)
                foreach (var ((row, col), refCell) in pair.Ref.Cells)
                {
                    pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                    if (gotCell is null || !eq(refCell, gotCell)) { anyChange = true; break; }
                }
            return MakeNoneResult(d, cap, anyChange);
        }

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
            foreach (var ((row, col), refCell) in pair.Ref.Cells)
            {
                total++;
                pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                if (gotCell is not null && eq(refCell, gotCell)) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row}"
                        + (gotCell is null ? "" : $" [{refCell.EffectiveFontName}->{gotCell.EffectiveFontName}]"));
            }
        return Classify(d, cap, matched, total, samples, "styled cells");
    }

    // Number-format comparison. Unlike the other style dimensions, only cells that actually carry a
    // NON-default ("General") number format hold any number-format information. A formatted-but-empty
    // (style-only) cell whose format is General has nothing to lose: if a format drops it entirely (e.g.
    // SpreadsheetML cannot carry its font/fill, so the empty cell is not emitted), the reloaded sheet's
    // implied format for that position is still General — an exact match, not a loss. Asserting such
    // cells would mis-score an EXPECTED styling drop (a None-cap dimension) as a NumberFormat BUG. So we
    // assert a ref cell only when its canonical format is non-General; a missing got cell then implies
    // General and is a genuine loss only for a non-General ref.
    private static DimensionResult CompareNumberFormats(Dim d, Cap cap, List<SheetPair> pairs)
    {
        if (cap == Cap.None)
        {
            bool anyChange = false;
            foreach (var pair in pairs)
                foreach (var ((row, col), refCell) in pair.Ref.Cells)
                {
                    if (Canonical(refCell.Style.NumberFormat) == "General") continue;
                    pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                    var gotFmt = gotCell is null ? "General" : Canonical(gotCell.Style.NumberFormat);
                    if (gotFmt != Canonical(refCell.Style.NumberFormat)) { anyChange = true; break; }
                }
            return MakeNoneResult(d, cap, anyChange);
        }

        int total = 0, matched = 0;
        var samples = new List<string>();
        foreach (var pair in pairs)
        {
            foreach (var ((row, col), refCell) in pair.Ref.Cells)
            {
                var refFmt = Canonical(refCell.Style.NumberFormat);
                if (refFmt == "General") continue; // no number-format information to preserve
                total++;
                pair.Got.Cells.TryGetValue((row, col), out var gotCell);
                // A dropped cell implies the default General format at that position.
                var gotFmt = gotCell is null ? "General" : Canonical(gotCell.Style.NumberFormat);
                if (string.Equals(refFmt, gotFmt, StringComparison.Ordinal)) matched++;
                else if (samples.Count < 6)
                    samples.Add($"{pair.Ref.Name}!{FidelityCompare.ColToLetter(col)}{row} [{refFmt} -> {gotFmt}]");
            }
        }
        return Classify(d, cap, matched, total, samples, "number-format cells");
    }

    private static bool FillsEqual(CellStyle a, CellStyle b) =>
        Nullable.Equals(a.FillColor, b.FillColor) && a.FillPatternStyle == b.FillPatternStyle &&
        Nullable.Equals(a.FillPatternColor, b.FillPatternColor);

    // Lossy (HTML): a fill is a flat background-color, so only the RESOLVED fill color is carried (theme
    // refs arrive as concrete RGB, any pattern collapses to a solid swatch). Compare presence + RGB.
    private static bool FillsEqualLossy(WorkbookSnapshot.CellEntry a, WorkbookSnapshot.CellEntry b) =>
        Nullable.Equals(a.ResolvedFillColor, b.ResolvedFillColor);

    private static bool BordersEqual(CellStyle a, CellStyle b) =>
        a.BorderTop == b.BorderTop && a.BorderRight == b.BorderRight &&
        a.BorderBottom == b.BorderBottom && a.BorderLeft == b.BorderLeft;

    // Lossy (HTML): each edge round-trips through the writer's BorderStyle -> (width,line) CSS quantization.
    // Compare each edge by that CSS bucket + color, so a model style that maps to the same CSS as its
    // reloaded form is a match (the nearest-CSS-equivalent tolerance the html profile documents).
    private static bool BordersEqualLossy(CellStyle a, CellStyle b) =>
        BorderEdgeEqualLossy(a.BorderTop, b.BorderTop) && BorderEdgeEqualLossy(a.BorderRight, b.BorderRight) &&
        BorderEdgeEqualLossy(a.BorderBottom, b.BorderBottom) && BorderEdgeEqualLossy(a.BorderLeft, b.BorderLeft);

    private static bool BorderEdgeEqualLossy(CellBorder a, CellBorder b)
    {
        var (wa, la) = CssBorderBucket(a.Style);
        var (wb, lb) = CssBorderBucket(b.Style);
        if (la is null && lb is null) return true;          // both "no border" once quantized
        if (la is null || lb is null) return false;
        return wa == wb && la == lb && a.Color == b.Color;
    }

    // Mirror of HtmlTableWriter.AppendBorder's BorderStyle -> (width-px, line) mapping. None -> (0,null).
    private static (int Width, string? Line) CssBorderBucket(BorderStyle style) => style switch
    {
        BorderStyle.None => (0, null),
        BorderStyle.Thin => (1, "solid"),
        BorderStyle.Medium => (2, "solid"),
        BorderStyle.Thick => (3, "solid"),
        BorderStyle.Dashed => (1, "dashed"),
        BorderStyle.Dotted => (1, "dotted"),
        BorderStyle.Double => (3, "double"),
        _ => (1, "solid"),
    };

    private static bool AlignmentEqual(CellStyle a, CellStyle b) =>
        a.HorizontalAlignment == b.HorizontalAlignment && a.VerticalAlignment == b.VerticalAlignment &&
        a.WrapText == b.WrapText && a.TextRotation == b.TextRotation && a.IndentLevel == b.IndentLevel;

    // Lossy (HTML): only horizontal text-align is carried, and only the {Left,Center,Right,Justify} subset
    // (General/Distributed are not emitted and reload as General). Compare by that CSS bucket; vertical
    // alignment / wrap / rotation / indent are not representable and are tolerated approximations.
    private static bool AlignmentEqualLossy(CellStyle a, CellStyle b) =>
        CssAlignBucket(a.HorizontalAlignment) == CssAlignBucket(b.HorizontalAlignment);

    private static string? CssAlignBucket(HorizontalAlignment h) => h switch
    {
        HorizontalAlignment.Left => "left",
        HorizontalAlignment.Center => "center",
        HorizontalAlignment.Right => "right",
        HorizontalAlignment.Justify => "justify",
        _ => null, // General / Distributed -> not emitted
    };

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

        var kind = refVal == gotVal
            ? ResultKind.Ok
            : cap == Cap.Lossy && gotVal < refVal
                ? ResultKind.ExpectedLoss
                : ResultKind.Bug;
        return new DimensionResult
        {
            Dimension = d,
            ChainCap = cap,
            Kind = kind,
            Detail = detail,
            Matched = kind == ResultKind.Ok ? 1 : 0,
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
