using System.Globalization;

namespace FreeP.App.Compositor;

public enum SlideSizeDialogSurfaceField
{
    Preset,
    Unit,
    Width,
    Height,
    Validation,
}

public enum SlideSizeDialogAction
{
    Accept,
    Cancel,
}

public sealed record SlideSizeDialogUnitOption(
    SlideSizeDialogUnit Unit,
    string Label);

public sealed record SlideSizeDialogSurfacePlan(
    PresentationDialogSurfacePlan<SlideSizeDialogSurfaceField, SlideSizeDialogAction> Schema,
    IReadOnlyList<string> PresetNames,
    IReadOnlyList<SlideSizeDialogUnitOption> UnitOptions)
{
    public string Title => Schema.Title;

    public PresentationDialogFieldPlan<SlideSizeDialogSurfaceField> Field(
        SlideSizeDialogSurfaceField id) => Schema.Field(id);

    public PresentationDialogActionPlan<SlideSizeDialogAction> Action(
        SlideSizeDialogAction id) => Schema.Action(id);

    public string UnitLabel(SlideSizeDialogUnit unit) =>
        UnitOptions.First(option => option.Unit == unit).Label;
}

public static class SlideSizeDialogSurfaceCatalog
{
    public static SlideSizeDialogSurfacePlan Surface { get; } = new(
        new PresentationDialogSurfacePlan<SlideSizeDialogSurfaceField, SlideSizeDialogAction>(
            "Slide Size",
            "Slide Size",
            "FreeP.SlideSize.Dialog",
            [
                Field(SlideSizeDialogSurfaceField.Preset, PresentationDialogControlKind.Choice,
                    "Preset:", "Slide size preset"),
                Field(SlideSizeDialogSurfaceField.Unit, PresentationDialogControlKind.Choice,
                    "Unit:", "Measurement unit"),
                Field(SlideSizeDialogSurfaceField.Width, PresentationDialogControlKind.Text,
                    "Width:", "Slide width"),
                Field(SlideSizeDialogSurfaceField.Height, PresentationDialogControlKind.Text,
                    "Height:", "Slide height"),
                Field(SlideSizeDialogSurfaceField.Validation, PresentationDialogControlKind.Status,
                    string.Empty, "Slide size validation message"),
            ],
            [
                Action(SlideSizeDialogAction.Accept, "OK", "Apply slide size", isDefault: true),
                Action(SlideSizeDialogAction.Cancel, "Cancel", "Cancel slide size changes", isCancel: true),
            ]),
        [
            "Standard (4:3)",
            "Widescreen (16:9)",
            "Custom",
        ],
        [
            new(SlideSizeDialogUnit.Inches, "Inches"),
            new(SlideSizeDialogUnit.Centimeters, "Centimeters"),
        ]);

    private static PresentationDialogFieldPlan<SlideSizeDialogSurfaceField> Field(
        SlideSizeDialogSurfaceField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName) =>
        new(id, kind, label, accessibleName, $"FreeP.SlideSize.{id}");

    private static PresentationDialogActionPlan<SlideSizeDialogAction> Action(
        SlideSizeDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.SlideSize.{id}", isDefault, isCancel);
}

public sealed record SlideSizeDialogState(
    int PresetIndex,
    SlideSizeDialogUnit Unit,
    SlideSizeDialogDisplayState Display);

public sealed class SlideSizeDialogSession
{
    private readonly EditingSession _editor;
    private readonly CultureInfo? _culture;

    public SlideSizeDialogSession(
        EditingSession editor,
        SlideSizeDialogUnit initialUnit = SlideSizeDialogUnit.Inches,
        CultureInfo? culture = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _culture = culture;
        InitialState = SlideSizeDialogPlanner.BuildInitialState(
            editor.Presentation.SlideSizeCxEmu,
            editor.Presentation.SlideSizeCyEmu,
            initialUnit,
            culture);
        State = new(
            PresetIndex(InitialState.Preset),
            initialUnit,
            InitialState.Display);
    }

    public static IReadOnlyList<string> PresetNames =>
        SlideSizeDialogSurfaceCatalog.Surface.PresetNames;

    public SlideSizeDialogInitialState InitialState { get; }

    public SlideSizeDialogState State { get; private set; }

    public SlideSizeDialogSurfacePlan Surface => SlideSizeDialogSurfaceCatalog.Surface;

    public int InitialPresetIndex => PresetIndex(InitialState.Preset);

    public SlideSizeDialogUnit Unit => State.Unit;

    public SlideSizeDialogResultPlan? LastResultPlan { get; private set; }

    public static int PresetIndex(SlideSizeDialogPreset preset) => preset switch
    {
        SlideSizeDialogPreset.Widescreen169 => 1,
        SlideSizeDialogPreset.Custom => 2,
        _ => 0,
    };

    public static SlideSizeDialogPreset PresetFromIndex(int selectedIndex) => selectedIndex switch
    {
        1 => SlideSizeDialogPreset.Widescreen169,
        2 => SlideSizeDialogPreset.Custom,
        _ => SlideSizeDialogPreset.Standard43,
    };

    public SlideSizeDialogDisplayState? SelectPreset(int selectedIndex)
    {
        var preset = PresetFromIndex(selectedIndex);
        var display = SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
            preset,
            State.Unit,
            _culture);
        State = State with
        {
            PresetIndex = PresetIndex(preset),
            Display = display ?? State.Display,
        };
        return display;
    }

    public SlideSizeDialogState ChangeUnit(
        string? widthText,
        string? heightText,
        SlideSizeDialogUnit newUnit)
    {
        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            State.Unit,
            newUnit,
            _culture);
        State = State with { Unit = newUnit, Display = display };
        return State;
    }

    public SlideSizeDialogState SetInputUnit(
        string? widthText,
        string? heightText,
        SlideSizeDialogUnit unit)
    {
        State = State with
        {
            Unit = unit,
            Display = SlideSizeDialogPlanner.BuildInputDisplay(widthText, heightText, unit),
        };
        return State;
    }

    public SlideSizeDialogState SetInput(string? widthText, string? heightText)
    {
        State = State with
        {
            Display = SlideSizeDialogPlanner.BuildInputDisplay(
                widthText,
                heightText,
                State.Unit),
        };
        return State;
    }

    public SlideSizeDialogParsePlan TryParse(string? widthText, string? heightText)
    {
        SetInput(widthText, heightText);
        return SlideSizeDialogPlanner.TryParsePositiveSize(
            State.Display.WidthText,
            State.Display.HeightText,
            State.Unit,
            _culture);
    }

    public SlideSizeDialogResultPlan BuildResult(string? widthText, string? heightText)
    {
        SetInput(widthText, heightText);
        LastResultPlan = SlideSizeDialogPlanner.BuildOkResult(
            State.Display.WidthText,
            State.Display.HeightText,
            State.Unit,
            _culture);
        return LastResultPlan;
    }

    public bool TryCommit(string? widthText, string? heightText)
        => SlideSizeDialogPlanner.TryApplyResult(
            _editor,
            BuildResult(widthText, heightText));
}
