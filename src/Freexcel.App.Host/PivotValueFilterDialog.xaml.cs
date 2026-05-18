using System.Globalization;
using System.Windows;
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
    }

    public PivotValueFilterModel? ResultFilter { get; private set; }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var option = Options[Math.Max(0, ValueFilterKindBox.SelectedIndex)];
        if (option.UsesCount)
        {
            if (!int.TryParse(ValueFilterValueBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                MessageBox.Show(this, "Enter a positive item count.", "Value Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultFilter = new PivotValueFilterModel(0, option.Kind, Count: count, SourceFieldIndex: _sourceFieldIndex);
            DialogResult = true;
            return;
        }

        if (option.Kind is PivotValueFilterKind.AboveAverage or PivotValueFilterKind.BelowAverage)
        {
            ResultFilter = new PivotValueFilterModel(0, option.Kind, SourceFieldIndex: _sourceFieldIndex);
            DialogResult = true;
            return;
        }

        if (!double.TryParse(ValueFilterValueBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            MessageBox.Show(this, "Enter a numeric comparison value.", "Value Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double? value2 = null;
        if (option.Kind is PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween)
        {
            if (!double.TryParse(ValueFilterValue2Box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue2))
            {
                MessageBox.Show(this, "Enter a numeric ending comparison value.", "Value Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            value2 = parsedValue2;
        }

        ResultFilter = new PivotValueFilterModel(0, option.Kind, ComparisonValue: value, ComparisonValue2: value2, SourceFieldIndex: _sourceFieldIndex);
        DialogResult = true;
    }
}
