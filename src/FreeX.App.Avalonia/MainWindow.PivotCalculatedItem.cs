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

        _ = OpenPivotCalculatedItemDialogAsync(pivot!);
    }

    private async Task OpenPivotCalculatedItemDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot));
        var workflowText = PivotCalculatedItemSessionText.Default with
        {
            NoSourceFieldMessage = UiText.Get("PivotCalcItem_NoField"),
            SavedStatusFormat = UiText.Get("PivotCalcItem_Saved"),
            DeletedStatusFormat = UiText.Get("PivotCalcItem_Deleted")
        };
        var calculatedItemSession = PivotCalculatedItemSession.Create(pivot, headers, workflowText);
        if (calculatedItemSession.OpenIssue is { } openIssue)
        {
            ShowEditIssue(openIssue.Message);
            return;
        }

        var fieldBox = new ComboBox { MinWidth = 260 };
        ApplyPivotComboBoxChrome(fieldBox);
        foreach (var field in calculatedItemSession.Fields)
            fieldBox.Items.Add(field.Caption);
        fieldBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(fieldBox, "PivotCalcItemFieldBox");
        AutomationProperties.SetName(fieldBox, UiText.Get("PivotCalcItem_FieldLabel"));

        var nameBox = new TextBox { MinWidth = 260 };
        ApplyPivotTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "PivotCalcItemNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("PivotCalcItem_NameLabel"));

        var formulaBox = new TextBox
        {
            MinWidth = 260,
            AcceptsReturn = true,
            MinHeight = 56,
            MaxHeight = double.PositiveInfinity,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            Text = "= ",
        };
        ApplyPivotTextBoxChrome(formulaBox, fixedHeight: false);
        AutomationProperties.SetAutomationId(formulaBox, "PivotCalcItemFormulaBox");
        AutomationProperties.SetName(formulaBox, UiText.Get("PivotCalcItem_FormulaLabel"));

        var existingBox = new ComboBox { MinWidth = 260 };
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

        ReloadExistingNames();
        fieldBox.SelectionChanged += (_, _) =>
        {
            var selectedField = calculatedItemSession.Fields[Math.Max(0, fieldBox.SelectedIndex)];
            var draft = calculatedItemSession.SelectSourceField(
                selectedField.SourceFieldIndex,
                startNew: true);
            ReloadExistingNames();
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

        var fieldsList = new ListBox { Height = 96 };
        ApplyPivotListBoxChrome(fieldsList);
        foreach (var reference in calculatedItemSession.FieldReferences)
            fieldsList.Items.Add(reference);
        if (calculatedItemSession.FieldReferences.Count > 0)
            fieldsList.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(fieldsList, "PivotCalcItemReferenceList");
        AutomationProperties.SetName(fieldsList, UiText.Get("PivotCalcItem_FieldsAutomation"));

        var insertButton = new Button { Content = UiText.Get("PivotCalcItem_InsertField") };
        ApplyPivotButtonChrome(insertButton, 110);
        AutomationProperties.SetAutomationId(insertButton, "PivotCalcItemInsertButton");
        void InsertSelectedReference()
        {
            if (fieldsList.SelectedItem is not string reference)
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

        var dialog = new Window
        {
            Title = UiText.Get("PivotCalcItem_Title"),
            Width = 500,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotCalculatedItemDialog");

        var save = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(save, 80, isDefault: true);
        AutomationProperties.SetAutomationId(save, "PivotCalcItemSaveButton");
        var delete = new Button { Content = UiText.Get("PivotCalcItem_Delete") };
        ApplyPivotButtonChrome(delete, 80);
        AutomationProperties.SetAutomationId(delete, "PivotCalcItemDeleteButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
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

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcItem_FieldLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(fieldBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcItem_ExistingLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(existingBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcItem_NameLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcItem_FormulaLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(formulaBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcItem_FieldsLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(fieldsList);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Spacing = 8,
            Children = { insertButton },
        });
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([save, delete, cancel], new Thickness(0, 8, 0, 0)));
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
