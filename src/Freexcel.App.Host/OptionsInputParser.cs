using System.Globalization;

namespace Freexcel.App.Host;

public static class OptionsInputParser
{
    public static int ParseDefaultFontSizeOrFallback(string input, int fallback)
    {
        return int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    public static int ParseDefaultSheetCountOrFallback(string input, int fallback)
    {
        return int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               parsed is >= 1 and <= 255
            ? parsed
            : fallback;
    }
}
