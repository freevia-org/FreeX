using Avalonia.Controls;
using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    partial void PrepareOptionalStartupState(IReadOnlyList<string> startupArguments);

    partial void CompleteOptionalStartupState(IReadOnlyList<string> startupArguments);

    partial void CompleteOptionalStartupFileOpen();

    partial void RecordOptionalNeutralCellSelection();

    partial void RecordOptionalNameBoxSelection(NameBoxNavigationItem item);

    partial void RecordOptionalNameBoxPopupOpened(string host, int x, int y, int width, int height);

    partial void RecordOptionalAutoFilterPlacementTarget(string? automationId);

    partial void AttachOptionalTextBoxInlineObservation();

    partial void RequestOptionalTextBoxInlineLayoutObservation();

    partial void RecordOptionalTextBoxInlineObservation(string phase, Guid textBoxId);

    partial void AttachOptionalFindDialogObservation(
        Window dialog,
        TextBox findBox,
        Button findNextButton,
        Button findAllButton,
        Button cancelButton,
        FindOptionsControls optionsControls,
        Button chooseFormatButton,
        Button clearFormatButton);

    partial void AttachOptionalReplaceDialogObservation(
        Window dialog,
        TextBox findBox,
        TextBox replaceBox,
        Button replaceButton,
        Button replaceAllButton,
        Button cancelButton,
        FindOptionsControls optionsControls,
        Button chooseFindFormatButton,
        Button clearFindFormatButton,
        Button chooseReplaceFormatButton,
        Button clearReplaceFormatButton);

    partial void AttachOptionalGoToDialogObservation(
        Window dialog,
        ListBox historyList,
        TextBox inputBox,
        Button specialButton,
        Button acceptButton,
        Button cancelButton);

    partial void AttachOptionalGoToSpecialDialogObservation(
        Window dialog,
        Control kindBox,
        CheckBox numbersBox,
        CheckBox textBox,
        CheckBox logicalsBox,
        CheckBox errorsBox,
        Button okButton,
        Button cancelButton);

    partial void AttachOptionalFormatCellsDialogObservation(
        Window dialog,
        TabControl tabStrip,
        TabItem numberTab,
        TabItem alignmentTab,
        TabItem fontTab,
        TabItem fillTab,
        TabItem borderTab,
        TabItem protectionTab,
        ListBox numberCategoryList,
        ComboBox numberFormatBox,
        TextBlock numberPreview,
        Button okButton,
        Button cancelButton);

    partial void AttachOptionalSortDialogObservation(
        Window dialog,
        CheckBox headersCheckBox,
        Control levelsGrid,
        ComboBox sortOnBox,
        ComboBox colorBox,
        Button addLevelButton,
        Button deleteLevelButton,
        Button copyLevelButton,
        Button moveUpButton,
        Button moveDownButton,
        Button optionsButton,
        Button okButton,
        Button cancelButton);

    partial void AttachOptionalDataValidationDialogObservation(
        Window dialog,
        TextBlock summaryText,
        ComboBox typeBox,
        ComboBox operatorBox,
        TextBox formula1Box,
        TextBox formula2Box,
        CheckBox allowBlankBox,
        CheckBox showDropdownBox,
        CheckBox showInputMessageBox,
        TextBox promptTitleBox,
        TextBox promptMessageBox,
        CheckBox showErrorMessageBox,
        ComboBox alertStyleBox,
        TextBox errorTitleBox,
        TextBox errorMessageBox,
        Button applyButton,
        Button clearButton,
        Button cancelButton);

    partial void AttachOptionalConditionalFormatRuleDialogObservation(
        Window dialog,
        ComboBox ruleTypeBox,
        ComboBox presetBox,
        ComboBox operatorBox,
        TextBox value1Box,
        TextBox formulaBox,
        TextBox textBox,
        TextBox rankBox,
        ComboBox topBottomBox,
        ComboBox iconSetBox,
        TextBox minColorBox,
        TextBox maxColorBox,
        ComboBox highlightBox,
        Button okButton,
        Button cancelButton);

    partial void AttachOptionalManageConditionalFormatsDialogObservation(
        Window dialog,
        ComboBox scopeBox,
        ListBox listBox,
        TextBox appliesToBox,
        Button newButton,
        Button editButton,
        Button deleteButton,
        Button moveUpButton,
        Button moveDownButton,
        Button applyAppliesToButton,
        Control appliesToRow,
        Button closeButton);

    partial void AttachOptionalPasteSpecialDialogObservation(
        Window dialog,
        IReadOnlyList<RadioButton> contentRadios,
        CheckBox skipBlanksBox,
        CheckBox transposeBox,
        CheckBox keepColumnWidthsBox,
        IReadOnlyList<RadioButton> operationRadios,
        Button pasteLinkButton,
        Button okButton,
        Button cancelButton);
}
