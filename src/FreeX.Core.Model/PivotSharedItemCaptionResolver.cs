using System.Globalization;

namespace FreeX.Core.Model;

/// <summary>
/// Resolves the display caption for a raw pivot-cache shared item.
/// </summary>
public static class PivotSharedItemCaptionResolver
{
    public static string Resolve(string raw, char? kind, PivotCacheFieldModel? field)
    {
        if (field is null || string.IsNullOrEmpty(raw))
            return raw;

        if (kind == 'd' || (kind is null && field.ContainsDate && !field.ContainsString && !field.ContainsNumber))
        {
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return raw;

            return field.Grouping switch
            {
                PivotFieldGrouping.Year => date.Year.ToString(CultureInfo.InvariantCulture),
                PivotFieldGrouping.Quarter => $"{date.Year}-Q{((date.Month - 1) / 3) + 1}",
                PivotFieldGrouping.Month => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PivotFieldGrouping.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => date.ToShortDateString()
            };
        }

        if (kind == 'n' || (kind is null && field.ContainsNumber && !field.ContainsString && !field.ContainsDate))
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.CurrentCulture)
                : raw;
        }

        return raw;
    }
}
