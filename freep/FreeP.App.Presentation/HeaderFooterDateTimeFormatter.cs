using System.Globalization;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves the automatic date formats exposed by the PowerPoint-style header/footer dialog.
/// Cached field text remains authoritative; this formatter is only for uncached automatic fields.
/// </summary>
public static class HeaderFooterDateTimeFormatter
{
    public static bool IsDateTimeField(string? fieldType)
    {
        var normalized = fieldType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.StartsWith("datetime", StringComparison.Ordinal) ||
            normalized is "date" or "time";
    }

    public static string Format(string? fieldType, DateTime value)
    {
        var normalized = fieldType?.Trim().ToLowerInvariant() ?? string.Empty;
        var format = normalized switch
        {
            "datetime2" => "dddd, MMMM d, yyyy",
            "datetime3" => "d MMMM yyyy",
            "datetime4" => "MMMM d, yyyy",
            _ => "M/d/yyyy",
        };

        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
