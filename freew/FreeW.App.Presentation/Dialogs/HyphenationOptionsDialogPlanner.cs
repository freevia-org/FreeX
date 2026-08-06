using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record HyphenationOptionsInitialState(
    bool AutoHyphenation,
    string ZoneText,
    string ConsecutiveLimitText,
    bool HyphenateCaps);

public sealed record HyphenationOptionsDialogInput(
    bool AutoHyphenation,
    string? ZoneText,
    string? ConsecutiveLimitText,
    bool HyphenateCaps);

public sealed record HyphenationOptionsDialogResult(
    bool AutoHyphenation,
    double ZonePt,
    int ConsecutiveLimit,
    bool HyphenateCaps);

public static class HyphenationOptionsDialogPlanner
{
    public const string Title = "Hyphenation";
    public const string AutomaticLabel = "Automatically hyphenate document";
    public const string ZoneLabel = "Hyphenation zone (pt):";
    public const string ConsecutiveLimitLabel = "Limit consecutive hyphens to (0 = no limit):";
    public const string HyphenateCapsLabel = "Hyphenate words in CAPS";
    public const string ValidationMessage =
        "Enter a non-negative hyphenation zone and a non-negative consecutive-hyphen limit (0 = no limit).";
    public const string AutomationId = "HyphenationOptionsDialog";
    public const string AutomaticAutomationId = "HyphenationAutomatic";
    public const string ZoneAutomationId = "HyphenationZone";
    public const string ConsecutiveLimitAutomationId = "HyphenationConsecutiveLimit";
    public const string HyphenateCapsAutomationId = "HyphenationCaps";

    public static HyphenationOptionsInitialState BuildInitialState(PageSettings page, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(culture);

        return new HyphenationOptionsInitialState(
            AutoHyphenation: page.AutoHyphenation,
            ZoneText: FormatNumber(page.HyphenationZonePt, culture),
            ConsecutiveLimitText: FormatNumber(page.ConsecutiveHyphenLimit, culture),
            HyphenateCaps: !page.DoNotHyphenateCaps);
    }

    public static bool TryBuildResult(
        HyphenationOptionsDialogInput input,
        CultureInfo culture,
        out HyphenationOptionsDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!TryParseNonNegative(input.ZoneText, culture, out var zone) ||
            !TryParseNonNegative(input.ConsecutiveLimitText, culture, out var limitValue))
        {
            errorMessage = ValidationMessage;
            return false;
        }

        result = new HyphenationOptionsDialogResult(
            AutoHyphenation: input.AutoHyphenation,
            ZonePt: zone,
            ConsecutiveLimit: (int)Math.Round(limitValue),
            HyphenateCaps: input.HyphenateCaps);
        return true;
    }

    public static string FormatNumber(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParseNonNegative(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value) && value >= 0;
    }
}
