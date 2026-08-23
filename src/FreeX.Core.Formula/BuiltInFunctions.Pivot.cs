using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue GetPivotData(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0)
            return ErrorValue.Value;
        if (ctx.CurrentSheet is null || ctx.CurrentWorkbook is null)
            return ErrorValue.Ref;
        if (args[0] is ErrorValue dataFieldError)
            return dataFieldError;
        if (args[1] is ErrorValue pivotRefError)
            return pivotRefError;
        // Real Excel's pivot_table argument accepts "a reference to any cell, range of cells, or
        // range named that is in a PivotTable" -- not just a single cell. FindPivotTableForReference
        // only ever reads the reference's top-left cell (StartRow/StartCol), so a multi-cell range
        // (e.g. a named range spanning the whole pivot) is resolved exactly the same way a 1x1
        // reference is; only reject when the argument isn't a reference at all.
        if (args[1] is not RangeValue pivotReference)
            return ErrorValue.Ref;

        var dataFieldCaption = PivotText(args[0]);
        if (string.IsNullOrWhiteSpace(dataFieldCaption))
            return ErrorValue.Value;

        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 2; index < args.Count; index += 2)
        {
            if (args[index] is ErrorValue fieldError)
                return fieldError;
            if (args[index + 1] is ErrorValue itemError)
                return itemError;
            var fieldName = PivotText(args[index]);
            var itemName = PivotText(args[index + 1]);
            if (string.IsNullOrWhiteSpace(fieldName))
                return ErrorValue.Value;
            if (filters.TryGetValue(fieldName, out var existingItem) &&
                !string.Equals(existingItem, itemName, StringComparison.OrdinalIgnoreCase))
            {
                return ErrorValue.Ref;
            }
            filters[fieldName] = itemName;
        }

        var locatedPivot = FindPivotTableForReference(ctx, pivotReference);
        if (locatedPivot is null)
            return ErrorValue.Ref;
        var (pivotSheet, pivotTable) = locatedPivot.Value;

        var headers = ReadPivotSourceHeaders(ctx.CurrentWorkbook, pivotTable);
        var dataFieldIndex = FindPivotDataFieldIndex(pivotTable, headers, dataFieldCaption);
        if (dataFieldIndex < 0)
            return ErrorValue.Ref;
        if (!GetPivotDataFilterFieldsAreVisible(pivotTable, headers, filters))
            return ErrorValue.Ref;
        if (!PageFieldFiltersMatch(pivotTable, headers, filters))
            return ErrorValue.Ref;

        var materialized = PivotTableTargetRangeResolver.GetOccupiedRange(pivotSheet, pivotTable);
        var headerRows = (uint)Math.Max(1, pivotTable.ColumnFields.Count);
        var firstDataRow = pivotTable.TargetRange.Start.Row + headerRows;
        var outputRow = ResolveGetPivotDataRow(pivotSheet, pivotTable, headers, filters, firstDataRow, materialized.End.Row);
        if (outputRow is null)
        {
            // A pure grand-total request (no field/item pairs at all) can fail to resolve to a
            // displayed cell when Show Row Grand Totals is turned off -- the aggregate still
            // exists in the pivot cache even though no row renders it. Recompute it directly from
            // the source data (or #REF! if that isn't safely resolvable) rather than surfacing a
            // bare #REF! for a value Excel can genuinely answer. See R57-formula-getpivotdata-5-1.
            if (filters.Count == 0 &&
                TryComputeGetPivotDataGrandTotal(ctx, pivotTable, headers, dataFieldIndex, out var rowGrandTotal))
                return rowGrandTotal;
            return ErrorValue.Ref;
        }

        var outputColumn = ResolveGetPivotDataColumn(pivotSheet, pivotTable, headers, filters, dataFieldIndex, materialized.End.Col);
        if (outputColumn is null)
        {
            if (filters.Count == 0 &&
                TryComputeGetPivotDataGrandTotal(ctx, pivotTable, headers, dataFieldIndex, out var columnGrandTotal))
                return columnGrandTotal;
            return ErrorValue.Ref;
        }

        return pivotSheet.GetCell(outputRow.Value, outputColumn.Value)?.Value ?? ErrorValue.Ref;
    }

    /// <summary>
    /// Computes GETPIVOTDATA's true grand-total aggregate directly from the pivot's source data
    /// for a pure grand-total request (no field/item pairs supplied at all), used when no
    /// rendered Grand Total row/column exists to read (Show Row/Column Grand Totals turned off).
    /// Only handles the safely-recomputable case -- a plain summary function with no calculated
    /// field and no "Show Values As" transform -- declining (returning false, so the caller
    /// surfaces #REF!) for anything more complex rather than risk a wrong number. See
    /// R57-formula-getpivotdata-5-1.
    /// </summary>
    private static bool TryComputeGetPivotDataGrandTotal(
        IEvalContext ctx,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        int dataFieldIndex,
        out ScalarValue result)
    {
        result = ErrorValue.Ref;
        if (dataFieldIndex < 0 || dataFieldIndex >= pivotTable.DataFields.Count)
            return false;
        var dataField = pivotTable.DataFields[dataFieldIndex];
        if (!string.IsNullOrWhiteSpace(dataField.CalculatedFieldName) || dataField.ShowValuesAs != PivotShowValuesAs.None)
            return false;
        var subtotalFuncNumber = PivotSummaryFunctionToSubtotalFuncNumber(dataField.SummaryFunction);
        if (subtotalFuncNumber is null)
            return false;
        if (dataField.SourceFieldIndex < 0)
            return false;

        var workbook = ctx.CurrentWorkbook;
        if (workbook is null)
            return false;
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return false;

        var dataCol = pivotTable.SourceRange.Start.Col + (uint)dataField.SourceFieldIndex;
        if (dataCol > pivotTable.SourceRange.End.Col)
            return false;

        // A page/filter field constrains which source rows belong to the pivot's current view
        // even when GETPIVOTDATA supplies no explicit field/item pair for it -- but only when it
        // is narrowed to exactly one selected item; a multi-select page filter has no single-item
        // isolation (mirroring PageFieldFiltersMatch), so its combined multi-item total is
        // genuinely the correct grand total and it is left unconstrained here.
        var pageConstraints = new List<(uint Col, string Expected)>();
        foreach (var pageField in pivotTable.PageFields)
        {
            if (pageField.SourceFieldIndex < 0)
                continue;
            var pageCol = pivotTable.SourceRange.Start.Col + (uint)pageField.SourceFieldIndex;
            if (pageCol > pivotTable.SourceRange.End.Col)
                continue;
            if (!string.IsNullOrWhiteSpace(pageField.SelectedItem))
                pageConstraints.Add((pageCol, pageField.SelectedItem));
        }

        var values = new List<ScalarValue>();
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            var included = true;
            foreach (var (col, expected) in pageConstraints)
            {
                var actual = PivotText(sourceSheet.GetCell(row, col)?.Value);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    included = false;
                    break;
                }
            }

            if (included)
                values.Add(sourceSheet.GetCell(row, dataCol)?.Value ?? BlankValue.Instance);
        }

        var subtotalArgs = new List<ScalarValue> { new NumberValue(subtotalFuncNumber.Value) };
        subtotalArgs.AddRange(values);
        var aggregate = Subtotal(subtotalArgs, ctx);
        if (aggregate is ErrorValue)
            return false;

        result = aggregate;
        return true;
    }

    /// <summary>
    /// Maps a PivotDataFieldModel.SummaryFunction ("sum", "count", "average", ...) to the
    /// equivalent SUBTOTAL function-number code, so the true grand-total aggregate can be
    /// computed by reusing SUBTOTAL's own accumulator (Subtotal(...) in
    /// BuiltInFunctions.Subtotal.cs) rather than duplicating aggregation arithmetic.
    /// </summary>
    private static int? PivotSummaryFunctionToSubtotalFuncNumber(string summaryFunction) =>
        summaryFunction.Trim().ToLowerInvariant() switch
        {
            "sum" => 9,
            "count" => 3,
            "countnums" => 2,
            "average" or "avg" => 1,
            "min" => 5,
            "max" => 4,
            "product" => 6,
            "stddev" or "stddevs" or "stddev.s" => 7,
            "stddevp" or "stddev.p" => 8,
            "var" or "vars" or "var.s" => 10,
            "varp" or "var.p" => 11,
            _ => null
        };

    private static bool GetPivotDataFilterFieldsAreVisible(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        var visibleFields = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .Select(field => PivotHeader(headers, field.SourceFieldIndex))
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filters.Keys.All(visibleFields.Contains);
    }

    private static bool PageFieldFiltersMatch(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters)
    {
        foreach (var pageField in pivotTable.PageFields)
        {
            var header = PivotHeader(headers, pageField.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            if (!string.IsNullOrWhiteSpace(pageField.SelectedItem))
            {
                if (!string.Equals(pageField.SelectedItem, expected, StringComparison.OrdinalIgnoreCase))
                    return false;
                continue;
            }

            if (pageField.SelectedItems is { Count: > 0 } selectedItems)
            {
                // A multi-select Page/Filter field (more than one item currently checked) has no
                // cell that isolates a single item's contribution -- the pivot only ever displays
                // the COMBINED total of every selected item. Only a page field narrowed to
                // exactly one selected item has a genuine per-item value to read; requesting one
                // of several selected items must fail here (the caller then returns #REF!,
                // matching real Excel) instead of silently returning the multi-item combined
                // total as if it were that single item's figure. See
                // R57-formula-getpivotdata-5-3.
                if (selectedItems.Count != 1 || !selectedItems.Contains(expected, StringComparer.OrdinalIgnoreCase))
                    return false;
                continue;
            }
        }

        return true;
    }

    private static (Sheet Sheet, PivotTableModel PivotTable)? FindPivotTableForReference(
        IEvalContext ctx,
        RangeValue reference)
    {
        var row = reference.StartRow;
        var col = reference.StartCol;
        if (!string.IsNullOrWhiteSpace(reference.SheetName))
        {
            var sheet = ctx.CurrentWorkbook?.GetSheet(reference.SheetName);
            if (sheet is null)
                return null;

            var address = new CellAddress(sheet.Id, row, col);
            var pivot = FindPivotTableContaining(sheet, address);
            return pivot is null ? null : (sheet, pivot);
        }

        if (ctx.CurrentSheet is not null)
        {
            var currentAddress = new CellAddress(ctx.CurrentSheet.Id, row, col);
            var currentPivot = FindPivotTableContaining(ctx.CurrentSheet, currentAddress);
            if (currentPivot is not null)
                return (ctx.CurrentSheet, currentPivot);
        }

        if (ctx.CurrentWorkbook is null)
            return null;

        foreach (var sheet in ctx.CurrentWorkbook.Sheets)
        {
            var address = new CellAddress(sheet.Id, row, col);
            var pivot = FindPivotTableContaining(sheet, address);
            if (pivot is not null)
                return (sheet, pivot);
        }

        return null;
    }

    private static int FindPivotDataFieldIndex(PivotTableModel pivotTable, IReadOnlyList<string> headers, string caption)
    {
        // Excel's GETPIVOTDATA data_field argument accepts either the data field's full
        // displayed caption (e.g. "Sum of Sales") or the bare underlying source-field name
        // (e.g. "Sales") -- both must resolve to the same data field.
        for (var i = 0; i < pivotTable.DataFields.Count; i++)
        {
            if (string.Equals(pivotTable.DataFields[i].Name, caption, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (var i = 0; i < pivotTable.DataFields.Count; i++)
        {
            var sourceFieldName = PivotHeader(headers, pivotTable.DataFields[i].SourceFieldIndex);
            if (!string.IsNullOrWhiteSpace(sourceFieldName) &&
                string.Equals(sourceFieldName, caption, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static PivotTableModel? FindPivotTableContaining(Sheet sheet, CellAddress address)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            if (pivot.TargetRange.Contains(address))
                return pivot;
        }

        return null;
    }

    private static uint? ResolveGetPivotDataRow(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint lastRow)
    {
        var rowFields = pivotTable.RowFields.ToList();
        if (rowFields.Count == 0)
            return firstDataRow <= lastRow ? firstDataRow : null;

        var requestedRowFieldCount = rowFields.Count(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex)));
        if (requestedRowFieldCount == 0)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (IsPivotGrandTotalText(pivotTable, sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
                    return row;
            }

            // No row field is constrained (a pure grand-total request for the row axis) and no
            // rendered Grand Total row was found (Show Row Grand Totals is off). The generic
            // per-field match loop below is only meaningful when at least one row field is
            // genuinely constrained -- with zero row-field filters requested, every
            // `filters.TryGetValue` in that loop would trivially miss and its "no constraint =>
            // keep matching" fallback would match the very first data row unconditionally
            // instead of the true (unrendered) aggregate. Signal unresolved so the caller can
            // fall back to computing the true aggregate directly, or #REF!. See
            // R57-formula-getpivotdata-5-1.
            return null;
        }

        if (requestedRowFieldCount > 0 && requestedRowFieldCount < rowFields.Count)
        {
            for (var row = firstDataRow; row <= lastRow; row++)
            {
                if (TryReadPivotSubtotalCaption(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value, out var subtotalItem) &&
                    PivotSubtotalMatches(sheet, pivotTable, headers, filters, firstDataRow, row, subtotalItem, requestedRowFieldCount))
                {
                    return row;
                }
            }
        }

        for (var row = firstDataRow; row <= lastRow; row++)
        {
            if (IsPivotGrandTotalText(pivotTable, sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value))
            {
                if (!rowFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
                    return row;
                continue;
            }

            if (TryCompactPivotRowMatch(sheet, pivotTable, headers, filters, row, rowFields, requestedRowFieldCount))
                return row;

            var matches = true;
            for (var index = 0; index < rowFields.Count; index++)
            {
                var header = PivotHeader(headers, rowFields[index].SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var actual = ReadPivotRowItem(sheet, pivotTable, row, firstDataRow, index, rowFields.Count);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return row;
        }

        return null;
    }

    private static bool TryCompactPivotRowMatch(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint row,
        IReadOnlyList<PivotFieldModel> rowFields,
        int requestedRowFieldCount)
    {
        if (pivotTable.ReportLayout != PivotReportLayout.Compact || rowFields.Count <= 1)
            return false;
        if (requestedRowFieldCount != rowFields.Count)
            return false;

        var expectedParts = new List<string>(rowFields.Count);
        foreach (var field in rowFields)
        {
            var header = PivotHeader(headers, field.SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                return false;
            expectedParts.Add(expected);
        }

        var actual = PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);
        var expectedCaption = string.Join(" ", expectedParts);
        return string.Equals(actual, expectedCaption, StringComparison.OrdinalIgnoreCase);
    }

    private static uint? ResolveGetPivotDataColumn(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        int dataFieldIndex,
        uint lastColumn)
    {
        var rowFieldColumns = PivotRowFieldOutputColumnCount(pivotTable);
        var firstValueColumn = pivotTable.TargetRange.Start.Col + (uint)rowFieldColumns;
        if (pivotTable.ColumnFields.Count == 0)
            return firstValueColumn + (uint)dataFieldIndex <= lastColumn ? firstValueColumn + (uint)dataFieldIndex : null;

        if (!pivotTable.ColumnFields.Any(field => filters.ContainsKey(PivotHeader(headers, field.SourceFieldIndex))))
        {
            for (var col = firstValueColumn; col <= lastColumn; col++)
            {
                var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
                if (columnDataFieldIndex != dataFieldIndex)
                    continue;
                for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
                {
                    if (IsPivotGrandTotalText(pivotTable, sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value))
                        return col;
                }
            }

            // No column field is constrained (a pure grand-total request for the column axis)
            // and no rendered Grand Total column was found (Show Column Grand Totals is off).
            // The generic per-level match loop below is only meaningful when at least one column
            // field is genuinely constrained -- with zero column-field filters requested, its
            // "no constraint => keep matching" fallback would match the first data-field column
            // unconditionally instead of the true (unrendered) aggregate. Signal unresolved so
            // the caller can fall back to computing the true aggregate directly, or #REF!. See
            // R57-formula-getpivotdata-5-1.
            return null;
        }

        for (var col = firstValueColumn; col <= lastColumn; col++)
        {
            var columnDataFieldIndex = (int)((col - firstValueColumn) % (uint)Math.Max(1, pivotTable.DataFields.Count));
            if (columnDataFieldIndex != dataFieldIndex)
                continue;

            var matches = true;
            for (var level = 0; level < pivotTable.ColumnFields.Count; level++)
            {
                var field = pivotTable.ColumnFields[level];
                var header = PivotHeader(headers, field.SourceFieldIndex);
                if (!filters.TryGetValue(header, out var expected))
                    continue;

                var caption = PivotText(sheet.GetCell(pivotTable.TargetRange.Start.Row + (uint)level, col)?.Value);
                if (pivotTable.DataFields.Count > 1 && level == pivotTable.ColumnFields.Count - 1)
                {
                    var dataFieldName = pivotTable.DataFields[dataFieldIndex].Name;
                    if (caption.EndsWith(dataFieldName, StringComparison.OrdinalIgnoreCase))
                        caption = caption[..^dataFieldName.Length].TrimEnd();
                }

                if (!string.Equals(caption, expected, StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return col;
        }

        return null;
    }

    private static bool PivotSubtotalMatches(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> filters,
        uint firstDataRow,
        uint subtotalRow,
        string subtotalItem,
        int requestedRowFieldCount)
    {
        for (var index = 0; index < pivotTable.RowFields.Count; index++)
        {
            var header = PivotHeader(headers, pivotTable.RowFields[index].SourceFieldIndex);
            if (!filters.TryGetValue(header, out var expected))
                continue;

            string? actual = null;
            if (index == 0)
                actual = subtotalItem;
            else if (index < requestedRowFieldCount)
                actual = ReadPivotRowItem(sheet, pivotTable, subtotalRow - 1, firstDataRow, index, pivotTable.RowFields.Count);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string? ReadPivotRowItem(
        Sheet sheet,
        PivotTableModel pivotTable,
        uint row,
        uint firstDataRow,
        int fieldIndex,
        int rowFieldCount)
    {
        if (pivotTable.ReportLayout == PivotReportLayout.Compact && rowFieldCount > 1)
            return PivotText(sheet.GetCell(row, pivotTable.TargetRange.Start.Col)?.Value);

        var col = pivotTable.TargetRange.Start.Col + (uint)fieldIndex;
        for (var current = row; current >= firstDataRow; current--)
        {
            var value = sheet.GetCell(current, col)?.Value;
            if (value is not null)
                return PivotText(value);
            if (current == firstDataRow)
                break;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadPivotSourceHeaders(Workbook workbook, PivotTableModel pivotTable)
    {
        var sourceSheet = workbook.GetSheet(pivotTable.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return [];
        var headers = new List<string>();
        for (var col = pivotTable.SourceRange.Start.Col; col <= pivotTable.SourceRange.End.Col; col++)
            headers.Add(PivotText(sourceSheet.GetCell(pivotTable.SourceRange.Start.Row, col)?.Value));
        return headers;
    }

    private static string PivotHeader(IReadOnlyList<string> headers, int index) =>
        index >= 0 && index < headers.Count ? headers[index] : "";

    private static int PivotRowFieldOutputColumnCount(PivotTableModel pivotTable) =>
        pivotTable.ReportLayout == PivotReportLayout.Compact && pivotTable.RowFields.Count > 1
            ? 1
            : pivotTable.RowFields.Count;

    private static bool IsPivotGrandTotalText(PivotTableModel pivotTable, ScalarValue? value) =>
        value is TextValue text && text.Value.StartsWith(PivotGrandTotalCaption(pivotTable), StringComparison.OrdinalIgnoreCase);

    // Mirrors PivotTableRefreshService.Captions.cs's GrandTotalCaption(pivotTable) fallback
    // (that writer-side helper lives in FreeX.Core.Commands, which Core.Formula cannot
    // reference): the pivot's actual, possibly user-renamed, Grand Total row/column caption,
    // falling back to Excel's default "Grand Total" text when unset. See
    // R57-formula-getpivotdata-5-2.
    private static string PivotGrandTotalCaption(PivotTableModel pivotTable) =>
        string.IsNullOrWhiteSpace(pivotTable.GrandTotalCaption) ? "Grand Total" : pivotTable.GrandTotalCaption.Trim();

    private static bool TryReadPivotSubtotalCaption(ScalarValue? value, out string item)
    {
        item = "";
        if (value is not TextValue text ||
            !text.Value.EndsWith(" Total", StringComparison.OrdinalIgnoreCase) ||
            text.Value.StartsWith("Grand Total", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        item = text.Value[..^" Total".Length];
        return item.Length > 0;
    }

    private static string PivotText(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        DirectTextLiteralValue text => text.Value,
        // Numeric/date item arguments must render with the same convention the pivot
        // layout itself uses for row/column labels (PivotTableRefreshService.Filters.cs's
        // KeyText, which formats with CurrentCulture) so a GETPIVOTDATA item argument
        // like 1000.5 matches the rendered label text in non-"."-decimal cultures instead
        // of comparing an invariant-formatted string against a culture-formatted one.
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        DateTimeValue date => date.ToDateTime().ToShortDateString(),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        ReferencedScalarValue referenced => PivotText(referenced.Value),
        // GETPIVOTDATA's field_name/item/data_field arguments are declared
        // SingleCellReferenceRangeFunctions, so a bare cell reference (e.g. G1, not "Region")
        // arrives here already wrapped in a 1x1 RangeValue by the generic arg-expansion path
        // (FormulaEvaluator.Functions.cs), not as the cell's own scalar. Without this arm the
        // switch fell through to the `_` case and stringified the RangeValue record itself
        // (garbage text), which never matched any pivot header and produced a spurious #REF!
        // (R50-formula-pivot-getpivotdata-3-1) — resolve to the referenced cell's actual value.
        RangeValue range when range.Cells.Length > 0 => PivotText(range.Cells[0, 0]),
        _ => value.ToString() ?? ""
    };

}
