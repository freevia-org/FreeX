using System.Globalization;

namespace Freexcel.App.Host;

public static class FormatCellsInputParser
{
    public static double? TryParseFontSize(string text)
    {
        if ((double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var currentCultureSize) ||
             double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out currentCultureSize)) &&
            currentCultureSize > 0 &&
            double.IsFinite(currentCultureSize))
        {
            return currentCultureSize;
        }

        return null;
    }

    public static int? TryParseIndentLevel(string text)
    {
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var indent)
            ? Math.Clamp(indent, 0, 15)
            : null;
    }

    public static int? TryParseSupportedTextRotation(string text)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotation))
            return null;

        return rotation == 255 || rotation is >= -90 and <= 90
            ? rotation
            : null;
    }
}
