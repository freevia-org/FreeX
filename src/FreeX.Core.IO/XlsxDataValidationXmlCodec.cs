using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Canonical OOXML tokens shared by legacy and x14 data-validation writers.
/// Formula normalization remains owned by the individual writer paths.
/// </summary>
internal static class XlsxDataValidationXmlCodec
{
    internal static bool RequiresOperator(DvType type) =>
        type is DvType.WholeNumber or DvType.Decimal or DvType.Date or DvType.Time or DvType.TextLength;

    internal static string FormatType(DvType type) => type switch
    {
        DvType.WholeNumber => "whole",
        DvType.Decimal => "decimal",
        DvType.List => "list",
        DvType.Date => "date",
        DvType.Time => "time",
        DvType.TextLength => "textLength",
        DvType.Custom => "custom",
        _ => "none",
    };

    internal static string FormatOperator(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "notBetween",
        DvOperator.Equal => "equal",
        DvOperator.NotEqual => "notEqual",
        DvOperator.GreaterThan => "greaterThan",
        DvOperator.LessThan => "lessThan",
        DvOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        DvOperator.LessThanOrEqual => "lessThanOrEqual",
        _ => "between",
    };

    internal static string FormatAlertStyle(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "warning",
        DvAlertStyle.Information => "information",
        _ => "stop",
    };
}
