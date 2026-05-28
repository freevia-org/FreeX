using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetXmlValueParser
{
    public static uint? ParsePaneSplit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (uint.TryParse(value, out var integer))
            return integer;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) &&
            floating > 0 &&
            floating <= uint.MaxValue)
        {
            return (uint)Math.Round(floating);
        }

        return null;
    }

    public static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        value switch
        {
            "pageBreakPreview" => WorksheetViewMode.PageBreakPreview,
            "pageLayout" => WorksheetViewMode.PageLayout,
            _ => WorksheetViewMode.Normal
        };

    public static bool IsTruthy(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsFalse(string? value) =>
        value is "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    public static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;
}
