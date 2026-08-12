using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog
{
    private static Button CreateMenuCommandButton(
        string content,
        RibbonCommandIconKind iconKind,
        Visibility visibility = Visibility.Visible)
    {
        var button = new Button
        {
            Visibility = visibility,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 2),
            MinHeight = 24,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        SetMenuCommandButtonContent(button, content, iconKind);
        return button;
    }

    private static void SetMenuCommandButtonContent(
        Button button,
        string content,
        RibbonCommandIconKind iconKind)
    {
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                RibbonIconFactory.CreateIcon(new RibbonCommandIcon(iconKind), 14, Brushes.Black),
                new TextBlock
                {
                    Text = content,
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            }
        };
    }

    private static string FormatCascadeMenuHeader(string header) => $"{header}    >";

    private static void AddFilterMenuSeparator(Panel stack)
    {
        stack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
    }

    private void FocusInitialKeyboardTarget()
    {
        _sortAscendingButton.Focus();
        Keyboard.Focus(_sortAscendingButton);
    }

    private void AutoFilterDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_useModelessFlyoutCommit && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (IsTextInputElement(e.OriginalSource))
            return;

        if (!TryOpenVisibleFilterFamilySubmenu())
            return;

        e.Handled = true;
    }

    private static bool IsTextInputElement(object? originalSource) =>
        originalSource is TextBox ||
        originalSource is ComboBox { IsEditable: true };

    private bool TryOpenVisibleFilterFamilySubmenu()
    {
        var filterButton = FindFirstVisibleFilterFamilyButton();
        return filterButton is not null && TryOpenFilterFamilySubmenu(filterButton);
    }

    private Button? FindFirstVisibleFilterFamilyButton()
    {
        if (_textFiltersButton.Visibility == Visibility.Visible)
            return _textFiltersButton;

        if (_numberFiltersButton.Visibility == Visibility.Visible)
            return _numberFiltersButton;

        return _dateFiltersButton.Visibility == Visibility.Visible
            ? _dateFiltersButton
            : null;
    }

    private bool TryOpenFilterFamilySubmenu(Button filterButton)
    {
        if (filterButton.ContextMenu is { } submenu)
        {
            submenu.PlacementTarget = filterButton;
            submenu.IsOpen = true;
            var firstItem = FindFirstSubmenuItem(submenu);
            if (firstItem is not null)
            {
                firstItem.Focus();
                Keyboard.Focus(firstItem);
            }

            return true;
        }

        _criteriaOperatorBox.Focus();
        Keyboard.Focus(_criteriaOperatorBox);
        ShowCustomFilterPanel();
        UpdateCriteriaTextFromTypedControls();
        return true;
    }

    private static MenuItem? FindFirstSubmenuItem(ContextMenu submenu)
    {
        foreach (var item in submenu.Items)
        {
            if (item is MenuItem menuItem)
                return menuItem;
        }

        return null;
    }

    private void ShowFilterFamilyButton(AutoFilterMenuFilterKind filterKind)
    {
        _textFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Text
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetMenuCommandButtonContent(_textFiltersButton, FormatCascadeMenuHeader(UiText.Get("AutoFilter_TextFilters")), RibbonCommandIconKind.Filter);
        _numberFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Number
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetMenuCommandButtonContent(_numberFiltersButton, FormatCascadeMenuHeader(UiText.Get("AutoFilter_NumberFilters")), RibbonCommandIconKind.Filter);
        _dateFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Date
            ? Visibility.Visible
            : Visibility.Collapsed;
        SetMenuCommandButtonContent(_dateFiltersButton, FormatCascadeMenuHeader(UiText.Get("AutoFilter_DateFilters")), RibbonCommandIconKind.Filter);
    }

    private void ConfigureFilterFamilySubmenu(AutoFilterMenuPlan menuPlan)
    {
        var family = FindFilterFamilyEntry(menuPlan);
        if (family is null || family.Children.Count == 0)
            return;

        var parentButton = GetFilterFamilyButton(menuPlan.FilterKind);
        var submenu = new ContextMenu();
        var usedAccessKeys = new HashSet<char>();
        foreach (var child in family.Children)
        {
            var menuItem = new MenuItem
            {
                Header = AddUniqueAccessKey(child.Header, usedAccessKeys),
                Tag = child
            };
            menuItem.Click += (_, _) => ApplyFilterFamilyChild(child);
            submenu.Items.Add(menuItem);
        }

        parentButton.ContextMenu = submenu;
    }

    private static AutoFilterMenuEntry? FindFilterFamilyEntry(AutoFilterMenuPlan menuPlan)
    {
        var entries = menuPlan.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Kind == AutoFilterMenuEntryKind.FilterFamily)
                return entry;
        }

        return null;
    }

    private Button GetFilterFamilyButton(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => _numberFiltersButton,
            AutoFilterMenuFilterKind.Date => _dateFiltersButton,
            _ => _textFiltersButton
        };

    private static string AddUniqueAccessKey(string header, HashSet<char> usedAccessKeys)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Contains('_', StringComparison.Ordinal))
            return header;

        for (var i = 0; i < header.Length; i++)
        {
            var ch = header[i];
            if (!char.IsLetterOrDigit(ch) || !usedAccessKeys.Add(char.ToUpperInvariant(ch)))
                continue;

            return string.Concat(header.AsSpan(0, i), "_", header.AsSpan(i));
        }

        return header;
    }

    private void ApplyFilterFamilyChild(AutoFilterMenuEntry child)
    {
        if (child.Kind != AutoFilterMenuEntryKind.FilterFamilyCommand)
            return;

        ShowCustomFilterPanel();
        var option = FindCriteriaOptionByPrefix(child.Value);
        if (option is not null)
            _criteriaOperatorBox.SelectedItem = option;

        _criteriaBox.Text = child.Value;
        UpdateCriteriaTextFromTypedControls();
        if (option?.RequiresValue == false)
            _criteriaBox.Text = child.Value;
        else
            _criteriaValueBox.Focus();
    }

    private AutoFilterCriteriaOption? FindCriteriaOptionByPrefix(string criteriaPrefix)
    {
        var items = _criteriaOperatorBox.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is AutoFilterCriteriaOption option && HasCriteriaPrefix(option, criteriaPrefix))
                return option;
        }

        return null;
    }

    private static bool HasCriteriaPrefix(AutoFilterCriteriaOption option, string criteriaPrefix) =>
        string.Equals(option.CriteriaPrefix, criteriaPrefix, StringComparison.Ordinal);

    private void ShowCustomFilterPanel()
    {
        _customFilterGroup.Visibility = Visibility.Visible;
        UpdateCriteriaTextFromTypedControls();
    }

    private void ApplySortCommand(AutoFilterSortDirection direction)
    {
        CommitResult(AutoFilterDialogCriteriaPlanner.BuildResult(
            direction,
            _allItems,
            string.Empty,
            string.Empty,
            null,
            addCurrentSelectionToFilter: false));
    }

    private void SetSortLabels(AutoFilterMenuPlan menuPlan)
    {
        var ascending = menuPlan.Entries.FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.SortAscending)?.Header;
        var descending = menuPlan.Entries.FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.SortDescending)?.Header;
        if (string.IsNullOrWhiteSpace(ascending) || string.IsNullOrWhiteSpace(descending))
        {
            SetSortLabels(menuPlan.FilterKind);
            return;
        }

        SetMenuCommandButtonContent(_sortAscendingButton, ascending, RibbonCommandIconKind.SortAscending);
        SetMenuCommandButtonContent(_sortDescendingButton, descending, RibbonCommandIconKind.SortDescending);
    }

    private void SetSortLabels(AutoFilterMenuFilterKind filterKind)
    {
        var labels = AutoFilterDropdownMenuPlanner.GetSortLabels(
            filterKind,
            AutoFilterMenuResources.TextProvider);
        SetMenuCommandButtonContent(_sortAscendingButton, labels.Ascending, RibbonCommandIconKind.SortAscending);
        SetMenuCommandButtonContent(_sortDescendingButton, labels.Descending, RibbonCommandIconKind.SortDescending);
    }

    private StackPanel CreateBetweenCriteriaPanel()
    {
        _betweenMinBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        _betweenMaxBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        var panel = _betweenCriteriaPanel;
        panel.Orientation = Orientation.Horizontal;
        panel.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(new Label { Content = UiText.Get("AutoFilter_MinimumLabel"), Target = _betweenMinBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_betweenMinBox);
        panel.Children.Add(new Label { Content = UiText.Get("AutoFilter_MaximumLabel"), Target = _betweenMaxBox, Padding = new Thickness(0), Margin = new Thickness(10, 3, 6, 0) });
        panel.Children.Add(_betweenMaxBox);
        return panel;
    }

    private StackPanel CreateTopBottomCriteriaPanel()
    {
        _topBottomCountBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        var panel = _topBottomCriteriaPanel;
        panel.Orientation = Orientation.Horizontal;
        panel.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(new Label { Content = UiText.Get("AutoFilter_ShowLabel"), Target = _topBottomCountBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_topBottomCountBox);
        panel.Children.Add(_topBottomUnitText);
        return panel;
    }

    private void PopulateColorChoices(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        _filterByColorPanel.Children.Clear();
        _colorChoiceButtons.Clear();
        foreach (var section in colorOptions.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor ? UiText.Get("AutoFilter_FontColor") : UiText.Get("AutoFilter_CellColor")))
        {
            _filterByColorPanel.Children.Add(new TextBlock
            {
                Text = section.Key,
                Margin = new Thickness(0, _filterByColorPanel.Children.Count == 0 ? 0 : 8, 0, 4)
            });

            var swatches = new WrapPanel();
            KeyboardNavigation.SetDirectionalNavigation(swatches, KeyboardNavigationMode.Contained);
            foreach (var option in section)
            {
                var button = CreateColorChoiceButton(option);
                _colorChoiceButtons.Add(button);
                swatches.Children.Add(button);
            }

            _filterByColorPanel.Children.Add(swatches);
        }

        _filterByColorGroup.Visibility = Visibility.Visible;
    }

    private Button CreateColorChoiceButton(AutoFilterColorOption option)
    {
        var colorFilter = new AutoFilterColorFilter(option.Kind, option.Color);
        var button = new Button
        {
            Width = 92,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = option.Label
        };
        AutomationProperties.SetName(button, CreateColorChoiceAutomationName(option));
        AutomationProperties.SetHelpText(button, UiText.Get("AutoFilter_ApplyThisColorFilter"));
        button.PreviewKeyDown += ColorChoiceButton_PreviewKeyDown;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateColorSwatch(option));
        content.Children.Add(new TextBlock
        {
            Text = option.Kind == AutoFilterColorFilterKind.NoFill ? UiText.Get("AutoFilter_NoFill") : ColorInputParser.FormatHexColor(option.Color!.Value),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => ApplyColorChoice(colorFilter);
        return button;
    }

    private static string CreateColorChoiceAutomationName(AutoFilterColorOption option) =>
        option.Kind switch
        {
            AutoFilterColorFilterKind.FontColor => UiText.Format("AutoFilter_FilterByFontColor", option.Label),
            AutoFilterColorFilterKind.NoFill => UiText.Get("AutoFilter_FilterByNoFill"),
            _ => UiText.Format("AutoFilter_FilterByCellColor", option.Label)
        };

    private void ColorChoiceButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Button button)
            return;

        var currentIndex = _colorChoiceButtons.IndexOf(button);
        if (currentIndex < 0)
            return;

        var targetIndex = e.Key switch
        {
            Key.Left or Key.Up => currentIndex - 1,
            Key.Right or Key.Down => currentIndex + 1,
            Key.Home => 0,
            Key.End => _colorChoiceButtons.Count - 1,
            _ => currentIndex
        };

        if (targetIndex == currentIndex)
            return;

        FocusColorChoiceButton(targetIndex);
        e.Handled = true;
    }

    private void ChecklistBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var handled = e.Key switch
        {
            Key.Space => ToggleFocusedChecklistItem(),
            Key.Home => FocusChecklistItem(0),
            Key.End => FocusChecklistItem(_items.Count - 1),
            _ => false
        };

        if (handled)
            e.Handled = true;
    }

    private bool ToggleFocusedChecklistItem()
    {
        var index = _checklistBox.SelectedIndex >= 0 ? _checklistBox.SelectedIndex : 0;
        if (index < 0 || index >= _items.Count)
            return false;

        var item = _items[index];
        item.IsSelected = !item.IsSelected;
        _checklistBox.Items.Refresh();
        UpdateSelectAllBoxState();
        FocusChecklistItem(index);
        return true;
    }

    private bool FocusChecklistItem(int index)
    {
        if (_items.Count == 0)
            return false;

        var item = _items[Math.Clamp(index, 0, _items.Count - 1)];
        _checklistBox.SelectedItem = item;
        _checklistBox.ScrollIntoView(item);
        _checklistBox.UpdateLayout();
        if (_checklistBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
            Keyboard.Focus(container);
        }

        return true;
    }

    private void FocusColorChoiceButton(int index)
    {
        if (_colorChoiceButtons.Count == 0)
            return;

        var button = _colorChoiceButtons[Math.Clamp(index, 0, _colorChoiceButtons.Count - 1)];
        button.Focus();
        Keyboard.Focus(button);
    }

    private void ApplyColorChoice(AutoFilterColorFilter colorFilter)
    {
        _selectedColorFilter = colorFilter;
        CommitResult(AutoFilterDialogCriteriaPlanner.BuildResult(
            AutoFilterSortDirection.None,
            _allItems,
            _searchBox.Text,
            GetCommittedCriteriaText(),
            colorFilter,
            _addCurrentSelectionToFilterBox.IsChecked == true));
    }

    // R76-render-autofilter-dropdown-4-2: Sort by Color mirrors Excel's swatch picker next to
    // Filter by Color, but moves the matching rows to the top instead of hiding non-matching ones --
    // see AutoFilterDropdownMenuPlanner.CreateSortByColorCommand for the SortCommand it produces.
    private void PopulateSortByColorChoices(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        _sortByColorPanel.Children.Clear();
        _sortByColorButtons.Clear();
        foreach (var section in colorOptions.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor ? UiText.Get("AutoFilter_FontColor") : UiText.Get("AutoFilter_CellColor")))
        {
            _sortByColorPanel.Children.Add(new TextBlock
            {
                Text = section.Key,
                Margin = new Thickness(0, _sortByColorPanel.Children.Count == 0 ? 0 : 8, 0, 4)
            });

            var swatches = new WrapPanel();
            KeyboardNavigation.SetDirectionalNavigation(swatches, KeyboardNavigationMode.Contained);
            foreach (var option in section)
            {
                var button = CreateSortByColorChoiceButton(option);
                _sortByColorButtons.Add(button);
                swatches.Children.Add(button);
            }

            _sortByColorPanel.Children.Add(swatches);
        }

        _sortByColorGroup.Visibility = Visibility.Visible;
    }

    private Button CreateSortByColorChoiceButton(AutoFilterColorOption option)
    {
        var colorFilter = new AutoFilterColorFilter(option.Kind, option.Color);
        var button = new Button
        {
            Width = 92,
            Height = 24,
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = option.Label
        };
        AutomationProperties.SetName(button, option.Kind == AutoFilterColorFilterKind.FontColor
            ? UiText.Format("AutoFilter_SortByFontColor", option.Label)
            : UiText.Format("AutoFilter_SortByCellColor", option.Label));
        AutomationProperties.SetHelpText(button, UiText.Get("AutoFilter_ApplyThisSortByColor"));

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateColorSwatch(option));
        content.Children.Add(new TextBlock
        {
            Text = ColorInputParser.FormatHexColor(option.Color!.Value),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => ApplySortByColorChoice(colorFilter);
        return button;
    }

    private void ApplySortByColorChoice(AutoFilterColorFilter colorFilter) =>
        CommitResult(AutoFilterDialogCriteriaPlanner.BuildSortByColorResult(colorFilter));

    private void ApplySearchTextChange()
    {
        var state = AutoFilterMenuPlanner.PlanChecklistState(_allItems, _searchBox.Text);
        ReplaceItems(state.VisibleItems);

        _addCurrentSelectionToFilterBox.Visibility = state.IsAddCurrentSelectionVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        _addCurrentSelectionToFilterBox.IsEnabled = state.IsAddCurrentSelectionEnabled;
        if (state.ShouldClearAddCurrentSelection)
            _addCurrentSelectionToFilterBox.IsChecked = false;
    }

    private void SetSelectionForVisibleItems(bool isSelected)
    {
        if (_updatingSelectAllBox)
            return;

        ReplaceAllItems(AutoFilterDialogCriteriaPlanner.SetSelectionForSearch(_allItems, _searchBox.Text, isSelected));
    }

    private void UpdateSelectAllBoxState()
    {
        _updatingSelectAllBox = true;
        try
        {
            var state = AutoFilterMenuPlanner.PlanChecklistState(_allItems, _searchBox.Text);
            _selectAllBox.IsEnabled = state.IsChecklistEnabled;
            _checklistBox.IsEnabled = state.IsChecklistEnabled;
            _addCurrentSelectionToFilterBox.IsEnabled = state.IsAddCurrentSelectionEnabled;
            _selectAllBox.IsChecked = state.SelectAllState;
        }
        finally
        {
            _updatingSelectAllBox = false;
        }
    }

    private void ChecklistItemSelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateSelectAllBoxState();
    }

    private static Rectangle CreateColorSwatch(AutoFilterColorOption option)
    {
        var fill = option.Color is { } color
            ? new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
            : Brushes.White;
        return new Rectangle
        {
            Width = 14,
            Height = 14,
            Fill = fill,
            Stroke = Brushes.Gray,
            StrokeThickness = 1,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
    }

    private DataTemplate CreateItemTemplate()
    {
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.DisplayText)));
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.IsSelected))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        checkBox.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(ChecklistItemSelectionChanged));
        checkBox.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(ChecklistItemSelectionChanged));
        return new DataTemplate { VisualTree = checkBox };
    }
}
