using FreeP.App.Compositor;
using Free.Shared.Ribbon;

namespace FreeP.Ribbon.Definitions;

public static class FreePRibbonDefinitionData
{
    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Segoe UI", "Georgia", "Verdana"];

    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "44", "54", "66", "80", "96"];

    public static readonly string[] FontColors =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.ColorChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> FontColorChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.ColorChoices);

    public static readonly string[] TextAutoFitOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TextAutoFitChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TextAutoFitChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TextAutoFitChoices);

    public static readonly string[] TextVerticalTypeOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TextVerticalTypeChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TextVerticalTypeChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TextVerticalTypeChoices);

    public static readonly string[] TextColumnCountOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TextColumnCountChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TextColumnCountChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TextColumnCountChoices);

    public static readonly string[] TextColumnSpacingOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TextColumnSpacingChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TextColumnSpacingChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TextColumnSpacingChoices);

    public static readonly string[] TableCellFillColors = FontColors;

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TableCellFillChoices = FontColorChoices;

    public static readonly string[] TableCellAnchorOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TableCellAnchorChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TableCellAnchorChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TableCellAnchorChoices);

    public static readonly string[] TableCellBorderOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TableCellBorderChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TableCellBorderChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TableCellBorderChoices);

    public static readonly string[] TableCellInsetOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TableCellInsetChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TableCellInsetChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TableCellInsetChoices);

    public static readonly string[] TableRowHeightOptions =
        FreePRibbonChoiceCatalog.Labels(FreePRibbonChoiceCatalog.TableRowHeightChoices);

    public static readonly IReadOnlyList<RibbonComboBoxChoice> TableRowHeightChoices =
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TableRowHeightChoices);

    public static readonly string[] TransitionDurations =
        ["0.50s", "0.75s", "1.00s", "1.50s", "2.00s"];

    public static IReadOnlyList<RibbonComboBoxChoice> TransitionDurationChoices =>
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TransitionDurationChoices);

    public static string[] TransitionAdvanceAfterOptions =>
        [FreePRibbonText.TransitionAdvanceAfterNoneOption, "1s", "2s", "3s", "5s", "10s"];

    public static IReadOnlyList<RibbonComboBoxChoice> TransitionAdvanceAfterChoices =>
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.TransitionAdvanceAfterChoices);

    public static string[] AnimationTriggers =>
    [
        FreePRibbonText.AnimationTriggerOnClickOption,
        FreePRibbonText.AnimationTriggerWithPreviousOption,
        FreePRibbonText.AnimationTriggerAfterPreviousOption,
    ];

    public static IReadOnlyList<RibbonComboBoxChoice> AnimationTriggerChoices =>
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.AnimationTriggerChoices);

    public static readonly string[] AnimationDurations =
        ["0.25s", "0.50s", "1.00s", "1.50s", "2.00s"];

    public static IReadOnlyList<RibbonComboBoxChoice> AnimationDurationChoices =>
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.AnimationDurationChoices);

    public static readonly string[] AnimationDelays =
        ["0s", "0.25s", "0.50s", "1.00s", "2.00s"];

    public static IReadOnlyList<RibbonComboBoxChoice> AnimationDelayChoices =>
        FreePRibbonChoiceCatalog.RibbonChoices(FreePRibbonChoiceCatalog.AnimationDelayChoices);
}
