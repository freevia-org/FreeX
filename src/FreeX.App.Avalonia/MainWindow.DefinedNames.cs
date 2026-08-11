using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Avalonia.Dialogs;
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
/// and create-from-selection planning come from <see cref="FreeX.App.Presentation.DefinedNames"/>; the
/// non-UI mapping onto Core named-range commands lives in <see cref="DefinedNamesShellGlue"/>; commands run
/// through the shared session command path.
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
    /// projected by <see cref="DefinedNamesShellGlue.ProjectRows"/>, a scope/error filter dropdown, and New /
    /// Edit / Delete buttons. New and Edit open the Define Name editor (Edit seeded from the selected row);
    /// Delete runs the Core remove-name command through the shared session command path. The list refreshes
    /// after each change.
    /// </summary>
    private async Task ShowNameManagerDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

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
            rows.AddRange(DefinedNamesShellGlue.ProjectRows(_session.Workbook, filter));
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
            // Uses the row's own tracked scope identity directly (not a re-resolution of
            // row.ScopeLabel's display text) -- a worksheet can legally be named exactly "Workbook",
            // which would otherwise make Delete indistinguishable from the workbook-global scope and
            // either fail outright or silently remove an unrelated pre-existing global name of the
            // same text. Mirrors the WPF host's NamedRangeDialog.DeleteButton_Click.
            var scopeSheetId = row.ScopeSheetId;
            var command = DefinedNamesShellGlue.BuildDeleteCommand(name, scopeSheetId);
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
    /// and Comment. The name and refers-to are validated live through <see cref="DefinedNameValidator"/> and
    /// <see cref="DefinedNameDraft.ValidateRefersTo()"/>; OK additionally resolves the refers-to to a
    /// <see cref="GridRange"/> and runs the Core define-name command (add or replace) through the shared
    /// session command path. When <paramref name="seed"/> is supplied the editor is in Edit mode (its name is
    /// excluded from the duplicate check).
    /// </summary>
    private async Task ShowDefineNameDialogAsync(DefinedNameRow? seed)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var isEdit = seed is not null;
        var dialog = new Window
        {
            Title = isEdit ? UiText.Get("InsertLoc_EditNameTitle") : UiText.Get("InsertLoc_NewNameTitle"),
            Width = 460,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "DefineNameDialog");

        var scopeChoices = DefinedNamesShellGlue.BuildScopeChoices(_session.Workbook);

        var nameBox = new TextBox { Text = seed?.Name ?? string.Empty, MinWidth = 240 };
        ApplyNamesTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "DefineNameNameBox");

        var scopeBox = new ComboBox
        {
            ItemsSource = scopeChoices.Select(c => c.Label).ToList(),
            SelectedIndex = FindScopeIndex(scopeChoices, seed?.ScopeSheetId),
            MinWidth = 200,
        };
        ApplyNamesComboBoxChrome(scopeBox);
        AutomationProperties.SetAutomationId(scopeBox, "DefineNameScopeBox");

        var refersToBox = new TextBox
        {
            Text = seed?.RefersTo ?? FormatRangeReferenceQualified(_session.SelectedRange),
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
            var liveScope = scopeChoices[Math.Max(0, scopeBox.SelectedIndex)].Scope;
            var existing = ExistingDefinedNames(_session.Workbook, liveScope);
            var nameResult = DefinedNameValidator.Validate(name, existing, OriginalNameForDuplicateCheck(seed, liveScope));
            if (!nameResult.IsValid)
            {
                ShowWarning(DescribeNameError(nameResult.Error));
                okButton.IsEnabled = false;
                return;
            }

            var refersToResult = DefinedNameDraft.ValidateRefersTo(refersToBox.Text);
            if (!refersToResult.IsValid)
            {
                ShowWarning(DescribeRefersToError(refersToResult.Error));
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
            var scope = scopeChoices[Math.Max(0, scopeBox.SelectedIndex)].Scope;
            var existing = ExistingDefinedNames(_session.Workbook, scope);
            var nameResult = DefinedNameValidator.Validate(name, existing, OriginalNameForDuplicateCheck(seed, scope));
            if (!nameResult.IsValid)
            {
                ShowWarning(DescribeNameError(nameResult.Error));
                return;
            }

            var refersToText = refersToBox.Text?.Trim() ?? string.Empty;
            if (!DefinedNameDraft.ValidateRefersTo(refersToText).IsValid)
            {
                ShowWarning(UiText.Get("InsertLoc_EnterValidRefersTo"));
                return;
            }

            var draft = new DefinedNameDraft(name, scope, refersToText, commentBox.Text?.Trim() ?? string.Empty);

            // The refers-to text is first tried as a range/cell/existing-name reference (the common case);
            // when it does not resolve to one but does parse as a formula expression (checked above), it is a
            // named formula/constant (e.g. "=1.05" or "=SUM(Sheet1!A:A)") and is defined as such instead of
            // being rejected — Excel's Define Name dialog accepts both equally.
            var isRange = TryParseDefinedNameRange(refersToText, out var range);

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

            var result = isRange
                ? _session.ExecuteReviewCommand(DefinedNamesShellGlue.BuildDefineCommand(draft, range))
                : _session.ExecuteReviewCommand(DefinedNamesShellGlue.BuildDefineFormulaCommand(draft));
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
            var planned = CreateNamesFromSelectionPlanner.Plan(
                _session.SelectedRange,
                options,
                address => DefinedNameLabelText(sheet.GetValue(address)),
                _session.Workbook.NamedRanges.Keys);

            if (planned.Count == 0)
            {
                warningText.Text = UiText.Get("InsertLoc_NoValidLabels");
                warningText.IsVisible = true;
                return;
            }

            var created = 0;
            foreach (var command in DefinedNamesShellGlue.BuildCreateCommands(planned))
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

    /// <summary>
    /// Every name already defined within <paramref name="scope"/> — the workbook scope, or a single
    /// worksheet's scope — for the Define Name dialog's duplicate check. A formula/constant name (<see
    /// cref="Workbook.NamedFormulas"/>/<see cref="Workbook.ScopedNamedFormulas"/>) occupies the same name
    /// namespace as a range name, so both kinds must be considered or a new name could silently collide with
    /// one of the other kind. Scoped separately from workbook-global names: Excel allows a workbook-scoped name
    /// and a sheet-scoped name with identical text to coexist (resolved by scope precedence), so only names
    /// already occupying the SAME scope as the one being defined count as duplicates here — otherwise a
    /// sheet-scoped name could never be told apart from an unrelated same-text sheet-scoped name on another
    /// sheet, or from a workbook-global name it is meant to coexist with.
    /// </summary>
    private static IEnumerable<string> ExistingDefinedNames(Workbook workbook, DefinedNameScope scope)
    {
        if (scope.IsWorkbook)
            return workbook.NamedRanges.Keys.Concat(workbook.NamedFormulas.Keys);

        var sheetId = scope.Sheet!.Value;
        return workbook.ScopedNamedRanges.Keys
            .Where(key => key.Sheet.Equals(sheetId))
            .Select(key => key.Name)
            .Concat(workbook.ScopedNamedFormulas.Keys
                .Where(key => key.Sheet.Equals(sheetId))
                .Select(key => key.Name));
    }

    /// <summary>
    /// R88-app-name-manager-ui-5-1: the seed's own name is only excluded from the duplicate check
    /// (i.e. treated as "the entry being edited, not a collision") when <paramref name="candidateScope"/>
    /// is the SAME scope the seed already occupies. Editing a name's Scope dropdown to a scope that
    /// already holds an unrelated same-text name must NOT be waved through as "the entry being
    /// edited" -- Excel's New Name dialog rejects that with "A name with that text already exists in
    /// this scope" instead of silently overwriting the pre-existing entry (mirrors the WPF host's
    /// NamedRangeDialog.DefineOrUpdateName, which computes its isSameEntry gate from BOTH the
    /// original name AND the original scope).
    ///
    /// Compares scope by IDENTITY (<see cref="DefinedNameRow.ScopeSheetId"/> vs
    /// <see cref="DefinedNameScope.Sheet"/>), not by display label: nothing reserves "Workbook" as a
    /// sheet name, so a worksheet can legally be named exactly "Workbook" -- a seed scoped to that sheet
    /// carries the display label "Workbook" too, indistinguishable from the true workbook-global scope
    /// if compared by text. Both sides are null for the workbook-global scope, so that case still
    /// compares equal.
    /// </summary>
    private static string? OriginalNameForDuplicateCheck(DefinedNameRow? seed, DefinedNameScope candidateScope) =>
        seed is not null && seed.ScopeSheetId == candidateScope.Sheet
            ? seed.Name
            : null;

    /// <summary>Test-only forwarder for <see cref="OriginalNameForDuplicateCheck"/>.</summary>
    internal static string? OriginalNameForDuplicateCheckForTest(DefinedNameRow? seed, DefinedNameScope candidateScope) =>
        OriginalNameForDuplicateCheck(seed, candidateScope);

    /// <summary>Test-only forwarder for <see cref="FindScopeIndex"/>.</summary>
    internal static int FindScopeIndexForTest(
        IReadOnlyList<DefinedNamesShellGlue.ScopeChoice> choices,
        SheetId? scopeSheetId) =>
        FindScopeIndex(choices, scopeSheetId);

    /// <summary>
    /// Finds the Scope combo index matching <paramref name="scopeSheetId"/> by identity, not by re-deriving
    /// it from a display label -- a worksheet can legally be named exactly "Workbook", so its scope label
    /// collides with <see cref="DefinedNamesShellGlue.ScopeChoice"/>'s workbook-global entry ("Workbook",
    /// index 0) even though the two are different scopes. <paramref name="scopeSheetId"/> is null for the
    /// workbook scope (or when there is no seed, i.e. New Name), matching index 0's <c>Scope.Sheet</c>.
    /// </summary>
    private static int FindScopeIndex(
        IReadOnlyList<DefinedNamesShellGlue.ScopeChoice> choices,
        SheetId? scopeSheetId)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Scope.Sheet == scopeSheetId)
                return i;
        }

        return 0;
    }

    private static string DescribeNameError(DefinedNameError error) => error switch
    {
        DefinedNameError.Blank => UiText.Get("InsertLoc_NameErrorBlank"),
        DefinedNameError.TooLong => UiText.Get("InsertLoc_NameErrorTooLong"),
        DefinedNameError.InvalidFirstCharacter => UiText.Get("InsertLoc_NameErrorInvalidFirstChar"),
        DefinedNameError.InvalidCharacter => UiText.Get("InsertLoc_NameErrorInvalidChar"),
        DefinedNameError.LooksLikeReference => UiText.Get("InsertLoc_NameErrorLooksLikeReference"),
        DefinedNameError.Reserved => UiText.Get("InsertLoc_NameErrorReserved"),
        DefinedNameError.Duplicate => UiText.Get("InsertLoc_NameErrorDuplicate"),
        _ => UiText.Get("InsertLoc_NameErrorGeneric"),
    };

    private static string DescribeRefersToError(RefersToError error) => error switch
    {
        RefersToError.Blank => UiText.Get("InsertLoc_RefersToErrorBlank"),
        RefersToError.NotAFormula => UiText.Get("InsertLoc_RefersToErrorNotAFormula"),
        _ => UiText.Get("InsertLoc_EnterValidRefersTo"),
    };

    private static string DefinedNameLabelText(ScalarValue? value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        _ => "",
    };

    /// <summary>Formats a range as sheet-qualified A1 refers-to text for the Define Name editor.</summary>
    private string FormatRangeReferenceQualified(GridRange range) =>
        DefinedNamesShellGlue.FormatRefersTo(range, _session.Workbook);

    /// <summary>
    /// Parses a Define Name "Refers to" expression (a cell, an <c>A1:B5</c> range, a sheet-qualified
    /// <c>Sheet!A1:B5</c> range, or an existing defined name) into a <see cref="GridRange"/>, resolving sheet
    /// names against the workbook and defaulting to the active sheet.
    /// </summary>
    private bool TryParseDefinedNameRange(string text, out GridRange range)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('='))
            trimmed = trimmed[1..].Trim();

        return WorkbookReferenceNavigator.TryParseReferenceRange(
            trimmed,
            _session.ActiveSheet.Id,
            name => _session.Workbook.GetSheet(name)?.Id,
            _session.Workbook.NamedRanges,
            out range);
    }

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
