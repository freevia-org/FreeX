using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class PageMarginInputParser
{
    public static bool TryParse(string input, out WorksheetPageMargins margins, out string? error)
    {
        margins = default;

        var parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            error = "Enter four comma-separated margins: left, right, top, bottom.";
            return false;
        }

        var values = new double[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                error = "Margins must be numbers in inches.";
                return false;
            }

            if (value < 0)
            {
                error = "Margins cannot be negative.";
                return false;
            }

            values[i] = value;
        }

        margins = new WorksheetPageMargins(values[0], values[1], values[2], values[3]);
        error = null;
        return true;
    }
}
