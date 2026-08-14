using System.Globalization;

namespace Free.Shared.AppServices;

public sealed class ZoomPercentPolicy
{
    public const double DefaultMinimumSliderValue = 0d;
    public const double DefaultMiddleSliderValue = 100d;
    public const double DefaultMaximumSliderValue = 200d;

    private const double WholePercentTolerance = 0.000001d;

    public ZoomPercentPolicy(double minimumPercent, double defaultPercent, double maximumPercent)
    {
        if (double.IsNaN(minimumPercent) ||
            double.IsNaN(defaultPercent) ||
            double.IsNaN(maximumPercent) ||
            minimumPercent >= defaultPercent ||
            defaultPercent >= maximumPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultPercent),
                "Zoom percent bounds must be ordered as minimum < default < maximum.");
        }

        MinimumPercent = minimumPercent;
        DefaultPercent = defaultPercent;
        MaximumPercent = maximumPercent;
    }

    public double MinimumPercent { get; }

    public double DefaultPercent { get; }

    public double MaximumPercent { get; }

    public double ClampPercent(double percent)
    {
        if (double.IsNaN(percent))
            return DefaultPercent;

        if (percent < MinimumPercent)
            return MinimumPercent;

        if (percent > MaximumPercent)
            return MaximumPercent;

        return percent;
    }

    public int NormalizeWholePercent(double percent) =>
        (int)Math.Round(ClampPercent(percent));

    public bool TryNormalizeWholePercent(double percent, out int wholePercent)
    {
        wholePercent = NormalizeWholePercent(percent);
        return Math.Abs(percent - wholePercent) <= WholePercentTolerance;
    }

    public bool ContainsPercent(double percent) =>
        percent >= MinimumPercent && percent <= MaximumPercent;

    public string FormatPercentText(double percent) =>
        NormalizeWholePercent(percent).ToString(CultureInfo.CurrentCulture);

    public string FormatPercentLabel(double percent) =>
        $"{FormatPercentText(percent)}%";

    public bool IsPresetPercent(int percent, IEnumerable<int> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        foreach (var preset in presets)
        {
            if (preset == percent)
                return true;
        }

        return false;
    }

    public bool TryParsePercent(string? text, out double percent)
    {
        percent = DefaultPercent;
        var normalized = NormalizePercentInput(text);
        if (normalized is null)
            return false;

        if (!double.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) &&
            !double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        percent = parsed;
        return true;
    }

    public bool TryParsePercentInRange(string? text, out double percent)
    {
        percent = DefaultPercent;
        if (!TryParsePercent(text, out var parsed) || !ContainsPercent(parsed))
            return false;

        percent = parsed;
        return true;
    }

    public bool TryParseWholePercent(string? text, out int percent)
    {
        percent = NormalizeWholePercent(DefaultPercent);
        var normalized = NormalizePercentInput(text);
        if (normalized is null)
            return false;

        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed) &&
            !int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        percent = parsed;
        return true;
    }

    /// <summary>
    /// The single shared route from a Zoom dialog's custom-percent text to a whole zoom percent.
    /// Normalizes the text (trimming whitespace and a trailing '%'), parses it in the current then
    /// the invariant culture, applies <paramref name="rangeMode"/> to out-of-range values, and finally
    /// requires the result to land on a whole percent. Every rejection is classified with a
    /// <see cref="ZoomPercentInputError"/> so each app can choose its own message for it.
    /// </summary>
    public bool TryResolveWholePercent(
        string? text,
        ZoomPercentRangeMode rangeMode,
        out int percent,
        out ZoomPercentInputError error)
    {
        percent = NormalizeWholePercent(DefaultPercent);

        var normalized = NormalizePercentInput(text);
        if (normalized is null)
        {
            error = ZoomPercentInputError.Missing;
            return false;
        }

        if (!double.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) &&
            !double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            error = ZoomPercentInputError.NotNumeric;
            return false;
        }

        if (rangeMode == ZoomPercentRangeMode.Reject && !ContainsPercent(parsed))
        {
            error = ZoomPercentInputError.OutOfRange;
            return false;
        }

        var effective = rangeMode == ZoomPercentRangeMode.Clamp ? ClampPercent(parsed) : parsed;
        if (!TryNormalizeWholePercent(effective, out var whole))
        {
            error = ZoomPercentInputError.NotWholePercent;
            return false;
        }

        percent = whole;
        error = ZoomPercentInputError.None;
        return true;
    }

    public double SliderToPercent(double sliderValue)
    {
        sliderValue = Math.Clamp(
            sliderValue,
            DefaultMinimumSliderValue,
            DefaultMaximumSliderValue);

        return sliderValue <= DefaultMiddleSliderValue
            ? MinimumPercent + sliderValue / DefaultMiddleSliderValue * (DefaultPercent - MinimumPercent)
            : DefaultPercent +
              (sliderValue - DefaultMiddleSliderValue) /
              DefaultMiddleSliderValue *
              (MaximumPercent - DefaultPercent);
    }

    public double PercentToSlider(double percent)
    {
        percent = ClampPercent(percent);
        return percent <= DefaultPercent
            ? (percent - MinimumPercent) / (DefaultPercent - MinimumPercent) * DefaultMiddleSliderValue
            : DefaultMiddleSliderValue +
              (percent - DefaultPercent) /
              (MaximumPercent - DefaultPercent) *
              DefaultMiddleSliderValue;
    }

    private static string? NormalizePercentInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim().TrimEnd('%').Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
