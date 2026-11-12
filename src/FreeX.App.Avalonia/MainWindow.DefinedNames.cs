using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
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
    private void NameManager() => RunGuarded(ShowNameManagerDialogAsync);

    private void DefineName() => RunGuarded(() => ShowDefineNameDialogAsync(null));

    private void CreateNamesFromSelection() => RunGuarded(ShowCreateNamesFromSelectionDialogAsync);

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
            // WPF NamedRangeDialog.xaml: Width="720" Height="440" (outer, resizable).
            Width = 720 - WpfResizableFrameWidth,
            Height = 440 - WpfResizableFrameHeight,
            MinWidth = 520,
            MinHeight = 360,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "NameManagerDialog");

        var filterBox = new ComboBox
        {
            ItemsSource = DefinedNameUiPolicy.Filters
                .Select(descriptor => UiText.Get(descriptor.LabelResourceKey))
                .ToList(),
            SelectedIndex = 0,
            // WPF: <ComboBox x:Name="FilterBox" Width="190">.
            Width = 190,
        };
        ApplyNamesComboBoxChrome(filterBox);
        AutomationProperties.SetAutomationId(filterBox, "NameManagerFilterBox");

        // WPF: a GridView with Name | Value | Refers To | Scope | Comment columns.
        var namesList = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<DefinedNameRow>(
                (row, _) => BuildNameManagerRowGrid(row), supportsRecycling: false),
        };
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

        // WPF: RefersToPickerButton is 26px wide with a 4px left margin in a 32px column.
        var selectedRefersToPicker = new Button
        {
            Content = "...",
            Width = 26,
            MinWidth = 26,
            IsEnabled = false,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
        };
        ApplyNamesButtonChrome(selectedRefersToPicker, minWidth: 26);
        selectedRefersToPicker.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(selectedRefersToPicker, "NameManagerSelectedRefersToPickerButton");
        AutomationProperties.SetName(selectedRefersToPicker, UiText.Get("NameDefinition_RangePickerAutomationName"));

        // WPF: New / Edit / Delete are Width="72", 8px apart.
        var newButton = new Button { Content = UiText.Get("InsertLoc_NewButton"), Width = 72, MinWidth = 72 };
        ApplyNamesButtonChrome(newButton, minWidth: 72);
        AutomationProperties.SetAutomationId(newButton, "NameManagerNewButton");
        var editButton = new Button { Content = UiText.Get("InsertLoc_EditButton"), Width = 72, MinWidth = 72, IsEnabled = false };
        ApplyNamesButtonChrome(editButton, minWidth: 72);
        AutomationProperties.SetAutomationId(editButton, "NameManagerEditButton");
        var deleteButton = new Button { Content = UiText.Get("InsertLoc_DeleteButton"), Width = 72, MinWidth = 72, IsEnabled = false };
        ApplyNamesButtonChrome(deleteButton, minWidth: 72);
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
            var filter = DefinedNameUiPolicy.ResolveFilter(filterBox.SelectedIndex);
            rows.Clear();
            rows.AddRange(definedNames.ProjectRows(filter));
            namesList.ItemsSource = rows.ToList();
            ApplySelectionPlan(DefinedNameUiPolicy.PlanManagerSelection(
                selectedRow: null,
                profile: DefinedNameUiProfile.Avalonia));
        }

        namesList.SelectionChanged += (_, _) =>
        {
            ApplySelectionPlan(DefinedNameUiPolicy.PlanManagerSelection(
                rows,
                namesList.SelectedIndex,
                DefinedNameUiProfile.Avalonia));
        };

        void ApplySelectionPlan(DefinedNameManagerSelectionPlan plan)
        {
            editButton.IsEnabled = plan.CanEdit;
            deleteButton.IsEnabled = plan.CanDelete;
            selectedRefersToPicker.IsEnabled = plan.CanSelectRefersTo;
            if (plan.ShouldUpdateRefersTo)
                selectedRefersToBox.Text = plan.RefersToText;
        }

        filterBox.SelectionChanged += (_, _) => RefreshRows();

        newButton.Click += (_, _) => RunGuarded(async () =>
        {
            warningText.IsVisible = false;
            await ShowDefineNameDialogAsync(null);
            RefreshRows();
        });

        editButton.Click += (_, _) => RunGuarded(async () =>
        {
            warningText.IsVisible = false;
            var selection = DefinedNameUiPolicy.PlanManagerSelection(
                rows,
                namesList.SelectedIndex,
                DefinedNameUiProfile.Avalonia);
            if (selection.SelectedRow is not { } selectedRow)
                return;

            await ShowDefineNameDialogAsync(selectedRow);
            RefreshRows();
        });

        deleteButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            var selection = DefinedNameUiPolicy.PlanManagerSelection(
                rows,
                namesList.SelectedIndex,
                DefinedNameUiProfile.Avalonia);
            if (selection.SelectedRow is not { } row)
                return;

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

        // WPF: the cancel (close) button is 64px wide.
        var closeButton = new Button { Content = UiText.Get("InsertLoc_CloseButton"), IsCancel = true, Width = 64, MinWidth = 64 };
        ApplyNamesButtonChrome(closeButton, minWidth: 64);
        AutomationProperties.SetAutomationId(closeButton, "NameManagerCloseButton");
        closeButton.Click += (_, _) => dialog.Close();

        RefreshRows();

        var commandButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { newButton, editButton, deleteButton },
        };
        DockPanel.SetDock(commandButtons, Dock.Left);

        // WPF: the refers-to row carries Margin="0,0,0,8" and the Close row follows it directly.
        var bottomRow = AvaloniaCompactDialogChrome.CreateActionRow([closeButton], new Thickness(0, 8, 0, 0));

        // WPF: the filter label (5px Label padding) sits directly left of the 190px combo, right-aligned.
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("InsertLoc_FilterLabel")),
                    FontSize = 12,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                },
                filterBox,
            },
        };

        var topRow = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { commandButtons, filterRow },
        };

        // WPF: a group box around the list (a plain Window, so a black caption).
        var namesHeader = BuildNameManagerColumnHeader();
        DockPanel.SetDock(namesHeader, Dock.Top);
        var namesGroup = new GroupBox
        {
            Header = StripDisplayMnemonic(UiText.Get("NamedRange_DefinedNames")),
            Margin = new Thickness(0),
            Content = new DockPanel { Children = { namesHeader, namesList } },
        };
        ApplyPlainWindowGroupBoxChrome(namesGroup, NamesDialogChromeStyle);
        AvaloniaGrid.SetRow(namesGroup, 1);

        AvaloniaGrid.SetRow(warningText, 2);
        warningText.Margin = new Thickness(0, 8, 0, 0);

        // WPF: 70 / * / 32 columns; the row sits 8px under the group box.
        var selectedRefersToRow = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("70,*,32"),
        };
        var selectedRefersToLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("InsertLoc_RefersToFieldLabel")),
            FontSize = 12,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0),
        };
        selectedRefersToRow.Children.Add(selectedRefersToLabel);
        AvaloniaGrid.SetColumn(selectedRefersToBox, 1);
        selectedRefersToRow.Children.Add(selectedRefersToBox);
        AvaloniaGrid.SetColumn(selectedRefersToPicker, 2);
        selectedRefersToRow.Children.Add(selectedRefersToPicker);
        AvaloniaGrid.SetRow(selectedRefersToRow, 4);
        AvaloniaGrid.SetRow(bottomRow, 5);

        // WPF: <Grid Margin="12"> with rows Auto / * / 8 / Auto / Auto / Auto. Children are added in
        // the WPF tab order: New, Edit, Delete, Filter, list, refers-to, picker, Close.
        dialog.Content = new AvaloniaGrid
        {
            Margin = new Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,8,Auto,Auto"),
            Children =
            {
                topRow,
                namesGroup,
                warningText,
                selectedRefersToRow,
                bottomRow,
            },
        };
        AttachDialogRangePicker(
            dialog,
            selectedRefersToPicker,
            selectedRefersToBox,
            "range.named-ranges.selected-refers-to");

        await dialog.ShowDialog(this);
    }

    // Name Manager list columns mirror the WPF GridView (Name 130 | Value 120 | Refers To 210 |
    // Scope 100 | Comment 120); the last column takes the remaining width instead of scrolling.
    private static readonly (string Key, GridLength Width)[] NameManagerColumns =
    [
        ("NamedRange_Name", new GridLength(130)),
        ("NamedRange_Value", new GridLength(120)),
        ("NamedRange_RefersTo", new GridLength(210)),
        ("NamedRange_Scope", new GridLength(100)),
        ("NamedRange_Comment", new GridLength(1, GridUnitType.Star)),
    ];

    private static AvaloniaGrid CreateNameManagerColumnGrid()
    {
        var grid = new AvaloniaGrid();
        foreach (var column in NameManagerColumns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width, MinWidth = 0 });
        return grid;
    }

    private static Border BuildNameManagerColumnHeader()
    {
        var grid = CreateNameManagerColumnGrid();
        for (var i = 0; i < NameManagerColumns.Length; i++)
        {
            var header = new TextBlock
            {
                Text = StripDisplayMnemonic(UiText.Get(NameManagerColumns[i].Key)),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = new Thickness(6, 3),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            AvaloniaGrid.SetColumn(header, i);
            grid.Children.Add(header);
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush(213, 213, 213),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Child = grid,
        };
    }

    private static Control BuildNameManagerRowGrid(DefinedNameRow row)
    {
        var grid = CreateNameManagerColumnGrid();
        AutomationProperties.SetName(grid, DefinedNameUiPolicy.FormatNameManagerRow(row));
        var values = new[] { row.Name, row.Value, row.RefersTo, row.ScopeLabel, row.Comment };
        for (var i = 0; i < values.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = values[i],
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                // The shared list chrome pads each item by 4px; 4 + 2 lines the text up with the header.
                Margin = new Thickness(2, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            AvaloniaGrid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
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
            // WPF NameDefinitionDialog: Width = 460, Height = 300 (outer), ResizeMode.NoResize. The
            // content is top-stacked; the rest of the client area stays empty (and holds the inline
            // validation line this shell shows instead of a warning box).
            Width = 460 - WpfNoResizeFrameWidth,
            Height = 300 - WpfNoResizeClientFrameHeight,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "DefineNameDialog");

        var scopeOptions = DefinedNameUiPolicy.BuildScopeOptions(definedNames.ScopeChoices);

        var nameBox = new TextBox { Text = seed?.Name ?? string.Empty };
        ApplyNamesTextBoxChrome(nameBox);
        AutomationProperties.SetAutomationId(nameBox, "DefineNameNameBox");

        var scopeBox = new ComboBox
        {
            ItemsSource = scopeOptions,
            SelectedIndex = definedNames.FindScopeIndex(seed?.Scope),
            // WPF: the Scope combo spans the editor columns like the text boxes.
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyNamesComboBoxChrome(scopeBox);
        AutomationProperties.SetAutomationId(scopeBox, "DefineNameScopeBox");

        var refersToBox = new TextBox
        {
            Text = seed?.RefersTo ?? definedNames.FormatRefersTo(_session.SelectedRange),
        };
        ApplyNamesTextBoxChrome(refersToBox);
        AutomationProperties.SetAutomationId(refersToBox, "DefineNameRefersToBox");

        // WPF: _rangePickerButton is 26px wide, 4px right of the box.
        var refersToPicker = new Button
        {
            Content = "...",
            Width = 26,
            MinWidth = 26,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
        };
        ApplyNamesButtonChrome(refersToPicker, minWidth: 26);
        refersToPicker.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(refersToPicker, "DefineNameRefersToPickerButton");
        AutomationProperties.SetName(refersToPicker, UiText.Get("NameDefinition_RangePickerAutomationName"));

        // WPF: the Comment editor is a plain single-line TextBox like Name.
        var commentBox = new TextBox
        {
            Text = seed?.Comment ?? string.Empty,
        };
        ApplyNamesTextBoxChrome(commentBox);
        AutomationProperties.SetAutomationId(commentBox, "DefineNameCommentBox");

        var warningText = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(warningText, "DefineNameWarningText");

        // WPF: DialogButtonRowFactory.Create(Accept, 72) -- OK then Cancel, 72px wide, 8px apart.
        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true, MinWidth = 72 };
        ApplyNamesButtonChrome(okButton, minWidth: 72, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "DefineNameOkButton");
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true, MinWidth = 72 };
        ApplyNamesButtonChrome(cancelButton, minWidth: 72);
        AutomationProperties.SetAutomationId(cancelButton, "DefineNameCancelButton");

        void ShowWarning(string message)
        {
            warningText.Text = message;
            warningText.IsVisible = true;
        }

        void ValidateLive(object? _, EventArgs __)
        {
            var draft = DefinedNameUiPolicy.CreateDraft(
                nameBox.Text,
                scopeOptions,
                scopeBox.SelectedIndex,
                refersToBox.Text,
                commentBox.Text);
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
            var draft = DefinedNameUiPolicy.CreateDraft(
                nameBox.Text,
                scopeOptions,
                scopeBox.SelectedIndex,
                refersToBox.Text,
                commentBox.Text);
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

            RefreshShell(isEdit
                ? UiText.Format("InsertLoc_UpdatedName", plan.Draft.Name)
                : UiText.Format("InsertLoc_DefinedName", plan.Draft.Name));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // WPF CreateContent: Grid { Margin = 16 } with an 82 / * / Auto column split; Name, Scope and
        // Comment span the two editor columns, Refers to shares its row with the "..." picker.
        var form = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("82,*,Auto") };
        for (var i = 0; i < 4; i++)
            form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddDefineNameRow(form, 0, UiText.Get("InsertLoc_NameFieldLabel"), nameBox);
        AddDefineNameRow(form, 1, UiText.Get("InsertLoc_ScopeFieldLabel"), scopeBox);
        AddDefineNameRow(form, 2, UiText.Get("InsertLoc_CommentFieldLabel"), commentBox);
        AddDefineNameRow(form, 3, UiText.Get("InsertLoc_RefersToFieldLabel"), refersToBox, columnSpan: 1);
        refersToBox.Margin = new Thickness(0, 0, 4, 8);
        refersToPicker.Margin = new Thickness(0, 0, 0, 8);
        AvaloniaGrid.SetRow(refersToPicker, 3);
        AvaloniaGrid.SetColumn(refersToPicker, 2);
        form.Children.Add(refersToPicker);

        // WPF: buttons.Margin = (0, 8, 0, 0) under the last field row.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [okButton, cancelButton],
            new Thickness(0, 8, 0, 0));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children = { form, warningText, buttonRow },
        };
        // WPF: Loaded focuses the Name box with its text selected.
        dialog.Opened += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
        AttachDialogRangePicker(
            dialog,
            refersToPicker,
            refersToBox,
            "range.named-ranges.definition-refers-to");

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// The Create Names from Selection dialog: Top row / Left column / Bottom row / Right column checkboxes,
    /// pre-checked by <see cref="CreateNamesFromSelectionPlanner.DetectOptions"/> for the current selection so
    /// this shell and the WPF shell open identically and match real Excel's auto-detection.
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
            // WPF CreateNamesFromSelectionDialog: Width = 280, ResizeMode.NoResize, a top-stacked
            // StackPanel { Margin = 16 }. The height follows the content so OK/Cancel are never cut.
            Width = 280,
            MinWidth = 280,
            MaxWidth = 280,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "CreateNamesFromSelectionDialog");

        var detected = CreateNamesFromSelectionPlanner.DetectOptions(
            _session.SelectedRange,
            _session.ActiveSheet.GetValue);

        var topRowBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesTopRow"), IsChecked = detected.UseTopRow };
        ApplyNamesCheckBoxChrome(topRowBox);
        AutomationProperties.SetAutomationId(topRowBox, "CreateNamesTopRowBox");
        var leftColumnBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesLeftColumn"), IsChecked = detected.UseLeftColumn };
        ApplyNamesCheckBoxChrome(leftColumnBox);
        AutomationProperties.SetAutomationId(leftColumnBox, "CreateNamesLeftColumnBox");
        var bottomRowBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesBottomRow"), IsChecked = detected.UseBottomRow };
        ApplyNamesCheckBoxChrome(bottomRowBox);
        AutomationProperties.SetAutomationId(bottomRowBox, "CreateNamesBottomRowBox");
        var rightColumnBox = new CheckBox { Content = UiText.Get("InsertLoc_CreateNamesRightColumn"), IsChecked = detected.UseRightColumn };
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

            if (!CreateNamesFromSelectionPlanner.TryCreateOptions(
                    topRowBox.IsChecked == true,
                    leftColumnBox.IsChecked == true,
                    bottomRowBox.IsChecked == true,
                    rightColumnBox.IsChecked == true,
                    out var options,
                    out _))
            {
                warningText.Text = UiText.Get("InsertLoc_SelectAtLeastOneLabel");
                warningText.IsVisible = true;
                return;
            }

            var sheet = _session.ActiveSheet;
            var planned = definedNames.PlanCreateNamesFromSelection(
                _session.SelectedRange,
                options,
                address => SpreadsheetDisplayFormatter.FormatScalarValue(
                    sheet.GetValue(address),
                    SpreadsheetScalarFormatProfile.DefinedNameLabel));

            if (planned.Count == 0)
            {
                warningText.Text = UiText.Get("InsertLoc_NoValidLabels");
                warningText.IsVisible = true;
                return;
            }

            // Built as one CompositeWorkbookCommand (rather than executed as N separate
            // DefineNamedRangeCommand calls) so this dialog action pushes a single undo entry,
            // matching the WPF host's CreateNamedRangesFromSelectionCommand and real Excel:
            // Ctrl+Z after "Create from Selection" must remove every name it just created in one
            // step, not walk back one name at a time.
            var commands = definedNames.BuildCreateCommands(planned);
            var command = commands.Count == 1
                ? (IWorkbookCommand)commands[0]
                : new CompositeWorkbookCommand("Create Names from Selection", commands);
            var result = _session.ExecuteReviewCommand(command);
            if (!result.Success)
            {
                warningText.Text = result.ErrorMessage ?? UiText.Get("InsertLoc_CouldNotCreateNames");
                warningText.IsVisible = true;
                return;
            }

            RefreshShell(UiText.Format("InsertLoc_CreatedNamesFromSelection", commands.Count));
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // WPF: DialogButtonRowFactory.Create(Accept, buttonWidth: 76) -- OK then Cancel.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);

        // WPF: each check box carries Margin = (0, 4, 0, 0) inside StackPanel { Margin = (8, 4, 8, 8) }.
        foreach (var box in new[] { topRowBox, leftColumnBox, bottomRowBox, rightColumnBox })
            box.Margin = new Thickness(0, 4, 0, 0);
        var optionsGroup = new GroupBox
        {
            Header = UiText.Get("CreateNamesFromSelection_GroupHeader"),
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(0),
            Content = new StackPanel
            {
                Margin = new Thickness(8, 4, 8, 8),
                Children = { topRowBox, leftColumnBox, bottomRowBox, rightColumnBox },
            },
        };
        ApplyPlainWindowGroupBoxChrome(optionsGroup, NamesDialogChromeStyle);
        AutomationProperties.SetName(optionsGroup, UiText.Get("CreateNamesFromSelection_GroupAutomationName"));
        warningText.Margin = new Thickness(0, 0, 0, 10);

        // WPF: intro text (6px above the group), the options group box (10px), a grey
        // explanatory line (10px) and the right-aligned button row.
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("InsertLoc_CreateNamesFromValuesIn"),
                    FontSize = 12,
                    Foreground = HeaderForeground,
                    Margin = new Thickness(0, 0, 0, 6),
                },
                optionsGroup,
                new TextBlock
                {
                    Text = UiText.Get("CreateNamesFromSelection_BodyText"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush(109, 109, 109),
                    Margin = new Thickness(0, 0, 0, 10),
                },
                warningText,
                buttonRow,
            },
        };
        // WPF: Loaded focuses the first check box.
        dialog.Opened += (_, _) => topRowBox.Focus();

        await dialog.ShowDialog(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string DescribeNameError(DefinedNameError error) =>
        DefinedNameValidationMessages.Describe(error).Resolve(UiText.Get);

    private static string DescribeRefersToError(RefersToError error) =>
        RefersToValidationMessages.Describe(error).Resolve(UiText.Get);

    private string FormatRangeReferenceQualified(GridRange range) =>
        new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id).FormatRefersTo(range);

    private bool TryParseDefinedNameRange(string text, out GridRange range) =>
        new DefinedNamesSession(_session.Workbook, _session.ActiveSheet.Id).TryParseRange(text, out range);

    private static void AddDefineNameRow(AvaloniaGrid grid, int row, string label, Control field, int columnSpan = 2)
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
        AvaloniaGrid.SetColumnSpan(field, columnSpan);
        grid.Children.Add(field);
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
