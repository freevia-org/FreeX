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
/// Windows-parity "Insert Calculated Item" PivotTable dialog for the Avalonia/macOS shell: choose the row or
/// column field the item belongs to, pick an existing calculated item on that field (or start a new one),
/// edit its name + formula, insert a source-field reference token into the formula at the caret, and save
/// (add/modify) or delete it. The field list, the existing-item list, the source-field reference list, the
/// name/formula validation, the formula-token insertion, and the add/modify/delete rebuild of the pivot's
/// calculated-item list all come from the portable <see cref="PivotCalculatedItemPlanner"/> so the behavior is
/// single-sourced with the WPF host and reusable on macOS. The rebuilt list round-trips through
/// <see cref="ConfigurePivotTableCalculatedItemsCommand"/> (the same command the calculated-field path uses),
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

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var fields = PivotCalculatedItemPlanner.AvailableFields(pivot, headers);
        if (fields.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotCalcItem_NoField"));
            return;
        }

        var references = PivotCalculatedItemPlanner.AvailableFieldReferences(headers);

        var fieldBox = new ComboBox { MinWidth = 260 };
        ApplyPivotComboBoxChrome(fieldBox);
        foreach (var field in fields)
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

        int CurrentFieldIndex() => fields[Math.Max(0, fieldBox.SelectedIndex)].SourceFieldIndex;

        IReadOnlyList<string> existingNames = [];
        void ReloadExistingNames()
        {
            existingNames = PivotCalculatedItemPlanner.ExistingItemNames(pivot, CurrentFieldIndex());
            existingBox.Items.Clear();
            existingBox.Items.Add(UiText.Get("PivotCalcItem_ExistingNone"));
            foreach (var name in existingNames)
                existingBox.Items.Add(name);
            existingBox.SelectedIndex = 0;
        }

        ReloadExistingNames();
        fieldBox.SelectionChanged += (_, _) =>
        {
            ReloadExistingNames();
            nameBox.Text = string.Empty;
            formulaBox.Text = "= ";
        };

        existingBox.SelectionChanged += (_, _) =>
        {
            if (existingBox.SelectedIndex <= 0)
            {
                nameBox.Text = string.Empty;
                formulaBox.Text = "= ";
                return;
            }

            var match = PivotCalculatedItemPlanner.FindByName(
                pivot, CurrentFieldIndex(), existingNames[existingBox.SelectedIndex - 1]);
            if (match is not null)
            {
                nameBox.Text = match.Name;
                formulaBox.Text = match.Formula;
            }
        };

        var fieldsList = new ListBox { Height = 96 };
        ApplyPivotListBoxChrome(fieldsList);
        foreach (var reference in references)
            fieldsList.Items.Add(reference);
        if (references.Count > 0)
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

            var (text, caret) = PivotCalculatedItemPlanner.InsertReference(
                formulaBox.Text, reference, formulaBox.SelectionStart, SelectionLength(formulaBox));
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

        // Outcome carried out of the dialog: Save (upsert) or Delete; null means cancel.
        PivotCalcItemOutcome? outcome = null;

        cancel.Click += (_, _) => dialog.Close(false);
        save.Click += (_, _) =>
        {
            if (!PivotCalculatedItemPlanner.TryCreateResult(
                    CurrentFieldIndex(), nameBox.Text, formulaBox.Text, out var result, out var error))
            {
                ShowEditIssue(error ?? PivotCalculatedItemPlanner.EmptyNameMessage);
                return;
            }

            outcome = new PivotCalcItemOutcome(IsDelete: false, result, CurrentFieldIndex(), result!.Name);
            dialog.Close(true);
        };
        delete.Click += (_, _) =>
        {
            var name = (nameBox.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowEditIssue(PivotCalculatedItemPlanner.NoItemToDeleteMessage);
                return;
            }

            outcome = new PivotCalcItemOutcome(IsDelete: true, null, CurrentFieldIndex(), name);
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
        if (!confirmed || outcome is null)
            return;

        if (outcome.IsDelete)
        {
            if (!PivotCalculatedItemPlanner.TryRemove(
                    pivot, outcome.SourceFieldIndex, outcome.Name, out var remaining, out var removeError))
            {
                ShowEditIssue(removeError ?? PivotCalculatedItemPlanner.NoItemToDeleteMessage);
                return;
            }

            ApplyPivotCalculatedItems(pivot, remaining, UiText.Format("PivotCalcItem_Deleted", outcome.Name));
            return;
        }

        var updated = PivotCalculatedItemPlanner.Upsert(pivot, outcome.Result!);
        ApplyPivotCalculatedItems(pivot, updated, UiText.Format("PivotCalcItem_Saved", outcome.Name));
    }

    private void ApplyPivotCalculatedItems(
        PivotTableModel pivot,
        IReadOnlyList<PivotCalculatedItemModel> calculatedItems,
        string status)
    {
        var command = new ConfigurePivotTableCalculatedItemsCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            pivot.CalculatedFields.ToList(),
            calculatedItems);
        ExecutePivotTabCommand(command, status);
    }

    private sealed record PivotCalcItemOutcome(
        bool IsDelete,
        PivotCalculatedItemPlanner.PivotCalculatedItemResult? Result,
        int SourceFieldIndex,
        string Name);
}
