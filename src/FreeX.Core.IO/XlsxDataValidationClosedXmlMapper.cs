using System.Globalization;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDataValidationClosedXmlMapper
{
    public static void Load(IXLWorksheet xlSheet, Sheet sheet, List<string>? warnings = null)
    {
        foreach (var xlDv in xlSheet.DataValidations)
        {
            try
            {
                var ranges = xlDv.Ranges.Select(range => range.RangeAddress).ToArray();
                var rangeAddr = ranges.Length == 0 ? null : ranges[0];
                if (rangeAddr == null) continue;

                var sheetId = sheet.Id;
                var start = new CellAddress(sheetId,
                    (uint)rangeAddr.FirstAddress.RowNumber,
                    (uint)rangeAddr.FirstAddress.ColumnNumber);
                var end = new CellAddress(sheetId,
                    (uint)rangeAddr.LastAddress.RowNumber,
                    (uint)rangeAddr.LastAddress.ColumnNumber);
                var appliesTo = new GridRange(start, end);

                var dv = new DataValidation
                {
                    AppliesTo = appliesTo,
                    AllowBlank = xlDv.IgnoreBlanks,
                    ShowDropdown = !xlDv.InCellDropdown.Equals(false),
                    AlertStyle = xlDv.ErrorStyle switch
                    {
                        XLErrorStyle.Warning => DvAlertStyle.Warning,
                        XLErrorStyle.Information => DvAlertStyle.Information,
                        _ => DvAlertStyle.Stop
                    },
                    ShowInputMessage = xlDv.ShowInputMessage,
                    ShowErrorMessage = xlDv.ShowErrorMessage,
                    // ClosedXML returns "" (never null) for these when the source XML has no
                    // error/errorTitle/prompt/promptTitle attribute at all -- the common case
                    // where the author never customized the Error Alert / Input Message tabs.
                    // Normalize to null so FreeX's `dv.ErrorMessage ?? "<default text>"`
                    // fallbacks (DataValidationService.cs/ListSources.cs) actually trigger
                    // instead of surfacing a blank alert body.
                    ErrorTitle = string.IsNullOrEmpty(xlDv.ErrorTitle) ? null : xlDv.ErrorTitle,
                    ErrorMessage = string.IsNullOrEmpty(xlDv.ErrorMessage) ? null : xlDv.ErrorMessage,
                    PromptTitle = string.IsNullOrEmpty(xlDv.InputTitle) ? null : xlDv.InputTitle,
                    PromptMessage = string.IsNullOrEmpty(xlDv.InputMessage) ? null : xlDv.InputMessage,
                };

                dv.Type = xlDv.AllowedValues switch
                {
                    XLAllowedValues.WholeNumber => DvType.WholeNumber,
                    XLAllowedValues.Decimal => DvType.Decimal,
                    XLAllowedValues.List => DvType.List,
                    XLAllowedValues.Date => DvType.Date,
                    XLAllowedValues.Time => DvType.Time,
                    XLAllowedValues.TextLength => DvType.TextLength,
                    XLAllowedValues.Custom => DvType.Custom,
                    _ => DvType.Any
                };

                dv.Operator = xlDv.Operator switch
                {
                    XLOperator.Between => DvOperator.Between,
                    XLOperator.NotBetween => DvOperator.NotBetween,
                    XLOperator.EqualTo => DvOperator.Equal,
                    XLOperator.NotEqualTo => DvOperator.NotEqual,
                    XLOperator.GreaterThan => DvOperator.GreaterThan,
                    XLOperator.LessThan => DvOperator.LessThan,
                    XLOperator.EqualOrGreaterThan => DvOperator.GreaterThanOrEqual,
                    XLOperator.EqualOrLessThan => DvOperator.LessThanOrEqual,
                    _ => DvOperator.Between
                };

                if (dv.Type == DvType.List)
                {
                    var raw = xlDv.MinValue ?? "";
                    if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length > 1)
                    {
                        // Inline literal list, e.g. <formula1>"Yes,No,Maybe"</formula1> -- strip
                        // the quotes and keep as a literal comma-separated item list (no '=').
                        dv.Formula1 = raw.Substring(1, raw.Length - 2).Replace("\"\"", "\"");
                    }
                    else if (raw.Length == 0)
                    {
                        dv.Formula1 = raw;
                    }
                    else
                    {
                        // Range / defined-name / cross-sheet reference, e.g. <formula1>MyColors</formula1>
                        // or <formula1>$D$1:$D$3</formula1>. Real Excel never stores the leading '=' in
                        // this element, but DataValidationService.ListSources gates range/named-source
                        // resolution strictly on "Formula1 starts with '='" -- re-add it here so the
                        // source resolves to the referenced range/name instead of being treated as one
                        // literal list item equal to the raw reference text.
                        dv.Formula1 = raw.StartsWith('=') ? raw : "=" + raw;
                    }
                }
                else
                {
                    dv.Formula1 = xlDv.MinValue;
                    dv.Formula2 = xlDv.MaxValue;
                }

                foreach (var additionalRange in ranges.Skip(1))
                    dv.AdditionalRanges.Add(ToGridRange(additionalRange, sheetId));

                if (IsDuplicateCoveredValidation(sheet.DataValidations, dv))
                    continue;

                sheet.DataValidations.Add(dv);
            }
            catch (Exception ex)
            {
                warnings?.Add($"[data-validation] Sheet '{xlSheet.Name}': one data validation rule could not be loaded and was skipped: {ex.Message}");
            }
        }
    }

    public static void Save(Sheet sheet, IXLWorksheet xlSheet, List<string>? warnings = null)
    {
        foreach (var dv in sheet.DataValidations)
        {
            if (!Enum.IsDefined(dv.Type) || !Enum.IsDefined(dv.Operator) || !Enum.IsDefined(dv.AlertStyle))
                continue;
            if (dv.AppliesTo.Start.Sheet != sheet.Id || dv.AppliesTo.End.Sheet != sheet.Id)
                continue;

            try
            {
                var xlRange = xlSheet.Range(ToA1Range(dv.AppliesTo));
#pragma warning disable CS0618 // SetDataValidation is obsolete in newer ClosedXML but CreateDataValidation may not exist in 0.105
                var xlDv = xlRange.CreateDataValidation();
#pragma warning restore CS0618
                foreach (var additionalRange in dv.AdditionalRanges)
                {
                    if (additionalRange.Start.Sheet != sheet.Id || additionalRange.End.Sheet != sheet.Id)
                        continue;

                    xlDv.AddRange(xlSheet.Range(ToA1Range(additionalRange)));
                }

                xlDv.IgnoreBlanks = dv.AllowBlank;
                xlDv.InCellDropdown = dv.ShowDropdown;
                xlDv.ErrorStyle = dv.AlertStyle switch
                {
                    DvAlertStyle.Warning => XLErrorStyle.Warning,
                    DvAlertStyle.Information => XLErrorStyle.Information,
                    _ => XLErrorStyle.Stop
                };
                xlDv.ShowInputMessage = dv.ShowInputMessage;
                xlDv.ShowErrorMessage = dv.ShowErrorMessage;

                if (!string.IsNullOrEmpty(dv.ErrorTitle)) xlDv.ErrorTitle = dv.ErrorTitle;
                if (!string.IsNullOrEmpty(dv.ErrorMessage)) xlDv.ErrorMessage = dv.ErrorMessage;
                if (!string.IsNullOrEmpty(dv.PromptTitle)) xlDv.InputTitle = dv.PromptTitle;
                if (!string.IsNullOrEmpty(dv.PromptMessage)) xlDv.InputMessage = dv.PromptMessage;

                // For x14 rules the real formula lives in the worksheet extLst x14 block;
                // the legacy <dataValidation> intentionally carries an empty formula1 so
                // that older readers gracefully ignore it. Pass empty strings here.
                //
                // NormalizeNumericFormulaForSave exists ONLY to canonicalize Date/Time/Decimal/
                // WholeNumber bounds (see its own doc comment) -- it must never run for List or
                // Custom. Its number-parse attempt falls back to CultureInfo.CurrentCulture, and on
                // any comma-decimal-separator locale (de-DE, fr-FR, es-ES, it-IT, ru-RU, pt-BR,
                // nl-NL, ...) a List rule's literal Formula1 text that looks like "digits,digits"
                // (e.g. a two-item literal list "1000,2000") gets silently reparsed as the single
                // decimal number 1000.2000 and reformatted to invariant dot notation BEFORE
                // NormalizeListFormulaForSave (below) ever sees the original text -- corrupting a
                // two-item dropdown into a single mangled literal on save. Custom formulas are
                // arbitrary boolean expressions, not numeric bounds, so they must be left untouched
                // too. Only WholeNumber/Decimal/Date/Time bounds ever need this canonicalization.
                var appliesNumericNormalization = dv.Type is DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time;
                var f1 = dv.IsX14 ? "" : ((appliesNumericNormalization ? NormalizeNumericFormulaForSave(dv.Type, dv.Formula1) : dv.Formula1) ?? "");
                var f2 = dv.IsX14 ? "" : ((appliesNumericNormalization ? NormalizeNumericFormulaForSave(dv.Type, dv.Formula2) : dv.Formula2) ?? "");

                switch (dv.Type)
                {
                    case DvType.List:
                        xlDv.List(NormalizeListFormulaForSave(f1), dv.ShowDropdown);
                        break;
                    case DvType.WholeNumber:
                        ApplyNumeric(xlDv.WholeNumber, dv.Operator, f1, f2);
                        break;
                    case DvType.Decimal:
                        ApplyNumeric(xlDv.Decimal, dv.Operator, f1, f2);
                        break;
                    case DvType.Date:
                        ApplyNumeric(xlDv.Date, dv.Operator, f1, f2);
                        break;
                    case DvType.Time:
                        ApplyNumeric(xlDv.Time, dv.Operator, f1, f2);
                        break;
                    case DvType.TextLength:
                        ApplyNumeric(xlDv.TextLength, dv.Operator, f1, f2);
                        break;
                    case DvType.Custom:
                        xlDv.Custom(f1);
                        break;
                }
            }
            catch (Exception ex)
            {
                var rangeDesc = ToA1Range(dv.AppliesTo);
                System.Diagnostics.Debug.WriteLine($"[XlsxDataValidationClosedXmlMapper] Skipping data-validation rule for '{rangeDesc}' on sheet '{sheet.Name}': {ex.Message}");
                warnings?.Add($"[data-validation] Data validation rule for range '{rangeDesc}' on sheet '{sheet.Name}' could not be saved and was skipped.");
            }
        }
    }

    internal static string NormalizeListFormulaForSave(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return formula;

        // Load (above) re-adds a leading '=' onto a range/defined-name List source purely as an
        // in-memory marker so DataValidationService.ListSources' "starts with '='" gate resolves
        // the reference instead of treating the raw text as one literal list item. Real Excel
        // never writes that marker to <formula1> for a range/name source (R36's own regression
        // test documents the un-prefixed on-disk convention) -- strip it back off here before
        // serializing. A quoted inline literal (e.g. "Red,Green,Blue") never carries the marker,
        // so only unmark when the second character isn't the opening quote of a literal.
        //
        // That same leading-'=' marker is also the ONLY authority this function trusts for deciding
        // literal-vs-reference (mirroring DataValidationCopySupport.RewriteValidationFormula's
        // identical "the leading '=' is the actual runtime authority" convention). It must NOT be
        // re-derived by sniffing the text for ':', '$', or '!' -- a literal list item can legitimately
        // contain any of those (e.g. "9:00,10:00,11:00", "$100,$200,$300", "Yes!,No!"), and treating
        // their mere presence as "already a reference" leaves a genuine literal unquoted on save,
        // producing an invalid <formula1> that Excel cannot parse (R95_ regression coverage).
        var isReferenceMarked = formula.Length > 1 && formula[0] == '=' && formula[1] != '"';
        var unmarked = isReferenceMarked ? formula.Substring(1) : formula;

        if (isReferenceMarked)
            return unmarked;

        var trimmed = unmarked.Trim();
        if (trimmed.Length == 0)
            return unmarked;

        // A single-item literal (no comma at all, e.g. "Approved") is just as much a literal as a
        // comma-separated one -- the '=' marker above is the ONLY authority this function trusts for
        // literal-vs-reference, so gating the quoting step on the presence of a comma left every
        // ordinary one-choice dropdown (and any x14 List source that happens to be exactly one item)
        // unquoted on disk, which Excel cannot parse back as a literal (R96 regression coverage).
        if ((trimmed.Length > 1 && trimmed.StartsWith('"') && trimmed.EndsWith('"')) || trimmed.StartsWith('='))
            return unmarked;

        return $"\"{trimmed.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// Canonicalizes a Date/Time/Decimal/WholeNumber data-validation bound before it is written to
    /// formula1/formula2. Excel always stores Date/Time bounds as the OLE Automation date serial
    /// (e.g. "45292" for 1/1/2024, not the human text "1/1/2024" -- which Excel itself would parse
    /// as (1/1)/2024, a near-zero number) and stores Decimal/WholeNumber bounds using an
    /// invariant dot-decimal separator regardless of the authoring locale. A value that already
    /// looks like a serial/invariant number is normalized in place (never reinterpreted as a
    /// date/time string), and a formula or cell/range reference (anything that fails to parse as
    /// either) is left completely untouched.
    /// </summary>
    internal static string? NormalizeNumericFormulaForSave(DvType type, string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return formula;

        var trimmed = formula.Trim();

        // A bare number is already meant to be the on-disk form for every one of these types --
        // normalize its decimal separator and stop, without ever trying to reinterpret it as a
        // date/time string (e.g. a Date bound already saved as "45292").
        if (TryParseInvariantOrCurrentCultureNumber(trimmed, out var numericText))
            return numericText;

        return type switch
        {
            DvType.Date => TryParseDateOnly(trimmed, out var dateSerial) ? dateSerial : formula,
            DvType.Time => TryParseTimeOnly(trimmed, out var timeSerial) ? timeSerial : formula,
            _ => formula,
        };
    }

    // This is a thin wrapper over DataValidationNumericBoundText.TryParse/ToInvariantString -- the
    // ONE shared parse also used by the dialog-entry gate
    // (FreeX.App.Presentation.Dialogs.DataValidationDialogModel) and by live enforcement while the
    // session runs (FreeX.Core.Commands.DataValidationBoundsParser). Before this was unified, this
    // save-side parse used a hand-picked NumberStyles set that omitted AllowThousands, so a
    // thousands-grouped bound (e.g. "1,234") failed to parse HERE even though the dialog/live-eval
    // styles accepted it -- the bound then fell through to the `_ => formula` branch in
    // NormalizeNumericFormulaForSave and was written to the XLSX verbatim, with its original
    // locale-specific grouping character still embedded, instead of being canonicalized to
    // invariant digits. Sharing the exact same parse (and this ToInvariantString formatter) with
    // the other two call sites guarantees the number enforced in-session and the number persisted
    // to disk are always the same one.
    private static bool TryParseInvariantOrCurrentCultureNumber(string trimmed, out string invariantText)
    {
        if (DataValidationNumericBoundText.TryParse(trimmed, out var value))
        {
            invariantText = DataValidationNumericBoundText.ToInvariantString(value);
            return true;
        }

        invariantText = "";
        return false;
    }

    private static bool TryParseDateOnly(string trimmed, out string serialText)
    {
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
        {
            // Excel serial, not the raw OADate — a date-validation bound is compared against stored
            // cell serials, which place 1900-01-01..1900-02-28 one day below their OADate. Mirrors
            // DataValidationBoundsParser.TryParseDateBound on the in-app side.
            serialText = FormatSerial(DateTimeValue.FromDateTime(parsed).Value);
            return true;
        }

        serialText = "";
        return false;
    }

    private static bool TryParseTimeOnly(string trimmed, out string serialText)
    {
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var parsed) ||
            DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out parsed))
        {
            serialText = FormatSerial(parsed.TimeOfDay.TotalDays);
            return true;
        }

        serialText = "";
        return false;
    }

    private static string FormatSerial(double value)
    {
        // Round away floating-point noise before formatting -- Excel's own serial resolution is
        // roughly 1 second (1/86400 of a day), so 8 decimal places is far more than enough.
        var rounded = Math.Round(value, 8, MidpointRounding.AwayFromZero);
        return rounded == Math.Floor(rounded)
            ? rounded.ToString("F0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.########", CultureInfo.InvariantCulture);
    }

    // Only treat `candidate` as covered by an already-loaded rule when BOTH the range AND the rule
    // content match -- ClosedXML sometimes surfaces one multi-area Excel rule as several separate
    // IXLDataValidation entries (one per area, all sharing the same content), and those split
    // artifacts are the only case this should collapse. Two independent rules that merely happen to
    // target the same range (e.g. a List rule and a Custom rule both on A1:A10) must both be kept.
    private static bool IsDuplicateCoveredValidation(IEnumerable<DataValidation> existingRules, DataValidation candidate) =>
        CandidateRanges(candidate).All(range => existingRules.Any(existing => CoversRange(existing, range, candidate)));

    private static IEnumerable<GridRange> CandidateRanges(DataValidation validation)
    {
        yield return validation.AppliesTo;
        foreach (var range in validation.AdditionalRanges)
            yield return range;
    }

    private static bool CoversRange(DataValidation validation, GridRange range, DataValidation candidate) =>
        (IsSameRange(validation.AppliesTo, range) ||
            validation.AdditionalRanges.Any(additionalRange => IsSameRange(additionalRange, range))) &&
        HasSameRuleContent(validation, candidate);

    /// <summary>
    /// True when two <see cref="DataValidation"/> rules represent the same underlying Excel rule
    /// rather than two independent rules that merely happen to cover the same range. Type/Operator
    /// must always agree. Formula1/Formula2 must agree too, EXCEPT that a blank Formula on one side
    /// is treated as matching any Formula on the other -- this is what lets an x14 cross-sheet List
    /// rule's inert legacy echo (same range/type, but an intentionally empty formula1 before the
    /// x14 merge fills it in) collapse into its formula-bearing counterpart, without ever letting
    /// two genuinely different, both-populated rules (e.g. a List "Yes,No" and a Custom
    /// "ISNUMBER(A1)" on the same range) collapse into one another.
    /// </summary>
    private static bool HasSameRuleContent(DataValidation left, DataValidation right) =>
        left.Type == right.Type &&
        left.Operator == right.Operator &&
        FormulaMatchesOrEitherBlank(left.Formula1, right.Formula1) &&
        FormulaMatchesOrEitherBlank(left.Formula2, right.Formula2) &&
        string.Equals(left.ErrorTitle, right.ErrorTitle, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        string.Equals(left.PromptTitle, right.PromptTitle, StringComparison.Ordinal) &&
        string.Equals(left.PromptMessage, right.PromptMessage, StringComparison.Ordinal);

    private static bool FormulaMatchesOrEitherBlank(string? left, string? right) =>
        string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) || string.Equals(left, right, StringComparison.Ordinal);

    private static bool IsSameRange(GridRange left, GridRange right) =>
        left.Start.Sheet == right.Start.Sheet &&
        left.Start.Row == right.Start.Row &&
        left.Start.Col == right.Start.Col &&
        left.End.Sheet == right.End.Sheet &&
        left.End.Row == right.End.Row &&
        left.End.Col == right.End.Col;

    private static void ApplyNumeric(IXLValidationCriteria rule, DvOperator op, string f1, string f2)
    {
        switch (op)
        {
            case DvOperator.Between: rule.Between(f1, f2); break;
            case DvOperator.NotBetween: rule.NotBetween(f1, f2); break;
            case DvOperator.Equal: rule.EqualTo(f1); break;
            case DvOperator.NotEqual: rule.NotEqualTo(f1); break;
            case DvOperator.GreaterThan: rule.GreaterThan(f1); break;
            case DvOperator.LessThan: rule.LessThan(f1); break;
            case DvOperator.GreaterThanOrEqual: rule.EqualOrGreaterThan(f1); break;
            case DvOperator.LessThanOrEqual: rule.EqualOrLessThan(f1); break;
        }
    }

    private static GridRange ToGridRange(IXLRangeAddress rangeAddress, SheetId sheetId) =>
        new(
            new CellAddress(
                sheetId,
                (uint)rangeAddress.FirstAddress.RowNumber,
                (uint)rangeAddress.FirstAddress.ColumnNumber),
            new CellAddress(
                sheetId,
                (uint)rangeAddress.LastAddress.RowNumber,
                (uint)rangeAddress.LastAddress.ColumnNumber));

    private static string ToA1Range(GridRange range)
    {
        var startCol = CellAddress.NumberToColumnName(range.Start.Col);
        var start    = $"{startCol}{range.Start.Row}";
        if (range.Start.Row == range.End.Row && range.Start.Col == range.End.Col)
            return start;   // single-cell: "A1" not "A1:A1"
        var endCol = CellAddress.NumberToColumnName(range.End.Col);
        return $"{start}:{endCol}{range.End.Row}";
    }
}
