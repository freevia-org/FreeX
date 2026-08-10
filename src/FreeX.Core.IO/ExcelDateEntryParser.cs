using System.Globalization;

namespace FreeX.Core.IO;

/// <summary>Parses date-shaped Excel entry text using the current culture.</summary>
public static class ExcelDateEntryParser
{
    private static readonly DateTime EarliestExcelDate = new(1900, 1, 1);

    public static bool TryParseCurrentCulture(
        string text,
        bool allowTimeOnly,
        out DateTime dateTime)
    {
        dateTime = default;
        var currentCulture = CultureInfo.CurrentCulture;
        if (string.IsNullOrEmpty(currentCulture.Name))
            return false;

        var dateSeparator = currentCulture.DateTimeFormat.DateSeparator;
        var dotCountsAsDateSeparator = dateSeparator.Length == 1 && dateSeparator[0] == '.';
        if (!DateEntryShapeRecognizer.LooksLikeDateCandidate(
                text.AsSpan(),
                dotCountsAsDateSeparator,
                colonAlwaysQualifies: allowTimeOnly))
        {
            return false;
        }

        var culture = (CultureInfo)currentCulture.Clone();
        culture.DateTimeFormat.Calendar.TwoDigitYearMax = 2029;
        if (!DateTime.TryParse(text, culture, DateTimeStyles.NoCurrentDateDefault, out dateTime))
            return false;

        if (dateTime.Date == DateTime.MinValue.Date)
            return allowTimeOnly;

        return dateTime.Date >= EarliestExcelDate;
    }
}
