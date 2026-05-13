using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Stateless service that evaluates data validation rules against cell values.
/// </summary>
public static class DataValidationService
{
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
            DvType.WholeNumber or DvType.Decimal => ValidateNumeric(dv, value),
            DvType.TextLength => ValidateTextLength(dv, value),
            DvType.Date    => ValidateDate(dv, value),
            DvType.Time    => ValidateNumeric(dv, value),   // times are OADate fractions
            DvType.Custom  => null,                         // formula-based — Phase 5
            _              => null
        };
    }

    /// <summary>
    /// Returns all validation rules that apply to the given cell address.
    /// </summary>
    public static IEnumerable<DataValidation> GetApplicable(Sheet sheet, CellAddress addr)
        => sheet.DataValidations.Where(dv => dv.AppliesTo.Contains(addr));

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? ValidateList(DataValidation dv, ScalarValue value)
    {
        if (string.IsNullOrEmpty(dv.Formula1))
            return null;

        // Split once; build case-insensitive set for O(1) lookup.
        var trimmed = dv.Formula1.Split(',').Select(i => i.Trim()).ToArray();
        var allowed = new HashSet<string>(trimmed, StringComparer.OrdinalIgnoreCase);

        var textValue = value switch
        {
            TextValue t   => t.Value,
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            BoolValue b   => b.Value ? "TRUE" : "FALSE",
            _             => value.ToString() ?? ""
        };

        return allowed.Contains(textValue)
            ? null
            : dv.ErrorMessage ?? $"Invalid entry. Allowed values: {string.Join(", ", trimmed)}";
    }

    private static string? ValidateNumeric(DataValidation dv, ScalarValue value)
    {
        double numericValue;
        if (value is NumberValue nv)
            numericValue = nv.Value;
        else if (value is DateTimeValue dtv)
            numericValue = dtv.Value;
        else
            return dv.ErrorMessage ?? "Value must be a number.";

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

        // Reuse numeric comparison logic with a temporary DV wrapper
        var numericDv = new DataValidation
        {
            Type      = DvType.Decimal,
            Operator  = dv.Operator,
            Formula1  = dv.Formula1,
            Formula2  = dv.Formula2,
            AllowBlank = dv.AllowBlank,
            ErrorMessage = dv.ErrorMessage
        };
        return ValidateNumeric(numericDv, new NumberValue(oaDate));
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
