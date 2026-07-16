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
                var f1 = dv.IsX14 ? "" : (dv.Formula1 ?? "");
                var f2 = dv.IsX14 ? "" : (dv.Formula2 ?? "");

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
        var unmarked = formula.Length > 1 && formula[0] == '=' && formula[1] != '"'
            ? formula.Substring(1)
            : formula;

        var trimmed = unmarked.Trim();
        if (trimmed.Length < 2 || !trimmed.Contains(',', StringComparison.Ordinal))
            return unmarked;

        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') ||
            trimmed.StartsWith('=') ||
            trimmed.Contains('!', StringComparison.Ordinal) ||
            trimmed.Contains(':', StringComparison.Ordinal) ||
            trimmed.Contains('$', StringComparison.Ordinal))
        {
            return unmarked;
        }

        return $"\"{trimmed.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsDuplicateCoveredValidation(IEnumerable<DataValidation> existingRules, DataValidation candidate) =>
        CandidateRanges(candidate).All(range => existingRules.Any(existing => CoversRange(existing, range)));

    private static IEnumerable<GridRange> CandidateRanges(DataValidation validation)
    {
        yield return validation.AppliesTo;
        foreach (var range in validation.AdditionalRanges)
            yield return range;
    }

    private static bool CoversRange(DataValidation validation, GridRange range) =>
        IsSameRange(validation.AppliesTo, range) ||
        validation.AdditionalRanges.Any(additionalRange => IsSameRange(additionalRange, range));

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
