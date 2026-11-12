using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Insert Calculated Item" PivotTable dialog for the Avalonia/macOS shell: choose the row or
/// column field the item belongs to, pick an existing calculated item on that field (or start a new one),
/// edit its name + formula, insert a source-field reference token into the formula at the caret, and save
/// (add/modify) or delete it. The field list, the existing-item list, the source-field reference list, the
/// name/formula validation, the formula-token insertion, and the add/modify/delete rebuild of the pivot's
/// calculated-item list all come from the portable <see cref="PivotCalculatedItemPlanner"/> so the behavior is
/// single-sourced with the WPF host and reusable on macOS. The rebuilt list round-trips through the shared
/// Pivot application session (the same command policy the calculated-field path uses),
/// carrying the row/column/page fields and calculated fields untouched. Reached from the Analyze ▸
/// Calculations ▸ Calculated Item ribbon command (<c>pivotAnalyze.calculatedItem</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ Calculated Item — opens the calculated-item dialog for the active pivot.</summary>
    private void OpenPivotCalculatedItem()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        RunGuarded(() => OpenPivotCalculatedItemDialogAsync(pivot!));
    }

    private async Task OpenPivotCalculatedItemDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var pivotTarget = new PivotApplicationTarget(_session.ActiveSheet, pivot);
        var headers = PivotApplication.ReadSourceHeaders(pivotTarget);
        // The source items of every row/column field a calculated item can attach to, so the dialog can
        // offer them as formula references (the WPF available-items list).
        var itemNamesBySourceFieldIndex = new Dictionary<int, IEnumerable<string>>();
        foreach (var layoutField in pivot.RowFields.Concat(pivot.ColumnFields))
        {
            if (!itemNamesBySourceFieldIndex.ContainsKey(layoutField.SourceFieldIndex))
            {
                itemNamesBySourceFieldIndex[layoutField.SourceFieldIndex] =
                    PivotApplication.ReadSourceItems(pivotTarget, layoutField.SourceFieldIndex);
            }
        }

        var workflowText = PivotCalculatedItemSessionText.Default with
        {
            NoSourceFieldMessage = UiText.Get("PivotCalcItem_NoField"),
            SavedStatusFormat = UiText.Get("PivotCalcItem_Saved"),
            DeletedStatusFormat = UiText.Get("PivotCalcItem_Deleted")
        };
        var calculatedItemSession = PivotCalculatedItemSession.Create(
            pivot,
            headers,
            workflowText,
            itemNamesBySourceFieldIndex: itemNamesBySourceFieldIndex);
        if (calculatedItemSession.OpenIssue is { } openIssue)
        {
            ShowEditIssue(openIssue.Message);
            return;
        }

        var fieldBox = new ComboBox { HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        ApplyPivotComboBoxChrome(fieldBox);
        foreach (var field in calculatedItemSession.Fields)
            fieldBox.Items.Add(field.Caption);
        fieldBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(fieldBox, "PivotCalcItemFieldBox");
        AutomationProperties.SetName(fieldBox, UiText.Get("PivotCalcItem_FieldLabel"));

        var nameBox = new TextBox();
        ApplyPivotTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "PivotCalcItemNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("PivotCalcItem_NameLabel"));

        var formulaBox = new TextBox
        {
            // WPF shows a one-line formula box; the box still grows with wrapped or multi-line text.
            AcceptsReturn = true,
            MinHeight = 20,
            MaxHeight = double.PositiveInfinity,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            Text = "= ",
        };
        ApplyPivotTextBoxChrome(formulaBox, fixedHeight: false);
        AutomationProperties.SetAutomationId(formulaBox, "PivotCalcItemFormulaBox");
        AutomationProperties.SetName(formulaBox, UiText.Get("PivotCalcItem_FormulaLabel"));

        var existingBox = new ComboBox { HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
        ApplyPivotComboBoxChrome(existingBox);
        AutomationProperties.SetAutomationId(existingBox, "PivotCalcItemExistingBox");
        AutomationProperties.SetName(existingBox, UiText.Get("PivotCalcItem_ExistingLabel"));

        IReadOnlyList<string> existingNames = [];
        void ReloadExistingNames()
        {
            existingNames = calculatedItemSession.ExistingNames;
            existingBox.Items.Clear();
            existingBox.Items.Add(UiText.Get("PivotCalcItem_ExistingNone"));
            foreach (var name in existingNames)
                existingBox.Items.Add(name);
            existingBox.SelectedIndex = 0;
        }

        // WPF PivotCalculatedItemDialog: an 80px list of the selected field's items below the field list.
        var itemsList = new ListBox { Height = 80 };
        ApplyPivotListBoxChrome(itemsList);
        AutomationProperties.SetAutomationId(itemsList, "PivotCalcItemItemList");
        AutomationProperties.SetName(itemsList, UiText.Get("PivotCalculated_AvailableItems"));
        void RefreshItemList()
        {
            var itemReferences = calculatedItemSession.ItemReferences;
            itemsList.ItemsSource = itemReferences;
            itemsList.SelectedIndex = itemReferences.Count > 0 ? 0 : -1;
        }

        ReloadExistingNames();
        RefreshItemList();
        fieldBox.SelectionChanged += (_, _) =>
        {
            var selectedField = calculatedItemSession.Fields[Math.Max(0, fieldBox.SelectedIndex)];
            var draft = calculatedItemSession.SelectSourceField(
                selectedField.SourceFieldIndex,
                startNew: true);
            ReloadExistingNames();
            RefreshItemList();
            nameBox.Text = draft.Name;
            formulaBox.Text = draft.Formula;
        };

        existingBox.SelectionChanged += (_, _) =>
        {
            var selectedName = existingBox.SelectedIndex > 0
                ? existingNames[existingBox.SelectedIndex - 1]
                : null;
            var draft = calculatedItemSession.SelectExisting(selectedName);
            nameBox.Text = draft.Name;
            formulaBox.Text = draft.Formula;
        };

        var fieldsList = new ListBox { Height = 80 };
        ApplyPivotListBoxChrome(fieldsList);
        foreach (var reference in calculatedItemSession.FieldReferences)
            fieldsList.Items.Add(reference);
        if (calculatedItemSession.FieldReferences.Count > 0)
            fieldsList.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(fieldsList, "PivotCalcItemReferenceList");
        AutomationProperties.SetName(fieldsList, UiText.Get("PivotCalcItem_FieldsAutomation"));

        var insertButton = new Button
        {
            Content = UiText.Get("PivotCalculated_InsertField"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPivotButtonChrome(insertButton, 110);
        insertButton.Width = 110;
        AutomationProperties.SetAutomationId(insertButton, "PivotCalcItemInsertButton");
        void InsertSelectedReference() => InsertFormulaReference(fieldsList.SelectedItem as string);
        void InsertSelectedItem() => InsertFormulaReference(itemsList.SelectedItem as string);
        void InsertFormulaReference(string? reference)
        {
            if (reference is null)
                return;

            calculatedItemSession.UpdateDraft(nameBox.Text, formulaBox.Text);
            var (text, caret) = calculatedItemSession.InsertReference(
                reference,
                formulaBox.SelectionStart,
                SelectionLength(formulaBox));
            formulaBox.Text = text;
            formulaBox.Focus();
            formulaBox.SelectionStart = caret;
            formulaBox.SelectionEnd = caret;
        }

        insertButton.Click += (_, _) => InsertSelectedReference();
        fieldsList.DoubleTapped += (_, _) => InsertSelectedReference();

        var insertItemButton = new Button
        {
            Content = UiText.Get("PivotCalculated_InsertItem"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPivotButtonChrome(insertItemButton, 110);
        insertItemButton.Width = 110;
        AutomationProperties.SetAutomationId(insertItemButton, "PivotCalcItemInsertItemButton");
        insertItemButton.Click += (_, _) => InsertSelectedItem();
        itemsList.DoubleTapped += (_, _) => InsertSelectedItem();

        var dialog = new Window
        {
            // WPF PivotCalculatedItemDialog: 500x560 outer, NoResize, so the app-wide dialog sizing
            // makes 560 the minimum and lets the height follow the content.
            Title = UiText.Get("PivotCalculated_CalculatedItem"),
            Width = 500,
            MinWidth = 500,
            MaxWidth = 500,
            MinHeight = 560 - WpfNoResizeFrameHeight,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotCalculatedItemDialog");

        var save = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(save, PivotDialogButtonMinWidth, isDefault: true);
        AutomationProperties.SetAutomationId(save, "PivotCalcItemSaveButton");
        var delete = new Button { Content = UiText.Get("PivotCalcItem_Delete") };
        ApplyPivotButtonChrome(delete, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(delete, "PivotCalcItemDeleteButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, PivotDialogButtonMinWidth);
        AutomationProperties.SetAutomationId(cancel, "PivotCalcItemCancelButton");

        PivotCalculatedItemSubmission? submission = null;

        cancel.Click += (_, _) => dialog.Close(false);
        save.Click += (_, _) =>
        {
            var plan = calculatedItemSession.PlanSave(nameBox.Text, formulaBox.Text);
            if (!plan.Success)
            {
                ShowEditIssue(plan.Issue!.Message);
                return;
            }

            submission = plan.Submission;
            dialog.Close(true);
        };
        delete.Click += (_, _) =>
        {
            var plan = calculatedItemSession.PlanDelete(nameBox.Text);
            if (!plan.Success)
            {
                ShowEditIssue(plan.Issue!.Message);
                return;
            }

            submission = plan.Submission;
            dialog.Close(true);
        };

        // Layout mirrors PivotCalculatedItemDialog: a 16px root margin, a field-and-item group and an
        // insert-into-formula group (each 12px above the next block, content inset 10,8,10,10), labels
        // above their controls with an 8px row gap, a 110px insert button left-aligned under each 80px
        // list (field list first, then the selected field's item list) with 8px below it, and the
        // button row 12px below. The existing-item picker and Delete are Avalonia-only.
        var itemPanel = new StackPanel();
        AddPivotDialogLabeledControl(itemPanel, UiText.Get("PivotCalculated_SourceFieldLabel"), fieldBox);
        AddPivotDialogLabeledControl(itemPanel, UiText.Get("PivotCalcItem_ExistingLabel"), existingBox);
        AddPivotDialogLabeledControl(itemPanel, UiText.Get("PivotCalculated_NameLabel"), nameBox);
        AddPivotDialogLabeledControl(itemPanel, UiText.Get("PivotCalculated_ItemFormulaLabel"), formulaBox);

        var insertPanel = new StackPanel();
        AddPivotDialogLabeledControl(insertPanel, UiText.Get("PivotCalculated_AvailableFieldsLabel"), fieldsList);
        insertButton.Margin = new Thickness(0, 0, 0, 8);
        insertPanel.Children.Add(insertButton);
        AddPivotDialogLabeledControl(insertPanel, UiText.Get("PivotCalculated_AvailableItemsLabel"), itemsList);
        insertItemButton.Margin = new Thickness(0, 0, 0, 8);
        insertPanel.Children.Add(insertItemButton);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(CreatePivotDialogGroupBox(UiText.Get("PivotCalculated_FieldAndItemGroup"), itemPanel));
        content.Children.Add(CreatePivotDialogGroupBox(UiText.Get("PivotCalculated_InsertIntoFormulaGroup"), insertPanel));
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([save, delete, cancel], new Thickness(0, 12, 0, 0)));
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, nameBox, selectAllText: true);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed || submission is null)
            return;

        var commit = calculatedItemSession.Commit(submission);
        if (!commit.Success)
        {
            ShowEditIssue(commit.Issue!.Message);
            return;
        }

        ApplyPivotCalculatedItems(pivot, commit.CalculatedItems, commit.Status ?? string.Empty);
    }

    private void ApplyPivotCalculatedItems(
        PivotTableModel pivot,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems,
        string status)
    {
        ApplyPivotApplicationPlan(
            PivotApplication.PlanCalculatedConfiguration(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                pivot.RowFields.ToList(),
                pivot.ColumnFields.ToList(),
                pivot.PageFields.ToList(),
                pivot.CalculatedFields.ToList(),
                calculatedItems),
            status);
    }
}
