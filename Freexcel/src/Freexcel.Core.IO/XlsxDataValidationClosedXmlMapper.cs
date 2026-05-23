using ClosedXML.Excel;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxDataValidationClosedXmlMapper
{
    public static void Load(IXLWorksheet xlSheet, Sheet sheet)
    {
        foreach (var xlDv in xlSheet.DataValidations)
        {
            try
            {
                var rangeAddr = xlDv.Ranges.FirstOrDefault()?.RangeAddress;
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
                    ErrorTitle = xlDv.ErrorTitle,
                    ErrorMessage = xlDv.ErrorMessage,
                    PromptTitle = xlDv.InputTitle,
                    PromptMessage = xlDv.InputMessage,
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
                        raw = raw.Substring(1, raw.Length - 2);
                    dv.Formula1 = raw.Replace("\"\"", "\"");
                }
                else
                {
                    dv.Formula1 = xlDv.MinValue;
                    dv.Formula2 = xlDv.MaxValue;
                }

                sheet.DataValidations.Add(dv);
            }
            catch
            {
                // Skip any individual validation we can't map.
            }
        }
    }

    public static void Save(Sheet sheet, IXLWorksheet xlSheet)
    {
        foreach (var dv in sheet.DataValidations)
        {
            if (!Enum.IsDefined(dv.Type) || !Enum.IsDefined(dv.Operator) || !Enum.IsDefined(dv.AlertStyle))
                continue;

            try
            {
                var rangeStr = $"{CellAddress.NumberToColumnName(dv.AppliesTo.Start.Col)}{dv.AppliesTo.Start.Row}" +
                               $":{CellAddress.NumberToColumnName(dv.AppliesTo.End.Col)}{dv.AppliesTo.End.Row}";

                var xlRange = xlSheet.Range(rangeStr);
#pragma warning disable CS0618 // SetDataValidation is obsolete in newer ClosedXML but CreateDataValidation may not exist in 0.105
                var xlDv = xlRange.CreateDataValidation();
#pragma warning restore CS0618

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

                var f1 = dv.Formula1 ?? "";
                var f2 = dv.Formula2 ?? "";

                switch (dv.Type)
                {
                    case DvType.List:
                        xlDv.List(f1, dv.ShowDropdown);
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
            catch
            {
                // Skip rules that can't be serialized.
            }
        }
    }

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
}
