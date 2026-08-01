namespace FreeP.Ribbon.Definitions;

public static class FreePRibbonDefinitionData
{
    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Segoe UI", "Georgia", "Verdana"];

    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "44", "54", "66", "80", "96"];

    public static readonly string[] FontColors =
        ["Automatic", "Black", "White", "Red", "Green", "Blue", "Yellow", "Orange", "Purple", "Dark Red", "Dark Blue"];

    public static readonly string[] TextAutoFitOptions =
        ["Do not autofit", "Shrink text on overflow", "Resize shape to fit text"];

    public static readonly string[] TextVerticalTypeOptions =
        ["Horizontal", "Rotate 90 degrees", "Rotate 270 degrees", "East Asian vertical", "WordArt vertical", "WordArt vertical RTL"];

    public static readonly string[] TextColumnCountOptions =
        ["1", "2", "3", "4", "5", "6"];

    public static readonly string[] TextColumnSpacingOptions =
        ["0 pt", "4 pt", "8 pt", "12 pt", "16 pt", "24 pt", "36 pt"];

    public static readonly string[] TableCellFillColors = FontColors;

    public static readonly string[] TableCellAnchorOptions =
        ["Automatic", "Top", "Middle", "Bottom"];

    public static readonly string[] TableCellBorderOptions =
    [
        "Left:Automatic", "Left:None", "Left:Black 0.5pt", "Left:Black 1pt",
        "Right:Automatic", "Right:None", "Right:Black 0.5pt", "Right:Black 1pt",
        "Top:Automatic", "Top:None", "Top:Black 0.5pt", "Top:Black 1pt",
        "Bottom:Automatic", "Bottom:None", "Bottom:Black 0.5pt", "Bottom:Black 1pt",
    ];

    public static readonly string[] TableCellInsetOptions =
    [
        "All:Automatic", "All:0pt", "All:2pt", "All:4pt", "All:6pt", "All:8pt",
        "Left:Automatic", "Left:0pt", "Left:2pt", "Left:4pt", "Left:6pt", "Left:8pt",
        "Right:Automatic", "Right:0pt", "Right:2pt", "Right:4pt", "Right:6pt", "Right:8pt",
        "Top:Automatic", "Top:0pt", "Top:2pt", "Top:4pt", "Top:6pt", "Top:8pt",
        "Bottom:Automatic", "Bottom:0pt", "Bottom:2pt", "Bottom:4pt", "Bottom:6pt", "Bottom:8pt",
    ];

    public static readonly string[] TableRowHeightOptions =
        ["Automatic", "0.25in", "0.5in", "0.75in", "1in", "1.5in"];

    public static readonly string[] TransitionDurations =
        ["0.50s", "0.75s", "1.00s", "1.50s", "2.00s"];

    public static string[] TransitionAdvanceAfterOptions =>
        [FreePRibbonText.TransitionAdvanceAfterNoneOption, "1s", "2s", "3s", "5s", "10s"];

    public static string[] AnimationTriggers =>
    [
        FreePRibbonText.AnimationTriggerOnClickOption,
        FreePRibbonText.AnimationTriggerWithPreviousOption,
        FreePRibbonText.AnimationTriggerAfterPreviousOption,
    ];

    public static readonly string[] AnimationDurations =
        ["0.25s", "0.50s", "1.00s", "1.50s", "2.00s"];

    public static readonly string[] AnimationDelays =
        ["0s", "0.25s", "0.50s", "1.00s", "2.00s"];
}
