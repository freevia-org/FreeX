using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowSettingsDialogField
{
    UseTimings,
    ShowWithoutAnimation,
    PlayNarration,
    ShowMediaControls,
    ShowMasterGraphics,
    LoopUntilStopped,
    ShowType,
    ShowBrowseScrollbar,
    KioskRestartMilliseconds,
}

public enum SlideShowSettingsDialogAction
{
    Accept,
    Cancel,
}

public static class SlideShowSettingsDialogSurfaceCatalog
{
    public static PresentationDialogSurfacePlan<
        SlideShowSettingsDialogField,
        SlideShowSettingsDialogAction> Surface { get; } = new(
            "Set Up Slide Show",
            "Set Up Slide Show",
            "FreeP.SlideShowSettings.Dialog",
            [
                Field(SlideShowSettingsDialogField.UseTimings, PresentationDialogControlKind.Toggle,
                    "Use timings, if present", "Use slide timings if present"),
                Field(SlideShowSettingsDialogField.ShowWithoutAnimation, PresentationDialogControlKind.Toggle,
                    "Show without animation", "Show without animation"),
                Field(SlideShowSettingsDialogField.PlayNarration, PresentationDialogControlKind.Toggle,
                    "Play narration", "Play narration"),
                Field(SlideShowSettingsDialogField.ShowMediaControls, PresentationDialogControlKind.Toggle,
                    "Show media controls", "Show media controls"),
                Field(SlideShowSettingsDialogField.ShowMasterGraphics, PresentationDialogControlKind.Toggle,
                    "Show master graphics", "Show master graphics"),
                Field(SlideShowSettingsDialogField.LoopUntilStopped, PresentationDialogControlKind.Toggle,
                    "Loop until stopped", "Loop slide show until stopped"),
                Field(SlideShowSettingsDialogField.ShowType, PresentationDialogControlKind.Choice,
                    "Show type", "Slide show type"),
                Field(SlideShowSettingsDialogField.ShowBrowseScrollbar, PresentationDialogControlKind.Toggle,
                    "Show scrollbar when browsing", "Show scrollbar when browsing"),
                Field(SlideShowSettingsDialogField.KioskRestartMilliseconds, PresentationDialogControlKind.Text,
                    "Kiosk restart milliseconds (optional)", "Kiosk restart milliseconds"),
            ],
            [
                Action(SlideShowSettingsDialogAction.Accept, "OK", "Apply slide show settings", isDefault: true),
                Action(SlideShowSettingsDialogAction.Cancel, "Cancel", "Cancel slide show settings", isCancel: true),
            ]);

    private static PresentationDialogFieldPlan<SlideShowSettingsDialogField> Field(
        SlideShowSettingsDialogField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName) =>
        new(id, kind, label, accessibleName, $"FreeP.SlideShowSettings.{id}");

    private static PresentationDialogActionPlan<SlideShowSettingsDialogAction> Action(
        SlideShowSettingsDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.SlideShowSettings.{id}", isDefault, isCancel);
}

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

public sealed class SlideShowSettingsDialogInputProjection
{
    private readonly IReadOnlyDictionary<
        SlideShowSettingsDialogField,
        PresentationDialogFieldValue> _fields;

    public SlideShowSettingsDialogInputProjection(
        IReadOnlyDictionary<SlideShowSettingsDialogField, PresentationDialogFieldValue> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        _fields = new Dictionary<SlideShowSettingsDialogField, PresentationDialogFieldValue>(fields);
    }

    public IReadOnlyDictionary<
        SlideShowSettingsDialogField,
        PresentationDialogFieldValue> Fields => _fields;

    public static SlideShowSettingsDialogInputProjection FromInput(
        SlideShowSettingsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(new Dictionary<SlideShowSettingsDialogField, PresentationDialogFieldValue>
        {
            [SlideShowSettingsDialogField.UseTimings] = new(IsChecked: input.UseSlideTimings),
            [SlideShowSettingsDialogField.ShowWithoutAnimation] = new(IsChecked: input.ShowWithoutAnimation),
            [SlideShowSettingsDialogField.PlayNarration] = new(IsChecked: input.ShowWithNarration),
            [SlideShowSettingsDialogField.ShowMediaControls] = new(IsChecked: input.ShowMediaControls),
            [SlideShowSettingsDialogField.ShowMasterGraphics] = new(IsChecked: input.ShowMasterShapes),
            [SlideShowSettingsDialogField.LoopUntilStopped] = new(IsChecked: input.LoopUntilStopped),
            [SlideShowSettingsDialogField.ShowType] = new(SelectedIndex: input.ShowTypeIndex),
            [SlideShowSettingsDialogField.ShowBrowseScrollbar] = new(IsChecked: input.ShowBrowseScrollbar),
            [SlideShowSettingsDialogField.KioskRestartMilliseconds] = new(Text: input.KioskRestartMilliseconds),
        });
    }

    public SlideShowSettingsDialogInput ToInput() =>
        SlideShowSettingsDialogSession.CreateInput(
            IsChecked(SlideShowSettingsDialogField.UseTimings),
            IsChecked(SlideShowSettingsDialogField.ShowWithoutAnimation),
            IsChecked(SlideShowSettingsDialogField.LoopUntilStopped),
            Value(SlideShowSettingsDialogField.ShowType).SelectedIndex,
            IsChecked(SlideShowSettingsDialogField.ShowBrowseScrollbar),
            Value(SlideShowSettingsDialogField.KioskRestartMilliseconds).Text,
            IsChecked(SlideShowSettingsDialogField.PlayNarration),
            IsChecked(SlideShowSettingsDialogField.ShowMediaControls),
            IsChecked(SlideShowSettingsDialogField.ShowMasterGraphics));

    private PresentationDialogFieldValue Value(SlideShowSettingsDialogField field) =>
        _fields.TryGetValue(field, out var value)
            ? value
            : new();

    private bool IsChecked(SlideShowSettingsDialogField field) =>
        Value(field).IsChecked == true;
}

/// <summary>
/// Owns renderer-neutral slide-show-settings form capture and application. Native hosts retain
/// control construction and the small value adapters required by each UI framework.
/// </summary>
public sealed class SlideShowSettingsDialogFormSession<TControl>
    where TControl : class
{
    private readonly Dictionary<SlideShowSettingsDialogField, TControl> _controls = [];
    private readonly Func<TControl, PresentationDialogFieldValue> _captureValue;
    private readonly Action<TControl, PresentationDialogFieldValue> _applyValue;

    public SlideShowSettingsDialogFormSession(
        Func<TControl, PresentationDialogFieldValue> captureValue,
        Action<TControl, PresentationDialogFieldValue> applyValue)
    {
        _captureValue = captureValue ?? throw new ArgumentNullException(nameof(captureValue));
        _applyValue = applyValue ?? throw new ArgumentNullException(nameof(applyValue));
    }

    public void Register(SlideShowSettingsDialogField field, TControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _controls.Add(field, control);
    }

    public void RegisterStandardControls(
        TControl useTimings,
        TControl showWithoutAnimation,
        TControl playNarration,
        TControl showMediaControls,
        TControl showMasterGraphics,
        TControl loopUntilStopped,
        TControl showType,
        TControl showBrowseScrollbar,
        TControl kioskRestartMilliseconds)
    {
        Register(SlideShowSettingsDialogField.UseTimings, useTimings);
        Register(SlideShowSettingsDialogField.ShowWithoutAnimation, showWithoutAnimation);
        Register(SlideShowSettingsDialogField.PlayNarration, playNarration);
        Register(SlideShowSettingsDialogField.ShowMediaControls, showMediaControls);
        Register(SlideShowSettingsDialogField.ShowMasterGraphics, showMasterGraphics);
        Register(SlideShowSettingsDialogField.LoopUntilStopped, loopUntilStopped);
        Register(SlideShowSettingsDialogField.ShowType, showType);
        Register(SlideShowSettingsDialogField.ShowBrowseScrollbar, showBrowseScrollbar);
        Register(SlideShowSettingsDialogField.KioskRestartMilliseconds, kioskRestartMilliseconds);
    }

    public SlideShowSettingsDialogInput CaptureInput() =>
        new SlideShowSettingsDialogInputProjection(
            _controls.ToDictionary(
                pair => pair.Key,
                pair => _captureValue(pair.Value)))
            .ToInput();

    public void ApplyInput(SlideShowSettingsDialogInput input)
    {
        var projection = SlideShowSettingsDialogInputProjection.FromInput(input);
        foreach (var (field, value) in projection.Fields)
        {
            if (_controls.TryGetValue(field, out var control))
                _applyValue(control, value);
        }
    }
}

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

    public PresentationDialogSurfacePlan<
        SlideShowSettingsDialogField,
        SlideShowSettingsDialogAction> Surface =>
        SlideShowSettingsDialogSurfaceCatalog.Surface;

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
