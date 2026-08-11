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
    /// <para>
    /// This is a thin wrapper over <see cref="DataValidationNumericBoundText.TryParse"/> -- the ONE
    /// shared parse also used by the dialog-entry gate
    /// (FreeX.App.Presentation.Dialogs.DataValidationDialogModel) and by save-time canonicalization
    /// (FreeX.Core.IO.XlsxDataValidationClosedXmlMapper). See that type's doc comment for why a
    /// single shared implementation matters here: three independently-drifted NumberStyles used to
    /// make a thousands-grouped bound enforce one number in-session and a different one after
    /// save/reload.
    /// </para>
    /// </summary>
    public static bool TryParseNumberBound(string? text, out double value) =>
        DataValidationNumericBoundText.TryParse(text, out value);

    /// <summary>
    /// Resolves a Formula1/Formula2 numeric bound, evaluating it as a formula (e.g. a cell
    /// reference like "A1" or an expression like "=A1+1") when it isn't a plain literal number.
    /// Falls back to literal parsing when no sheet context is supplied, matching Excel's
    /// behavior of evaluating DV bounds in the context of the cell being validated.
    /// <paramref name="ruleAnchor"/> must be the <c>AppliesTo.Start</c> of the specific rule
    /// actually being validated (the caller always has it — it owns <paramref name="text"/>) so a
    /// relative bound formula is shifted from the correct origin even when another rule overlaps
    /// the same cell and happens to carry identical bound text.
    /// </summary>
    public static bool TryParseNumberBound(string? text, Sheet? sheet, CellAddress? address, CellAddress? ruleAnchor, Workbook? workbook, out double value)
    {
        if (TryParseNumberBound(text, out value))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, ruleAnchor, workbook, out var result))
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
            // DateTimeValue.FromDateTime, not a bare ToOADate(): ValidateDate compares this bound
            // against the cell's raw serial (DateTimeValue.Value / NumberValue.Value), so the bound
            // has to live in the same Excel serial space. OADate places 1900-01-01..1900-02-28 one
            // day high, which would make a "1/15/1900" bound miss a cell typed as 1/15/1900.
            oaDate = DateTimeValue.FromDateTime(currentCultureDate).Value;
            return true;
        }

        return false;
    }

    public static bool TryParseDateBound(string? text, Sheet? sheet, CellAddress? address, CellAddress? ruleAnchor, Workbook? workbook, out double oaDate)
    {
        if (TryParseDateBound(text, out oaDate))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, ruleAnchor, workbook, out var result))
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

    public static bool TryParseTimeBound(string? text, Sheet? sheet, CellAddress? address, CellAddress? ruleAnchor, Workbook? workbook, out double timeValue)
    {
        if (TryParseTimeBound(text, out timeValue))
            return true;

        if (sheet is null || address is null || string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryEvaluateBoundFormula(text, sheet, address.Value, ruleAnchor, workbook, out var result))
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
        CellAddress? ruleAnchor,
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
            //
            // The anchor MUST be the caller-supplied AppliesTo.Start of the specific rule being
            // validated, not rediscovered by matching bound TEXT against sheet.DataValidations:
            // when two rules overlap the same cell and happen to share identical bound text
            // (e.g. both use "=A1"), a text-match anchor lookup can silently resolve against the
            // FIRST matching rule's anchor instead of the rule actually being validated,
            // evaluating the relative bound formula from the wrong origin.
            var ast = FormulaEvaluator.ParseFormula(formulaText);

            // Only shift from a REAL anchor. DataValidation.AppliesTo is a non-nullable GridRange,
            // so a rule that was never registered against a range (constructed standalone -- which
            // every caller that validates an ad-hoc rule does, and which the Core.Calc tests do)
            // reports AppliesTo.Start as the default CellAddress: row 0, col 0. That is not a cell
            // (rows/cols are 1-based), but it is not null either, so a plain `ruleAnchor ?? address`
            // happily treats it as the origin and shifts every relative reference off the grid --
            // turning "=A1" into an out-of-range ref that evaluates to 0, i.e. bounds of "0 and 0".
            // Treat an out-of-grid anchor as "no anchor" and evaluate in place.
            var anchor = ruleAnchor is { Row: >= 1, Col: >= 1 } valid ? valid : address;
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

    private static bool Assign(double resolved, out double value)
    {
        value = resolved;
        return true;
    }
}
