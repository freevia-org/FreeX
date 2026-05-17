using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class WorkbookThemeDialogColorCodec
{
    public static string FormatColor(CellColor color) =>
        FormattableString.Invariant($"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static CellColor ParseColor(string text)
    {
        var value = text.Trim();
        if (value.StartsWith('#'))
            value = value[1..];

        if (value.Length != 6 ||
            !byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            throw new FormatException("Enter theme colors as #RRGGBB values.");
        }

        return new CellColor(red, green, blue);
    }
}
