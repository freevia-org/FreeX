using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PivotLabelFilterDialog : Window
{
    private static readonly (string Label, PivotLabelFilterKind Kind)[] Options =
        PivotFieldFilterPlanner.LabelFilterKinds
            .Select(option => (UiText.Get(option.ResourceKey), option.Kind))
            .ToArray();

    private readonly int _sourceFieldIndex;

    public PivotLabelFilterDialog(int sourceFieldIndex, PivotLabelFilterModel? existingFilter = null)
    {
        _sourceFieldIndex = sourceFieldIndex;
        InitializeComponent();
        LabelFilterKindBox.ItemsSource = Options.Select(option => option.Label);
        LoadFilter(existingFilter);
        UpdateSecondValueState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotLabelFilterModel? ResultFilter { get; private set; }

    private void LoadFilter(PivotLabelFilterModel? filter)
    {
        if (filter is null)
        {
            LabelFilterKindBox.SelectedIndex = PivotFieldFilterPlanner.DefaultLabelKindIndex;
            return;
        }

        LabelFilterKindBox.SelectedIndex = PivotFieldFilterPlanner.FindLabelKindIndex(filter.Kind);
        LabelFilterValueBox.Text = filter.Value;
        LabelFilterValue2Box.Text = filter.Value2 ?? "";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var kind = GetSelectedKind();
        if (!PivotFieldFilterPlanner.TryCreateLabelFilterWithValidationError(
                _sourceFieldIndex,
                kind,
                LabelFilterValueBox.Text,
                LabelFilterValue2Box.Text,
                out var filter,
                out var error))
        {
            var errorPlan = PivotFieldFilterPlanner.DescribeLabelFilterValidationError(error);
            var target = ResolveInvalidLabelValue(error);
            DialogFocus.ShowWarningAndFocus(
                this,
                errorPlan is null ? UiText.Get("PivotLabelFilter_ValueRequiredMessage") : UiText.Get(errorPlan.ResourceKey),
                UiText.Get("PivotLabelFilter_LabelFilter"),
                target);
            return;
        }

        ResultFilter = filter!;
        DialogResult = true;
    }

    private PivotLabelFilterKind GetSelectedKind() =>
        PivotFieldFilterPlanner.LabelKindFromIndex(LabelFilterKindBox.SelectedIndex);

    private void LabelFilterKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSecondValueState();

    private void UpdateSecondValueState()
    {
        var usesSecondValue = PivotFieldFilterPlanner.LabelKindNeedsSecondValue(GetSelectedKind());
        var visibility = usesSecondValue ? Visibility.Visible : Visibility.Collapsed;
        LabelFilterValue2Label.Visibility = visibility;
        LabelFilterValue2Box.Visibility = visibility;
        LabelFilterValue2Box.IsEnabled = usesSecondValue;
    }

    private void FocusInitialKeyboardTarget()
    {
        LabelFilterKindBox.Focus();
        Keyboard.Focus(LabelFilterKindBox);
    }

    private TextBox ResolveInvalidLabelValue(PivotLabelFilterValidationError error) =>
        error == PivotLabelFilterValidationError.SecondValueRequired
            ? LabelFilterValue2Box
            : LabelFilterValueBox;
}
