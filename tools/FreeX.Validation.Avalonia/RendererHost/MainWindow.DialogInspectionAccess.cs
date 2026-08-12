using Avalonia.Controls;
using Avalonia.Threading;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal sealed record FindDialogInspection(
        Window Dialog,
        TextBox FindBox,
        Button FindNextButton,
        Button FindAllButton,
        Button CancelButton,
        FindOptionsControls OptionsControls,
        Button ChooseFormatButton,
        Button ClearFormatButton);

    internal sealed record ReplaceDialogInspection(
        Window Dialog,
        TextBox FindBox,
        TextBox ReplaceBox,
        Button ReplaceButton,
        Button ReplaceAllButton,
        Button CancelButton,
        FindOptionsControls OptionsControls,
        Button ChooseFindFormatButton,
        Button ClearFindFormatButton,
        Button ChooseReplaceFormatButton,
        Button ClearReplaceFormatButton);

    internal sealed record GoToDialogInspection(
        Window Dialog,
        ListBox HistoryList,
        TextBox InputBox,
        Button SpecialButton,
        Button AcceptButton,
        Button CancelButton);

    internal sealed record GoToSpecialDialogInspection(
        Window Dialog,
        Control KindBox,
        CheckBox NumbersBox,
        CheckBox TextBox,
        CheckBox LogicalsBox,
        CheckBox ErrorsBox,
        Button OkButton,
        Button CancelButton);

    internal sealed record SortDialogInspection(
        Window Dialog,
        CheckBox HeadersCheckBox,
        Control LevelsGrid,
        ComboBox SortOnBox,
        ComboBox ColorBox,
        Button AddLevelButton,
        Button DeleteLevelButton,
        Button CopyLevelButton,
        Button MoveUpButton,
        Button MoveDownButton,
        Button OptionsButton,
        Button OkButton,
        Button CancelButton);

    internal sealed record DataValidationDialogInspection(
        Window Dialog,
        TextBlock SummaryText,
        ComboBox TypeBox,
        ComboBox OperatorBox,
        TextBox Formula1Box,
        TextBox Formula2Box,
        CheckBox AllowBlankBox,
        CheckBox ShowDropdownBox,
        CheckBox ShowInputMessageBox,
        TextBox PromptTitleBox,
        TextBox PromptMessageBox,
        CheckBox ShowErrorMessageBox,
        ComboBox AlertStyleBox,
        TextBox ErrorTitleBox,
        TextBox ErrorMessageBox,
        Button ApplyButton,
        Button ClearButton,
        Button CancelButton);

    internal sealed record FormatCellsDialogInspection(
        Window Dialog,
        TabControl TabStrip,
        TabItem NumberTab,
        TabItem AlignmentTab,
        TabItem FontTab,
        TabItem FillTab,
        TabItem BorderTab,
        TabItem ProtectionTab,
        ListBox NumberCategoryList,
        ComboBox NumberFormatBox,
        TextBlock NumberPreview,
        Button OkButton,
        Button CancelButton);

    internal sealed record ConditionalFormatRuleDialogInspection(
        Window Dialog,
        ComboBox RuleTypeBox,
        ComboBox PresetBox,
        ComboBox OperatorBox,
        TextBox Value1Box,
        TextBox FormulaBox,
        TextBox TextBox,
        TextBox RankBox,
        ComboBox TopBottomBox,
        ComboBox IconSetBox,
        TextBox MinColorBox,
        TextBox MaxColorBox,
        ComboBox HighlightBox,
        Button OkButton,
        Button CancelButton);

    internal sealed record ManageConditionalFormatsDialogInspection(
        Window Dialog,
        ComboBox ScopeBox,
        ListBox ListBox,
        TextBox AppliesToBox,
        Button NewButton,
        Button EditButton,
        Button DeleteButton,
        Button MoveUpButton,
        Button MoveDownButton,
        Button ApplyAppliesToButton,
        Control AppliesToRow,
        Button CloseButton);

    internal sealed record PasteSpecialDialogInspection(
        Window Dialog,
        IReadOnlyList<RadioButton> ContentRadios,
        CheckBox SkipBlanksBox,
        CheckBox TransposeBox,
        CheckBox KeepColumnWidthsBox,
        IReadOnlyList<RadioButton> OperationRadios,
        Button PasteLinkButton,
        Button OkButton,
        Button CancelButton);

    private Action<FindDialogInspection>? _findDialogInspectionCallback;
    private Action<ReplaceDialogInspection>? _replaceDialogInspectionCallback;
    private Action<GoToDialogInspection>? _goToDialogInspectionCallback;
    private Action<GoToSpecialDialogInspection>? _goToSpecialDialogInspectionCallback;
    private Action<SortDialogInspection>? _sortDialogInspectionCallback;
    private Action<DataValidationDialogInspection>? _dataValidationDialogInspectionCallback;
    private Action<FormatCellsDialogInspection>? _formatCellsDialogInspectionCallback;
    private Action<ConditionalFormatRuleDialogInspection>? _conditionalFormatRuleDialogInspectionCallback;
    private Action<ManageConditionalFormatsDialogInspection>? _manageConditionalFormatsDialogInspectionCallback;
    private Action<PasteSpecialDialogInspection>? _pasteSpecialDialogInspectionCallback;

    private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogInspection> inspectionCallback)
    {
        var previous = _findDialogInspectionCallback;
        _findDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowFindInputDialogAsync();
        }
        finally
        {
            _findDialogInspectionCallback = previous;
        }
    }

    private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogInspection> inspectionCallback)
    {
        var previous = _replaceDialogInspectionCallback;
        _replaceDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowReplaceInputDialogAsync();
        }
        finally
        {
            _replaceDialogInspectionCallback = previous;
        }
    }

    private async Task<GoToDialogResult?> ShowGoToInputDialogAsync(Action<GoToDialogInspection> inspectionCallback)
    {
        var previous = _goToDialogInspectionCallback;
        _goToDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowGoToInputDialogAsync();
        }
        finally
        {
            _goToDialogInspectionCallback = previous;
        }
    }

    private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(
        Action<GoToSpecialDialogInspection> inspectionCallback)
    {
        var previous = _goToSpecialDialogInspectionCallback;
        _goToSpecialDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowGoToSpecialInputDialogAsync();
        }
        finally
        {
            _goToSpecialDialogInspectionCallback = previous;
        }
    }

    private async Task<SortDialogResult?> ShowSortInputDialogAsync(Action<SortDialogInspection> inspectionCallback)
    {
        var previous = _sortDialogInspectionCallback;
        _sortDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowSortInputDialogAsync();
        }
        finally
        {
            _sortDialogInspectionCallback = previous;
        }
    }

    private async Task<DataValidationDialogResult?> ShowDataValidationInputDialogAsync(
        Action<DataValidationDialogInspection> inspectionCallback)
    {
        var previous = _dataValidationDialogInspectionCallback;
        _dataValidationDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowDataValidationInputDialogAsync();
        }
        finally
        {
            _dataValidationDialogInspectionCallback = previous;
        }
    }

    internal async Task<FormatCellsCompactDialogPlan?> ShowFormatCellsInputDialogAsync(
        Action<FormatCellsDialogInspection> inspectionCallback,
        int initialTabIndex = 0)
    {
        var previous = _formatCellsDialogInspectionCallback;
        _formatCellsDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowFormatCellsInputDialogAsync(initialTabIndex);
        }
        finally
        {
            _formatCellsDialogInspectionCallback = previous;
        }
    }

    internal async Task<FormatCellsCompactDialogPlan?> ShowPivotNumberFormatInputDialogAsync(
        string? currentNumberFormat,
        Action<FormatCellsDialogInspection> inspectionCallback)
    {
        var previous = _formatCellsDialogInspectionCallback;
        _formatCellsDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowPivotNumberFormatInputDialogAsync(currentNumberFormat);
        }
        finally
        {
            _formatCellsDialogInspectionCallback = previous;
        }
    }

    private async Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(
        ConditionalFormat? existingRule,
        Action<ConditionalFormatRuleDialogInspection> inspectionCallback)
    {
        var previous = _conditionalFormatRuleDialogInspectionCallback;
        _conditionalFormatRuleDialogInspectionCallback = inspectionCallback;
        try
        {
            return await ShowConditionalFormatRuleEditorAsync(existingRule);
        }
        finally
        {
            _conditionalFormatRuleDialogInspectionCallback = previous;
        }
    }

    internal async Task ShowManageConditionalFormatsDialogAsync(
        Action<ManageConditionalFormatsDialogInspection> inspectionCallback)
    {
        var previous = _manageConditionalFormatsDialogInspectionCallback;
        _manageConditionalFormatsDialogInspectionCallback = inspectionCallback;
        try
        {
            await ShowManageConditionalFormatsDialogAsync();
        }
        finally
        {
            _manageConditionalFormatsDialogInspectionCallback = previous;
        }
    }

    internal async Task<PasteSpecialDialogSelection?> PromptPasteSpecialModeAsync(
        Action<PasteSpecialDialogInspection> inspectionCallback)
    {
        var previous = _pasteSpecialDialogInspectionCallback;
        _pasteSpecialDialogInspectionCallback = inspectionCallback;
        try
        {
            return await PromptPasteSpecialModeAsync();
        }
        finally
        {
            _pasteSpecialDialogInspectionCallback = previous;
        }
    }

    partial void AttachOptionalFindDialogObservation(
        Window dialog,
        TextBox findBox,
        Button findNextButton,
        Button findAllButton,
        Button cancelButton,
        FindOptionsControls optionsControls,
        Button chooseFormatButton,
        Button clearFormatButton) =>
        AttachDialogObservation(
            dialog,
            _findDialogInspectionCallback,
            () => new FindDialogInspection(
                dialog,
                findBox,
                findNextButton,
                findAllButton,
                cancelButton,
                optionsControls,
                chooseFormatButton,
                clearFormatButton));

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
        Button clearReplaceFormatButton) =>
        AttachDialogObservation(
            dialog,
            _replaceDialogInspectionCallback,
            () => new ReplaceDialogInspection(
                dialog,
                findBox,
                replaceBox,
                replaceButton,
                replaceAllButton,
                cancelButton,
                optionsControls,
                chooseFindFormatButton,
                clearFindFormatButton,
                chooseReplaceFormatButton,
                clearReplaceFormatButton));

    partial void AttachOptionalGoToDialogObservation(
        Window dialog,
        ListBox historyList,
        TextBox inputBox,
        Button specialButton,
        Button acceptButton,
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _goToDialogInspectionCallback,
            () => new GoToDialogInspection(
                dialog,
                historyList,
                inputBox,
                specialButton,
                acceptButton,
                cancelButton));

    partial void AttachOptionalGoToSpecialDialogObservation(
        Window dialog,
        Control kindBox,
        CheckBox numbersBox,
        CheckBox textBox,
        CheckBox logicalsBox,
        CheckBox errorsBox,
        Button okButton,
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _goToSpecialDialogInspectionCallback,
            () => new GoToSpecialDialogInspection(
                dialog,
                kindBox,
                numbersBox,
                textBox,
                logicalsBox,
                errorsBox,
                okButton,
                cancelButton));

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
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _formatCellsDialogInspectionCallback,
            () => new FormatCellsDialogInspection(
                dialog,
                tabStrip,
                numberTab,
                alignmentTab,
                fontTab,
                fillTab,
                borderTab,
                protectionTab,
                numberCategoryList,
                numberFormatBox,
                numberPreview,
                okButton,
                cancelButton));

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
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _sortDialogInspectionCallback,
            () => new SortDialogInspection(
                dialog,
                headersCheckBox,
                levelsGrid,
                sortOnBox,
                colorBox,
                addLevelButton,
                deleteLevelButton,
                copyLevelButton,
                moveUpButton,
                moveDownButton,
                optionsButton,
                okButton,
                cancelButton));

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
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _dataValidationDialogInspectionCallback,
            () => new DataValidationDialogInspection(
                dialog,
                summaryText,
                typeBox,
                operatorBox,
                formula1Box,
                formula2Box,
                allowBlankBox,
                showDropdownBox,
                showInputMessageBox,
                promptTitleBox,
                promptMessageBox,
                showErrorMessageBox,
                alertStyleBox,
                errorTitleBox,
                errorMessageBox,
                applyButton,
                clearButton,
                cancelButton));

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
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _conditionalFormatRuleDialogInspectionCallback,
            () => new ConditionalFormatRuleDialogInspection(
                dialog,
                ruleTypeBox,
                presetBox,
                operatorBox,
                value1Box,
                formulaBox,
                textBox,
                rankBox,
                topBottomBox,
                iconSetBox,
                minColorBox,
                maxColorBox,
                highlightBox,
                okButton,
                cancelButton));

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
        Button closeButton) =>
        AttachDialogObservation(
            dialog,
            _manageConditionalFormatsDialogInspectionCallback,
            () => new ManageConditionalFormatsDialogInspection(
                dialog,
                scopeBox,
                listBox,
                appliesToBox,
                newButton,
                editButton,
                deleteButton,
                moveUpButton,
                moveDownButton,
                applyAppliesToButton,
                appliesToRow,
                closeButton));

    partial void AttachOptionalPasteSpecialDialogObservation(
        Window dialog,
        IReadOnlyList<RadioButton> contentRadios,
        CheckBox skipBlanksBox,
        CheckBox transposeBox,
        CheckBox keepColumnWidthsBox,
        IReadOnlyList<RadioButton> operationRadios,
        Button pasteLinkButton,
        Button okButton,
        Button cancelButton) =>
        AttachDialogObservation(
            dialog,
            _pasteSpecialDialogInspectionCallback,
            () => new PasteSpecialDialogInspection(
                dialog,
                contentRadios,
                skipBlanksBox,
                transposeBox,
                keepColumnWidthsBox,
                operationRadios,
                pasteLinkButton,
                okButton,
                cancelButton));

    private static void AttachDialogObservation<TInspection>(
        Window dialog,
        Action<TInspection>? inspectionCallback,
        Func<TInspection> createInspection)
    {
        if (inspectionCallback is null)
            return;

        dialog.Opened += (_, _) =>
            CompleteDialogInspection(dialog, () => inspectionCallback(createInspection()));
    }

    private static void CompleteDialogInspection(Window dialog, Action inspect)
    {
        try
        {
            inspect();
        }
        finally
        {
            Dispatcher.UIThread.Post(() => dialog.Close());
        }
    }
}
