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

public sealed class SlideShowSettingsDialogSession
{
    private readonly EditingSession _editor;

    public SlideShowSettingsDialogSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        InitialState = SlideShowSettingsPlanner.BuildState(editor.Presentation);
        InitialInput = new SlideShowSettingsDialogInput(
            InitialState.UseSlideTimings,
            !InitialState.ShowWithAnimation,
            InitialState.LoopUntilStopped,
            (int)InitialState.ShowType,
            InitialState.ShowBrowseScrollbar,
            FormatRestartMilliseconds(
                InitialState.KioskRestartAfterMilliseconds),
            InitialState.ShowWithNarration,
            InitialState.ShowMediaControls,
            InitialState.ShowMasterShapes);
    }

    public SlideShowSettingsState InitialState { get; }

    public SlideShowSettingsDialogInput InitialInput { get; }

    public bool TryApply(SlideShowSettingsDialogInput input) =>
        SlideShowSettingsPlanner.TryApply(
            _editor,
            input.UseSlideTimings,
            !input.ShowWithoutAnimation,
            input.LoopUntilStopped,
            ShowTypeFromIndex(input.ShowTypeIndex),
            input.ShowBrowseScrollbar,
            ParseRestartMilliseconds(input.KioskRestartMilliseconds),
            input.ShowWithNarration,
            input.ShowMediaControls,
            input.ShowMasterShapes);

    public static PresentationShowType ShowTypeFromIndex(int selectedIndex) =>
        (PresentationShowType)Math.Clamp(selectedIndex, 0, 2);

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
