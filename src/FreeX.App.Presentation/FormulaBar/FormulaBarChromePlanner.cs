namespace FreeX.App.Presentation.FormulaBar;

public enum FormulaBarChromeElement
{
    NameBox,
    FormulaBox,
    CancelEditButton,
    EnterEditButton,
    InsertFunctionButton,
    ExpandButton,
    CollapseButton,
    Row
}

public enum FormulaBarChromeGlyph
{
    None,
    Cancel,
    Enter,
    Text,
    Chevron
}

public readonly record struct FormulaBarChromeElementPlan(
    FormulaBarChromeElement Element,
    string AutomationId,
    string AutomationNameResourceKey,
    string HelpTextResourceKey,
    string CommandName,
    string KeyTip,
    FormulaBarChromeGlyph Glyph,
    string ContentResourceKey = "",
    bool IsItalic = false);

public readonly record struct FormulaBarExpansionPlan(
    bool Expanded,
    bool AcceptsReturn,
    double EditorHeight,
    double HostHeight,
    bool ChevronPointsUp,
    FormulaBarChromeElementPlan Button,
    string TooltipTitleResourceKey,
    string TooltipDescriptionResourceKey);

public static class FormulaBarChromePlanner
{
    public const double CollapsedEditorHeight = 30;
    public const double ExpandedEditorHeight = 84;
    public const double CollapsedHostHeight = 40;
    public const double ExpandedHostHeight = 94;

    public static FormulaBarChromeElementPlan NameBox { get; } = new(
        FormulaBarChromeElement.NameBox,
        "CellAddressBox",
        "MainWindow_AutomationName_NameBox",
        "MainWindow_AutomationHelpText_GoToACellOrNamedRange",
        "Name Box",
        "",
        FormulaBarChromeGlyph.None);

    public static FormulaBarChromeElementPlan FormulaBox { get; } = new(
        FormulaBarChromeElement.FormulaBox,
        "FormulaBar",
        "MainWindow_AutomationName_FormulaBar",
        "MainWindow_AutomationHelpText_EditTheActiveCellValueOrFormula",
        "Formula Bar",
        "",
        FormulaBarChromeGlyph.None);

    public static FormulaBarChromeElementPlan CancelEditButton { get; } = new(
        FormulaBarChromeElement.CancelEditButton,
        "FormulaBarCancelButton",
        "MainWindow_TooltipTitle_CancelFormulaBarEdit",
        "MainWindow_TooltipDescription_CancelFormulaBarEdit",
        "Cancel Formula Bar Edit",
        "FC",
        FormulaBarChromeGlyph.Cancel);

    public static FormulaBarChromeElementPlan EnterEditButton { get; } = new(
        FormulaBarChromeElement.EnterEditButton,
        "FormulaBarEnterButton",
        "MainWindow_TooltipTitle_EnterFormulaBarEdit",
        "MainWindow_TooltipDescription_EnterFormulaBarEdit",
        "Enter Formula Bar Edit",
        "FE",
        FormulaBarChromeGlyph.Enter);

    public static FormulaBarChromeElementPlan InsertFunctionButton { get; } = new(
        FormulaBarChromeElement.InsertFunctionButton,
        "FormulaBarFxButton",
        "MainWindow_TooltipTitle_InsertFunction",
        "MainWindow_TooltipDescription_SearchForAndInsertAFunctionIntoTheSelectedCell",
        "Insert Function",
        "FX",
        FormulaBarChromeGlyph.Text,
        "MainWindow_Content_Fx",
        IsItalic: true);

    public static FormulaBarChromeElementPlan ExpandButton { get; } = new(
        FormulaBarChromeElement.ExpandButton,
        "FormulaBarExpandBtn",
        "MainWindow_AutomationName_ExpandFormulaBar",
        "MainWindow_AutomationHelpText_ExpandTheFormulaBarToAMultiLineEditor",
        "Expand Formula Bar",
        "BX",
        FormulaBarChromeGlyph.Chevron);

    public static FormulaBarChromeElementPlan CollapseButton { get; } = new(
        FormulaBarChromeElement.CollapseButton,
        "FormulaBarExpandBtn",
        "MainWindow_AutomationName_CollapseFormulaBar",
        "MainWindow_AutomationHelpText_CollapseTheFormulaBarToASingleLineEditor",
        "Collapse Formula Bar",
        "BX",
        FormulaBarChromeGlyph.Chevron);

    public static FormulaBarChromeElementPlan Row { get; } = new(
        FormulaBarChromeElement.Row,
        "FormulaBarRow",
        "MainWindow_AutomationName_FormulaBar",
        "MainWindow_AutomationHelpText_EditTheActiveCellValueOrFormula",
        "Formula Bar Row",
        "",
        FormulaBarChromeGlyph.None);

    public static IReadOnlyList<FormulaBarChromeElementPlan> CommandButtons { get; } =
    [
        CancelEditButton,
        EnterEditButton,
        InsertFunctionButton
    ];

    public static FormulaBarChromeElementPlan ExpansionButton(bool expanded) =>
        BuildExpansion(expanded).Button;

    public static FormulaBarExpansionPlan BuildExpansion(bool expanded) =>
        expanded
            ? new FormulaBarExpansionPlan(
                Expanded: true,
                AcceptsReturn: true,
                EditorHeight: ExpandedEditorHeight,
                HostHeight: ExpandedHostHeight,
                ChevronPointsUp: true,
                Button: CollapseButton,
                TooltipTitleResourceKey: "MainWindow_TooltipTitle_CollapseFormulaBar",
                TooltipDescriptionResourceKey: "MainWindow_TooltipDescription_CollapseTheFormulaBarToASingleLineEditor")
            : new FormulaBarExpansionPlan(
                Expanded: false,
                AcceptsReturn: false,
                EditorHeight: CollapsedEditorHeight,
                HostHeight: CollapsedHostHeight,
                ChevronPointsUp: false,
                Button: ExpandButton,
                TooltipTitleResourceKey: "MainWindow_TooltipTitle_ExpandFormulaBar",
                TooltipDescriptionResourceKey: "MainWindow_TooltipDescription_ExpandTheFormulaBarToAMultiLineEditor");
}
