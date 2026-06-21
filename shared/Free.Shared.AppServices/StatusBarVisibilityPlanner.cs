namespace Free.Shared.AppServices;

public static class StatusBarOptionTags
{
    public const string CellMode = "CellMode";
    public const string EndMode = "EndMode";
    public const string SelectionMode = "SelectionMode";
    public const string PageNumber = "PageNumber";
    public const string Average = "Average";
    public const string Count = "Count";
    public const string NumericalCount = "NumericalCount";
    public const string Minimum = "Minimum";
    public const string Maximum = "Maximum";
    public const string Sum = "Sum";
    public const string ViewShortcuts = "ViewShortcuts";
    public const string Zoom = "Zoom";
    public const string ZoomSlider = "ZoomSlider";
}

public sealed record StatusBarOptionVisibility(
    bool CellMode = true,
    bool EndMode = false,
    bool SelectionMode = false,
    bool PageNumber = false,
    bool Average = true,
    bool Count = true,
    bool NumericalCount = false,
    bool Minimum = false,
    bool Maximum = false,
    bool Sum = true,
    bool ViewShortcuts = true,
    bool Zoom = true,
    bool ZoomSlider = true)
{
    public static StatusBarOptionVisibility ExcelDefaults { get; } = new();

    public static StatusBarOptionVisibility FullReadoutDefaults { get; } =
        new(SelectionMode: true, NumericalCount: true);

    public static StatusBarOptionVisibility From(Func<string, bool> isOptionVisible)
    {
        ArgumentNullException.ThrowIfNull(isOptionVisible);

        return new StatusBarOptionVisibility(
            CellMode: isOptionVisible(StatusBarOptionTags.CellMode),
            EndMode: isOptionVisible(StatusBarOptionTags.EndMode),
            SelectionMode: isOptionVisible(StatusBarOptionTags.SelectionMode),
            PageNumber: isOptionVisible(StatusBarOptionTags.PageNumber),
            Average: isOptionVisible(StatusBarOptionTags.Average),
            Count: isOptionVisible(StatusBarOptionTags.Count),
            NumericalCount: isOptionVisible(StatusBarOptionTags.NumericalCount),
            Minimum: isOptionVisible(StatusBarOptionTags.Minimum),
            Maximum: isOptionVisible(StatusBarOptionTags.Maximum),
            Sum: isOptionVisible(StatusBarOptionTags.Sum),
            ViewShortcuts: isOptionVisible(StatusBarOptionTags.ViewShortcuts),
            Zoom: isOptionVisible(StatusBarOptionTags.Zoom),
            ZoomSlider: isOptionVisible(StatusBarOptionTags.ZoomSlider));
    }

    public Dictionary<string, bool> ToDictionary() =>
        new(StringComparer.Ordinal)
        {
            [StatusBarOptionTags.CellMode] = CellMode,
            [StatusBarOptionTags.EndMode] = EndMode,
            [StatusBarOptionTags.SelectionMode] = SelectionMode,
            [StatusBarOptionTags.PageNumber] = PageNumber,
            [StatusBarOptionTags.Average] = Average,
            [StatusBarOptionTags.Count] = Count,
            [StatusBarOptionTags.NumericalCount] = NumericalCount,
            [StatusBarOptionTags.Minimum] = Minimum,
            [StatusBarOptionTags.Maximum] = Maximum,
            [StatusBarOptionTags.Sum] = Sum,
            [StatusBarOptionTags.ViewShortcuts] = ViewShortcuts,
            [StatusBarOptionTags.Zoom] = Zoom,
            [StatusBarOptionTags.ZoomSlider] = ZoomSlider,
        };

    public bool IsVisible(string optionTag) =>
        optionTag switch
        {
            StatusBarOptionTags.CellMode => CellMode,
            StatusBarOptionTags.EndMode => EndMode,
            StatusBarOptionTags.SelectionMode => SelectionMode,
            StatusBarOptionTags.PageNumber => PageNumber,
            StatusBarOptionTags.Average => Average,
            StatusBarOptionTags.Count => Count,
            StatusBarOptionTags.NumericalCount => NumericalCount,
            StatusBarOptionTags.Minimum => Minimum,
            StatusBarOptionTags.Maximum => Maximum,
            StatusBarOptionTags.Sum => Sum,
            StatusBarOptionTags.ViewShortcuts => ViewShortcuts,
            StatusBarOptionTags.Zoom => Zoom,
            StatusBarOptionTags.ZoomSlider => ZoomSlider,
            _ => false
        };

    public StatusBarOptionVisibility With(string optionTag, bool isVisible) =>
        optionTag switch
        {
            StatusBarOptionTags.CellMode => this with { CellMode = isVisible },
            StatusBarOptionTags.EndMode => this with { EndMode = isVisible },
            StatusBarOptionTags.SelectionMode => this with { SelectionMode = isVisible },
            StatusBarOptionTags.PageNumber => this with { PageNumber = isVisible },
            StatusBarOptionTags.Average => this with { Average = isVisible },
            StatusBarOptionTags.Count => this with { Count = isVisible },
            StatusBarOptionTags.NumericalCount => this with { NumericalCount = isVisible },
            StatusBarOptionTags.Minimum => this with { Minimum = isVisible },
            StatusBarOptionTags.Maximum => this with { Maximum = isVisible },
            StatusBarOptionTags.Sum => this with { Sum = isVisible },
            StatusBarOptionTags.ViewShortcuts => this with { ViewShortcuts = isVisible },
            StatusBarOptionTags.Zoom => this with { Zoom = isVisible },
            StatusBarOptionTags.ZoomSlider => this with { ZoomSlider = isVisible },
            _ => this
        };
}

public sealed record StatusBarVisibilityPlan(
    bool ReadyTextVisible,
    bool PageNumberVisible,
    bool StatsPanelVisible,
    bool AverageVisible,
    bool CountVisible,
    bool NumericalCountVisible,
    bool SumVisible,
    bool MinimumVisible,
    bool MaximumVisible,
    bool ViewShortcutsVisible,
    bool ZoomVisible,
    bool ZoomSliderVisible,
    bool ZoomControlsVisible,
    bool InteractiveControlsVisible,
    IReadOnlyList<StatusBarReadoutItem> VisibleReadouts,
    string VisibleReadoutText,
    string AutomationText);

public static class StatusBarVisibilityPlanner
{
    public const string ReadoutSeparator = "   ";
    private const string AutomationSeparator = "; ";

    public static Dictionary<string, bool> CreateDefaultOptionVisibility(StatusBarOptionVisibility defaults) =>
        defaults.ToDictionary();

    public static bool IsOptionVisible(IReadOnlyDictionary<string, bool> optionVisibility, string optionTag) =>
        optionVisibility.TryGetValue(optionTag, out var visible) && visible;

    public static StatusBarOptionVisibility FromOptionVisibility(IReadOnlyDictionary<string, bool> optionVisibility)
    {
        ArgumentNullException.ThrowIfNull(optionVisibility);

        return StatusBarOptionVisibility.From(optionTag => IsOptionVisible(optionVisibility, optionTag));
    }

    public static StatusBarVisibilityPlan Build(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility,
        bool hasPageNumberText = false,
        string fallbackAutomationText = "")
    {
        ArgumentNullException.ThrowIfNull(model);

        var visibleReadouts = GetVisibleReadouts(model, optionVisibility);
        return new StatusBarVisibilityPlan(
            ReadyTextVisible: optionVisibility.CellMode && model.IsReadyVisible,
            PageNumberVisible: optionVisibility.PageNumber && hasPageNumberText,
            StatsPanelVisible: model.AreStatsVisible && HasVisibleStatisticOption(optionVisibility),
            AverageVisible: optionVisibility.Average,
            CountVisible: optionVisibility.Count,
            NumericalCountVisible: optionVisibility.NumericalCount,
            SumVisible: optionVisibility.Sum,
            MinimumVisible: optionVisibility.Minimum,
            MaximumVisible: optionVisibility.Maximum,
            ViewShortcutsVisible: optionVisibility.ViewShortcuts,
            ZoomVisible: optionVisibility.Zoom,
            ZoomSliderVisible: optionVisibility.ZoomSlider,
            ZoomControlsVisible: optionVisibility.Zoom || optionVisibility.ZoomSlider,
            InteractiveControlsVisible: optionVisibility.ViewShortcuts || optionVisibility.Zoom || optionVisibility.ZoomSlider,
            VisibleReadouts: visibleReadouts,
            VisibleReadoutText: JoinReadoutValues(visibleReadouts, ReadoutSeparator),
            AutomationText: FormatAutomationText(visibleReadouts, fallbackAutomationText));
    }

    public static IReadOnlyList<StatusBarReadoutItem> GetVisibleReadouts(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.AreStatsVisible)
            return [];

        var readouts = new List<StatusBarReadoutItem>(model.Readouts.Count);
        foreach (var readout in model.Readouts)
        {
            if (!readout.IsVisible || readout.Value.Length == 0)
                continue;
            if (!optionVisibility.IsVisible(ReadoutOptionTag(readout.Kind)))
                continue;

            readouts.Add(readout);
        }

        return readouts;
    }

    public static string FormatVisibleReadouts(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility) =>
        JoinReadoutValues(GetVisibleReadouts(model, optionVisibility), ReadoutSeparator);

    public static string FormatAutomationText(
        StatusBarViewModel model,
        StatusBarOptionVisibility optionVisibility,
        string fallbackAutomationText) =>
        FormatAutomationText(GetVisibleReadouts(model, optionVisibility), fallbackAutomationText);

    public static bool HasVisibleStatisticOption(StatusBarOptionVisibility optionVisibility) =>
        optionVisibility.Average ||
        optionVisibility.Count ||
        optionVisibility.NumericalCount ||
        optionVisibility.Sum ||
        optionVisibility.Minimum ||
        optionVisibility.Maximum;

    public static string ReadoutOptionTag(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => StatusBarOptionTags.Average,
            StatusBarReadoutKind.Count => StatusBarOptionTags.Count,
            StatusBarReadoutKind.NumericalCount => StatusBarOptionTags.NumericalCount,
            StatusBarReadoutKind.Sum => StatusBarOptionTags.Sum,
            StatusBarReadoutKind.Minimum => StatusBarOptionTags.Minimum,
            StatusBarReadoutKind.Maximum => StatusBarOptionTags.Maximum,
            _ => StatusBarOptionTags.Count
        };

    private static string FormatAutomationText(
        IReadOnlyList<StatusBarReadoutItem> readouts,
        string fallbackAutomationText)
    {
        if (readouts.Count == 0)
            return fallbackAutomationText;

        return JoinReadoutValues(readouts, AutomationSeparator);
    }

    private static string JoinReadoutValues(
        IReadOnlyList<StatusBarReadoutItem> readouts,
        string separator)
    {
        if (readouts.Count == 0)
            return "";

        var values = new string[readouts.Count];
        for (var index = 0; index < readouts.Count; index++)
            values[index] = readouts[index].Value;

        return string.Join(separator, values);
    }
}
