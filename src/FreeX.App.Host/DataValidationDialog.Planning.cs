using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class DataValidationDialog
{
    public static bool TryValidateCriteriaInputs(
        string typeTag,
        string operatorTag,
        string? formula1,
        string? formula2,
        out string? error)
    {
        var type = DataValidationDialogPlanner.TypeFromTag(typeTag);
        var op = DataValidationDialogPlanner.OperatorFromTag(operatorTag);
        var result = DataValidationDialogPlanner.ValidateCriteria(type, op, formula1, formula2);
        error = LocalizeValidationError(result.FirstError);
        return result.IsValid;
    }

    private static bool ShouldFocusSecondCriteriaInput(
        DvType type,
        DvOperator op,
        string? formula1,
        string? formula2) =>
        DataValidationDialogPlanner.FocusTargetForInvalidCriteria(type, op, formula1, formula2) ==
        DvRuleEditorFocusTarget.Formula2;

    public static DataValidationRangeSelectionRequest CreateRangeSelectionRequest(
        DataValidationRangeSelectionTarget target,
        string currentText) =>
        DataValidationDialogPlanner.CreateRangeSelectionRequest(target, currentText);

    private static string TypeTag(DvType type) =>
        DataValidationDialogPlanner.TypeTag(type);

    private static string OperatorTag(DvOperator op) =>
        DataValidationDialogPlanner.OperatorTag(op);

    private static string AlertStyleTag(DvAlertStyle style) =>
        DataValidationDialogPlanner.AlertStyleTag(style);

    private static string? LocalizeValidationError(DvValidationError? error) =>
        error?.Kind switch
        {
            null => null,
            DvValidationErrorKind.SourceRequired => UiText.Get("DataValidation_SourceRequired"),
            DvValidationErrorKind.FormulaRequired => UiText.Get("DataValidation_FormulaRequired"),
            DvValidationErrorKind.ValueRequired => UiText.Get("DataValidation_ValueRequired"),
            DvValidationErrorKind.MaximumRequired => UiText.Get("DataValidation_MaximumRequired"),
            DvValidationErrorKind.InvalidWholeNumberCriteria => UiText.Get("DataValidation_InvalidWholeNumberCriteria"),
            DvValidationErrorKind.InvalidDecimalCriteria => UiText.Get("DataValidation_InvalidDecimalCriteria"),
            DvValidationErrorKind.InvalidDateCriteria => UiText.Get("DataValidation_InvalidDateCriteria"),
            DvValidationErrorKind.InvalidTimeCriteria => UiText.Get("DataValidation_InvalidTimeCriteria"),
            DvValidationErrorKind.InvalidTextLengthCriteria => UiText.Get("DataValidation_InvalidTextLengthCriteria"),
            DvValidationErrorKind.InvalidListCriteria => UiText.Get("DataValidation_InvalidListCriteria"),
            DvValidationErrorKind.InvalidCustomCriteria => UiText.Get("DataValidation_InvalidCustomCriteria"),
            _ => error.Message
        };
}
