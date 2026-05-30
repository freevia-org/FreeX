namespace FreeX.App.Host;

internal sealed record RibbonScreenshotTourTab(string Header, string FileName);

internal sealed record RibbonScreenshotTourWidth(string Label, double? WindowWidth);

internal static class RibbonScreenshotTourWidthExtensions
{
    public static string EvidencePurpose(this RibbonScreenshotTourWidth width) =>
        width.WindowWidth switch
        {
            null => "Maximized baseline before resize pressure.",
            >= 1100 => "Wide ribbon breakpoint before most command groups collapse.",
            >= 900 => "Medium ribbon breakpoint where grouped commands begin to compress.",
            _ => "Narrow ribbon breakpoint for overflow and compact command layouts."
        };
}

internal sealed record RibbonScreenshotTourPhase(string Label, string? FileNameSuffix);

internal sealed record RibbonScreenshotTourPhaseCaptureGroup(
    RibbonScreenshotTourPhase Phase,
    IReadOnlyList<string> FileNames);

internal sealed record RibbonScreenshotTourCapture(
    RibbonScreenshotTourTab Tab,
    RibbonScreenshotTourWidth Width,
    RibbonScreenshotTourPhase Phase)
{
    public string FileName => Phase.FileNameSuffix is { Length: > 0 } suffix
        ? $"{Width.Label}_{Tab.FileName}_{suffix}"
        : $"{Width.Label}_{Tab.FileName}";

    public string OutputFileName => $"{FileName}.png";
}

internal sealed record RibbonScreenshotTourPlan(
    IReadOnlyList<RibbonScreenshotTourTab> Tabs,
    IReadOnlyList<RibbonScreenshotTourWidth> Widths,
    IReadOnlyList<RibbonScreenshotTourPhase> Phases,
    IReadOnlyList<RibbonScreenshotTourCapture> Captures)
{
    public bool IsBurst => Phases.Count > 1;

    public IReadOnlyList<string> ExpectedCaptureFileNames() =>
        Captures
            .Select(capture => capture.OutputFileName)
            .ToArray();

    public IReadOnlyList<RibbonScreenshotTourPhaseCaptureGroup> ExpectedCaptureFileNamesByPhase() =>
        Phases
            .Select(phase => new RibbonScreenshotTourPhaseCaptureGroup(
                phase,
                Captures
                    .Where(capture => capture.Phase == phase)
                    .Select(capture => capture.OutputFileName)
                    .ToArray()))
            .ToArray();
}

internal static class RibbonScreenshotTourPlanner
{
    public static IReadOnlyList<RibbonScreenshotTourTab> DefaultTabs { get; } =
    [
        new("Home", "Home"),
        new("Insert", "Insert"),
        new("Draw", "Draw"),
        new("Page Layout", "Page_Layout"),
        new("Formulas", "Formulas"),
        new("Data", "Data"),
        new("Review", "Review"),
        new("View", "View"),
        new("Help", "Help")
    ];

    public static IReadOnlyList<RibbonScreenshotTourWidth> DefaultWidths { get; } =
    [
        new("max", null),
        new("1100", 1100),
        new("900", 900),
        new("750", 750)
    ];

    public static RibbonScreenshotTourPhase SettledPhase { get; } =
        new("settled", null);

    public static IReadOnlyList<RibbonScreenshotTourPhase> DefaultPhases { get; } =
    [
        SettledPhase
    ];

    public static IReadOnlyList<RibbonScreenshotTourPhase> BurstPhases { get; } =
    [
        new("immediate", "immediate"),
        new("first-render", "first_render"),
        new("settled", "settled")
    ];

    public static RibbonScreenshotTourPlan CreatePlan(string? requestedTabs, string? requestedWidths) =>
        CreatePlan(requestedTabs, requestedWidths, burstMode: false);

    public static RibbonScreenshotTourPlan CreatePlan(string? requestedTabs, string? requestedWidths, bool burstMode)
    {
        var tabs = FilterTabs(DefaultTabs, requestedTabs);
        var widths = ParseWidths(requestedWidths);
        var phases = burstMode ? BurstPhases : DefaultPhases;
        var captures = widths
            .SelectMany(width => tabs.SelectMany(tab => phases.Select(phase => new RibbonScreenshotTourCapture(tab, width, phase))))
            .ToList();

        return new RibbonScreenshotTourPlan(tabs, widths, phases, captures);
    }

    public static IReadOnlyList<RibbonScreenshotTourTab> FilterTabs(
        IReadOnlyList<RibbonScreenshotTourTab> tabs,
        string? requestedTabs)
    {
        if (string.IsNullOrWhiteSpace(requestedTabs))
            return tabs;

        var requested = SplitConfiguredList(requestedTabs, "tab list")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = requested
            .Where(value => !tabs.Any(tab => MatchesTab(tab, value)))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
        {
            var validTabs = string.Join(", ", tabs.Select(tab => tab.Header));
            throw new InvalidOperationException(
                $"Ribbon screenshot tour tab list contains unknown tab(s): {string.Join(", ", unknown)}. Valid tabs: {validTabs}.");
        }

        return tabs
            .Where(tab => requested.Any(value => MatchesTab(tab, value)))
            .ToList();
    }

    public static IReadOnlyList<RibbonScreenshotTourWidth> ParseWidths(string? requestedWidths)
    {
        if (string.IsNullOrWhiteSpace(requestedWidths))
            return DefaultWidths;

        var widths = new List<RibbonScreenshotTourWidth>();
        var invalid = new List<string>();
        foreach (var value in SplitConfiguredList(requestedWidths, "width list"))
        {
            if (string.Equals(value, "max", StringComparison.OrdinalIgnoreCase))
            {
                widths.Add(DefaultWidths[0]);
                continue;
            }

            if (!double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var width) ||
                !double.IsFinite(width) ||
                width <= 0)
            {
                invalid.Add(value);
                continue;
            }

            widths.Add(new RibbonScreenshotTourWidth(width.ToString(System.Globalization.CultureInfo.InvariantCulture), width));
        }

        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour width list contains invalid width(s): {string.Join(", ", invalid)}. Use positive finite numbers or max.");

        return widths;
    }

    private static bool MatchesTab(RibbonScreenshotTourTab tab, string value) =>
        string.Equals(tab.Header, value, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tab.FileName, value, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitConfiguredList(string value, string settingName)
    {
        var entries = value.Split(',');
        var values = new List<string>(entries.Length);
        var emptyPositions = new List<int>();

        for (var index = 0; index < entries.Length; index++)
        {
            var trimmed = entries[index].Trim();
            if (trimmed.Length == 0)
            {
                emptyPositions.Add(index + 1);
                continue;
            }

            values.Add(trimmed);
        }

        if (emptyPositions.Count > 0)
            throw new InvalidOperationException(
                $"Ribbon screenshot tour {settingName} contains empty entr{(emptyPositions.Count == 1 ? "y" : "ies")} at position(s): {string.Join(", ", emptyPositions)}.");

        return values;
    }
}
