namespace FreeP.Ribbon.Definitions;

public static class FreePRibbonDefinitionData
{
    public static readonly string[] FontFamilies =
        ["Calibri", "Arial", "Segoe UI", "Georgia", "Verdana"];

    public static readonly string[] FontSizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "44", "54", "66", "80", "96"];

    public static readonly string[] FontColors =
        ["Automatic", "Black", "White", "Red", "Green", "Blue", "Yellow", "Orange", "Purple", "Dark Red", "Dark Blue"];

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
