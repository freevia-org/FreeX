using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Shared planning layer for the Evaluate Formula dialog. The shells own their visual chrome, while summary
/// creation, step-session construction, and resource-key contracts stay in one portable place.
/// </summary>
public static class EvaluateFormulaDialogPlanner
{
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
}
