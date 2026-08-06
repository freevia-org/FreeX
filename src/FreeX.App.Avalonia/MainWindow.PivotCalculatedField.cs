using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Avalonia.Pivot;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Insert Calculated Field" PivotTable dialog for the Avalonia/macOS shell: pick an existing
/// calculated field (or start a new one), edit its name + formula, insert a source-field reference token into
/// the formula at the caret, and save (add/modify) or delete it. The existing-field list, the source-field
/// reference list, the name/formula validation, the formula-token insertion, and the add/modify/delete rebuild
/// of the pivot's calculated-field list come from the portable <see cref="PivotCalculatedFieldPlanner"/> so the
/// behavior is single-sourced with the WPF host and reusable on macOS. The rebuilt list round-trips through
/// <see cref="ConfigurePivotTableCalculatedItemsCommand"/> (the same command the desktop host uses), carrying
/// the row/column/page fields and calculated items untouched. Reached from the Analyze ▸ Fields, Items &amp;
/// Sets ▸ Calculated Field ribbon command (<c>pivotAnalyze.calculatedField</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ Calculated Field — opens the calculated-field dialog for the active pivot.</summary>
    private void OpenPivotCalculatedField()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        _ = OpenPivotCalculatedFieldDialogAsync(pivot!);
    }

    private async Task OpenPivotCalculatedFieldDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var workflowText = PivotCalculatedFieldSessionText.Default with
        {
            SavedStatusFormat = UiText.Get("PivotCalcField_Saved"),
            DeletedStatusFormat = UiText.Get("PivotCalcField_Deleted")
        };
        var calculatedFieldSession = PivotCalculatedFieldSession.Create(pivot, headers, workflowText);

        var nameBox = new TextBox { MinWidth = 260 };
        ApplyPivotTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "PivotCalcFieldNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("PivotCalcField_NameLabel"));

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
        AutomationProperties.SetAutomationId(formulaBox, "PivotCalcFieldFormulaBox");
        AutomationProperties.SetName(formulaBox, UiText.Get("PivotCalcField_FormulaLabel"));

        var existingBox = new ComboBox { MinWidth = 260 };
        ApplyPivotComboBoxChrome(existingBox);
        existingBox.Items.Add(UiText.Get("PivotCalcField_ExistingNone"));
        foreach (var name in calculatedFieldSession.ExistingNames)
            existingBox.Items.Add(name);
        existingBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(existingBox, "PivotCalcFieldExistingBox");
        AutomationProperties.SetName(existingBox, UiText.Get("PivotCalcField_ExistingLabel"));

        existingBox.SelectionChanged += (_, _) =>
        {
            var selectedName = existingBox.SelectedIndex > 0
                ? calculatedFieldSession.ExistingNames[existingBox.SelectedIndex - 1]
                : null;
            var draft = calculatedFieldSession.SelectExisting(selectedName);
            nameBox.Text = draft.Name;
            formulaBox.Text = draft.Formula;
        };

        var fieldsList = new ListBox { Height = 96 };
        ApplyPivotListBoxChrome(fieldsList);
        foreach (var reference in calculatedFieldSession.FieldReferences)
            fieldsList.Items.Add(reference);
        if (calculatedFieldSession.FieldReferences.Count > 0)
            fieldsList.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(fieldsList, "PivotCalcFieldReferenceList");
        AutomationProperties.SetName(fieldsList, UiText.Get("PivotCalcField_FieldsAutomation"));

        var insertButton = new Button { Content = UiText.Get("PivotCalcField_InsertField") };
        ApplyPivotButtonChrome(insertButton, 110);
        AutomationProperties.SetAutomationId(insertButton, "PivotCalcFieldInsertButton");
        void InsertSelectedReference()
        {
            if (fieldsList.SelectedItem is not string reference)
                return;

            calculatedFieldSession.UpdateDraft(nameBox.Text, formulaBox.Text);
            var (text, caret) = calculatedFieldSession.InsertReference(
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
            Title = UiText.Get("PivotCalcField_Title"),
            Width = 480,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotCalculatedFieldDialog");

        var save = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotButtonChrome(save, 80, isDefault: true);
        AutomationProperties.SetAutomationId(save, "PivotCalcFieldSaveButton");
        var delete = new Button { Content = UiText.Get("PivotCalcField_Delete") };
        ApplyPivotButtonChrome(delete, 80);
        AutomationProperties.SetAutomationId(delete, "PivotCalcFieldDeleteButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotCalcFieldCancelButton");

        PivotCalculatedFieldSubmission? submission = null;

        cancel.Click += (_, _) => dialog.Close(false);
        save.Click += (_, _) =>
        {
            var plan = calculatedFieldSession.PlanSave(nameBox.Text, formulaBox.Text);
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
            var plan = calculatedFieldSession.PlanDelete(nameBox.Text);
            if (!plan.Success)
            {
                ShowEditIssue(plan.Issue!.Message);
                return;
            }

            submission = plan.Submission;
            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcField_ExistingLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(existingBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcField_NameLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcField_FormulaLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(formulaBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotCalcField_FieldsLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
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

        var commit = calculatedFieldSession.Commit(submission);
        if (!commit.Success)
        {
            ShowEditIssue(commit.Issue!.Message);
            return;
        }

        ApplyPivotCalculatedFields(pivot, commit.CalculatedFields, commit.Status ?? string.Empty);
    }

    private void ApplyPivotCalculatedFields(
        PivotTableModel pivot,
        IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        string status)
    {
        var command = new ConfigurePivotTableCalculatedItemsCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            calculatedFields,
            pivot.CalculatedItems.ToList());
        ExecutePivotTabCommand(command, status);
    }

    private static int SelectionLength(TextBox box) => Math.Abs(box.SelectionEnd - box.SelectionStart);
}
