using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class PivotTableRefreshService
{
    private static void ApplyPivotTableStyle(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        bool preserveExistingVisualStyles = false,
        string? styleNameOverride = null)
    {
        var materialized = GetMaterializedOutputRange(sheet, pivotTable);
        var palette = PivotStylePaletteResolver.Resolve(styleNameOverride ?? pivotTable.StyleName, workbook.Theme);
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
        var compactGroupHeaderStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = palette.HeaderFont,
            FillColor = palette.CompactGroupHeaderFill ?? palette.SubtotalFill,
            BorderTop = new CellBorder(BorderStyle.Thin, palette.Border),
            BorderBottom = new CellBorder(BorderStyle.Thin, palette.Border)
        });
        var outlineGroupHeaderStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.Black,
            FillColor = palette.CompactGroupHeaderFill ?? palette.SubtotalFill,
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
        var grandTotalColumnStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = palette.GrandTotalFill is not null,
            FillColor = palette.GrandTotalFill,
            FontColor = palette.GrandTotalFont,
            BorderTop = palette.GrandTotalFill is not null ? new CellBorder(BorderStyle.Thin, palette.Border) : default,
            BorderBottom = palette.GrandTotalFill is not null ? new CellBorder(BorderStyle.Thin, palette.Border) : default
        });
        var stripeStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = palette.StripeFill,
            BorderTop = CreatePivotBodyBorder(palette),
            BorderRight = CreatePivotBodyBorder(palette),
            BorderBottom = CreatePivotBodyBorder(palette),
            BorderLeft = CreatePivotBodyBorder(palette)
        });
        var loadedTabularOuterRowLabelStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = palette.StripeFill,
            BorderTop = CreatePivotBodyBorder(palette),
            BorderRight = CreatePivotBodyBorder(palette),
            BorderBottom = CreatePivotBodyBorder(palette),
            BorderLeft = CreatePivotBodyBorder(palette)
        });
        var materializeLoadedBodySurface =
            preserveExistingVisualStyles &&
            HasLoadedNativePivotLocation(pivotTable) &&
            palette.BodyFill is null;
        var materializeBodySurface =
            palette.BodyFill is not null ||
            palette.BodyBorder is not null ||
            materializeLoadedBodySurface;
        StyleId? bodyStyle = materializeBodySurface
            ? workbook.RegisterStyle(new CellStyle
            {
                FillColor = palette.BodyFill ?? CellColor.White,
                BorderTop = CreatePivotBodyBorder(palette),
                BorderRight = CreatePivotBodyBorder(palette),
                BorderBottom = CreatePivotBodyBorder(palette),
                BorderLeft = CreatePivotBodyBorder(palette)
            })
            : null;

        var bodyStart = HasLoadedNativePivotLocation(pivotTable)
            ? pivotTable.TargetRange.Start
            : GetPivotBodyStart(pivotTable);
        var pageFieldRows = GetPageFieldRowSpan(pivotTable);
        var pageFieldEndRow = pageFieldRows == 0 ? 0 : pivotTable.TargetRange.Start.Row + pageFieldRows - 1;
        var headerEndRow = GetStyledPivotHeaderEndRow(sheet, pivotTable, bodyStart);
        var subtotalRows = new HashSet<uint>();
        var grandTotalRows = new HashSet<uint>();
        var grandTotalColumns = new HashSet<uint>();
        var compactGroupHeaderRows = FindCompactGroupHeaderRows(workbook, sheet, pivotTable, materialized, headerEndRow);
        var outlineGroupHeaderRows = FindOutlineGroupHeaderRows(sheet, pivotTable, materialized, headerEndRow);
        var groupHeaderRows = compactGroupHeaderRows.Concat(outlineGroupHeaderRows).ToHashSet();
        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            if (sheet.GetCell(row, col)?.Value is not TextValue text)
                continue;
            if (IsPivotGrandTotalCaption(pivotTable, text.Value))
            {
                if (row <= headerEndRow)
                    grandTotalColumns.Add(col);
                else
                    grandTotalRows.Add(row);
            }
            else if (IsPivotSubtotalCaption(text.Value))
                subtotalRows.Add(row);
        }

        MaterializePivotTotalStyleFootprintCells(
            sheet,
            pivotTable,
            materialized,
            bodyStart,
            headerEndRow,
            subtotalRows,
            grandTotalRows,
            grandTotalColumns,
            groupHeaderRows);
        MaterializePivotBandStyleFootprintCells(
            sheet,
            pivotTable,
            materialized,
            bodyStart,
            headerEndRow,
            materializeBodySurface);

        var firstDataRow = GetStyledPivotFirstDataRow(pivotTable, bodyStart, headerEndRow);
        var firstDataColumn = GetStyledPivotFirstDataColumn(pivotTable, materialized);

        for (var row = materialized.Start.Row; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            var cell = sheet.GetCell(row, col);
            if (cell is null)
                continue;

            if (row < bodyStart.Row)
            {
                if (pageFieldRows > 0 && row <= pageFieldEndRow)
                    ApplyPivotVisualStyle(workbook, cell, headerStyle, preserveExistingVisualStyles);
                continue;
            }

            if (row <= headerEndRow)
            {
                if (ShouldApplyPivotHeaderStyle(pivotTable, col))
                    ApplyPivotVisualStyle(workbook, cell, headerStyle, preserveExistingVisualStyles);
                continue;
            }

            if (grandTotalRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, grandTotalStyle, preserveExistingVisualStyles);
                continue;
            }

            if (grandTotalColumns.Contains(col))
            {
                ApplyPivotVisualStyle(workbook, cell, grandTotalColumnStyle, preserveExistingVisualStyles);
                continue;
            }

            if (compactGroupHeaderRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, compactGroupHeaderStyle, preserveExistingVisualStyles);
                continue;
            }

            if (outlineGroupHeaderRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, outlineGroupHeaderStyle, preserveExistingVisualStyles);
                continue;
            }

            if (subtotalRows.Contains(row))
            {
                ApplyPivotVisualStyle(workbook, cell, subtotalStyle, preserveExistingVisualStyles);
                continue;
            }

            if (ShouldApplyLoadedTabularOuterRowLabelStyle(sheet, pivotTable, materialized, firstDataRow, row, col))
            {
                ApplyPivotVisualStyle(workbook, cell, loadedTabularOuterRowLabelStyle, preserveExistingVisualStyles);
                continue;
            }

            var bodyColIndex = col - firstDataColumn;
            var isRowStripe =
                pivotTable.ShowRowStripes &&
                (palette.BodyFill is not null || col >= firstDataColumn) &&
                row >= firstDataRow &&
                GetPivotBodyBandIndex(row, firstDataRow, groupHeaderRows, subtotalRows, grandTotalRows) % 2 == 0;
            var isColumnStripe =
                pivotTable.ShowColumnStripes &&
                col >= firstDataColumn &&
                IsPivotColumnStripeColumn(pivotTable, bodyColIndex);

            if (isRowStripe || isColumnStripe)
                ApplyPivotVisualStyle(workbook, cell, stripeStyle, preserveExistingVisualStyles);
            else if (bodyStyle is not null)
                ApplyPivotVisualStyle(workbook, cell, bodyStyle.Value, preserveExistingVisualStyles);
        }

        ApplyCompactRowLabelIndent(workbook, sheet, pivotTable, materialized, headerEndRow, subtotalRows, grandTotalRows);
    }

    private static CellBorder CreatePivotBodyBorder(PivotStylePalette palette) =>
        palette.BodyBorder is { } color
            ? new CellBorder(BorderStyle.Thin, color)
            : default;

    private static bool ShouldApplyLoadedTabularOuterRowLabelStyle(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint firstDataRow,
        uint row,
        uint col)
    {
        if (!HasLoadedNativePivotLocation(pivotTable) ||
            pivotTable.ReportLayout != PivotReportLayout.Tabular ||
            pivotTable.RowFields.Count <= 1 ||
            col != materialized.Start.Col ||
            row < firstDataRow)
        {
            return false;
        }

        return sheet.GetCell(row, col)?.Value is TextValue text &&
               !string.IsNullOrWhiteSpace(text.Value) &&
               !IsPivotGrandTotalCaption(pivotTable, text.Value) &&
               !IsPivotSubtotalCaption(text.Value);
    }

    private static IReadOnlySet<uint> FindCompactGroupHeaderRows(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint headerEndRow)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact ||
            pivotTable.RowFields.Count <= 1)
        {
            return new HashSet<uint>();
        }

        var rows = new HashSet<uint>();
        var labelCol = materialized.Start.Col;
        for (var row = headerEndRow + 1; row < materialized.End.Row; row++)
        {
            if (sheet.GetCell(row, labelCol)?.Value is not TextValue currentText ||
                string.IsNullOrWhiteSpace(currentText.Value) ||
                IsPivotGrandTotalCaption(pivotTable, currentText.Value))
            {
                continue;
            }

            var currentIndent = GetLoadedRowLabelIndent(workbook, sheet, row, labelCol);
            var nextIndent = FindNextLoadedRowLabelIndent(workbook, sheet, pivotTable, materialized, row + 1, labelCol);
            if (nextIndent > currentIndent)
                rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlySet<uint> FindOutlineGroupHeaderRows(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint headerEndRow)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Outline ||
            pivotTable.RowFields.Count <= 1)
        {
            return new HashSet<uint>();
        }

        var rows = new HashSet<uint>();
        var labelCol = materialized.Start.Col;
        var lastRowFieldCol = Math.Min(
            materialized.End.Col,
            labelCol + checked((uint)pivotTable.RowFields.Count) - 1);
        for (var row = headerEndRow + 1; row < materialized.End.Row; row++)
        {
            if (sheet.GetCell(row, labelCol)?.Value is not TextValue currentText ||
                string.IsNullOrWhiteSpace(currentText.Value) ||
                IsPivotGrandTotalCaption(pivotTable, currentText.Value) ||
                IsPivotSubtotalCaption(currentText.Value))
            {
                continue;
            }

            if (HasOutlineContinuationChild(sheet, pivotTable, materialized, row + 1, labelCol, lastRowFieldCol))
                rows.Add(row);
        }

        return rows;
    }

    private static bool HasOutlineContinuationChild(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint startRow,
        uint labelCol,
        uint lastRowFieldCol)
    {
        for (var row = startRow; row <= materialized.End.Row; row++)
        {
            if (sheet.GetCell(row, labelCol)?.Value is TextValue firstFieldText &&
                !string.IsNullOrWhiteSpace(firstFieldText.Value))
            {
                return false;
            }

            for (var col = labelCol + 1; col <= lastRowFieldCol; col++)
            {
                if (sheet.GetCell(row, col)?.Value is TextValue childText &&
                    !string.IsNullOrWhiteSpace(childText.Value) &&
                    !IsPivotGrandTotalCaption(pivotTable, childText.Value) &&
                    !IsPivotSubtotalCaption(childText.Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int FindNextLoadedRowLabelIndent(
        Workbook workbook,
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        uint startRow,
        uint labelCol)
    {
        for (var row = startRow; row <= materialized.End.Row; row++)
        {
            if (sheet.GetCell(row, labelCol)?.Value is not TextValue text ||
                string.IsNullOrWhiteSpace(text.Value) ||
                IsPivotGrandTotalCaption(pivotTable, text.Value))
            {
                continue;
            }

            return GetLoadedRowLabelIndent(workbook, sheet, row, labelCol);
        }

        return -1;
    }

    private static int GetLoadedRowLabelIndent(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var cell = sheet.GetCell(row, col);
        return cell is null
            ? 0
            : Math.Clamp(workbook.GetStyle(cell.StyleId).IndentLevel, 0, 15);
    }

    private static uint GetPivotBodyBandIndex(
        uint row,
        uint firstDataRow,
        IReadOnlySet<uint> compactGroupHeaderRows,
        IReadOnlySet<uint> subtotalRows,
        IReadOnlySet<uint> grandTotalRows)
    {
        var index = 0u;
        for (var current = firstDataRow; current < row; current++)
        {
            if (compactGroupHeaderRows.Contains(current) ||
                subtotalRows.Contains(current) ||
                grandTotalRows.Contains(current))
            {
                continue;
            }

            index++;
        }

        return index;
    }

    private static void MaterializePivotTotalStyleFootprintCells(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        CellAddress bodyStart,
        uint headerEndRow,
        IReadOnlySet<uint> subtotalRows,
        IReadOnlySet<uint> grandTotalRows,
        IReadOnlySet<uint> grandTotalColumns,
        IReadOnlySet<uint> groupHeaderRows)
    {
        foreach (var row in groupHeaderRows)
            MaterializePivotBlankRowCells(sheet, pivotTable, row, materialized.Start.Col, materialized.End.Col);

        foreach (var row in subtotalRows.Concat(grandTotalRows))
            MaterializePivotBlankRowCells(sheet, pivotTable, row, materialized.Start.Col, materialized.End.Col);

        foreach (var col in grandTotalColumns)
            MaterializePivotBlankColumnCells(sheet, pivotTable, bodyStart.Row, headerEndRow, col);
    }

    private static void MaterializePivotBandStyleFootprintCells(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange materialized,
        CellAddress bodyStart,
        uint headerEndRow,
        bool materializeBodySurface)
    {
        for (var row = bodyStart.Row; row <= headerEndRow; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            if (IsDeferredCompactRowLabelHeaderCell(pivotTable, bodyStart, headerEndRow, row, col))
                continue;

            MaterializePivotBlankCell(sheet, pivotTable, row, col);
        }

        if (!pivotTable.ShowRowStripes && !pivotTable.ShowColumnStripes && !materializeBodySurface)
            return;

        var firstDataRow = GetStyledPivotFirstDataRow(pivotTable, bodyStart, headerEndRow);
        var firstDataColumn = GetStyledPivotFirstDataColumn(pivotTable, materialized);
        for (var row = firstDataRow; row <= materialized.End.Row; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            if (materializeBodySurface ||
                pivotTable.ShowColumnStripes && col >= firstDataColumn ||
                pivotTable.ShowRowStripes)
            {
                MaterializePivotBlankCell(sheet, pivotTable, row, col);
            }
        }
    }

    private static void MaterializePivotBlankRowCells(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint startCol,
        uint endCol)
    {
        for (var col = startCol; col <= endCol; col++)
            MaterializePivotBlankCell(sheet, pivotTable, row, col);
    }

    private static bool IsDeferredCompactRowLabelHeaderCell(
        PivotTableModel pivotTable,
        CellAddress bodyStart,
        uint headerEndRow,
        uint row,
        uint col) =>
        pivotTable.MergeAndCenterLabels &&
        pivotTable.ReportLayout == PivotReportLayout.Compact &&
        pivotTable.RowFields.Count > 1 &&
        pivotTable.ColumnFields.Count > 1 &&
        col == bodyStart.Col &&
        row > bodyStart.Row &&
        row <= headerEndRow;

    private static void MaterializePivotBlankColumnCells(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint startRow,
        uint endRow,
        uint col)
    {
        for (var row = startRow; row <= endRow; row++)
            MaterializePivotBlankCell(sheet, pivotTable, row, col);
    }

    private static void MaterializePivotBlankCell(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint col)
    {
        if (sheet.GetCell(row, col) is not null)
            return;

        SetPivotCell(sheet, new CellAddress(sheet.Id, row, col), BlankValue.Instance);
    }

    private static void ApplyPivotVisualStyle(
        Workbook workbook,
        Cell cell,
        StyleId visualStyleId,
        bool preserveExistingVisualStyles = false)
    {
        // The modern pivot style (PivotStyleLight16 etc., carried by pivotTableStyleInfo) supplies the
        // header/total fills, bold font and borders. Excel applies it independently of the legacy
        // applyFontFormats / applyPatternFormats / applyBorderFormats autoformat flags, which real-world
        // files routinely persist as "0"; gating on those flags dropped ALL visible pivot styling for
        // pivots loaded from such files (Issue 123). Apply the full visual style while preserving
        // cell-local content formatting that Excel keeps outside the PivotTable style layer.
        var existingStyle = workbook.GetStyle(cell.StyleId);
        var visualStyle = workbook.GetStyle(visualStyleId);
        var style = existingStyle.Clone();

        var applyFont = !preserveExistingVisualStyles || HasPivotFontVisualStyle(visualStyle);
        var applyPattern = !preserveExistingVisualStyles || !HasExistingPatternVisualStyle(existingStyle);
        var applyBorder = !preserveExistingVisualStyles || !HasExistingBorderVisualStyle(existingStyle);
        if (!applyFont && !applyPattern && !applyBorder)
            return;

        if (applyFont)
        {
            var existingFontName = existingStyle.FontName;
            var existingFontSize = existingStyle.FontSize;
            var existingFontScheme = existingStyle.FontScheme;

            ApplyPivotFontStyle(style, visualStyle);
            style.FontName = existingFontName;
            style.FontSize = existingFontSize;
            style.FontScheme = existingFontScheme;
        }

        if (applyPattern)
            ApplyPivotPatternStyle(style, visualStyle);
        if (applyBorder)
            ApplyPivotBorderStyle(style, visualStyle);

        cell.StyleId = workbook.RegisterStyle(style);
    }

    private static bool HasExistingPatternVisualStyle(CellStyle style) =>
        style.FillColor is not null ||
        style.FillThemeColor is not null ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.FillPatternColor is not null ||
        style.FillPatternThemeColor is not null;

    private static bool HasExistingBorderVisualStyle(CellStyle style) =>
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None;

    private static bool HasPivotFontVisualStyle(CellStyle style) =>
        style.FontColor != CellColor.Black ||
        style.Bold ||
        style.Italic ||
        style.Underline ||
        style.DoubleUnderline ||
        style.Strikethrough ||
        style.Superscript ||
        style.Subscript;

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
        target.FontThemeColor = null;
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

    private static uint GetStyledPivotHeaderEndRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        CellAddress bodyStart)
    {
        if (ShouldUseLoadedNativeFirstDataRowForHeaders(pivotTable))
        {
            return bodyStart.Row + checked((uint)pivotTable.FirstDataRow) - 1;
        }

        var headerRowCount = Math.Max(1, pivotTable.ColumnFields.Count);
        if (HasLoadedNativeMatrixHeaderPreamble(sheet, pivotTable, bodyStart))
            headerRowCount++;

        return bodyStart.Row + (uint)headerRowCount - 1;
    }

    private static bool ShouldUseLoadedNativeFirstDataRowForHeaders(PivotTableModel pivotTable) =>
        HasLoadedNativePivotLocation(pivotTable) &&
        pivotTable.FirstDataRow > 0 &&
        (!HasLoadedNativePageFieldCaptionLayout(pivotTable) ||
         pivotTable.PageFields.Count > 1 ||
         pivotTable.PageWrap > 0);

    private static uint GetStyledPivotFirstDataRow(
        PivotTableModel pivotTable,
        CellAddress bodyStart,
        uint headerEndRow)
    {
        if (HasLoadedNativePivotLocation(pivotTable) && pivotTable.FirstDataRow > 0)
            return bodyStart.Row + checked((uint)pivotTable.FirstDataRow);

        return headerEndRow + 1;
    }

    private static uint GetStyledPivotFirstDataColumn(
        PivotTableModel pivotTable,
        GridRange materialized)
    {
        if (HasLoadedNativePivotLocation(pivotTable) && pivotTable.FirstDataColumn > 0)
            return Math.Min(
                materialized.End.Col,
                pivotTable.TargetRange.Start.Col + checked((uint)pivotTable.FirstDataColumn));

        return materialized.Start.Col;
    }

    private static bool IsPivotColumnStripeColumn(PivotTableModel pivotTable, uint bodyColIndex) =>
        HasLoadedNativePivotLocation(pivotTable)
            ? bodyColIndex % 2 == 0
            : bodyColIndex % 2 == 1;

    private static bool HasLoadedNativePivotLocation(PivotTableModel pivotTable) =>
        pivotTable.LastRenderedRange is not null;

    private static bool HasLoadedNativeMatrixHeaderPreamble(
        Sheet sheet,
        PivotTableModel pivotTable,
        CellAddress bodyStart)
    {
        if (pivotTable.RowFields.Count == 0 || pivotTable.ColumnFields.Count == 0)
            return false;

        if (HasLoadedNativePageFieldCaptionLayout(pivotTable))
            return false;

        if (sheet.GetCell(bodyStart.Row, bodyStart.Col)?.Value is not TextValue firstCell ||
            !pivotTable.DataFields.Any(field => string.Equals(field.Name, firstCell.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var nextHeaderRow = bodyStart.Row + 1;
        if (sheet.GetCell(nextHeaderRow, bodyStart.Col)?.Value is not TextValue rowHeader)
            return false;

        return string.Equals(rowHeader.Value, "Row Labels", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLoadedNativePageFieldCaptionLayout(PivotTableModel pivotTable) =>
        HasLoadedNativePivotLocation(pivotTable) &&
        pivotTable.ShowFieldHeaders &&
        pivotTable.PageFields.Count > 0;

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

        // For multi-row-field compact layout, use per-row indent levels stored during WriteRowPivot.
        // Each row's level was recorded as k * indentStep (already pre-multiplied), so we apply directly.
        var perRowIndents = CurrentRenderFootprint.Value?.CompactRowIndentLevels;
        if (perRowIndents is not null)
        {
            for (var row = headerEndRow + 1; row <= materialized.End.Row; row++)
            {
                if (grandTotalRows.Contains(row))
                    continue;
                if (!perRowIndents.TryGetValue(row, out var indent) || indent <= 0)
                    continue;

                var cell = sheet.GetCell(row, materialized.Start.Col);
                if (cell is null)
                    continue;

                var style = workbook.GetStyle(cell.StyleId).Clone();
                style.IndentLevel = Math.Clamp(indent, 0, 15);
                cell.StyleId = workbook.RegisterStyle(style);
            }
            return;
        }

        // Legacy flat-indent path (N==1 compact is excluded above by the RowFields.Count <= 1 guard,
        // so this path is only reached when CompactRowIndentLevels was not populated).
        var flatIndent = Math.Clamp(pivotTable.CompactRowLabelIndent, 0, 15);
        for (var row = headerEndRow + 1; row <= materialized.End.Row; row++)
        {
            if (subtotalRows.Contains(row) || grandTotalRows.Contains(row))
                continue;

            var cell = sheet.GetCell(row, materialized.Start.Col);
            if (cell is null)
                continue;

            var style = workbook.GetStyle(cell.StyleId).Clone();
            style.IndentLevel = flatIndent;
            cell.StyleId = workbook.RegisterStyle(style);
        }
    }

    private static bool IsPivotSubtotalCaption(string value) =>
        value.EndsWith(" Total", StringComparison.OrdinalIgnoreCase);
}
