using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum TabsDialogValidationError
{
    NonNegativePositionRequired,
    PositiveDefaultTabStopRequired
}

public sealed record TabsDialogChoice<TValue>(TValue Value, string Label);

public sealed record TabsDialogStopRow(TabStop Stop, string DisplayText);

public sealed record TabsDialogState(
    IReadOnlyList<TabStop> TabStops,
    IReadOnlyList<TabsDialogStopRow> Rows,
    string DefaultTabStopText);

public sealed record TabsDialogStopSelection(
    string PositionText,
    int AlignmentIndex,
    int LeaderIndex);

public sealed record TabsDialogSetRequest(
    string? PositionText,
    int AlignmentIndex,
    int LeaderIndex);

public sealed record TabsDialogSetPlan(
    TabsDialogState State,
    int SelectedIndex);

public sealed record TabsDialogResult(
    IReadOnlyList<TabStop> TabStops,
    double DefaultTabStopPt);

public sealed record TabsDialogMutationPlan(
    bool Applied,
    TabsDialogState State,
    int SelectedIndex,
    TabsDialogValidationError? ValidationError)
{
    public string? ValidationMessage => Applied
        ? null
        : TabsDialogPlanner.ValidationMessageFor(ValidationError);
}

public sealed record TabsDialogAcceptance(
    TabsDialogResult? Result,
    TabsDialogValidationError? ValidationError)
{
    public bool IsAccepted => Result is not null;

    public string? ValidationMessage => IsAccepted
        ? null
        : TabsDialogPlanner.ValidationMessageFor(ValidationError);
}

public sealed class TabsDialogSession
{
    private readonly CultureInfo _culture;

    public TabsDialogSession(
        IReadOnlyList<TabStop> tabStops,
        double defaultTabStopPt,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        State = TabsDialogPlanner.BuildInitialState(tabStops, defaultTabStopPt, culture);
    }

    public IReadOnlyList<TabsDialogChoice<TabStopAlignment>> Alignments => TabsDialogPlanner.Alignments;

    public IReadOnlyList<TabsDialogChoice<TabLeader>> Leaders => TabsDialogPlanner.Leaders;

    public TabsDialogState State { get; private set; }

    public TabsDialogStopSelection? ProjectSelection(int selectedIndex) =>
        TabsDialogPlanner.ProjectSelectedStop(State, selectedIndex, _culture);

    public TabsDialogMutationPlan SetStop(TabsDialogSetRequest request)
    {
        if (!TabsDialogPlanner.TrySetStop(State, request, _culture, out var plan, out var error))
            return new TabsDialogMutationPlan(false, State, -1, error);

        State = plan!.State;
        return new TabsDialogMutationPlan(true, State, plan.SelectedIndex, ValidationError: null);
    }

    public TabsDialogState ClearStop(int selectedIndex, string? positionText)
    {
        State = TabsDialogPlanner.ClearStop(State, selectedIndex, positionText, _culture);
        return State;
    }

    public TabsDialogState ClearAll()
    {
        State = TabsDialogPlanner.ClearAll(State);
        return State;
    }

    public TabsDialogAcceptance PlanAcceptance(string? defaultTabStopText)
    {
        if (!TabsDialogPlanner.TryBuildResult(State, defaultTabStopText, _culture, out var result, out var error))
            return new TabsDialogAcceptance(null, error);

        return new TabsDialogAcceptance(result, ValidationError: null);
    }
}

public static class TabsDialogPlanner
{
    public const double PositionTolerancePt = 0.01;
    public const string Title = "Tabs";
    public const string PositionLabel = "Tab stop position (pt):";
    public const string StopsLabel = "Stops:";
    public const string AlignmentLabel = "Alignment:";
    public const string LeaderLabel = "Leader:";
    public const string DefaultTabStopLabel = "Default tab stops (pt):";
    public const string SetButtonLabel = "Set";
    public const string SetButtonAccessLabel = "_Set";
    public const string ClearButtonLabel = "Clear";
    public const string ClearButtonAccessLabel = "C_lear";
    public const string ClearAllButtonLabel = "Clear All";
    public const string ClearAllButtonAccessLabel = "Clear _All";
    public const string AutomationId = "TabsDialog";
    public const string StopListAutomationId = "TabsStopList";
    public const string PositionAutomationId = "TabsPositionTextBox";
    public const string AlignmentAutomationId = "TabsAlignmentComboBox";
    public const string LeaderAutomationId = "TabsLeaderComboBox";
    public const string DefaultTabStopAutomationId = "TabsDefaultStopTextBox";

    public static readonly IReadOnlyList<TabsDialogChoice<TabStopAlignment>> Alignments =
    [
        new(TabStopAlignment.Left, "Left"),
        new(TabStopAlignment.Center, "Center"),
        new(TabStopAlignment.Right, "Right"),
        new(TabStopAlignment.Decimal, "Decimal")
    ];

    public static readonly IReadOnlyList<TabsDialogChoice<TabLeader>> Leaders =
    [
        new(TabLeader.None, "1 None"),
        new(TabLeader.Dots, "2 ...."),
        new(TabLeader.Dashes, "3 ----"),
        new(TabLeader.Underline, "4 ____")
    ];

    public static TabsDialogState BuildInitialState(
        IReadOnlyList<TabStop> tabStops,
        double defaultTabStopPt,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(tabStops);
        ArgumentNullException.ThrowIfNull(culture);

        return CreateState(tabStops, FormatPoints(defaultTabStopPt, culture), culture);
    }

    public static TabsDialogStopSelection? ProjectSelectedStop(
        TabsDialogState state,
        int selectedIndex,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(culture);

        if (selectedIndex < 0 || selectedIndex >= state.TabStops.Count)
            return null;

        var stop = state.TabStops[selectedIndex];
        return new TabsDialogStopSelection(
            FormatPoints(stop.PositionPt, culture),
            ChoiceIndex(Alignments, stop.Alignment),
            ChoiceIndex(Leaders, stop.Leader));
    }

    public static bool TrySetStop(
        TabsDialogState state,
        TabsDialogSetRequest request,
        CultureInfo culture,
        out TabsDialogSetPlan? plan,
        out TabsDialogValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(culture);

        plan = null;
        error = null;

        if (!TryParsePosition(request.PositionText, culture, out var position))
        {
            error = TabsDialogValidationError.NonNegativePositionRequired;
            return false;
        }

        var stop = new TabStop(
            position,
            ChoiceAt(Alignments, request.AlignmentIndex).Value,
            ChoiceAt(Leaders, request.LeaderIndex).Value);

        var stops = state.TabStops.ToList();
        var existing = FindPositionIndex(stops, position);
        if (existing >= 0)
            stops[existing] = stop;
        else
            stops.Add(stop);

        var next = CreateState(stops, state.DefaultTabStopText, culture);
        plan = new TabsDialogSetPlan(next, FindPositionIndex(next.TabStops, position));
        return true;
    }

    public static TabsDialogState ClearStop(
        TabsDialogState state,
        int selectedIndex,
        string? positionText,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(culture);

        var index = selectedIndex;
        if (index < 0 && TryParseDouble(positionText, culture, out var position))
            index = FindPositionIndex(state.TabStops, position);

        if (index < 0 || index >= state.TabStops.Count)
            return state;

        var stops = state.TabStops.Where((_, i) => i != index).ToArray();
        return CreateState(stops, state.DefaultTabStopText, culture);
    }

    public static TabsDialogState ClearAll(TabsDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new TabsDialogState([], [], state.DefaultTabStopText);
    }

    public static bool TryBuildResult(
        TabsDialogState state,
        string? defaultTabStopText,
        CultureInfo culture,
        out TabsDialogResult? result,
        out TabsDialogValidationError? error)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        error = null;

        if (!TryParseDefaultTabStop(defaultTabStopText, culture, out var defaultTabStop))
        {
            error = TabsDialogValidationError.PositiveDefaultTabStopRequired;
            return false;
        }

        result = new TabsDialogResult(NormalizeStops(state.TabStops), defaultTabStop);
        return true;
    }

    public static string ValidationMessageFor(TabsDialogValidationError? error) =>
        error switch
        {
            TabsDialogValidationError.NonNegativePositionRequired =>
                "Enter a non-negative tab-stop position in points.",
            TabsDialogValidationError.PositiveDefaultTabStopRequired =>
                "Enter a positive default tab-stop interval in points.",
            _ => "Enter a valid tab-stop value in points."
        };

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static TabsDialogState CreateState(
        IEnumerable<TabStop> tabStops,
        string defaultTabStopText,
        CultureInfo culture)
    {
        var stops = NormalizeStops(tabStops);
        var rows = stops
            .Select(stop => new TabsDialogStopRow(stop, Describe(stop, culture)))
            .ToArray();
        return new TabsDialogState(stops, rows, defaultTabStopText);
    }

    private static IReadOnlyList<TabStop> NormalizeStops(IEnumerable<TabStop> tabStops)
    {
        ArgumentNullException.ThrowIfNull(tabStops);

        var stops = tabStops.OrderBy(stop => stop.PositionPt).ToList();
        var normalized = new List<TabStop>();
        foreach (var stop in stops)
        {
            var existing = FindPositionIndex(normalized, stop.PositionPt);
            if (existing >= 0)
                normalized[existing] = stop;
            else
                normalized.Add(stop);
        }

        normalized.Sort((a, b) => a.PositionPt.CompareTo(b.PositionPt));
        return normalized;
    }

    private static string Describe(TabStop stop, CultureInfo culture)
    {
        var leader = stop.Leader == TabLeader.None ? "" : $"  {stop.Leader}";
        return $"{FormatPoints(stop.PositionPt, culture)} pt  {stop.Alignment}{leader}";
    }

    private static bool TryParsePosition(string? text, CultureInfo culture, out double value) =>
        TryParseDouble(text, culture, out value) && value >= 0;

    private static bool TryParseDefaultTabStop(string? text, CultureInfo culture, out double value) =>
        TryParseDouble(text, culture, out value) && value > 0;

    private static bool TryParseDouble(string? text, CultureInfo culture, out double value) =>
        double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, culture, out value);

    private static int FindPositionIndex(IReadOnlyList<TabStop> stops, double position)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            if (PositionsMatch(stops[i].PositionPt, position))
                return i;
        }

        return -1;
    }

    private static bool PositionsMatch(double first, double second) =>
        Math.Abs(first - second) < PositionTolerancePt;

    private static TabsDialogChoice<TValue> ChoiceAt<TValue>(
        IReadOnlyList<TabsDialogChoice<TValue>> choices,
        int index) =>
        choices[Math.Clamp(index, 0, choices.Count - 1)];

    private static int ChoiceIndex<TValue>(
        IReadOnlyList<TabsDialogChoice<TValue>> choices,
        TValue value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (EqualityComparer<TValue>.Default.Equals(choices[i].Value, value))
                return i;
        }

        return 0;
    }
}
