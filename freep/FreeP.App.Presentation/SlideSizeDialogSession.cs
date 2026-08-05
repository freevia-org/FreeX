using System.Globalization;

namespace FreeP.App.Compositor;

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
        Unit = initialUnit;
        InitialState = SlideSizeDialogPlanner.BuildInitialState(
            editor.Presentation.SlideSizeCxEmu,
            editor.Presentation.SlideSizeCyEmu,
            Unit,
            culture);
    }

    public static IReadOnlyList<string> PresetNames { get; } =
    [
        "Standard (4:3)",
        "Widescreen (16:9)",
        "Custom",
    ];

    public SlideSizeDialogInitialState InitialState { get; }

    public int InitialPresetIndex => PresetIndex(InitialState.Preset);

    public SlideSizeDialogUnit Unit { get; private set; }

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
        => SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
            PresetFromIndex(selectedIndex),
            Unit,
            _culture);

    public SlideSizeDialogDisplayState ChangeUnit(
        string? widthText,
        string? heightText,
        SlideSizeDialogUnit newUnit)
    {
        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            Unit,
            newUnit,
            _culture);
        Unit = newUnit;
        return display;
    }

    public SlideSizeDialogDisplayState SetInputUnit(
        string? widthText,
        string? heightText,
        SlideSizeDialogUnit unit)
    {
        Unit = unit;
        return new(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            unit == SlideSizeDialogUnit.Inches ? "in" : "cm");
    }

    public SlideSizeDialogParsePlan TryParse(string? widthText, string? heightText)
        => SlideSizeDialogPlanner.TryParsePositiveSize(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            Unit,
            _culture);

    public SlideSizeDialogResultPlan BuildResult(string? widthText, string? heightText)
    {
        LastResultPlan = SlideSizeDialogPlanner.BuildOkResult(
            widthText ?? string.Empty,
            heightText ?? string.Empty,
            Unit,
            _culture);
        return LastResultPlan;
    }

    public bool TryApply(string? widthText, string? heightText)
        => SlideSizeDialogPlanner.TryApplyResult(
            _editor,
            BuildResult(widthText, heightText));
}
