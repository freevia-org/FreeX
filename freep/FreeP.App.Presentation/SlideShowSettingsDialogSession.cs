using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowSettingsDialogInput(
    bool UseSlideTimings,
    bool ShowWithoutAnimation,
    bool LoopUntilStopped,
    int ShowTypeIndex,
    bool ShowBrowseScrollbar,
    string KioskRestartMilliseconds,
    bool ShowWithNarration,
    bool ShowMediaControls,
    bool ShowMasterShapes);

public sealed record SlideShowSettingsShowTypeOption(
    PresentationShowType ShowType,
    string Label)
{
    public override string ToString() => Label;
}

public sealed record SlideShowSettingsDialogCommitPlan(
    SlideShowSettingsState Settings);

public sealed class SlideShowSettingsDialogSession
{
    private readonly EditingSession _editor;

    public static IReadOnlyList<SlideShowSettingsShowTypeOption> ShowTypeOptions { get; } =
    [
        new(PresentationShowType.PresentedBySpeaker, "Presented by a speaker"),
        new(PresentationShowType.BrowsedByIndividual, "Browsed by an individual"),
        new(PresentationShowType.BrowsedAtKiosk, "Browsed at a kiosk"),
    ];

    public SlideShowSettingsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        InitialState = SlideShowSettingsPlanner.BuildState(editor.Presentation);
        InitialInput = CreateInput(
            InitialState.UseSlideTimings,
            !InitialState.ShowWithAnimation,
            InitialState.LoopUntilStopped,
            ShowTypeIndex(InitialState.ShowType),
            InitialState.ShowBrowseScrollbar,
            FormatRestartMilliseconds(
                InitialState.KioskRestartAfterMilliseconds),
            InitialState.ShowWithNarration,
            InitialState.ShowMediaControls,
            InitialState.ShowMasterShapes);
    }

    public SlideShowSettingsState InitialState { get; }

    public SlideShowSettingsDialogInput InitialInput { get; }

    public SlideShowSettingsDialogCommitPlan? LastCommitPlan { get; private set; }

    public static SlideShowSettingsDialogInput CreateInput(
        bool useSlideTimings,
        bool showWithoutAnimation,
        bool loopUntilStopped,
        int showTypeIndex,
        bool showBrowseScrollbar,
        string? kioskRestartMilliseconds,
        bool showWithNarration,
        bool showMediaControls,
        bool showMasterShapes) =>
        new(
            useSlideTimings,
            showWithoutAnimation,
            loopUntilStopped,
            showTypeIndex,
            showBrowseScrollbar,
            kioskRestartMilliseconds ?? string.Empty,
            showWithNarration,
            showMediaControls,
            showMasterShapes);

    public SlideShowSettingsDialogCommitPlan BuildCommitPlan(
        SlideShowSettingsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        LastCommitPlan = new SlideShowSettingsDialogCommitPlan(new SlideShowSettingsState(
            input.UseSlideTimings,
            !input.ShowWithoutAnimation,
            input.LoopUntilStopped,
            ShowTypeFromIndex(input.ShowTypeIndex),
            input.ShowBrowseScrollbar,
            ParseRestartMilliseconds(input.KioskRestartMilliseconds),
            input.ShowWithNarration,
            input.ShowMediaControls,
            input.ShowMasterShapes));
        return LastCommitPlan;
    }

    public bool TryApply(SlideShowSettingsDialogInput input) =>
        SlideShowSettingsPlanner.TryApply(
            _editor,
            BuildCommitPlan(input).Settings);

    public static PresentationShowType ShowTypeFromIndex(int selectedIndex)
    {
        var normalizedIndex = Math.Clamp(selectedIndex, 0, ShowTypeOptions.Count - 1);
        return ShowTypeOptions[normalizedIndex].ShowType;
    }

    public static int ShowTypeIndex(PresentationShowType showType)
    {
        for (var index = 0; index < ShowTypeOptions.Count; index++)
        {
            if (ShowTypeOptions[index].ShowType == showType)
                return index;
        }

        return 0;
    }

    public static string FormatRestartMilliseconds(uint? milliseconds) =>
        milliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public static uint? ParseRestartMilliseconds(string? text) =>
        uint.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var milliseconds)
            ? milliseconds
            : null;
}
