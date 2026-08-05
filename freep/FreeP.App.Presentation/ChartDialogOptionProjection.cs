using System.Globalization;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral projections used by chart option dialogs.
/// </summary>
public static class ChartDialogOptionProjection
{
    public static int FindIndex<TOption, TValue>(
        IReadOnlyList<TOption> options,
        TValue value,
        Func<TOption, TValue> valueSelector,
        int fallbackIndex = 0,
        IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(valueSelector);
        comparer ??= EqualityComparer<TValue>.Default;

        for (var index = 0; index < options.Count; index++)
        {
            if (comparer.Equals(valueSelector(options[index]), value))
                return index;
        }

        return fallbackIndex;
    }

    public static TValue ValueAtOrDefault<TOption, TValue>(
        IReadOnlyList<TOption> options,
        int selectedIndex,
        Func<TOption, TValue> valueSelector,
        TValue fallbackValue = default!)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(valueSelector);

        return selectedIndex >= 0 && selectedIndex < options.Count
            ? valueSelector(options[selectedIndex])
            : fallbackValue;
    }

    public static double? ParseOptionalDouble(
        string? text,
        CultureInfo culture,
        Func<double, bool> isValid,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(isValid);

        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (double.TryParse(text, NumberStyles.Float, culture, out var value) && isValid(value))
            return value;
        throw new FormatException(errorMessage);
    }

    public static int? ParseOptionalInt(
        string? text,
        CultureInfo culture,
        Func<int, bool> isValid,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(isValid);

        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (int.TryParse(text, NumberStyles.Integer, culture, out var value) && isValid(value))
            return value;
        throw new FormatException(errorMessage);
    }

    public static int ParseRequiredInt(
        string? text,
        CultureInfo culture,
        Func<int, bool> isValid,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(isValid);

        if (int.TryParse(text, NumberStyles.Integer, culture, out var value) && isValid(value))
            return value;
        throw new FormatException(errorMessage);
    }

    public static IReadOnlyList<int> ParseNonNegativeIntList(
        string? text,
        CultureInfo culture,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<int>();

        var values = new List<int>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, NumberStyles.Integer, culture, out var value) || value < 0)
                throw new FormatException(errorMessage);
            values.Add(value);
        }

        return values;
    }

    public static string Format(double? value, CultureInfo culture, string format = "G")
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value?.ToString(format, culture) ?? string.Empty;
    }

    public static string Format(int? value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value?.ToString(culture) ?? string.Empty;
    }
}
