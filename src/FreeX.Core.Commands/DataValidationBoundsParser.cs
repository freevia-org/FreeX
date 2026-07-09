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
            // Parse once so we can shift relative references from the rule's anchor cell
            // (AppliesTo.Start) to the cell actually being validated, mirroring the way
            // ValidateCustom / ResolveListValues already handle List/Custom rules: a bound
            // formula like "=A1" is authored as if the rule's anchor cell were active, so for
            // a multi-cell rule (e.g. B1:B3 anchored at B1) it must shift row/column-wise for
            // B2, B3, etc. rather than always evaluating against the anchor's neighbor.
            var ast = FormulaEvaluator.ParseFormula(formulaText);
            var anchor = FindRuleAnchor(sheet, address, text) ?? address;
            if (anchor != address)
                ast = FormulaEvaluator.ShiftFormulaForCell(ast, anchor, address);

            var evaluated = new FormulaEvaluator().Evaluate(ast, sheet, workbook, currentCell: address);
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

    /// <summary>
    /// Recovers the anchor cell (<c>AppliesTo.Start</c>) of the data-validation rule that owns
    /// this bound formula. The bound parser only receives the raw Formula1/Formula2 text (see
    /// the callers in DataValidationService), not the owning <see cref="DataValidation"/>, so
    /// the anchor is found by matching which rule covers <paramref name="address"/> and carries
    /// this exact bound text, rather than requiring every caller to thread the anchor through.
    /// Returns null (no shift) when no matching rule is found, preserving prior behavior.
    /// </summary>
    private static CellAddress? FindRuleAnchor(Sheet sheet, CellAddress address, string text)
    {
        var validations = sheet.DataValidations;
        for (var i = 0; i < validations.Count; i++)
        {
            var dv = validations[i];
            if (dv.Type is not (DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time or DvType.TextLength))
                continue;

            if (!string.Equals(dv.Formula1, text, StringComparison.Ordinal) &&
                !string.Equals(dv.Formula2, text, StringComparison.Ordinal))
                continue;

            if (dv.AppliesTo.Contains(address) || AdditionalRangeContains(dv.AdditionalRanges, address))
                return dv.AppliesTo.Start;
        }

        return null;
    }

    private static bool AdditionalRangeContains(IReadOnlyList<GridRange> ranges, CellAddress addr)
    {
        for (var i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Contains(addr))
                return true;
        }

        return false;
    }

    private static bool Assign(double resolved, out double value)
    {
        value = resolved;
        return true;
    }
}
