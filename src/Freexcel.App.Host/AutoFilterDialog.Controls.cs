using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        panel.Children.Add(new TextBlock { Text = "_Minimum:", Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_betweenMinBox);
        panel.Children.Add(new TextBlock { Text = "And _maximum:", Margin = new Thickness(10, 3, 6, 0) });
        panel.Children.Add(_betweenMaxBox);
        return panel;
    }

    private StackPanel CreateTopBottomCriteriaPanel()
    {
        _topBottomCountBox.TextChanged += (_, _) => UpdateCriteriaTextFromTypedControls();
        var panel = _topBottomCriteriaPanel;
        panel.Orientation = Orientation.Horizontal;
        panel.Margin = new Thickness(0, 4, 0, 4);
        panel.Children.Add(new TextBlock { Text = "_Show:", Margin = new Thickness(0, 3, 6, 0) });
        panel.Children.Add(_topBottomCountBox);
        panel.Children.Add(_topBottomUnitText);
        return panel;
    }

    private void PopulateColorChoices(IReadOnlyList<AutoFilterColorOption> colorOptions)
    {
        _filterByColorPanel.Children.Clear();
        foreach (var section in colorOptions.GroupBy(option => option.Kind == AutoFilterColorFilterKind.FontColor ? "Font Color" : "Cell Color"))
        {
            _filterByColorPanel.Children.Add(new TextBlock
            {
                Text = section.Key,
                Margin = new Thickness(0, _filterByColorPanel.Children.Count == 0 ? 0 : 8, 0, 4)
            });

            var swatches = new WrapPanel();
            foreach (var option in section)
                swatches.Children.Add(CreateColorChoiceButton(option));
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

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(CreateColorSwatch(option));
        content.Children.Add(new TextBlock
        {
            Text = option.Kind == AutoFilterColorFilterKind.NoFill ? "No Fill" : ColorInputParser.FormatHexColor(option.Color!.Value),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        button.Content = content;
        button.Click += (_, _) => _selectedColorFilter = colorFilter;
        return button;
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
