using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PivotLabelFilterDialog : Window
{
    private static readonly (string Label, PivotLabelFilterKind Kind)[] Options =
    [
        ("Equals", PivotLabelFilterKind.Equals),
        ("Does Not Equal", PivotLabelFilterKind.DoesNotEqual),
        ("Begins With", PivotLabelFilterKind.BeginsWith),
        ("Ends With", PivotLabelFilterKind.EndsWith),
        ("Contains", PivotLabelFilterKind.Contains),
        ("Does Not Contain", PivotLabelFilterKind.DoesNotContain),
        ("Greater Than", PivotLabelFilterKind.GreaterThan),
        ("Greater Than Or Equal To", PivotLabelFilterKind.GreaterThanOrEqual),
        ("Less Than", PivotLabelFilterKind.LessThan),
        ("Less Than Or Equal To", PivotLabelFilterKind.LessThanOrEqual),
        ("Between", PivotLabelFilterKind.Between)
    ];

    private readonly int _sourceFieldIndex;

    public PivotLabelFilterDialog(int sourceFieldIndex)
    {
        _sourceFieldIndex = sourceFieldIndex;
        InitializeComponent();
        LabelFilterKindBox.ItemsSource = Options.Select(option => option.Label);
        LabelFilterKindBox.SelectedIndex = 4;
        UpdateSecondValueState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotLabelFilterModel? ResultFilter { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var value = LabelFilterValueBox.Text.Trim();
        if (value.Length == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a label filter value.", "Label Filter");
            FocusInvalidLabelValue(LabelFilterValueBox);
            return;
        }

        var kind = GetSelectedKind();
        var value2 = kind == PivotLabelFilterKind.Between ? LabelFilterValue2Box.Text.Trim() : "";
        if (kind == PivotLabelFilterKind.Between && value2.Length == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter an ending label filter value.", "Label Filter");
            FocusInvalidLabelValue(LabelFilterValue2Box);
            return;
        }

        ResultFilter = new PivotLabelFilterModel(_sourceFieldIndex, kind, value, string.IsNullOrWhiteSpace(value2) ? null : value2);
        DialogResult = true;
    }

    private PivotLabelFilterKind GetSelectedKind() =>
        Options[Math.Max(0, LabelFilterKindBox.SelectedIndex)].Kind;

    private void LabelFilterKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSecondValueState();

    private void UpdateSecondValueState()
    {
        var usesSecondValue = GetSelectedKind() == PivotLabelFilterKind.Between;
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

    private void FocusInvalidLabelValue(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
