using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;
using Free.Shared.Shell.Avalonia;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity PivotTable field-filter dialogs for the Avalonia/macOS shell. The field-pane header
/// dropdown (built in <see cref="BuildPivotFieldChip"/> / <see cref="ShowPivotHeaderDropdown"/>) exposes
/// "Label Filters…", "Value Filters…" and a manual item (checkbox) filter; each opens a modal dialog and
/// applies the result through a shared plan carrying the row/column/page field lists (for manual
/// <see cref="PivotFieldModel.SelectedItems"/>),
/// the <see cref="PivotLabelFilterModel"/> list and the <see cref="PivotValueFilterModel"/> list together.
/// Member text for the checkbox list comes from the shared Pivot application session, so both renderers
/// present the same distinct, sorted source items that the refresh service consumes.
/// </summary>
public sealed partial class MainWindow
{
    private const double PivotFieldFilterWindowWidth = 380;
    private const double PivotFieldFilterWindowHeight = 470;
    private const double PivotFieldFilterClientWidth = 364;
    private const double PivotFieldFilterClientHeight = 431;

    private static void ApplyPivotFilterButtonChrome(Button button, double width, bool isDefault = false)
    {
        ApplyPivotButtonChrome(button, width, isDefault);
        button.Width = width;
        button.Height = 20;
        button.MinHeight = 20;
        button.MaxHeight = 20;
        button.Padding = new Thickness(8, 0);
        button.CornerRadius = new CornerRadius(0);
    }

    private static void ApplyPivotFilterTextBoxChrome(TextBox textBox)
    {
        ApplyPivotTextBoxChrome(textBox);
        textBox.Height = 18;
        textBox.MinHeight = 18;
        textBox.MaxHeight = 18;
        textBox.CornerRadius = new CornerRadius(0);
    }

    private static void ApplyPivotFilterCheckBoxChrome(CheckBox checkBox)
    {
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, PivotDialogChromeStyle);
        if (!checkBox.IsThreeState)
            return;

        var checkMark = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 2 6 L 5 9 L 11 2"),
            Stroke = Brush(31, 31, 31),
            StrokeThickness = 1.4,
            Width = 11,
            Height = 10,
            IsVisible = checkBox.IsChecked == true,
        };
        var indeterminateMark = new Border
        {
            Width = 7,
            Height = 2,
            Background = Brush(31, 31, 31),
            IsVisible = checkBox.IsChecked is null,
        };
        checkBox.PropertyChanged += (_, args) =>
        {
            if (args.Property != ToggleButton.IsCheckedProperty)
                return;

            checkMark.IsVisible = checkBox.IsChecked == true;
            indeterminateMark.IsVisible = checkBox.IsChecked is null;
        };

        checkBox.Template = new global::Avalonia.Controls.Templates.FuncControlTemplate<CheckBox>((control, _) =>
        {
            var indicator = new Border
            {
                Width = 13,
                Height = 13,
                Background = Brushes.White,
                BorderBrush = Brush(112, 112, 112),
                BorderThickness = new Thickness(1),
                Child = new Panel
                {
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Children = { checkMark, indeterminateMark },
                },
            };
            var content = new ContentPresenter
            {
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            };
            content.Bind(ContentPresenter.ContentProperty, new global::Avalonia.Data.Binding(nameof(ContentControl.Content)) { Source = control });
            content.Bind(ContentPresenter.ContentTemplateProperty, new global::Avalonia.Data.Binding(nameof(ContentControl.ContentTemplate)) { Source = control });
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Spacing = 4,
                Children = { indicator, content },
            };
        });
    }

    /// <summary>
    /// Entry point for the field pane's header dropdown. Opens the manual item (checkbox) filter, the label
    /// filter, or the value filter dialog for <paramref name="target"/> depending on <paramref name="action"/>.
    /// Returns true when the action was a filter action this partial handled (so the caller skips the
    /// deferred path), false otherwise.
    /// </summary>
    internal bool TryOpenPivotFieldFilter(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action)
    {
        switch (action)
        {
            case PivotHeaderMenuAction.LabelFilter:
                _ = OpenPivotLabelFilterDialogAsync(pivot, target);
                return true;
            case PivotHeaderMenuAction.ValueFilter:
                _ = OpenPivotValueFilterDialogAsync(pivot, target);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens the manual item (checkbox) filter for the field. Wired to a dedicated "Item Filter…" pane menu
    /// entry / field-pane affordance. Reads the field's distinct members, lets the user check the ones to
    /// keep, and writes them to the field's <see cref="PivotFieldModel.SelectedItems"/>.
    /// </summary>
    internal void OpenPivotItemFilter(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        _ = OpenPivotItemFilterDialogAsync(pivot, headers, target);
    }

    // ── Manual item (checkbox) filter ─────────────────────────────────────────
    private async Task OpenPivotItemFilterDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        bool exposeActiveFilterActions = true)
    {
        if (_isOpening || _isSaving)
            return;

        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, target.SourceFieldIndex);
        var members = PivotApplication.ReadSourceItems(
            new PivotApplicationTarget(_session.ActiveSheet, pivot),
            target.SourceFieldIndex);
        if (members.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotLoc_NoItemsToFilter"));
            return;
        }

        var filterState = PivotFieldFilterSummary.CreateState(
            pivot,
            target.SourceFieldIndex,
            target.Area,
            caption,
            members,
            PivotFieldFilterText);
        // No explicit selection (or "(All)") means every item is shown.
        var currentSet = PivotFieldFilterPlanner.ResolveAllowedItems(filterState.SelectedItems);
        var hasItemFilter = exposeActiveFilterActions && currentSet is { Count: > 0 } && currentSet.Count < members.Count;
        var labelFilter = filterState.LabelFilter;
        var valueFilter = filterState.ValueFilter;

        var checkBoxes = new List<CheckBox>();
        var listPanel = new StackPanel();
        foreach (var member in members)
        {
            var box = new CheckBox
            {
                Content = member,
                Tag = member,
                IsChecked = currentSet is null || currentSet.Contains(member),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };
            ApplyPivotFilterCheckBoxChrome(box);
            checkBoxes.Add(box);
            listPanel.Children.Add(box);
        }

        var selectAll = new CheckBox
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_SelectAll")),
            IsChecked = checkBoxes.All(box => box.IsChecked == true),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(2, 0, 0, 6),
            IsThreeState = true,
        };
        ApplyPivotFilterCheckBoxChrome(selectAll);
        var updatingSelection = false;
        void UpdateSelectAllState()
        {
            selectAll.IsChecked = PivotFieldFilterPlanner.ResolveSelectAllState(
                checkBoxes
                    .Where(box => box.IsVisible)
                    .Select(box => box.IsChecked == true)
                    .ToList());
        }
        selectAll.IsCheckedChanged += (_, _) =>
        {
            if (updatingSelection || selectAll.IsChecked is not { } value)
                return;

            updatingSelection = true;
            foreach (var box in checkBoxes)
                if (box.IsVisible)
                    box.IsChecked = value;
            updatingSelection = false;
            UpdateSelectAllState();
        };
        foreach (var box in checkBoxes)
            box.IsCheckedChanged += (_, _) =>
            {
                if (!updatingSelection)
                    UpdateSelectAllState();
            };
        UpdateSelectAllState();

        // ── Search box filters the checkbox list (matches WPF Select Items tab) ──
        var searchBox = new TextBox
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyPivotFilterTextBoxChrome(searchBox);
        AutomationProperties.SetAutomationId(searchBox, "PivotItemFilterSearchBox");
        AutomationProperties.SetName(searchBox, UiText.Get("PivotFieldFilter_Search"));
        searchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty)
                return;
            var query = searchBox.Text?.Trim() ?? string.Empty;
            foreach (var box in checkBoxes)
            {
                var label = (string)box.Tag!;
                box.IsVisible = PivotFieldFilterPlanner.IsFilterItemVisible(label, query);
            }
            UpdateSelectAllState();
        };

        var dialog = new Window
        {
            Title = UiText.Format("MainWindowMessage_PivotFieldFilterTitle", caption),
            Width = PivotFieldFilterWindowWidth,
            Height = PivotFieldFilterWindowHeight,
            MinWidth = PivotFieldFilterWindowWidth,
            MinHeight = PivotFieldFilterWindowHeight,
            MaxWidth = PivotFieldFilterWindowWidth,
            MaxHeight = PivotFieldFilterWindowHeight,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotItemFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true };
        ApplyPivotFilterButtonChrome(ok, 74, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotItemFilterOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyPivotFilterButtonChrome(cancel, 74);
        AutomationProperties.SetAutomationId(cancel, "PivotItemFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(0);
        ok.Click += (_, _) => dialog.Close(1);

        // "Clear Item Filter" lives inside the Select Items tab (matches WPF).
        var clearItemFilterBtn = new Button
        {
            Content = UiText.Get("PivotFieldFilter_ClearItemFilter"),
            MinWidth = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            IsEnabled = hasItemFilter,
        };
        ApplyPivotFilterButtonChrome(clearItemFilterBtn, 120);
        AutomationProperties.SetAutomationId(clearItemFilterBtn, "PivotItemFilterClearItemFilterButton");
        clearItemFilterBtn.Click += (_, _) => dialog.Close(4);

        // ── Select Items tab content ───────────────────────────────────────────
        var selectItemsPanel = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*"),
        };
        var chooseItemsText = new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_ChooseItemsToShow"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(chooseItemsText, 0);
        selectItemsPanel.Children.Add(chooseItemsText);
        var itemSummaryText = new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_NoItemFilter"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(itemSummaryText, 1);
        selectItemsPanel.Children.Add(itemSummaryText);
        var searchLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_Search")),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(searchLabel, 2);
        selectItemsPanel.Children.Add(searchLabel);
        searchBox.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(searchBox, 3);
        selectItemsPanel.Children.Add(searchBox);
        Grid.SetRow(selectAll, 4);
        selectItemsPanel.Children.Add(selectAll);
        clearItemFilterBtn.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(clearItemFilterBtn, 5);
        selectItemsPanel.Children.Add(clearItemFilterBtn);
        var itemListBorder = new Border
        {
            BorderBrush = Brush(189, 189, 189),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Padding = new Thickness(4),
                Content = listPanel,
            },
        };
        Grid.SetRow(itemListBorder, 6);
        selectItemsPanel.Children.Add(itemListBorder);

        // ── Label Filters / Value Filters tabs (route to dedicated dialogs) ─────
        var labelFilterBtn = new Button
        {
            Content = labelFilter is null ? "Add Label Filter..." : "Edit Label Filter...",
            MinWidth = 140,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPivotFilterButtonChrome(labelFilterBtn, 140);
        AutomationProperties.SetAutomationId(labelFilterBtn, "PivotItemFilterLabelFilterButton");
        labelFilterBtn.Click += (_, _) => dialog.Close(2);

        var labelFiltersPanel = new StackPanel { Margin = new Thickness(10) };
        labelFiltersPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_NoLabelFilter"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        labelFiltersPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_ManageLabelFiltersDescription"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var removeLabelFilterBtn = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_RemoveLabelFilter")),
            MinWidth = 140,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            IsEnabled = labelFilter is not null,
        };
        ApplyPivotFilterButtonChrome(removeLabelFilterBtn, 140);
        AutomationProperties.SetAutomationId(removeLabelFilterBtn, "PivotItemFilterRemoveLabelFilterButton");
        removeLabelFilterBtn.Click += (_, _) => dialog.Close(6);
        labelFiltersPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { labelFilterBtn, removeLabelFilterBtn },
        });

        var valueFilterBtn = new Button
        {
            Content = valueFilter is null ? "Add Value Filter..." : "Edit Value Filter...",
            MinWidth = 140,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            IsEnabled = pivot.DataFields.Count > 0,
        };
        ApplyPivotFilterButtonChrome(valueFilterBtn, 140);
        AutomationProperties.SetAutomationId(valueFilterBtn, "PivotItemFilterValueFilterButton");
        valueFilterBtn.Click += (_, _) => dialog.Close(3);

        var valueFiltersPanel = new StackPanel { Margin = new Thickness(10) };
        valueFiltersPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_NoValueFilter"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        valueFiltersPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_ManageValueFiltersDescription"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var valueFilterUnavailable = new TextBlock
        {
            Text = UiText.Get("PivotFieldFilter_AddAtLeastOnePivotTableValueFieldToUseValueFilters"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = pivot.DataFields.Count == 0,
        };
        valueFiltersPanel.Children.Add(valueFilterUnavailable);
        var removeValueFilterBtn = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_RemoveValueFilter")),
            MinWidth = 140,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            IsEnabled = pivot.DataFields.Count > 0 && valueFilter is not null,
        };
        ApplyPivotFilterButtonChrome(removeValueFilterBtn, 140);
        AutomationProperties.SetAutomationId(removeValueFilterBtn, "PivotItemFilterRemoveValueFilterButton");
        removeValueFilterBtn.Click += (_, _) => dialog.Close(7);
        valueFiltersPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { valueFilterBtn, removeValueFilterBtn },
        });

        var tabs = new TabControl
        {
            Padding = new Thickness(0),
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_SelectItems")), Content = selectItemsPanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_LabelFilters")), Content = labelFiltersPanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotFieldFilter_ValueFilters")), Content = valueFiltersPanel, FontSize = 12, FontFamily = FormulaBarFontFamily },
            },
        };
        AutomationProperties.SetAutomationId(tabs, "PivotItemFilterTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);

        // WPF layout: "Clear Filters from This Field" at bottom-left; [OK][Cancel] at bottom-right.
        var clearFiltersBtn = new Button
        {
            Content = UiText.Get("PivotFieldFilter_ClearFiltersFromThisField"),
            MinWidth = 160,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            IsEnabled = hasItemFilter || labelFilter is not null || valueFilter is not null,
        };
        ApplyPivotFilterButtonChrome(clearFiltersBtn, 160);
        AutomationProperties.SetAutomationId(clearFiltersBtn, "PivotItemFilterClearFiltersButton");
        clearFiltersBtn.Click += (_, _) => dialog.Close(5);

        var bottomGrid = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(clearFiltersBtn, 0);
        bottomGrid.Children.Add(clearFiltersBtn);
        var okCancelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, cancel },
        };
        Grid.SetColumn(okCancelRow, 1);
        bottomGrid.Children.Add(okCancelRow);

        var content = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(bottomGrid, Dock.Bottom);
        content.Children.Add(bottomGrid);
        content.Children.Add(tabs);
        KeyboardNavigation.SetTabNavigation(content, KeyboardNavigationMode.Cycle);
        dialog.Content = new Border
        {
            Width = PivotFieldFilterClientWidth,
            Height = PivotFieldFilterClientHeight,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Background = Brushes.White,
            Child = content,
        };
        ConfigurePivotDialogLifecycle(dialog, searchBox, selectAllText: true);
        dialog.Opened += (_, _) =>
        {
            searchBox.Focus();
            searchBox.SelectAll();
        };

        var result = await dialog.ShowDialog<int>(this);
        switch (result)
        {
            case 2:
                await OpenPivotLabelFilterDialogAsync(pivot, target);
                return;
            case 3:
                await OpenPivotValueFilterDialogAsync(pivot, target);
                return;
            case 4:
                ApplyPivotItemFilter(pivot, target, null);
                return;
            case 5:
                ClearPivotFieldFilters(pivot, target);
                return;
            case 6:
                RemovePivotLabelFilter(pivot, target.SourceFieldIndex);
                return;
            case 7:
                RemovePivotValueFilter(pivot, target.SourceFieldIndex);
                return;
            case 1:
                break;
            default:
                return;
        }

        var checked_ = checkBoxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
        // Selecting every item is "no filter": clear the selection so new members stay visible.
        var selection = PivotFieldFilterPlanner.ResolveItemSelection(checked_, members.Count);
        ApplyPivotItemFilter(pivot, target, selection);
    }

    private void ApplyPivotItemFilter(
        PivotTableModel pivot,
        PivotHeaderDropdownTargetModel target,
        IReadOnlyList<string>? selectedItems)
    {
        ApplyPivotApplicationPlan(
            PivotApplication.PlanFieldItemSelection(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                target.Area,
                target.SourceFieldIndex,
                selectedItems));
    }

    private void ClearPivotFieldFilters(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        ApplyPivotApplicationPlan(
            PivotApplication.PlanClearFieldFilters(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                target.Area,
                target.SourceFieldIndex));
    }

    private void RemovePivotLabelFilter(PivotTableModel pivot, int sourceFieldIndex) =>
        ApplyPivotApplicationPlan(
            PivotApplication.PlanReplaceLabelFilter(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                sourceFieldIndex,
                filter: null));

    private void RemovePivotValueFilter(PivotTableModel pivot, int sourceFieldIndex) =>
        ApplyPivotApplicationPlan(
            PivotApplication.PlanRemoveValueFilter(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                sourceFieldIndex));

    // ── Label filter (Equals / Contains / Begins With / …) ─────────────────────
    private async Task OpenPivotLabelFilterDialogAsync(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var existing = pivot.LabelFilters.FirstOrDefault(filter => filter.SourceFieldIndex == target.SourceFieldIndex);

        var kindBox = new ComboBox { MinWidth = 200 };
        foreach (var (label, _) in PivotFieldFilterPlanner.LabelFilterKinds)
            kindBox.Items.Add(label);
        kindBox.SelectedIndex = PivotFieldFilterPlanner.FindLabelKindIndex(existing?.Kind ?? PivotLabelFilterKind.Equals);
        ApplyPivotComboBoxChrome(kindBox);
        AutomationProperties.SetAutomationId(kindBox, "PivotLabelFilterKindBox");
        AutomationProperties.SetName(kindBox, "Label filter kind");

        var value1 = new TextBox { MinWidth = 200, Text = existing?.Value ?? string.Empty, PlaceholderText = UiText.Get("PivotLoc_ValuePlaceholder") };
        ApplyPivotTextBoxChrome(value1);
        AutomationProperties.SetAutomationId(value1, "PivotLabelFilterValueBox");
        AutomationProperties.SetName(value1, "Value");
        var value2 = new TextBox { MinWidth = 200, Text = existing?.Value2 ?? string.Empty, PlaceholderText = UiText.Get("PivotLoc_SecondValuePlaceholder") };
        ApplyPivotTextBoxChrome(value2);
        AutomationProperties.SetAutomationId(value2, "PivotLabelFilterValue2Box");
        AutomationProperties.SetName(value2, "Second value");

        void SyncSecond()
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            value2.IsVisible = PivotFieldFilterPlanner.LabelKindNeedsSecondValue(kind);
        }

        kindBox.SelectionChanged += (_, _) => SyncSecond();
        SyncSecond();

        var dialog = new Window
        {
            Title = UiText.Format("PivotFilter_LabelTitle", target.FieldCaption),
            Width = 380,
            Height = 260,
            MinWidth = 380,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotLabelFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotLabelFilterOkButton");
        var clear = new Button { Content = UiText.Get("Common_Clear"), MinWidth = 80, IsEnabled = existing is not null };
        ApplyPivotButtonChrome(clear, 80);
        AutomationProperties.SetAutomationId(clear, "PivotLabelFilterClearButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotLabelFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(0);
        ok.Click += (_, _) =>
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateLabelFilter(
                    target.SourceFieldIndex, kind, value1.Text, value2.Text, out _, out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.LabelValueRequiredMessage);
                return;
            }

            dialog.Close(1);
        };
        clear.Click += (_, _) => dialog.Close(2);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFilter_LabelHeading"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
        });
        content.Children.Add(kindBox);
        content.Children.Add(value1);
        content.Children.Add(value2);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, clear, cancel },
        });
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, kindBox);

        var result = await dialog.ShowDialog<int>(this);
        if (result == 0)
            return;

        PivotLabelFilterModel? filter = null;
        if (result == 1)
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateLabelFilter(
                    target.SourceFieldIndex, kind, value1.Text, value2.Text, out filter, out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.LabelValueRequiredMessage);
                return;
            }
        }

        ApplyPivotApplicationPlan(
            PivotApplication.PlanReplaceLabelFilter(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                target.SourceFieldIndex,
                filter));
    }

    // ── Value filter (Top N / Greater Than / Between / …) ──────────────────────
    private async Task OpenPivotValueFilterDialogAsync(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        if (pivot.DataFields.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotLoc_AddValueFieldBeforeFilter"));
            return;
        }

        var existing = pivot.ValueFilters
            .FirstOrDefault(filter => PivotFilterOwnership.BelongsToSourceField(filter, target.SourceFieldIndex));

        var kindBox = new ComboBox { MinWidth = 200 };
        foreach (var (label, _) in PivotFieldFilterPlanner.ValueFilterKinds)
            kindBox.Items.Add(label);
        kindBox.SelectedIndex = PivotFieldFilterPlanner.FindValueKindIndex(existing?.Kind ?? PivotValueFilterKind.GreaterThan);
        ApplyPivotComboBoxChrome(kindBox);
        AutomationProperties.SetAutomationId(kindBox, "PivotValueFilterKindBox");
        AutomationProperties.SetName(kindBox, "Value filter kind");

        var dataFieldBox = new ComboBox { MinWidth = 200 };
        for (var index = 0; index < pivot.DataFields.Count; index++)
            dataFieldBox.Items.Add(pivot.DataFields[index].Name);
        dataFieldBox.SelectedIndex = PivotFieldFilterPlanner.InitialDataFieldIndex(existing, pivot.DataFields.Count);
        ApplyPivotComboBoxChrome(dataFieldBox);
        AutomationProperties.SetAutomationId(dataFieldBox, "PivotValueFilterDataFieldBox");
        AutomationProperties.SetName(dataFieldBox, "Summarize by");

        var primary = new TextBox
        {
            MinWidth = 200,
            PlaceholderText = UiText.Get("PivotLoc_CountOrValuePlaceholder"),
            Text = PivotFieldFilterPlanner.PrimaryInputText(existing),
        };
        ApplyPivotTextBoxChrome(primary);
        AutomationProperties.SetAutomationId(primary, "PivotValueFilterPrimaryBox");
        AutomationProperties.SetName(primary, "Count or value");
        var secondary = new TextBox
        {
            MinWidth = 200,
            PlaceholderText = UiText.Get("PivotLoc_SecondValuePlaceholder"),
            Text = PivotFieldFilterPlanner.SecondaryInputText(existing),
        };
        ApplyPivotTextBoxChrome(secondary);
        AutomationProperties.SetAutomationId(secondary, "PivotValueFilterSecondaryBox");
        AutomationProperties.SetName(secondary, "Second value");

        void SyncInputs()
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            primary.IsVisible = PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput(kind);
            secondary.IsVisible = PivotFieldFilterPlanner.ValueKindNeedsSecondValue(kind);
        }

        kindBox.SelectionChanged += (_, _) => SyncInputs();
        SyncInputs();

        var dialog = new Window
        {
            Title = UiText.Format("PivotFilter_ValueTitle", target.FieldCaption),
            Width = 380,
            Height = 260,
            MinWidth = 380,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotValueFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotValueFilterOkButton");
        var clear = new Button { Content = UiText.Get("Common_Clear"), MinWidth = 80, IsEnabled = existing is not null };
        ApplyPivotButtonChrome(clear, 80);
        AutomationProperties.SetAutomationId(clear, "PivotValueFilterClearButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotValueFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(0);
        ok.Click += (_, _) =>
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateValueFilter(
                    target.SourceFieldIndex,
                    dataFieldBox.SelectedIndex,
                    kind,
                    primary.Text,
                    secondary.Text,
                    out _,
                    out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.NumericValueRequiredMessage);
                return;
            }

            dialog.Close(1);
        };
        clear.Click += (_, _) => dialog.Close(2);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotFilter_SummarizeBy"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(dataFieldBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotFilter_WhereValueIs"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(kindBox);
        content.Children.Add(primary);
        content.Children.Add(secondary);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, clear, cancel },
        });
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, kindBox);

        var result = await dialog.ShowDialog<int>(this);
        if (result == 0)
            return;

        PivotValueFilterModel? filter = null;
        if (result == 1)
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateValueFilter(
                    target.SourceFieldIndex,
                    dataFieldBox.SelectedIndex,
                    kind,
                    primary.Text,
                    secondary.Text,
                    out filter,
                    out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.NumericValueRequiredMessage);
                return;
            }
        }

        ApplyPivotApplicationPlan(
            PivotApplication.PlanReplaceValueFilter(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                target.SourceFieldIndex,
                filter));
    }

    // ── Shared command execution + member reading ─────────────────────────────
}
