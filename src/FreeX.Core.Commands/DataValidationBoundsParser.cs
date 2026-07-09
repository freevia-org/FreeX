using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static class DataValidationBoundsParser
{
    /// <summary>
    /// Parses a user-entered numeric DV bound (e.g. from the Data Validation dialog). Tries the
    /// current UI culture first so a comma-decimal locale (e.g. de-DE "1,5") is read as the user
    /// intended, then falls back to invariant parsing for bounds that were stored/typed in
    /// invariant form. The returned value is a culture-neutral double either way, so persistence
    /// (file/model layer) is unaffected.
    /// </summary>
    public static bool TryParseNumberBound(string? text, out double value) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.CurrentCulture,
            out value) ||
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    /// <summary>
    /// Resolves a Formula1/Formula2 numeric bound, evaluating it as a formula (e.g. a cell
    /// reference like "A1" or an expression like "=A1+1") when it isn't a plain literal number.
    /// Falls back to literal parsing when no sheet context is supplied, matching Excel's
    /// behavior of evaluating DV bounds in the context of the cell being validated.
    /// </summary>
    public static bool TryParseNumberBound(string? text, Sheet? sheet, CellAddress? address, Workbook? workbook, out double value)
    {
        if (TryParseNumberBound(text, out value))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, workbook, out var result))
            return false;

        return result switch
        {
            NumberValue nv => Assign(nv.Value, out value),
            DateTimeValue dtv => Assign(dtv.Value, out value),
            BoolValue bv => Assign(bv.Value ? 1 : 0, out value),
            _ => false
        };
    }

    public static bool TryParseDateBound(string? text, out double oaDate)
    {
        oaDate = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (TryParseNumberBound(text, out oaDate))
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

    public static bool TryParseDateBound(string? text, Sheet? sheet, CellAddress? address, Workbook? workbook, out double oaDate)
    {
        if (TryParseDateBound(text, out oaDate))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, workbook, out var result))
            return false;

        return result switch
        {
            NumberValue nv => Assign(nv.Value, out oaDate),
            DateTimeValue dtv => Assign(dtv.Value, out oaDate),
            _ => false
        };
    }

    public static bool TryParseTimeBound(string? text, out double timeValue)
    {
        timeValue = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (TryParseNumberBound(text, out timeValue))
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

    public static bool TryParseTimeBound(string? text, Sheet? sheet, CellAddress? address, Workbook? workbook, out double timeValue)
    {
        if (TryParseTimeBound(text, out timeValue))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, workbook, out var result))
            return false;

        return result switch
        {
            NumberValue nv => Assign(nv.Value - Math.Floor(nv.Value), out timeValue),
            DateTimeValue dtv => Assign(dtv.Value - Math.Floor(dtv.Value), out timeValue),
            _ => false
        };
    }

    private static bool TryEvaluateBoundFormula(
        string text,
        Sheet sheet,
        CellAddress address,
        Workbook? workbook,
        out ScalarValue result)
    {
        var formulaText = text.Trim();
        if (!formulaText.StartsWith('='))
            formulaText = "=" + formulaText;

        try
        {
            var evaluated = new FormulaEvaluator().Evaluate(formulaText, sheet, workbook, currentCell: address);
            if (evaluated is ErrorValue)
            {
                result = evaluated;
                return false;
            }

            result = evaluated;
            return true;
        }
        catch
        {
            result = ErrorValue.Value;
            return false;
        }
    }

    private static bool Assign(double resolved, out double value)
    {
        value = resolved;
        return true;
    }
}
