using System.Globalization;

namespace Freexcel.App.Host;

public static class OptionsInputParser
{
    public static bool TryParseDefaultFontSize(string input, out int fontSize)
    {
        fontSize = 0;
        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
            return false;

        fontSize = parsed;
        return true;
    }

    public static bool TryParseDefaultSheetCount(string input, out int sheetCount)
    {
        sheetCount = 0;
        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 1 or > 255)
            return false;

        sheetCount = parsed;
        return true;
    }
}
