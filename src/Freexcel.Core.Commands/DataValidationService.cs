using Freexcel.Core.Model;
using Freexcel.Core.Formula;

namespace Freexcel.Core.Commands;

public enum DataValidationInvalidEntryAction { Allow, Block, AskToContinue }

/// <summary>
/// Stateless service that evaluates data validation rules against cell values.
/// </summary>
public static class DataValidationService
{
    public sealed record InputPrompt(string Title, string Message);

    /// <summary>
    /// Returns null if the value is valid according to the rule, or an error message if it is not.
    /// </summary>
    public static string? Validate(DataValidation dv, ScalarValue value)
    {
        // Blanks always pass when AllowBlank is true
        if (value is BlankValue)
            return dv.AllowBlank ? null : "A value is required.";

        return dv.Type switch
        {
            DvType.Any     => null,
            DvType.List    => ValidateList(dv, value),
            DvType.WholeNumber => ValidateNumeric(dv, value, requireInteger: true),
            DvType.Decimal => ValidateNumeric(dv, value),
            DvType.TextLength => ValidateTextLength(dv, value),
            DvType.Date    => ValidateDate(dv, value),
            DvType.Time    => ValidateTime(dv, value),
            DvType.Custom  => null,                         // formula-based — Phase 5
            _              => null
        };
    }

    public static string? Validate(
        DataValidation dv,
        ScalarValue value,
        Sheet sheet,
        CellAddress address,
        Workbook? workbook = null)
    {
        if (value is BlankValue)
            return dv.AllowBlank ? null : "A value is required.";

        return dv.Type switch
        {
            DvType.List => ValidateList(dv, value, sheet, workbook),
            DvType.Custom => ValidateCustom(dv, value, sheet, address, workbook),
            _ => Validate(dv, value)
        };
    }

    /// <summary>
    /// Returns all validation rules that apply to the given cell address.
    /// </summary>
    public static IEnumerable<DataValidation> GetApplicable(Sheet sheet, CellAddress addr)
        => sheet.DataValidations.Where(dv => dv.AppliesTo.Contains(addr));

    public static InputPrompt? GetInputPrompt(Sheet sheet, CellAddress addr)
    {
        foreach (var rule in GetApplicable(sheet, addr))
        {
            if (!rule.ShowInputMessage)
                continue;

            var title = rule.PromptTitle?.Trim() ?? "";
            var message = rule.PromptMessage?.Trim() ?? "";
            if (title.Length == 0 && message.Length == 0)
                continue;

            return new InputPrompt(title, message);
        }

        return null;
    }

    public static IReadOnlyList<string> GetListItems(DataValidation dv, Sheet sheet, Workbook? workbook = null)
    {
        if (dv.Type != DvType.List || !dv.ShowDropdown || string.IsNullOrWhiteSpace(dv.Formula1))
            return Array.Empty<string>();

        return ResolveListValues(dv.Formula1, sheet, workbook).ToArray();
    }

    public static string FormatListSourceRange(GridRange range, string? sheetName = null)
        => FormatListSourceRange(range, sheetName, hostSheetName: null);

    public static string FormatListSourceRange(GridRange range, string? sourceSheetName, string? hostSheetName)
    {
        var start = FormatAbsoluteCell(range.Start);
        var end = FormatAbsoluteCell(range.End);
        var reference = start == end ? start : $"{start}:{end}";
        if (string.IsNullOrWhiteSpace(sourceSheetName) ||
            string.Equals(sourceSheetName, hostSheetName, StringComparison.OrdinalIgnoreCase))
            return "=" + reference;

        return $"='{sourceSheetName.Replace("'", "''")}'!{reference}";
    }

    public static DataValidationInvalidEntryAction GetInvalidEntryAction(DataValidation dv)
    {
        if (!dv.ShowErrorMessage)
            return DataValidationInvalidEntryAction.Allow;

        return dv.AlertStyle == DvAlertStyle.Stop
            ? DataValidationInvalidEntryAction.Block
            : DataValidationInvalidEntryAction.AskToContinue;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? ValidateList(DataValidation dv, ScalarValue value)
    {
        if (string.IsNullOrEmpty(dv.Formula1))
            return null;

        // Split once; build case-insensitive set for O(1) lookup.
        var trimmed = dv.Formula1.Split(',').Select(i => i.Trim()).ToArray();
        return ValidateListAgainstValues(dv, value, trimmed);
    }

    private static string? ValidateList(DataValidation dv, ScalarValue value, Sheet sheet, Workbook? workbook)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return null;

        var source = dv.Formula1.Trim();
        if (source.StartsWith('='))
        {
            var allowed = ResolveListValues(source, sheet, workbook);
            if (allowed.Count > 0)
                return ValidateListAgainstValues(dv, value, allowed);
        }

        return ValidateList(dv, value);
    }

    private static IReadOnlyCollection<string> ResolveListValues(string formulaText, Sheet sheet, Workbook? workbook)
    {
        var source = formulaText.Trim();
        if (source.StartsWith('='))
        {
            if (TryReadRangeOrNamedSource(source, sheet, workbook, out var rangeValues))
                return rangeValues;

            var result = new FormulaEvaluator().Evaluate(source, sheet, workbook);
            if (result is RangeValue range)
                return range.Flatten().Select(ToValidationText).ToArray();

            if (result is not ErrorValue)
                return new[] { ToValidationText(result) };
        }

        return formulaText.Split(',').Select(i => i.Trim()).ToArray();
    }

    private static bool TryReadRangeOrNamedSource(
        string formulaText,
        Sheet sheet,
        Workbook? workbook,
        out IReadOnlyCollection<string> values)
    {
        values = Array.Empty<string>();

        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();

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

                values = ReadRangeValues(sourceSheet, range.Start.Row, range.Start.ColumnNumber, range.End.Row, range.End.ColumnNumber);
                return true;
            }

            if (ast is NamedRangeNode named && workbook is not null && workbook.TryGetNamedRange(named.Name, out var namedRange))
            {
                var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
                values = ReadRangeValues(
                    sourceSheet,
                    namedRange.Start.Row,
                    namedRange.Start.Col,
                    namedRange.End.Row,
                    namedRange.End.Col);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyCollection<string> ReadRangeValues(
        Sheet sheet,
        uint firstRow,
        uint firstCol,
        uint lastRow,
        uint lastCol)
    {
        var startRow = Math.Min(firstRow, lastRow);
        var endRow = Math.Max(firstRow, lastRow);
        var startCol = Math.Min(firstCol, lastCol);
        var endCol = Math.Max(firstCol, lastCol);
        var list = new List<string>();

        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                var cellValue = sheet.GetCell(row, col)?.Value ?? BlankValue.Instance;
                list.Add(ToValidationText(cellValue));
            }
        }

        return list;
    }

    private static string? ValidateListAgainstValues(DataValidation dv, ScalarValue value, IReadOnlyCollection<string> allowedValues)
    {
        var allowed = new HashSet<string>(allowedValues, StringComparer.OrdinalIgnoreCase);
        var textValue = ToValidationText(value);

        return allowed.Contains(textValue)
            ? null
            : dv.ErrorMessage ?? $"Invalid entry. Allowed values: {string.Join(", ", allowedValues)}";
    }

    private static string ToValidationText(ScalarValue value)
    {
        return value switch
        {
            TextValue t   => t.Value,
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            BoolValue b   => b.Value ? "TRUE" : "FALSE",
            _             => value.ToString() ?? ""
        };
    }

    private static string FormatAbsoluteCell(CellAddress address) =>
        $"${CellAddress.NumberToColumnName(address.Col)}${address.Row}";

    private static string? ValidateNumeric(DataValidation dv, ScalarValue value, bool requireInteger = false)
    {
        double numericValue;
        if (value is NumberValue nv)
            numericValue = nv.Value;
        else if (value is DateTimeValue dtv)
            numericValue = dtv.Value;
        else
            return dv.ErrorMessage ?? "Value must be a number.";

        if (requireInteger && Math.Abs(numericValue - Math.Round(numericValue)) > double.Epsilon)
            return dv.ErrorMessage ?? "Value must be a whole number.";

        if (!double.TryParse(dv.Formula1, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v1))
            return null; // can't evaluate — treat as valid

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!double.TryParse(dv.Formula2, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out v2))
                return null;
        }

        bool passes = dv.Operator switch
        {
            DvOperator.Between             => numericValue >= v1 && numericValue <= v2,
            DvOperator.NotBetween          => numericValue < v1 || numericValue > v2,
            DvOperator.Equal               => numericValue == v1,
            DvOperator.NotEqual            => numericValue != v1,
            DvOperator.GreaterThan         => numericValue > v1,
            DvOperator.LessThan            => numericValue < v1,
            DvOperator.GreaterThanOrEqual  => numericValue >= v1,
            DvOperator.LessThanOrEqual     => numericValue <= v1,
            _                              => true
        };

        return passes ? null : dv.ErrorMessage ?? BuildNumericErrorMessage(dv, v1, v2);
    }

    private static string? ValidateTextLength(DataValidation dv, ScalarValue value)
    {
        if (value is not TextValue tv)
            return dv.ErrorMessage ?? "Value must be text.";

        double length = tv.Value.Length;

        if (!double.TryParse(dv.Formula1, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v1))
            return null;

        double v2 = 0;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!double.TryParse(dv.Formula2, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out v2))
                return null;
        }

        bool passes = dv.Operator switch
        {
            DvOperator.Between             => length >= v1 && length <= v2,
            DvOperator.NotBetween          => length < v1 || length > v2,
            DvOperator.Equal               => length == v1,
            DvOperator.NotEqual            => length != v1,
            DvOperator.GreaterThan         => length > v1,
            DvOperator.LessThan            => length < v1,
            DvOperator.GreaterThanOrEqual  => length >= v1,
            DvOperator.LessThanOrEqual     => length <= v1,
            _                              => true
        };

        return passes ? null : dv.ErrorMessage ?? $"Text length must satisfy the rule (length {(int)length}).";
    }

    private static string? ValidateDate(DataValidation dv, ScalarValue value)
    {
        // Dates are stored as OADate numbers or DateTimeValue
        double oaDate;
        if (value is NumberValue nv)
            oaDate = nv.Value;
        else if (value is DateTimeValue dtv)
            oaDate = dtv.Value;
        else
            return dv.ErrorMessage ?? "Value must be a date.";

        if (!TryParseDateBound(dv.Formula1, out var v1))
            return null;

        string? formula2 = null;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!TryParseDateBound(dv.Formula2, out var v2))
                return null;

            formula2 = v2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Reuse numeric comparison logic with a temporary DV wrapper
        var numericDv = new DataValidation
        {
            Type      = DvType.Decimal,
            Operator  = dv.Operator,
            Formula1  = v1.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Formula2  = formula2,
            AllowBlank = dv.AllowBlank,
            ErrorMessage = dv.ErrorMessage
        };
        return ValidateNumeric(numericDv, new NumberValue(oaDate));
    }

    private static string? ValidateTime(DataValidation dv, ScalarValue value)
    {
        double timeValue;
        if (value is NumberValue nv)
            timeValue = nv.Value - Math.Floor(nv.Value);
        else if (value is DateTimeValue dtv)
            timeValue = dtv.Value - Math.Floor(dtv.Value);
        else
            return dv.ErrorMessage ?? "Value must be a time.";

        if (!TryParseTimeBound(dv.Formula1, out var v1))
            return null;

        string? formula2 = null;
        if (dv.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            if (!TryParseTimeBound(dv.Formula2, out var v2))
                return null;

            formula2 = v2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var numericDv = new DataValidation
        {
            Type = DvType.Decimal,
            Operator = dv.Operator,
            Formula1 = v1.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Formula2 = formula2,
            AllowBlank = dv.AllowBlank,
            ErrorMessage = dv.ErrorMessage
        };
        return ValidateNumeric(numericDv, new NumberValue(timeValue));
    }

    private static string? ValidateCustom(
        DataValidation dv,
        ScalarValue value,
        Sheet sheet,
        CellAddress address,
        Workbook? workbook)
    {
        if (string.IsNullOrWhiteSpace(dv.Formula1))
            return null;

        var original = sheet.GetCell(address)?.Clone();
        try
        {
            sheet.SetCell(address, value);
            var formulaText = dv.Formula1.TrimStart();
            if (!formulaText.StartsWith('='))
                formulaText = "=" + formulaText;

            var result = new FormulaEvaluator().Evaluate(formulaText, sheet, workbook);
            var passes = result switch
            {
                BoolValue b => b.Value,
                NumberValue n => Math.Abs(n.Value) > double.Epsilon,
                _ => false
            };

            return passes ? null : dv.ErrorMessage ?? "Value does not satisfy the custom validation rule.";
        }
        finally
        {
            if (original is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, original);
        }
    }

    private static bool TryParseDateBound(string? text, out double oaDate)
    {
        oaDate = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out oaDate))
            return true;

        if (DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.None, out var currentCultureDate) ||
            DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out currentCultureDate))
        {
            oaDate = currentCultureDate.ToOADate();
            return true;
        }

        return false;
    }

    private static bool TryParseTimeBound(string? text, out double timeValue)
    {
        timeValue = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out timeValue))
            return true;

        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, out var currentCultureTime) ||
            TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out currentCultureTime))
        {
            timeValue = currentCultureTime.TotalDays;
            return true;
        }

        if (DateTime.TryParse(text, System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.None, out var currentCultureDateTime) ||
            DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out currentCultureDateTime))
        {
            timeValue = currentCultureDateTime.TimeOfDay.TotalDays;
            return true;
        }

        return false;
    }

    private static string BuildNumericErrorMessage(DataValidation dv, double v1, double v2)
    {
        return dv.Operator switch
        {
            DvOperator.Between            => $"Value must be between {v1} and {v2}.",
            DvOperator.NotBetween         => $"Value must not be between {v1} and {v2}.",
            DvOperator.Equal              => $"Value must equal {v1}.",
            DvOperator.NotEqual           => $"Value must not equal {v1}.",
            DvOperator.GreaterThan        => $"Value must be greater than {v1}.",
            DvOperator.LessThan           => $"Value must be less than {v1}.",
            DvOperator.GreaterThanOrEqual => $"Value must be greater than or equal to {v1}.",
            DvOperator.LessThanOrEqual    => $"Value must be less than or equal to {v1}.",
            _                             => "Invalid value."
        };
    }
}
