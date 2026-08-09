using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Defined Names dialogs for the Avalonia/macOS shell (Formulas menu): Name Manager (a filtered list of the
/// workbook's defined names with New / Edit / Delete), the Define Name editor (name / scope / refers-to /
/// comment with live validation), and Create Names from Selection. The portable list projection, validation,
/// and create-from-selection planning come from <see cref="DefinedNamesSession"/>; commands run through the
/// shared session command path.
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle NamesDialogChromeStyle => new(FormulaBarFontFamily);

    // ── Formulas ▸ Defined Names menu entry points ────────────────────────────
    private void NameManager() => _ = ShowNameManagerDialogAsync();

    private void DefineName() => _ = ShowDefineNameDialogAsync(null);

    private void CreateNamesFromSelection() => _ = ShowCreateNamesFromSelectionDialogAsync();

    /// <summary>
    /// The Name Manager dialog: a list of the workbook's defined names (Name | Scope | Refers To | Value)
    /// projected by <see cref="DefinedNamesSession"/>, a scope/error filter dropdown, and New /
    /// Edit / Delete buttons. New and Edit open the Define Name editor (Edit seeded from the selected row);
    /// Delete runs the Core remove-name command through the shared session command path. The list refreshes
    /// after each change.
    /// </summary>
    private async Task ShowNameManagerDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var definedNames = new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id);

        var dialog = new Window
        {
            Title = UiText.Get("InsertLoc_NameManagerTitle"),
            Width = 620,
            Height = 460,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "NameManagerDialog");

        var filterBox = new ComboBox
        {
            ItemsSource = NameManagerFilterChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 160,
        };
        ApplyNamesComboBoxChrome(filterBox);
        AutomationProperties.SetAutomationId(filterBox, "NameManagerFilterBox");

        var namesList = new ListBox { MinHeight = 220 };
        ApplyNamesListBoxStyle(namesList);
        AutomationProperties.SetAutomationId(namesList, "NameManagerNamesList");

        var selectedRefersToBox = new TextBox
        {
            IsReadOnly = true,
            MinWidth = 240,
        };
        ApplyNamesTextBoxChrome(selectedRefersToBox);
        AutomationProperties.SetAutomationId(selectedRefersToBox, "NameManagerSelectedRefersToBox");
        AutomationProperties.SetName(selectedRefersToBox, UiText.Get("InsertLoc_RefersToFieldLabel"));

        var selectedRefersToPicker = new Button
        {
            Content = "...",
            Width = 30,
            MinWidth = 30,
            IsEnabled = false,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyNamesButtonChrome(selectedRefersToPicker, minWidth: 30);
        AutomationProperties.SetAutomationId(selectedRefersToPicker, "NameManagerSelectedRefersToPickerButton");
        AutomationProperties.SetName(selectedRefersToPicker, "Select referenced range");

        var newButton = new Button { Content = UiText.Get("InsertLoc_NewButton"), MinWidth = 84 };
        ApplyNamesButtonChrome(newButton, minWidth: 84);
        AutomationProperties.SetAutomationId(newButton, "NameManagerNewButton");
        var editButton = new Button { Content = UiText.Get("InsertLoc_EditButton"), MinWidth = 84, IsEnabled = false };
        ApplyNamesButtonChrome(editButton, minWidth: 84);
        AutomationProperties.SetAutomationId(editButton, "NameManagerEditButton");
        var deleteButton = new Button { Content = UiText.Get("InsertLoc_DeleteButton"), MinWidth = 84, IsEnabled = false };
        ApplyNamesButtonChrome(deleteButton, minWidth: 84);
        AutomationProperties.SetAutomationId(deleteButton, "NameManagerDeleteButton");

        var warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "NameManagerWarningText");

        var rows = new List<DefinedNameRow>();

        void RefreshRows()
        {
            var filter = NameManagerFilterChoices[Math.Max(0, filterBox.SelectedIndex)].Filter;
            rows.Clear();
            rows.AddRange(definedNames.ProjectRows(filter));
            namesList.ItemsSource = rows.Select(FormatNameManagerRow).ToList();
            editButton.IsEnabled = false;
            deleteButton.IsEnabled = false;
            selectedRefersToBox.Text = string.Empty;
            selectedRefersToPicker.IsEnabled = false;
        }

        namesList.SelectionChanged += (_, _) =>
        {
            var hasSelection = namesList.SelectedIndex >= 0 && namesList.SelectedIndex < rows.Count;
            editButton.IsEnabled = hasSelection;
            deleteButton.IsEnabled = hasSelection;
            selectedRefersToPicker.IsEnabled = hasSelection;
            selectedRefersToBox.Text = hasSelection
                ? rows[namesList.SelectedIndex].RefersTo
                : string.Empty;
        };

        filterBox.SelectionChanged += (_, _) => RefreshRows();

        newButton.Click += async (_, _) =>
        {
            warningText.IsVisible = false;
            await ShowDefineNameDialogAsync(null);
            RefreshRows();
        };

        editButton.Click += async (_, _) =>
        {
            warningText.IsVisible = false;
            if (namesList.SelectedIndex < 0 || namesList.SelectedIndex >= rows.Count)
                return;

            await ShowDefineNameDialogAsync(rows[namesList.SelectedIndex]);
            RefreshRows();
        };

        deleteButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            if (namesList.SelectedIndex < 0 || namesList.SelectedIndex >= rows.Count)
                return;

            var row = rows[namesList.SelectedIndex];
            var name = row.Name;
            var command = definedNames.BuildDeleteCommand(row);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                warningText.Text = result.ErrorMessage ?? UiText.Format("InsertLoc_CouldNotDeleteName", name);
                warningText.IsVisible = true;
                return;
            }

            RefreshShell(UiText.Format("InsertLoc_DeletedName", name));
            RefreshRows();
        };

        var closeButton = new Button { Content = UiText.Get("InsertLoc_CloseButton"), IsCancel = true, MinWidth = 84 };
        ApplyNamesButtonChrome(closeButton, minWidth: 84);
        AutomationProperties.SetAutomationId(closeButton, "NameManagerCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRows();

        var commandButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { newButton, editButton, deleteButton },
        };

        var bottomRow = AvaloniaCompactDialogChrome.CreateActionRow([closeButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(bottomRow, Dock.Bottom);

        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("InsertLoc_FilterLabel"), FontSize = 12, VerticalAlignment = AvaloniaVerticalAlignment.Center },
                filterBox,
            },
        };

        var selectedRefersToRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var selectedRefersToLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("InsertLoc_RefersToFieldLabel")),
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        selectedRefersToRow.Children.Add(selectedRefersToLabel);
        AvaloniaGrid.SetColumn(selectedRefersToBox, 1);
        selectedRefersToRow.Children.Add(selectedRefersToBox);
        AvaloniaGrid.SetColumn(selectedRefersToPicker, 2);
        selectedRefersToRow.Children.Add(selectedRefersToPicker);
        DockPanel.SetDock(selectedRefersToRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                bottomRow,
                new DockPanel
                {
                    Children =
                    {
                        WithDock(commandButtons, Dock.Top, new Thickness(0, 0, 0, 8)),
                        WithDock(filterRow, Dock.Top, new Thickness(0, 0, 0, 8)),
                        WithDock(warningText, Dock.Bottom, new Thickness(0, 8, 0, 0)),
                        selectedRefersToRow,
                        namesList,
                    },
                },
            },
        };
        AttachDialogRangePicker(
            dialog,
            selectedRefersToPicker,
            selectedRefersToBox,
            "range.named-ranges.selected-refers-to");

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// The Define Name editor: Name, Scope (Workbook or any sheet), Refers To (a sheet-qualified A1 reference),
    /// and Comment. <see cref="DefinedNamesSession"/> validates the draft, resolves range versus formula
    /// definitions, and constructs the Core command. When <paramref name="seed"/> is supplied the editor is in
    /// Edit mode and its exact name/scope identity is excluded from the duplicate check.
    /// </summary>
    private async Task ShowDefineNameDialogAsync(DefinedNameRow? seed)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var isEdit = seed is not null;
        var definedNames = new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id);
        var dialog = new Window
        {
            Title = isEdit ? UiText.Get("InsertLoc_EditNameTitle") : UiText.Get("InsertLoc_NewNameTitle"),
            Width = 460,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "DefineNameDialog");

        var scopeChoices = definedNames.ScopeChoices;

        var nameBox = new TextBox { Text = seed?.Name ?? string.Empty, MinWidth = 240 };
        ApplyNamesTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "DefineNameNameBox");

        var scopeBox = new ComboBox
        {
            ItemsSource = scopeChoices.Select(scope => scope.Label).ToList(),
            SelectedIndex = definedNames.FindScopeIndex(seed?.Scope),
            MinWidth = 200,
        };
        ApplyNamesComboBoxChrome(scopeBox);
        AutomationProperties.SetAutomationId(scopeBox, "DefineNameScopeBox");

        var refersToBox = new TextBox
        {
            Text = seed?.RefersTo ?? definedNames.FormatRefersTo(_session.SelectedRange),
            MinWidth = 240,
        };
        ApplyNamesTextBoxChrome(refersToBox);
        AutomationProperties.SetAutomationId(refersToBox, "DefineNameRefersToBox");

        var refersToPicker = new Button
        {
            Content = "...",
            Width = 30,
            MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyNamesButtonChrome(refersToPicker, minWidth: 30);
        AutomationProperties.SetAutomationId(refersToPicker, "DefineNameRefersToPickerButton");
        AutomationProperties.SetName(refersToPicker, "Select referenced range");

        var commentBox = new TextBox
        {
            Text = seed?.Comment ?? string.Empty,
            MinWidth = 240,
            AcceptsReturn = true,
            MinHeight = 48,
            TextWrapping = TextWrapping.Wrap,
        };
        ApplyNamesTextBoxChrome(commentBox, fixedHeight: false);
        AutomationProperties.SetAutomationId(commentBox, "DefineNameCommentBox");

        var warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "DefineNameWarningText");

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true, MinWidth = 84 };
        ApplyNamesButtonChrome(okButton, minWidth: 84, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "DefineNameOkButton");
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true, MinWidth = 84 };
        ApplyNamesButtonChrome(cancelButton, minWidth: 84);
        AutomationProperties.SetAutomationId(cancelButton, "DefineNameCancelButton");

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        void ValidateLive(object? _, EventArgs __)
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            var liveScope = scopeChoices[Math.Max(0, scopeBox.SelectedIndex)];
            var draft = new DefinedNameDraft(
                name,
                liveScope,
                refersToBox.Text ?? string.Empty,
                commentBox.Text ?? string.Empty);
            var validation = definedNames.ValidateDraft(draft, seed?.Identity);
            if (!validation.Name.IsValid)
            {
                ShowWarning(DescribeNameError(validation.Name.Error));
                okButton.IsEnabled = false;
                return;
            }

            if (!validation.RefersTo.IsValid)
            {
                ShowWarning(DescribeRefersToError(validation.RefersTo.Error));
                okButton.IsEnabled = false;
                return;
            }

            warningText.IsVisible = false;
            okButton.IsEnabled = true;
        }

        nameBox.GetObservable(TextBox.TextProperty).Subscribe(new SimpleObserver<string?>(_ => ValidateLive(null, EventArgs.Empty)));
        refersToBox.GetObservable(TextBox.TextProperty).Subscribe(new SimpleObserver<string?>(_ => ValidateLive(null, EventArgs.Empty)));
        scopeBox.SelectionChanged += ValidateLive;
        ValidateLive(null, EventArgs.Empty);

        okButton.Click += (_, _) =>
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            var scope = scopeChoices[Math.Max(0, scopeBox.SelectedIndex)];
            var draft = new DefinedNameDraft(
                name,
                scope,
                refersToBox.Text?.Trim() ?? string.Empty,
                commentBox.Text?.Trim() ?? string.Empty);
            var plan = definedNames.PlanSave(draft, seed?.Identity);
            if (!plan.Validation.Name.IsValid)
            {
                ShowWarning(DescribeNameError(plan.Validation.Name.Error));
                return;
            }

            if (!plan.Validation.RefersTo.IsValid)
            {
                ShowWarning(DescribeRefersToError(plan.Validation.RefersTo.Error));
                return;
            }

            // The refers-to text is first tried as a range/cell/existing-name reference (the common case);
            // when it does not resolve to one but does parse as a formula expression (checked above), it is a
            // named formula/constant (e.g. "=1.05" or "=SUM(Sheet1!A:A)") and is defined as such instead of
            // being rejected — Excel's Define Name dialog accepts both equally.
            // NOTE: renaming an existing name intentionally does NOT remove the old entry first
            // (this used to call BuildDeleteCommand for the seed's old name before defining the new
            // one). FreeX resolves names in formulas by literal text (e.g. =SUM(Revenue)), and
            // nothing rewrites referencing formulas old-name -> new-name on rename; removing the old
            // entry here would turn every such formula into #NAME? the instant the rename is
            // applied. Leaving the old entry in place means a rename creates a second, orphaned name
            // alongside the new one (visible/deletable from Name Manager like any other name) - a
            // lesser, cosmetic bug compared to silently breaking live formulas. This matches the WPF
            // host's NamedRangeDialog.DefineOrUpdateName, which deliberately makes the same choice
            // (see its comment for the full rationale).

            var result = _session.ExecuteReviewCommand(plan.Command!);
            if (!result.Success)
            {
                ShowWarning(result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotDefineName"));
                return;
            }

            RefreshShell(isEdit ? UiText.Format("InsertLoc_UpdatedName", name) : UiText.Format("InsertLoc_DefinedName", name));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var form = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (var i = 0; i < 4; i++)
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddDefineNameRow(form, 0, UiText.Get("InsertLoc_NameFieldLabel"), nameBox);
        AddDefineNameRow(form, 1, UiText.Get("InsertLoc_ScopeFieldLabel"), scopeBox);
        AddDefineNameRow(form, 2, UiText.Get("InsertLoc_CommentFieldLabel"), commentBox);
        var refersToRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        refersToRow.Children.Add(refersToBox);
        AvaloniaGrid.SetColumn(refersToPicker, 1);
        refersToRow.Children.Add(refersToPicker);
        AddDefineNameRow(form, 3, UiText.Get("InsertLoc_RefersToFieldLabel"), refersToRow);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [cancelButton, okButton],
            new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children = { form, warningText },
                },
            },
        };
        AttachDialogRangePicker(
            dialog,
            refersToPicker,
            refersToBox,
            "range.named-ranges.definition-refers-to");

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// The Create Names from Selection dialog: Top row / Left column / Bottom row / Right column checkboxes.
    /// OK runs <see cref="CreateNamesFromSelectionPlanner.Plan"/> over the active selection (reading label
    /// text from the active sheet), then commits each planned name through a Core define-name command on the
    /// shared session command path.
    /// </summary>
    private async Task ShowCreateNamesFromSelectionDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var definedNames = new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id);

        var dialog = new Window
        {
            Title = UiText.Get("InsertLoc_CreateNamesTitle"),
            Width = 280,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "CreateNamesFromSelectionDialog");

        var topRowBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesTopRow"), IsChecked = true };
        ApplyNamesCheckBoxChrome(topRowBox);
        AutomationProperties.SetAutomationId(topRowBox, "CreateNamesTopRowBox");
        var leftColumnBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesLeftColumn") };
        ApplyNamesCheckBoxChrome(leftColumnBox);
        AutomationProperties.SetAutomationId(leftColumnBox, "CreateNamesLeftColumnBox");
        var bottomRowBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesBottomRow") };
        ApplyNamesCheckBoxChrome(bottomRowBox);
        AutomationProperties.SetAutomationId(bottomRowBox, "CreateNamesBottomRowBox");
        var rightColumnBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesRightColumn") };
        ApplyNamesCheckBoxChrome(rightColumnBox);
        AutomationProperties.SetAutomationId(rightColumnBox, "CreateNamesRightColumnBox");

        var warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "CreateNamesWarningText");

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true, MinWidth = 76 };
        ApplyNamesButtonChrome(okButton, minWidth: 76, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "CreateNamesOkButton");
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true, MinWidth = 76 };
        ApplyNamesButtonChrome(cancelButton, minWidth: 76);
        AutomationProperties.SetAutomationId(cancelButton, "CreateNamesCancelButton");

        okButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var options = new CreateNamesFromSelectionOptions(
                UseTopRow: topRowBox.IsChecked == true,
                UseLeftColumn: leftColumnBox.IsChecked == true,
                UseBottomRow: bottomRowBox.IsChecked == true,
                UseRightColumn: rightColumnBox.IsChecked == true);

            if (!options.HasAnyEdge)
            {
                warningText.Text = UiText.Get("InsertLoc_SelectAtLeastOneLabel");
                warningText.IsVisible = true;
                return;
            }

            var sheet = _session.ActiveSheet;
            var planned = definedNames.PlanCreateNamesFromSelection(
                _session.SelectedRange,
                options,
                address => DefinedNameLabelText(sheet.GetValue(address)));

            if (planned.Count == 0)
            {
                warningText.Text = UiText.Get("InsertLoc_NoValidLabels");
                warningText.IsVisible = true;
                return;
            }

            var created = 0;
            foreach (var command in definedNames.BuildCreateCommands(planned))
            {
                var result = _session.ExecuteReviewCommand(command);
                if (!result.Success)
                {
                    warningText.Text = result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotCreateNames");
                    warningText.IsVisible = true;
                    return;
                }

                created++;
            }

            RefreshShell(UiText.Format("InsertLoc_CreatedNamesFromSelection", created));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [cancelButton, okButton],
            new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = UiText.Get("InsertLoc_CreateNamesFromValuesIn"),
                            FontSize = 12,
                            Foreground = HeaderForeground,
                        },
                        topRowBox,
                        leftColumnBox,
                        bottomRowBox,
                        rightColumnBox,
                        warningText,
                    },
                },
            },
        };

        await dialog.ShowDialog(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record NameManagerFilterChoice(string Label, DefinedNameFilter Filter);

    private static readonly IReadOnlyList<NameManagerFilterChoice> NameManagerFilterChoices =
    [
        new("All names", DefinedNameFilter.All),
        new("Names scoped to workbook", DefinedNameFilter.Workbook),
        new("Names scoped to worksheet", DefinedNameFilter.Worksheet),
        new("Names with errors", DefinedNameFilter.Errors),
        new("Names without errors", DefinedNameFilter.NoErrors),
    ];

    private static string FormatNameManagerRow(DefinedNameRow row) =>
        $"{row.Name}    [{row.ScopeLabel}]    {row.RefersTo}    {row.Value}";

    private static string DescribeNameError(DefinedNameError error) =>
        DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get);

    private static string DescribeRefersToError(RefersToError error) =>
        RefersToValidationMessages.Describe(error).Resolve(UiText.Get);

    private static string DefinedNameLabelText(ScalarValue? value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => "",
    };

    private string FormatRangeReferenceQualified(GridRange range) =>
        new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id).FormatRefersTo(range);

    private bool TryParseDefinedNameRange(string text, out GridRange range) =>
        new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id).TryParseRange(text, out range);

    private static void AddDefineNameRow(AvaloniaGrid grid, int row, string label, Control field)
    {
        var labelBlock = new TextBlock
        {
            Text = StripDisplayMnemonic(label),
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8),
        };
        AvaloniaGrid.SetRow(labelBlock, row);
        AvaloniaGrid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);

        field.Margin = new Thickness(0, 0, 0, 8);
        AvaloniaGrid.SetRow(field, row);
        AvaloniaGrid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Control WithDock(Control control, Dock dock, Thickness margin)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = margin;
        return control;
    }

    /// <summary>A minimal <see cref="IObserver{T}"/> that forwards values to an action; used for live validation.</summary>
    private sealed class SimpleObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(T value) => onNext(value);
    }

    // ── Visual chrome helpers (Names/DefinedNames dialogs) ───────────────────

    /// <summary>
    /// Applies standard Names-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// <paramref name="minWidth"/> sets MinWidth; <paramref name="isDefault"/> uses blue border for the
    /// default/OK button.
    /// </summary>
    private static void ApplyNamesButtonChrome(Button button, double minWidth = 84, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, NamesDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard Names-dialog text-box chrome (Height=24, Padding=(4,1), FontSize=12, grey border).
    /// Pass <paramref name="fixedHeight"/>=false for multi-line boxes (e.g. Comment) that must grow.
    /// </summary>
    private static void ApplyNamesTextBoxChrome(TextBox textBox, bool fixedHeight = true)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, NamesDialogChromeStyle, fixedHeight);

    /// <summary>
    /// Applies standard Names-dialog combo-box chrome (Height=24, Padding=(5,0,4,0), FontSize=12, grey border).
    /// </summary>
    private static void ApplyNamesComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, NamesDialogChromeStyle);

    /// <summary>
    /// Applies standard Names-dialog check-box chrome (FontSize=12, FontFamily=FormulaBarFontFamily).
    /// </summary>
    private static void ApplyNamesCheckBoxChrome(CheckBox checkBox)
        => AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, NamesDialogChromeStyle);

    /// <summary>
    /// Applies standard Names-dialog list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplyNamesListBoxStyle(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, NamesDialogChromeStyle);
}
