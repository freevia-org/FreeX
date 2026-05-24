using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotLabelFilterModel? ResultFilter { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var value = LabelFilterValueBox.Text.Trim();
        if (value.Length == 0)
        {
            MessageBox.Show(this, "Enter a label filter value.", "Label Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var kind = Options[Math.Max(0, LabelFilterKindBox.SelectedIndex)].Kind;
        var value2 = LabelFilterValue2Box.Text.Trim();
        if (kind == PivotLabelFilterKind.Between && value2.Length == 0)
        {
            MessageBox.Show(this, "Enter an ending label filter value.", "Label Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultFilter = new PivotLabelFilterModel(_sourceFieldIndex, kind, value, string.IsNullOrWhiteSpace(value2) ? null : value2);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        LabelFilterKindBox.Focus();
        Keyboard.Focus(LabelFilterKindBox);
    }
}
