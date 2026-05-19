using System.Globalization;
using System.Windows;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PivotValueFieldSettingsDialog : Window
{
    private const string AutomaticBaseFieldLabel = "(Automatic)";

    private static readonly (string Label, string Value)[] SummaryFunctions =
    [
        ("Sum", "sum"),
        ("Count", "count"),
        ("Average", "average"),
        ("Max", "max"),
        ("Min", "min"),
        ("Product", "product"),
        ("Count Numbers", "countNums"),
        ("StdDev", "stdDev"),
        ("StdDevp", "stdDevP"),
        ("Var", "var"),
        ("Varp", "varP")
    ];

    private static readonly (string Label, PivotShowValuesAs Value)[] ShowValuesAsOptions =
    [
        ("No Calculation", PivotShowValuesAs.None),
        ("% of Grand Total", PivotShowValuesAs.PercentOfGrandTotal),
        ("% of Row Total", PivotShowValuesAs.PercentOfRowTotal),
        ("% of Column Total", PivotShowValuesAs.PercentOfColumnTotal),
        ("Running Total In", PivotShowValuesAs.RunningTotalIn),
        ("Difference From", PivotShowValuesAs.DifferenceFrom),
        ("% Difference From", PivotShowValuesAs.PercentDifferenceFrom),
        ("Rank Smallest to Largest", PivotShowValuesAs.RankSmallest),
        ("Rank Largest to Smallest", PivotShowValuesAs.RankLargest),
        ("Index", PivotShowValuesAs.Index),
        ("% of Parent Row Total", PivotShowValuesAs.PercentOfParentRowTotal),
        ("% of Parent Column Total", PivotShowValuesAs.PercentOfParentColumnTotal),
        ("% of Parent Total", PivotShowValuesAs.PercentOfParentTotal)
    ];

    private readonly PivotDataFieldModel _initialField;
    private readonly IReadOnlyList<string> _sourceHeaders;

    public PivotValueFieldSettingsDialog(PivotDataFieldModel field, IReadOnlyList<string>? sourceHeaders = null)
    {
        _initialField = field;
        _sourceHeaders = sourceHeaders ?? [];
        ResultDataField = field;

        InitializeComponent();
        LoadOptions(field);
    }

    public PivotDataFieldModel ResultDataField { get; private set; }

    private void LoadOptions(PivotDataFieldModel field)
    {
        CustomNameBox.Text = field.Name;
        SummaryFunctionBox.ItemsSource = SummaryFunctions.Select(item => item.Label);
        SummaryFunctionBox.SelectedIndex = Math.Max(0, Array.FindIndex(
            SummaryFunctions,
            item => string.Equals(item.Value, field.SummaryFunction, StringComparison.OrdinalIgnoreCase)));

        ShowValuesAsBox.ItemsSource = ShowValuesAsOptions.Select(item => item.Label);
        ShowValuesAsBox.SelectedIndex = Math.Max(0, Array.FindIndex(
            ShowValuesAsOptions,
            item => item.Value == field.ShowValuesAs));

        BaseFieldBox.ItemsSource = new[] { AutomaticBaseFieldLabel }.Concat(_sourceHeaders).ToList();
        BaseFieldBox.SelectedIndex = field.BaseFieldIndex is { } baseFieldIndex
            && baseFieldIndex >= 0
            && baseFieldIndex < _sourceHeaders.Count
                ? baseFieldIndex + 1
                : 0;
        BaseItemBox.Text = field.BaseItem ?? "";

        NumberFormatBox.Text = field.NumberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PivotValueFieldSettingsInputParser.TryParseOptionalNumberFormatId(NumberFormatBox.Text, out var numberFormatId))
        {
            MessageBox.Show(this, "Number format ID must be a whole number.", "Value Field Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var summaryFunction = SummaryFunctions[Math.Max(0, SummaryFunctionBox.SelectedIndex)].Value;
        var showValuesAs = ShowValuesAsOptions[Math.Max(0, ShowValuesAsBox.SelectedIndex)].Value;
        int? baseFieldIndex = BaseFieldBox.SelectedIndex > 0 ? BaseFieldBox.SelectedIndex - 1 : null;
        var baseItem = string.IsNullOrWhiteSpace(BaseItemBox.Text)
            ? null
            : BaseItemBox.Text.Trim();
        var name = string.IsNullOrWhiteSpace(CustomNameBox.Text)
            ? _initialField.Name
            : CustomNameBox.Text.Trim();

        ResultDataField = _initialField with
        {
            Name = name,
            SummaryFunction = summaryFunction,
            NumberFormatId = numberFormatId,
            ShowValuesAs = showValuesAs,
            BaseFieldIndex = baseFieldIndex,
            BaseItem = baseItem
        };
        DialogResult = true;
    }
}
