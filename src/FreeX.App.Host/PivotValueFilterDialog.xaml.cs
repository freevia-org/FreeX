using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PivotValueFilterDialog : Window
{
    private static readonly (string Label, PivotValueFilterKind Kind, bool UsesCount)[] Options =
    [
        (UiText.Get("PivotValueFilter_Top"), PivotValueFilterKind.Top, true),
        (UiText.Get("PivotValueFilter_Bottom"), PivotValueFilterKind.Bottom, true),
        (UiText.Get("PivotValueFilter_GreaterThan"), PivotValueFilterKind.GreaterThan, false),
        (UiText.Get("PivotValueFilter_GreaterThanOrEqual"), PivotValueFilterKind.GreaterThanOrEqual, false),
        (UiText.Get("PivotValueFilter_LessThan"), PivotValueFilterKind.LessThan, false),
        (UiText.Get("PivotValueFilter_LessThanOrEqual"), PivotValueFilterKind.LessThanOrEqual, false),
        (UiText.Get("PivotValueFilter_Equals"), PivotValueFilterKind.Equals, false),
        (UiText.Get("PivotValueFilter_DoesNotEqual"), PivotValueFilterKind.DoesNotEqual, false),
        (UiText.Get("PivotValueFilter_Between"), PivotValueFilterKind.Between, false),
        (UiText.Get("PivotValueFilter_NotBetween"), PivotValueFilterKind.NotBetween, false),
        (UiText.Get("PivotValueFilter_AboveAverage"), PivotValueFilterKind.AboveAverage, false),
        (UiText.Get("PivotValueFilter_BelowAverage"), PivotValueFilterKind.BelowAverage, false)
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
            DialogMessageHelper.ShowWarning(this, error ?? UiText.Get("PivotValueFilter_InvalidValueMessage"), UiText.Get("PivotValueFilter_ValueFilter"));
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
        var target = string.Equals(error, UiText.Get("PivotValueFilter_NumericEndingComparisonMessage"), StringComparison.Ordinal)
            ? ValueFilterValue2Box
            : ValueFilterValueBox;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
