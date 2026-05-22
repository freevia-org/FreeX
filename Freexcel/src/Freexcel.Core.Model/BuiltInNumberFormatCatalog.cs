namespace Freexcel.Core.Model;

public sealed record BuiltInNumberFormatDefinition(int? NumberFormatId, string FormatCode);

public static class BuiltInNumberFormatCatalog
{
    public static IReadOnlyList<BuiltInNumberFormatDefinition> Formats { get; } =
    [
        new(null, "General"),
        new(0, "General"),
        new(1, "0"),
        new(2, "0.00"),
        new(3, "#,##0"),
        new(4, "#,##0.00"),
        new(5, "$#,##0"),
        new(6, "$#,##0;[Red]($#,##0)"),
        new(7, "$#,##0.00"),
        new(8, "$#,##0.00;[Red]($#,##0.00)"),
        new(9, "0%"),
        new(10, "0.00%"),
        new(11, "0.00E+00"),
        new(12, "# ?/?"),
        new(13, "# ??/??"),
        new(14, "m/d/yy"),
        new(15, "d-mmm-yy"),
        new(16, "d-mmm"),
        new(17, "mmm-yy"),
        new(18, "h:mm AM/PM"),
        new(19, "h:mm:ss AM/PM"),
        new(20, "h:mm"),
        new(21, "h:mm:ss"),
        new(22, "m/d/yy h:mm"),
        new(37, "#,##0;(#,##0)"),
        new(38, "#,##0;[Red](#,##0)"),
        new(39, "#,##0.00;(#,##0.00)"),
        new(40, "#,##0.00;[Red](#,##0.00)"),
        new(41, "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)"),
        new(42, "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)"),
        new(43, "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)"),
        new(44, "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)"),
        new(45, "mm:ss"),
        new(46, "[h]:mm:ss"),
        new(47, "mm:ss.0"),
        new(48, "##0.0E+0"),
        new(49, "@")
    ];

    public static bool TryResolveFormatCode(int? numberFormatId, out string formatCode)
    {
        var definition = Formats.FirstOrDefault(format => format.NumberFormatId == numberFormatId);
        if (definition is null)
        {
            formatCode = "";
            return false;
        }

        formatCode = definition.FormatCode;
        return true;
    }

    public static int? ResolveNumberFormatIdForCode(string? formatCode) =>
        TryResolveNumberFormatIdForCode(formatCode, out var numberFormatId)
            ? numberFormatId
            : null;

    public static bool TryResolveNumberFormatIdForCode(string? formatCode, out int? numberFormatId)
    {
        numberFormatId = null;
        if (string.IsNullOrWhiteSpace(formatCode))
            return false;

        var trimmed = formatCode.Trim();
        var definition = Formats.FirstOrDefault(format =>
            string.Equals(format.FormatCode, trimmed, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return false;

        numberFormatId = definition.NumberFormatId;
        return true;
    }
}
