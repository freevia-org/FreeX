using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FreeX.Core.Model;

namespace FreeX.FormatFidelity;

/// <summary>
/// A flattened, comparable snapshot of a workbook (§3c). Extraction goes strictly through the model
/// APIs named in the spec: <c>Sheet.GetOccupiedCellMap()</c>, <c>Cell.Value/HasFormula/FormulaText/
/// StyleId</c>, <c>Workbook.GetStyle(StyleId)</c>, <c>Sheet.MergedRegions/DefaultColumnWidth/
/// DefaultRowHeight</c>, <c>Workbook.NamedRanges</c>, <c>Sheet.GetStyleOnlyEntries()</c>.
///
/// Cells are keyed by <c>(sheetName, row, col)</c> rather than sheet id, because formats that drop
/// multi-sheet support (csv/txt) reload into a single freshly-named "Sheet1": for those chains the
/// chain cap collapses sheet structure to None and the per-cell comparison falls back to ordering by
/// position within the single surviving sheet (handled by the comparer, not the snapshot).
/// </summary>
internal sealed class WorkbookSnapshot
{
    public sealed record CellEntry(
        ScalarValue Value,
        bool HasFormula,
        string? FormulaText,
        CellStyle Style,
        string EffectiveFontName);

    /// <summary>Per-sheet, ordered cell entries keyed by (row,col). Sheet order preserved.</summary>
    public List<SheetSnapshot> Sheets { get; } = new();

    public sealed class SheetSnapshot
    {
        public required string Name { get; init; }
        public Dictionary<(uint Row, uint Col), CellEntry> Cells { get; } = new();
        public List<((uint, uint) Start, (uint, uint) End)> MergedRanges { get; } = new();
        public double DefaultColumnWidth { get; set; }
        public double DefaultRowHeight { get; set; }
        public Dictionary<uint, double> ColumnWidths { get; } = new();
        public Dictionary<uint, double> RowHeights { get; } = new();
        public uint FrozenRows { get; set; }
        public uint FrozenCols { get; set; }
        public int HyperlinkCount { get; set; }
        public int CommentCount { get; set; }
        public int DataValidationCount { get; set; }
        public int ConditionalFormatCount { get; set; }
        public int ChartCount { get; set; }
        public int ImageCount { get; set; }
    }

    public Dictionary<string, string> NamedRanges { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasVba { get; set; }

    public static WorkbookSnapshot Capture(Workbook wb)
    {
        var snap = new WorkbookSnapshot
        {
            HasVba = wb.HasVbaProjectPackage
        };

        foreach (var sheet in wb.Sheets)
        {
            var ss = new SheetSnapshot { Name = sheet.Name };

            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                var style = wb.GetStyle(cell.StyleId);
                ss.Cells[(row, col)] = new CellEntry(
                    cell.Value,
                    cell.HasFormula,
                    cell.FormulaText,
                    style,
                    style.ResolveEffectiveFontName(wb.Theme));
            }

            // Style-only cells (formatted-but-empty) carry styling we must still compare for Full-cap
            // style chains; merge them in (value = blank) if not already present.
            foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
            {
                if (!ss.Cells.ContainsKey(key))
                {
                    var soStyle = wb.GetStyle(styleId);
                    ss.Cells[key] = new CellEntry(
                        BlankValue.Instance,
                        HasFormula: false,
                        FormulaText: null,
                        soStyle,
                        soStyle.ResolveEffectiveFontName(wb.Theme));
                }
            }

            foreach (var region in sheet.MergedRegions)
            {
                ss.MergedRanges.Add((
                    (region.Start.Row, region.Start.Col),
                    (region.End.Row, region.End.Col)));
            }

            ss.DefaultColumnWidth = sheet.DefaultColumnWidth;
            ss.DefaultRowHeight = sheet.DefaultRowHeight;
            foreach (var (k, v) in sheet.ColumnWidths) ss.ColumnWidths[k] = v;
            foreach (var (k, v) in sheet.RowHeights) ss.RowHeights[k] = v;
            ss.FrozenRows = sheet.FrozenRows;
            ss.FrozenCols = sheet.FrozenCols;
            ss.HyperlinkCount = sheet.Hyperlinks.Count;
            ss.CommentCount = sheet.Comments.Count;
            ss.DataValidationCount = sheet.DataValidations.Count();
            ss.ConditionalFormatCount = sheet.ConditionalFormats.Count;
            ss.ChartCount = sheet.Charts.Count;
            ss.ImageCount = CountImages(sheet);

            snap.Sheets.Add(ss);
        }

        foreach (var (name, range) in wb.NamedRanges)
            snap.NamedRanges[name] = RangeKey(range);

        return snap;
    }

    private static int CountImages(Sheet sheet) => sheet.Pictures.Count;

    private static string RangeKey(GridRange r) =>
        $"{r.Start.Row},{r.Start.Col}:{r.End.Row},{r.End.Col}";
}
