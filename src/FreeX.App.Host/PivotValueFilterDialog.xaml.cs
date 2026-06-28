using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PivotValueFilterDialog : Window
{
    private static readonly (string Label, PivotValueFilterKind Kind)[] Options =
        PivotFieldFilterPlanner.ValueFilterKinds
            .Select(option => (UiText.Get(option.ResourceKey), option.Kind))
            .ToArray();

    private readonly int _sourceFieldIndex;
    private readonly int _dataFieldIndex;

    public PivotValueFilterDialog(int sourceFieldIndex, PivotValueFilterModel? existingFilter = null)
    {
        _sourceFieldIndex = sourceFieldIndex;
        _dataFieldIndex = existingFilter?.DataFieldIndex ?? 0;
        InitializeComponent();
        ValueFilterKindBox.ItemsSource = Options.Select(option => option.Label);
        LoadFilter(existingFilter);
        UpdateValueInputState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotValueFilterModel? ResultFilter { get; private set; }

    private void LoadFilter(PivotValueFilterModel? filter)
    {
        if (filter is null)
        {
            ValueFilterKindBox.SelectedIndex = PivotFieldFilterPlanner.DefaultValueKindIndex;
            ValueFilterValueBox.Text = PivotFieldFilterPlanner.DefaultValueFilterPrimaryText;
            return;
        }

        ValueFilterKindBox.SelectedIndex = PivotFieldFilterPlanner.FindValueKindIndex(filter.Kind);
        ValueFilterValueBox.Text = PivotFieldFilterPlanner.PrimaryInputText(filter, CultureInfo.InvariantCulture);
        ValueFilterValue2Box.Text = PivotFieldFilterPlanner.SecondaryInputText(filter, CultureInfo.InvariantCulture);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var option = Options[Math.Max(0, ValueFilterKindBox.SelectedIndex)];
        if (!PivotFieldFilterPlanner.TryCreateValueFilter(
                _sourceFieldIndex,
                _dataFieldIndex,
                option.Kind,
                ValueFilterValueBox.Text,
                ValueFilterValue2Box.Text,
                CultureInfo.InvariantCulture,
                out var filter,
                out var error))
        {
            var errorPlan = PivotFieldFilterPlanner.DescribeValueFilterValidationError(error);
            DialogMessageHelper.ShowWarning(
                this,
                errorPlan is null ? UiText.Get("PivotValueFilter_InvalidValueMessage") : UiText.Get(errorPlan.ResourceKey),
                UiText.Get("PivotValueFilter_ValueFilter"));
            FocusInvalidValueFilterInput(error);
            return;
        }

        ResultFilter = filter!;
        DialogResult = true;
    }

    private (string Label, PivotValueFilterKind Kind) GetSelectedOption() =>
        Options[Math.Max(0, ValueFilterKindBox.SelectedIndex)];

    private void ValueFilterKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateValueInputState();

    private void UpdateValueInputState()
    {
        var option = GetSelectedOption();
        var usesPrimaryValue = PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput(option.Kind);
        var usesSecondValue = PivotFieldFilterPlanner.ValueKindNeedsSecondValue(option.Kind);

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

    private void FocusInvalidValueFilterInput(PivotValueFilterValidationError error)
    {
        var target = error == PivotValueFilterValidationError.NumericSecondValueRequired
            ? ValueFilterValue2Box
            : ValueFilterValueBox;
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
