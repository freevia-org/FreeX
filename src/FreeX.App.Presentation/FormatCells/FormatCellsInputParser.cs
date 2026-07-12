using System.Globalization;

namespace FreeX.App.Presentation.FormatCells;

public static class FormatCellsInputParser
{
    /// <summary>
    /// Excel's Format Cells &gt; Font &gt; Size box rejects values outside 1-409 points
    /// (inclusive) with a "between 1 and 409" style error, regardless of how large a
    /// finite number is typed.
    /// </summary>
    public const double MaxFontSize = 409;

    public static double? TryParseFontSize(string text)
    {
        if ((double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var currentCultureSize) ||
             double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out currentCultureSize)) &&
            currentCultureSize > 0 &&
            currentCultureSize <= MaxFontSize &&
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

    public static bool IsSupportedCustomNumberFormat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var sectionCount = 1;
        var inQuote = false;
        var bracketDepth = 0;
        var trimmed = text.Trim();

        for (var i = 0; i < trimmed.Length; i++)
        {
            var character = trimmed[i];
            if (character == '\\')
            {
                i++;
                continue;
            }

            if (character == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (character == '[')
            {
                bracketDepth++;
                continue;
            }

            if (character == ']')
            {
                if (bracketDepth == 0)
                    return false;

                bracketDepth--;
                continue;
            }

            if (character == ';' && bracketDepth == 0)
            {
                sectionCount++;
                if (sectionCount > 4)
                    return false;
            }
        }

        return !inQuote && bracketDepth == 0;
    }
}
