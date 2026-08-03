using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Shared planning layer for the Evaluate Formula dialog. The shells own their visual chrome, while summary
/// creation, step-session construction, and resource-key contracts stay in one portable place.
/// </summary>
public static class EvaluateFormulaDialogPlanner
{
    public const double Width = 600;
    public const double Height = 360;
    public const double MinWidth = 420;
    public const double MinHeight = 240;
    public const double RootMargin = 12;
    public const double ActionRowTopMargin = 10;
    public const double ActionSpacing = 4;
    public const double ButtonHeight = 26;
    public const double EvaluateButtonWidth = 80;
    public const double StepInButtonWidth = 68;
    public const double StepOutButtonWidth = 76;
    public const double RestartButtonWidth = 80;
    public const double CloseButtonWidth = 80;
    public const double HelpButtonWidth = 142;
    public const double LabelFontSize = 12;
    public const double StepFontSize = 16;
    public const double ValueFontSize = 14;

    public const string TitleKey = "EvaluateFormula_Title";
    public const string SelectFormulaMessageKey = "EvaluateFormula_SelectFormulaMessage";
    public const string EvaluationLabelKey = "EvaluateFormula_EvaluationLabel";
    public const string FormulaPrefixKey = "EvaluateFormula_FormulaPrefix";
    public const string ResultTextKey = "EvaluateFormula_ResultText";
    public const string StepPositionTextKey = "EvaluateFormula_StepPositionText";
    public const string NoIntermediateStepsTextKey = "EvaluateFormula_NoIntermediateStepsText";
    public const string ValueTextKey = "EvaluateFormula_ValueText";
    public const string EvaluateButtonKey = "EvaluateFormula_EvaluateButton";
    public const string StepInButtonKey = "EvaluateFormula_StepInButton";
    public const string StepOutButtonKey = "EvaluateFormula_StepOutButton";
    public const string RestartButtonKey = "EvaluateFormula_RestartButton";
    public const string CloseButtonKey = "EvaluateFormula_CloseButton";
    public const string HelpButtonKey = "EvaluateFormula_HelpButton";
    public const string HelpBodyKey = "EvaluateFormula_HelpBody";

    public static FormulaEvaluationSummary? CreateSummary(Workbook workbook, CellAddress address) =>
        FormulaEvaluationSummaryService.GetSummary(workbook, address);

    public static FormulaEvaluationSession CreateSession(FormulaEvaluationSummary summary) =>
        FormulaEvaluationSession.Start(summary);

    /// <summary>
    /// Returns the deterministic dialog state used by both parity capture hosts. Keeping the summary in
    /// the portable planner prevents a WPF/Avalonia capture from silently drifting to different cells or
    /// calculation steps while preserving the real FormulaEvaluationSession behavior in each shell.
    /// </summary>
    public static FormulaEvaluationSummary CreateParitySummary(SheetId sheetId)
    {
        var address = new CellAddress(sheetId, 6, 4);
        return new FormulaEvaluationSummary(
            sheetId,
            "Sheet1",
            address,
            "=SUM(D2:D5)",
            "469",
            [
                new FormulaEvaluationStep("SUM(D2:D5)", "469"),
                new FormulaEvaluationStep("D2:D5", "{120;85;200;64}"),
                new FormulaEvaluationStep("=SUM(D2:D5)", "469"),
            ]);
    }
}
