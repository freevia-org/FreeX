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

        NumberFormatPresetBox.ItemsSource = PivotValueFieldSettingsInputParser.NumberFormatPresets.Select(preset => preset.Label);
        NumberFormatPresetBox.SelectedIndex = FindNumberFormatPresetIndex(field.NumberFormatId);
        NumberFormatBox.Text = field.NumberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = field.NumberFormatCode ?? "";
        UpdateBaseFieldState();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PivotValueFieldSettingsInputParser.TryParseOptionalNumberFormatId(NumberFormatBox.Text, out var numberFormatId))
        {
            MessageBox.Show(this, "Number format ID must be a whole number.", "Value Field Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        numberFormatId ??= PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatId(NumberFormatPresetBox.SelectedItem as string);
        var numberFormatCode = PivotValueFieldSettingsInputParser.ResolveOptionalNumberFormatCode(NumberFormatCodeBox.Text);
        numberFormatId = PivotValueFieldSettingsInputParser.ResolveNumberFormatIdForCode(numberFormatId, numberFormatCode);

        var summaryFunction = SummaryFunctions[Math.Max(0, SummaryFunctionBox.SelectedIndex)].Value;
        var showValuesAs = ShowValuesAsOptions[Math.Max(0, ShowValuesAsBox.SelectedIndex)].Value;
        var usesBaseField = ShowValuesAsRequiresBaseField(showValuesAs);
        int? baseFieldIndex = usesBaseField && BaseFieldBox.SelectedIndex > 0 ? BaseFieldBox.SelectedIndex - 1 : null;
        var baseItem = !usesBaseField || string.IsNullOrWhiteSpace(BaseItemBox.Text)
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
            NumberFormatCode = numberFormatCode,
            ShowValuesAs = showValuesAs,
            BaseFieldIndex = baseFieldIndex,
            BaseItem = baseItem
        };
        DialogResult = true;
    }

    private void NumberFormatPresetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var numberFormatId = PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatId(NumberFormatPresetBox.SelectedItem as string);
        NumberFormatBox.Text = numberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = "";
    }

    private void ShowValuesAsBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateBaseFieldState();

    private void NumberFormatButton_Click(object sender, RoutedEventArgs e)
    {
        var style = new CellStyle { NumberFormat = CurrentNumberFormatCode() };
        var dialog = new FormatCellsDialog(style, FormatCellsDialogTab.Number)
        {
            Owner = this,
            Title = "Format Cells"
        };

        if (dialog.ShowDialog() != true || dialog.ResultDiff?.NumberFormat is not { } numberFormat)
            return;

        NumberFormatCodeBox.Text = numberFormat;
        NumberFormatBox.Text = PivotValueFieldSettingsInputParser.DefaultCustomNumberFormatId.ToString(CultureInfo.InvariantCulture);
        NumberFormatPresetBox.Text = numberFormat;
    }

    private string CurrentNumberFormatCode()
    {
        var customCode = PivotValueFieldSettingsInputParser.ResolveOptionalNumberFormatCode(NumberFormatCodeBox.Text);
        if (!string.IsNullOrWhiteSpace(customCode))
            return customCode;

        if (NumberFormatPresetBox.SelectedItem is string selectedPreset &&
            PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatCode(selectedPreset) is { } selectedCode)
        {
            return selectedCode;
        }

        return PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatCode(NumberFormatPresetBox.Text)
            ?? NumberFormatPresetBox.Text
            ?? "General";
    }

    private void UpdateBaseFieldState()
    {
        if (BaseFieldPanel is null || BaseItemPanel is null || ShowValuesAsBox is null)
            return;

        var showValuesAs = ShowValuesAsOptions[Math.Max(0, ShowValuesAsBox.SelectedIndex)].Value;
        var visible = ShowValuesAsRequiresBaseField(showValuesAs);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BaseFieldPanel.Visibility = visibility;
        BaseFieldPanel.IsEnabled = visible;
        BaseItemPanel.Visibility = visibility;
        BaseItemPanel.IsEnabled = visible;
    }

    private static bool ShowValuesAsRequiresBaseField(PivotShowValuesAs showValuesAs) =>
        showValuesAs is PivotShowValuesAs.RunningTotalIn
            or PivotShowValuesAs.DifferenceFrom
            or PivotShowValuesAs.PercentDifferenceFrom
            or PivotShowValuesAs.RankSmallest
            or PivotShowValuesAs.RankLargest
            or PivotShowValuesAs.PercentOfParentTotal;

    private static int FindNumberFormatPresetIndex(int? numberFormatId)
    {
        var presets = PivotValueFieldSettingsInputParser.NumberFormatPresets;
        var index = presets
            .Select((preset, i) => (preset, i))
            .FirstOrDefault(item => item.preset.NumberFormatId == numberFormatId)
            .i;
        return index < 0 ? 0 : index;
    }
}
