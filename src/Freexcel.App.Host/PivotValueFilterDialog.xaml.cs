using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PivotValueFilterDialog : Window
{
    private static readonly (string Label, PivotValueFilterKind Kind, bool UsesCount)[] Options =
    [
        ("Top", PivotValueFilterKind.Top, true),
        ("Bottom", PivotValueFilterKind.Bottom, true),
        ("Greater Than", PivotValueFilterKind.GreaterThan, false),
        ("Greater Than Or Equal", PivotValueFilterKind.GreaterThanOrEqual, false),
        ("Less Than", PivotValueFilterKind.LessThan, false),
        ("Less Than Or Equal", PivotValueFilterKind.LessThanOrEqual, false),
        ("Equals", PivotValueFilterKind.Equals, false),
        ("Does Not Equal", PivotValueFilterKind.DoesNotEqual, false),
        ("Between", PivotValueFilterKind.Between, false),
        ("Not Between", PivotValueFilterKind.NotBetween, false),
        ("Above Average", PivotValueFilterKind.AboveAverage, false),
        ("Below Average", PivotValueFilterKind.BelowAverage, false)
    ];

    private readonly int _sourceFieldIndex;

    public PivotValueFilterDialog(int sourceFieldIndex)
    {
        _sourceFieldIndex = sourceFieldIndex;
        InitializeComponent();
        ValueFilterKindBox.ItemsSource = Options.Select(option => option.Label);
        ValueFilterKindBox.SelectedIndex = 2;
        ValueFilterValueBox.Text = "0";
        UpdateValueInputState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotValueFilterModel? ResultFilter { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var option = Options[Math.Max(0, ValueFilterKindBox.SelectedIndex)];
        if (!PivotValueFilterInputParser.TryCreateFilter(
                option.Kind,
                option.UsesCount,
                ValueFilterValueBox.Text,
                ValueFilterValue2Box.Text,
                _sourceFieldIndex,
                out var filter,
                out var error))
        {
            MessageBox.Show(this, error ?? "Enter a valid value filter.", "Value Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidValueFilterInput(error);
            return;
        }

        ResultFilter = filter;
        DialogResult = true;
    }

    private (string Label, PivotValueFilterKind Kind, bool UsesCount) GetSelectedOption() =>
        Options[Math.Max(0, ValueFilterKindBox.SelectedIndex)];

    private void ValueFilterKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateValueInputState();

    private void UpdateValueInputState()
    {
        var option = GetSelectedOption();
        var usesPrimaryValue = option.UsesCount ||
            option.Kind is not (PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage);
        var usesSecondValue = option.Kind is PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween;

        SetInputState(ValueFilterValueLabel, ValueFilterValueBox, usesPrimaryValue);
        SetInputState(ValueFilterValue2Label, ValueFilterValue2Box, usesSecondValue);
    }

    private static void SetInputState(UIElement label, Control input, bool isVisible)
    {
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        label.Visibility = visibility;
        input.Visibility = visibility;
        input.IsEnabled = isVisible;
    }

    private void FocusInitialKeyboardTarget()
    {
        ValueFilterKindBox.Focus();
        Keyboard.Focus(ValueFilterKindBox);
    }

    private void FocusInvalidValueFilterInput(string? error)
    {
        var target = string.Equals(error, "Enter a numeric ending comparison value.", StringComparison.Ordinal)
            ? ValueFilterValue2Box
            : ValueFilterValueBox;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
