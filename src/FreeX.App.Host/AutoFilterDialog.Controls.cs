using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog
{
    private static void AddFilterMenuSeparator(Panel stack)
    {
        stack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
    }

    private void FocusInitialKeyboardTarget()
    {
        _sortAscending.Focus();
        Keyboard.Focus(_sortAscending);
    }

    private void AutoFilterDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
        var filterButton = new[] { _textFiltersButton, _numberFiltersButton, _dateFiltersButton }
            .FirstOrDefault(button => button.Visibility == Visibility.Visible);
        return filterButton is not null && TryOpenFilterFamilySubmenu(filterButton);
    }

    private bool TryOpenFilterFamilySubmenu(Button filterButton)
    {
        if (filterButton.ContextMenu is { } submenu)
        {
            submenu.PlacementTarget = filterButton;
            submenu.IsOpen = true;
            var firstItem = submenu.Items.OfType<MenuItem>().FirstOrDefault();
            if (firstItem is not null)
            {
                firstItem.Focus();
                Keyboard.Focus(firstItem);
            }

            return true;
        }

        _criteriaOperatorBox.Focus();
        Keyboard.Focus(_criteriaOperatorBox);
        UpdateCriteriaTextFromTypedControls();
        return true;
    }

    private void ShowFilterFamilyButton(AutoFilterMenuFilterKind filterKind)
    {
        _textFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Text
            ? Visibility.Visible
            : Visibility.Collapsed;
        _numberFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Number
            ? Visibility.Visible
            : Visibility.Collapsed;
        _dateFiltersButton.Visibility = filterKind == AutoFilterMenuFilterKind.Date
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ConfigureFilterFamilySubmenu(AutoFilterMenuPlan menuPlan)
    {
        var family = menuPlan.Entries.FirstOrDefault(entry => entry.Kind == AutoFilterMenuEntryKind.FilterFamily);
        if (family is null || family.Children.Count == 0)
            return;

        var parentButton = menuPlan.FilterKind switch
        {
            AutoFilterMenuFilterKind.Number => _numberFiltersButton,
            AutoFilterMenuFilterKind.Date => _dateFiltersButton,
            _ => _textFiltersButton
        };
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

        var option = _criteriaOperatorBox.Items
            .OfType<AutoFilterCriteriaOption>()
            .FirstOrDefault(item => string.Equals(item.CriteriaPrefix, child.Value, StringComparison.Ordinal));
        if (option is not null)
            _criteriaOperatorBox.SelectedItem = option;

        _criteriaBox.Text = child.Value;
        UpdateCriteriaTextFromTypedControls();
        if (option?.RequiresValue == false)
            _criteriaBox.Text = child.Value;
        else
            _criteriaValueBox.Focus();
    }

    public static (string Ascending, string Descending) GetSortLabels(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => ("Sort _Smallest to Largest", "Sort _Largest to Smallest"),
            AutoFilterMenuFilterKind.Date => ("Sort _Oldest to Newest", "Sort _Newest to Oldest"),
            _ => ("Sort _A to Z", "Sort _Z to A")
        };

    private void SetSortLabels(AutoFilterMenuFilterKind filterKind)
    {
        var labels = GetSortLabels(filterKind);
        _sortAscending.Content = labels.Ascending;
        _sortDescending.Content = labels.Descending;
    }

    private StackPanel CreateBetweenCriteriaPanel()
    {
        _betweenMinBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        _betweenMaxBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        var panel = _betweenCriteriaPanel;
        panel.Orientation = Orientation.Horizontal;
        panel.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(new Label { Content = "_Minimum:", Target = _betweenMinBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_betweenMinBox);
        panel.Children.Add(new Label { Content = "And _maximum:", Target = _betweenMaxBox, Padding = new Thickness(0), Margin = new Thickness(10, 3, 6, 0) });
        panel.Children.Add(_betweenMaxBox);
        return panel;
    }

    private StackPanel CreateTopBottomCriteriaPanel()
    {
        _topBottomCountBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        var panel = _topBottomCriteriaPanel;
        panel.Orientation = Orientation.Horizontal;
        panel.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(new Label { Content = "_Show:", Target = _topBottomCountBox, Padding = new Thickness(0), Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_topBottomCountBox);
        panel.Children.Add(_topBottomUnitText);
        return panel;
    }

    private void PopulateColorChoices(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        _filterByColorPanel.Children.Clear();
        _colorChoiceButtons.Clear();
        foreach (var section in colorOptions.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor ? "Font Color" : "Cell Color"))
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
        button.PreviewKeyDown += ColorChoiceButton_PreviewKeyDown;

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateColorSwatch(option));
        content.Children.Add(new TextBlock
        {
            Text = option.Kind == AutoFilterColorFilterKind.NoFill ? "No Fill" : ColorInputParser.FormatHexColor(option.Color!.Value),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => ApplyColorChoice(colorFilter);
        return button;
    }

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
        Result = BuildResult(
            GetSortDirection(),
            _allItems,
            _searchBox.Text,
            _criteriaBox.Text,
            colorFilter,
            _addCurrentSelectionToFilterBox.IsChecked == true);
        DialogResult = true;
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

    private static DataTemplate CreateItemTemplate()
    {
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.DisplayText)));
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(AutoFilterDialogItem.IsSelected))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return new DataTemplate { VisualTree = checkBox };
    }
}
