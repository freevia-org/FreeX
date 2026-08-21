using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class DataValidationService
{
    private static readonly string MissingBlankCellText = ToValidationText(BlankValue.Instance);

    // Real Excel's default List-validation rejection dialog (no custom ErrorMessage authored)
    // is this fixed, generic sentence -- it never enumerates the source list's actual values,
    // regardless of how many items the source has (mirrors the sibling default fallbacks below,
    // e.g. "Value must be a number.", which describe the rule rather than dumping its data).
    private const string GenericListErrorMessage = "Value must match one of the list items.";

    private static string? ValidateList(DataValidation dv, ScalarValue value)
    {
        if (string.IsNullOrEmpty(dv.Formula1))
            return null;

        // Split once; build case-insensitive set for O(1) lookup.
        var trimmed = ParseInlineListItems(dv.Formula1);
        return ValidateListAgainstValues(dv, value, trimmed);
    }

    private static string? ValidateList(DataValidation dv, ScalarValue value, Sheet sheet, CellAddress address, Workbook? workbook)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return null;

        var source = dv.Formula1.Trim();
        if (source.StartsWith('='))
        {
            // The source formula is authored as if the rule's anchor cell (AppliesTo.Start) were
            // active; relative references (e.g. a cascading =INDIRECT($A2) source) must be
            // shifted to the cell actually being validated, mirroring ValidateCustom.
            var anchor = dv.AppliesTo.Start;
            if (TryValidateRangeOrNamedSource(source, sheet, workbook, anchor, address, value, out var rangeMatch))
            {
                if (rangeMatch)
                    return null;

                // The fast path above has already determined there is no match against the
                // source range/named range, so return directly instead of falling through to
                // ResolveListValues below. For a source spanning a column's full nominal extent
                // (e.g. "=$A$1:$A$1048576") that fallback would otherwise materialize and re-scan
                // up to ~1,048,576 list items (RangeListItems) just to report a rejection that is
                // already known.
                return dv.ErrorMessage ?? GenericListErrorMessage;
            }

            var allowed = ResolveListValues(source, sheet, anchor, address, workbook, forDisplay: false);
            if (allowed.Count > 0)
                return ValidateListAgainstValues(dv, value, allowed);

            // The source formula could not be resolved to any list items (e.g. a cascading
            // =INDIRECT($A2) dropdown whose upstream cell is blank, so INDIRECT errors). Real
            // Excel does not enforce List validation when the source formula can't be evaluated
            // to a set of allowed values -- it accepts any entry rather than rejecting every
            // value against the raw, unevaluated formula text. Falling through to the 2-arg
            // ValidateList below would do exactly that: ParseInlineListItems("=INDIRECT($A2)")
            // treats the literal formula string as the one and only allowed value, rejecting
            // every real entry.
            return null;
        }

        return ValidateList(dv, value);
    }

    /// <summary>
    /// Resolves a List-validation source formula to its item strings.
    /// </summary>
    /// <param name="forDisplay">
    /// When <see langword="true"/>, items are rendered for a human to read (e.g. a date-sourced
    /// item shows as "2024-01-02"), matching how <see cref="FreeX.App.Presentation.SpreadsheetDisplayFormatter"/>
    /// would show the same cell. When <see langword="false"/> (the default), items are rendered
    /// via <see cref="ToValidationText"/> for value-membership matching (e.g. a date-sourced item
    /// is the raw OADate serial), so they compare equal to a value's own <see cref="ToValidationText"/>
    /// regardless of locale. Callers that show items to the user (the in-cell dropdown's
    /// <c>GetListItems</c>, the rule preview) want <paramref name="forDisplay"/>: true; callers that
    /// use the resolved items to accept/reject an entered value want it false (R163-DV-F1).
    /// </param>
    private static IReadOnlyList<string> ResolveListValues(
        string formulaText,
        Sheet sheet,
        CellAddress anchor,
        CellAddress address,
        Workbook? workbook,
        bool forDisplay = false)
    {
        var source = formulaText.Trim();
        if (source.StartsWith('='))
        {
            if (TryReadRangeOrNamedSource(source, sheet, workbook, anchor, address, forDisplay, out var rangeValues))
                return rangeValues;

            var ast = FormulaEvaluator.ParseFormula(source);
            if (anchor != address)
                ast = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, address);

            var result = new FormulaEvaluator().Evaluate(ast, sheet, workbook, currentCell: address);
            if (result is RangeValue range)
            {
                return forDisplay
                    ? range.Flatten().Select(ToDisplayText).ToArray()
                    : range.Flatten().Select(ToValidationText).ToArray();
            }

            if (result is ErrorValue)
            {
                // A formula-based list source (e.g. a cascading =INDIRECT($A2) dropdown) that
                // currently errors out has no valid list items. Falling through to
                // ParseInlineListItems would treat the raw, unevaluated formula text itself as a
                // single bogus list entry, surfacing "=INDIRECT($A2)" as a dropdown item and
                // rejecting every real value the user enters/selects.
                return Array.Empty<string>();
            }

            return new[] { forDisplay ? ToDisplayText(result) : ToValidationText(result) };
        }

        return ParseInlineListItems(formulaText);
    }

    private static IReadOnlyList<string> ParseInlineListItems(string text)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                items.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        items.Add(current.ToString().Trim());
        return items;
    }

    private static bool TryReadRangeOrNamedSource(
        string formulaText,
        Sheet sheet,
        Workbook? workbook,
        CellAddress anchor,
        CellAddress address,
        bool forDisplay,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();

        // The simple-range fast path strips $ markers and can't distinguish relative from
        // absolute references, so it can only be used when no anchor->address shift is needed.
        if (anchor == address && TryReadSimpleSameSheetRangeSource(formulaText, sheet, forDisplay, out values))
            return true;

        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();
            if (anchor != address)
                ast = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, address);

            if (ast is ErrorNode)
            {
                values = Array.Empty<string>();
                return true;
            }

            if (ast is RangeRefNode range)
            {
                var sourceSheet = sheet;
                var sheetName = range.SheetName ?? range.Start.SheetName ?? range.End.SheetName;
                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    sourceSheet = workbook?.GetSheet(sheetName) ?? sheet;
                    if (!string.Equals(sourceSheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                values = ReadRangeValues(sourceSheet, range.Start.Row, range.Start.ColumnNumber, range.End.Row, range.End.ColumnNumber, forDisplay);
                return true;
            }

            // Sheet-qualification-aware, shared with TryValidateRangeOrNamedSource below (and
            // FormulaAuditingService's precedent collectors) so a sheet-scoped name reference
            // (e.g. an explicit "=Sheet2!Data" naming Sheet2's OWN local "Data", authored on a
            // cell that lives on Sheet1, which also happens to have its own local "Data") is
            // resolved against the QUALIFIED sheet's scope, not the validated cell's own sheet
            // (R92-io-defined-name-scope-eval-5-2). See NamedRangeNodeScopeResolver's doc comment
            // for the full scope-precedence rule this mirrors from the formula evaluator.
            if (ast is NamedRangeNode named && workbook is not null &&
                NamedRangeNodeScopeResolver.TryResolveNamedRange(workbook, named, sheet.Id, out var namedRange))
            {
                var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
                values = ReadRangeValues(
                    sourceSheet,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col,
                    forDisplay);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadSimpleSameSheetRangeSource(
        string formulaText,
        Sheet sheet,
        bool forDisplay,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();

        var source = formulaText.AsSpan().Trim();
        if (source.IsEmpty || source[0] != '=')
            return false;

        source = source[1..].Trim();
        if (source.IndexOf('!') >= 0)
            return false;

        var colon = source.IndexOf(':');
        if (colon < 0)
        {
            if (!TryParseA1Cell(source, sheet.Id, out var cell))
                return false;

            values = ReadRangeValues(sheet, cell.Row, cell.Col, cell.Row, cell.Col, forDisplay);
            return true;
        }

        if (!TryParseA1Cell(source[..colon], sheet.Id, out var start) ||
            !TryParseA1Cell(source[(colon + 1)..], sheet.Id, out var end))
        {
            return false;
        }

        values = ReadRangeValues(sheet, start.Row, start.Col, end.Row, end.Col, forDisplay);
        return true;
    }

    private static bool TryValidateRangeOrNamedSource(
        string formulaText,
        Sheet sheet,
        Workbook? workbook,
        CellAddress anchor,
        CellAddress address,
        ScalarValue value,
        out bool matches)
    {
        // Same fast-path caveat as TryReadRangeOrNamedSource: it can't tell relative from
        // absolute references, so only take it when no shift is needed.
        if (anchor == address && TryValidateSimpleSameSheetRangeSource(formulaText, sheet, value, out matches))
            return true;

        matches = false;

        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();
            if (anchor != address)
                ast = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, address);

            if (ast is ErrorNode)
            {
                matches = false;
                return true;
            }

            if (ast is RangeRefNode range)
            {
                var sourceSheet = sheet;
                var sheetName = range.SheetName ?? range.Start.SheetName ?? range.End.SheetName;
                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    sourceSheet = workbook?.GetSheet(sheetName) ?? sheet;
                    if (!string.Equals(sourceSheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                matches = RangeContainsValue(
                    sourceSheet,
                    range.Start.Row,
                    range.Start.ColumnNumber,
                    range.End.Row,
                    range.End.ColumnNumber,
                    value);
                return true;
            }

            // See TryReadRangeOrNamedSource above: same sheet-qualification-aware shared resolver
            // (R92-io-defined-name-scope-eval-5-2).
            if (ast is NamedRangeNode named && workbook is not null &&
                NamedRangeNodeScopeResolver.TryResolveNamedRange(workbook, named, sheet.Id, out var namedRange))
            {
                var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
                matches = RangeContainsValue(
                    sourceSheet,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col,
                    value);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryValidateSimpleSameSheetRangeSource(
        string formulaText,
        Sheet sheet,
        ScalarValue value,
        out bool matches)
    {
        matches = false;
        var source = formulaText.AsSpan().Trim();
        if (source.IsEmpty || source[0] != '=')
            return false;

        source = source[1..].Trim();
        if (source.IndexOf('!') >= 0)
            return false;

        var colon = source.IndexOf(':');
        if (colon < 0)
        {
            if (!TryParseA1Cell(source, sheet.Id, out var cell))
                return false;

            matches = RangeContainsValue(sheet, cell.Row, cell.Col, cell.Row, cell.Col, value);
            return true;
        }

        if (!TryParseA1Cell(source[..colon], sheet.Id, out var start) ||
            !TryParseA1Cell(source[(colon + 1)..], sheet.Id, out var end))
        {
            return false;
        }

        matches = RangeContainsValue(sheet, start.Row, start.Col, end.Row, end.Col, value);
        return true;
    }

    private static bool TryParseA1Cell(ReadOnlySpan<char> text, SheetId sheetId, out CellAddress address)
    {
        // Strip optional $ absolute markers and delegate to the canonical parser.
        var normalized = text.Trim().ToString().Replace("$", "", StringComparison.Ordinal);
        return CellAddress.TryParse(normalized, sheetId, out address);
    }

    private static bool RangeContainsValue(
        Sheet sheet,
        uint firstRow,
        uint firstCol,
        uint lastRow,
        uint lastCol,
        ScalarValue value)
    {
        var startRow = Math.Min(firstRow, lastRow);
        var endRow = Math.Max(firstRow, lastRow);
        var startCol = Math.Min(firstCol, lastCol);
        var endCol = Math.Max(firstCol, lastCol);
        var textValue = ToValidationText(value);
        var occupiedCells = sheet.GetOccupiedCellMap();
        var rowCount = (ulong)(endRow - startRow) + 1;
        var colCount = (ulong)(endCol - startCol) + 1;

        // GetOccupiedCellMap() only covers Sheet._cells; a dynamic-array spill's non-anchor
        // member cells live in the separate Sheet._spillValues overlay (Sheet.SetSpillRange),
        // so they must be added into the sparse-scan bound too, or a source range overlapping a
        // spill would report itself as sparse enough for the fast path while still being blind
        // to the spilled values (R140-DV-1). EnumerateValueBearingCells is the sheet's existing
        // union-of-both accessor, already used the same way by DataValidationCirclePlanner and
        // WorkbookSelectionStatsCalculator.
        var valueBearingCount = (ulong)occupiedCells.Count + (ulong)sheet.SpillValueCount;

        if (valueBearingCount > 0 &&
            valueBearingCount <= rowCount * colCount &&
            !CouldMatchMissingBlankCell(textValue))
        {
            foreach (var address in sheet.EnumerateValueBearingCells())
            {
                if (address.Row < startRow || address.Row > endRow || address.Col < startCol || address.Col > endCol)
                    continue;

                if (string.Equals(ToValidationText(sheet.GetValue(address)), textValue, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                var cellValue = sheet.GetValue(row, col);
                if (string.Equals(ToValidationText(cellValue), textValue, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static bool CouldMatchMissingBlankCell(string textValue) =>
        string.Equals(textValue, MissingBlankCellText, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadRangeValues(
        Sheet sheet,
        uint firstRow,
        uint firstCol,
        uint lastRow,
        uint lastCol,
        bool forDisplay = false)
    {
        var startRow = Math.Min(firstRow, lastRow);
        var endRow = Math.Max(firstRow, lastRow);
        var startCol = Math.Min(firstCol, lastCol);
        var endCol = Math.Max(firstCol, lastCol);
        return RangeListItems.Create(sheet, startRow, endRow, startCol, endCol, forDisplay);
    }

    private sealed class RangeListItems : IReadOnlyList<string>
    {
        private readonly Sheet _sheet;
        private readonly uint _startRow;
        private readonly uint _startCol;
        private readonly uint _columnCount;
        private readonly int _count;
        private readonly bool _forDisplay;

        private RangeListItems(Sheet sheet, uint startRow, uint startCol, uint columnCount, int count, bool forDisplay)
        {
            _sheet = sheet;
            _startRow = startRow;
            _startCol = startCol;
            _columnCount = columnCount;
            _count = count;
            _forDisplay = forDisplay;
        }

        public static RangeListItems Create(Sheet sheet, uint startRow, uint endRow, uint startCol, uint endCol, bool forDisplay = false)
        {
            var rowCount = (ulong)(endRow - startRow) + 1;
            var columnCount = (ulong)(endCol - startCol) + 1;
            var cellCount = rowCount * columnCount;
            if (cellCount > int.MaxValue)
                throw new InvalidOperationException("Validation list range is too large to expose as an item list.");

            return new RangeListItems(sheet, startRow, startCol, (uint)columnCount, (int)cellCount, forDisplay);
        }

        public int Count => _count;

        public string this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                var row = _startRow + (uint)index / _columnCount;
                var col = _startCol + (uint)index % _columnCount;
                // GetValue (not GetCell) so a source range/name that overlaps a dynamic-array
                // spill's non-anchor member cells (which live only in Sheet._spillValues, never
                // in Sheet._cells -- see Sheet.SetSpillRange) still reads its real value instead
                // of BlankValue (R140-DV-1).
                var value = _sheet.GetValue(row, col);
                return _forDisplay ? ToDisplayText(value) : ToValidationText(value);
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            for (var i = 0; i < _count; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static string? ValidateListAgainstValues(DataValidation dv, ScalarValue value, IReadOnlyCollection<string> allowedValues)
    {
        var textValue = ToValidationText(value);
        foreach (var allowedValue in allowedValues)
        {
            if (string.Equals(allowedValue, textValue, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Mirrors real Excel: the default rejection message is this fixed, generic sentence --
        // Excel never enumerates the allowed source values into the dialog, no matter how many
        // items the list has. Building a string.Join here would additionally re-enumerate (and,
        // for a large range/named source, allocate a multi-megabyte string from) the very list
        // the loop above just scanned looking for a match.
        return dv.ErrorMessage ?? GenericListErrorMessage;
    }

    /// <summary>
    /// Renders <paramref name="value"/> the way a human should see it in a List validation's
    /// in-cell dropdown / rule preview -- as opposed to <see cref="ToValidationText"/>, which
    /// renders it for raw value-membership matching. The two agree for every scalar kind except
    /// dates: <see cref="ToValidationText"/> must keep rendering a <see cref="DateTimeValue"/> as
    /// its raw OADate serial so it compares equal to another cell's own serial regardless of
    /// locale, but showing that same raw serial to the user in a dropdown ("45293" instead of
    /// "2024-01-02") is wrong -- Excel shows the formatted date. This mirrors the "yyyy-MM-dd"
    /// invariant format <c>SpreadsheetDisplayFormatter.FormatDateTimeCellValue</c> uses for the
    /// same cell in the grid/formula bar (that formatter lives in FreeX.App.Presentation, which
    /// this project cannot reference, hence the small local duplicate), so the dropdown's items
    /// and the active cell's own displayed text (computed via that formatter by
    /// DataValidationDropdownPlanner) use the identical string and the current value's item can be
    /// found/highlighted (R163-DV-F1).
    /// </summary>
    private static string ToDisplayText(ScalarValue value)
    {
        if (value is DateTimeValue dateTimeValue)
        {
            return dateTimeValue.TryToDateTime(out var dateTime)
                ? dateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : ToValidationText(dateTimeValue);
        }

        return ToValidationText(value);
    }

    private static string ToValidationText(ScalarValue value)
    {
        return value switch
        {
            TextValue t   => t.Value,
            // Inline list items (ParseInlineListItems) are raw invariant-culture text taken verbatim
            // from Formula1 (e.g. "1.5,2.5,3.5"), never reformatted for the current locale. Excel
            // itself matches list validation by value, not by locale-formatted text, so the scalar
            // value must be rendered the same way here or a comma-decimal locale (e.g. de-DE) would
            // format 1.5 as "1,5" and never match the "1.5" list item. Mirrors
            // DataValidationDropdownPlanner.FormatCellValue, which already uses InvariantCulture.
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Dates are stored as OADate serials (DateTimeValue), interchangeable with NumberValue
            // for comparison purposes elsewhere (e.g. DataValidationService.ValidateDate/
            // ValidateNumeric already treat NumberValue and DateTimeValue as the same OADate
            // serial). Render the same way here so a date-formatted list source cell and a raw
            // serial number typed/pasted into the validated cell compare equal.
            DateTimeValue dt => dt.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue b   => b.Value ? "TRUE" : "FALSE",
            _             => value.ToString() ?? ""
        };
    }
}
