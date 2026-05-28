using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void ApplyPivotTableStyle(Workbook workbook, Sheet sheet, PivotTableModel pivotTable)
    {
        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var palette = PivotStylePaletteResolver.Resolve(pivotTable.StyleName, workbook.Theme);
        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = palette.HeaderFont,
            FillColor = palette.HeaderFill,
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var subtotalStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = palette.SubtotalFill,
            BorderTop = new CellBorder(BorderStyle.Thin, palette.Border),
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var grandTotalStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = palette.GrandTotalFill,
            FontColor = palette.GrandTotalFont,
            BorderTop = new CellBorder(BorderStyle.Thin, palette.Border),
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var stripeStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = palette.StripeFill
        });

        var bodyStart = GetPivotBodyStart(pivotTable);
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        var pageFieldEndRow = pageFieldRows == 0 ? 0 : pivotTable.TargetRange.Start.Row + pageFieldRows - 1;
        var headerEndRow = bodyStart.Row + (uint)Math.Max(1, pivotTable.ColumnFields.Count) - 1;
        var subtotalRows = new HashSet<uint>();
        var grandTotalRows = new HashSet<uint>();
        var grandTotalColumns = new HashSet<uint>();
        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            if (sheet.GetCell(row, col)?.Value is not TextValue text)
                continue;
            if (IsPivotGrandTotalCaption(text.Value))
            {
                if (row <= headerEndRow)
                    grandTotalColumns.Add(col);
                else
                    grandTotalRows.Add(row);
            }
            else if (IsPivotSubtotalCaption(text.Value))
                subtotalRows.Add(row);
        }

        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            var cell = sheet.GetCell(row, col);
            if (cell is null)
                continue;

            if (row < bodyStart.Row)
            {
                if (pageFieldRows > 0 && row <= pageFieldEndRow)
                    ApplyPivotVisualStyle(workbook, cell, headerStyle, pivotTable);
                continue;
            }

            if (row <= headerEndRow)
            {
                if (ShouldApplyPivotHeaderStyle(pivotTable, col))
                    ApplyPivotVisualStyle(workbook, cell, headerStyle, pivotTable);
                continue;
            }

            if (grandTotalRows.Contains(row) || grandTotalColumns.Contains(col))
            {
                ApplyPivotVisualStyle(workbook, cell, grandTotalStyle, pivotTable);
                continue;
            }

            if (subtotalRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, subtotalStyle, pivotTable);
                continue;
            }

            var bodyRowIndex = row - headerEndRow - 1;
            var bodyColIndex = col - materialized.Start.Col;
            if (pivotTable.ShowRowStripes && bodyRowIndex % 2 == 0)
                ApplyPivotVisualStyle(workbook, cell, stripeStyle, pivotTable);
            if (pivotTable.ShowColumnStripes && bodyColIndex % 2 == 1)
                ApplyPivotVisualStyle(workbook, cell, stripeStyle, pivotTable);
        }

        ApplyCompactRowLabelIndent(workbook, sheet, pivotTable, materialized, headerEndRow, subtotalRows, grandTotalRows);
    }

    private static void ApplyPivotVisualStyle(Workbook workbook, Cell cell, StyleId visualStyleId, PivotTableModel pivotTable)
    {
        var numberFormat = workbook.GetStyle(cell.StyleId).NumberFormat;
        if (pivotTable.ApplyFontFormats &&
            pivotTable.ApplyPatternFormats &&
            pivotTable.ApplyBorderFormats &&
            numberFormat == CellStyle.Default.NumberFormat)
        {
            cell.StyleId = visualStyleId;
            return;
        }

        var visualStyle = workbook.GetStyle(visualStyleId);
        var style = CellStyle.Default.Clone();
        style.NumberFormat = numberFormat;

        if (pivotTable.ApplyFontFormats)
            ApplyPivotFontStyle(style, visualStyle);
        if (pivotTable.ApplyPatternFormats)
            ApplyPivotPatternStyle(style, visualStyle);
        if (pivotTable.ApplyBorderFormats)
            ApplyPivotBorderStyle(style, visualStyle);

        cell.StyleId = workbook.RegisterStyle(style);
    }

    private static void ApplyPivotFontStyle(CellStyle target, CellStyle source)
    {
        target.FontName = source.FontName;
        target.FontSize = source.FontSize;
        target.Bold = source.Bold;
        target.Italic = source.Italic;
        target.Underline = source.Underline;
        target.Strikethrough = source.Strikethrough;
        target.Superscript = source.Superscript;
        target.Subscript = source.Subscript;
        target.FontColor = source.FontColor;
        target.DoubleUnderline = source.DoubleUnderline;
    }

    private static void ApplyPivotPatternStyle(CellStyle target, CellStyle source)
    {
        target.FillColor = source.FillColor;
        target.FillPatternStyle = source.FillPatternStyle;
        target.FillPatternColor = source.FillPatternColor;
    }

    private static void ApplyPivotBorderStyle(CellStyle target, CellStyle source)
    {
        target.BorderTop = source.BorderTop;
        target.BorderRight = source.BorderRight;
        target.BorderBottom = source.BorderBottom;
        target.BorderLeft = source.BorderLeft;
    }

    private static bool ShouldApplyPivotHeaderStyle(PivotTableModel pivotTable, uint col)
    {
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)RowFieldOutputColumnCount(pivotTable);
        return col < firstValueColumn
            ? pivotTable.ShowRowHeaders
            : pivotTable.ShowColumnHeaders;
    }

    private static void ApplyCompactRowLabelIndent(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint headerEndRow,
        IReadOnlySet<uint> subtotalRows,
        IReadOnlySet<uint> grandTotalRows)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact ||
            pivotTable.RowFields.Count <= 1 ||
            pivotTable.CompactRowLabelIndent <= 0)
        {
            return;
        }

        var indent = Math.Clamp(pivotTable.CompactRowLabelIndent, 0, 15);
        for (var row = headerEndRow + 1; row <= materialized.End.Row; row++)
        {
            if (subtotalRows.Contains(row) || grandTotalRows.Contains(row))
                continue;

            var cell = sheet.GetCell(row, materialized.Start.Col);
            if (cell is null)
                continue;

            var style = workbook.GetStyle(cell.StyleId).Clone();
            style.IndentLevel = indent;
            cell.StyleId = workbook.RegisterStyle(style);
        }
    }

    private static bool IsPivotGrandTotalCaption(string value) =>
        string.Equals(value, "Grand Total", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Grand Total ", StringComparison.OrdinalIgnoreCase);

    private static bool IsPivotSubtotalCaption(string value) =>
        value.EndsWith(" Total", StringComparison.OrdinalIgnoreCase);
}
