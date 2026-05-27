using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PivotValueFieldSettingsDialog : Window
{
    private readonly PivotDataFieldModel _initialField;
    private readonly IReadOnlyList<string> _sourceHeaders;

    public PivotValueFieldSettingsDialog(PivotDataFieldModel field, IReadOnlyList<string>? sourceHeaders = null)
    {
        _initialField = field;
        _sourceHeaders = sourceHeaders ?? [];
        ResultDataField = field;

        InitializeComponent();
        LoadOptions(field);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public PivotDataFieldModel ResultDataField { get; private set; }

    private void LoadOptions(PivotDataFieldModel field)
    {
        CustomNameBox.Text = field.Name;
        SummaryFunctionBox.ItemsSource = PivotValueFieldSettingsDialogPlanner.SummaryFunctions.Select(item => item.Label);
        SummaryFunctionBox.SelectedIndex = PivotValueFieldSettingsDialogPlanner.FindSummaryFunctionIndex(field.SummaryFunction);

        ShowValuesAsBox.ItemsSource = PivotValueFieldSettingsDialogPlanner.ShowValuesAsOptions.Select(item => item.Label);
        ShowValuesAsBox.SelectedIndex = PivotValueFieldSettingsDialogPlanner.FindShowValuesAsIndex(field.ShowValuesAs);

        BaseFieldBox.ItemsSource = new[] { PivotValueFieldSettingsDialogPlanner.AutomaticBaseFieldLabel }.Concat(_sourceHeaders).ToList();
        BaseFieldBox.SelectedIndex = PivotValueFieldSettingsDialogPlanner.FindBaseFieldIndex(field.BaseFieldIndex, _sourceHeaders.Count);
        BaseItemBox.Text = field.BaseItem ?? "";

        NumberFormatPresetBox.ItemsSource = PivotValueFieldSettingsInputParser.NumberFormatPresets.Select(preset => preset.Label);
        NumberFormatPresetBox.SelectedIndex = PivotValueFieldSettingsDialogPlanner.FindNumberFormatPresetIndex(field.NumberFormatId);
        NumberFormatBox.Text = field.NumberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = field.NumberFormatCode ?? "";
        UpdateBaseFieldState();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PivotValueFieldSettingsInputParser.TryParseOptionalNumberFormatId(NumberFormatBox.Text, out var numberFormatId))
        {
            MessageBox.Show(this, "Number format ID must be a whole number.", "Value Field Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidNumberFormatInput();
            return;
        }

        numberFormatId ??= PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatId(NumberFormatPresetBox.SelectedItem as string);
        var numberFormatCode = PivotValueFieldSettingsInputParser.ResolveOptionalNumberFormatCode(NumberFormatCodeBox.Text);
        numberFormatId = PivotValueFieldSettingsInputParser.ResolveNumberFormatIdForCode(numberFormatId, numberFormatCode);

        var showValuesAs = PivotValueFieldSettingsDialogPlanner.ShowValuesAsFromIndex(ShowValuesAsBox.SelectedIndex);
        var baseFieldIndex = PivotValueFieldSettingsDialogPlanner.ResolveBaseFieldIndex(showValuesAs, BaseFieldBox.SelectedIndex);
        var baseItem = PivotValueFieldSettingsDialogPlanner.ResolveBaseItem(showValuesAs, BaseItemBox.Text);
        if (!TryValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem, out var showValuesAsError))
        {
            MessageBox.Show(this, showValuesAsError, "Value Field Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidShowValuesAsInput(baseFieldIndex);
            return;
        }

        ResultDataField = PivotValueFieldSettingsDialogPlanner.CreateResult(
            _initialField,
            CustomNameBox.Text,
            SummaryFunctionBox.SelectedIndex,
            ShowValuesAsBox.SelectedIndex,
            BaseFieldBox.SelectedIndex,
            BaseItemBox.Text,
            numberFormatId,
            numberFormatCode);
        DialogResult = true;
    }

    private void FocusInvalidNumberFormatInput()
    {
        ValueFieldTabs.SelectedItem = NumberFormatTab;
        FocusAndSelect(NumberFormatBox);
    }

    private void FocusInvalidShowValuesAsInput(int? baseFieldIndex)
    {
        ValueFieldTabs.SelectedItem = ShowValuesAsTab;
        if (baseFieldIndex is null)
        {
            BaseFieldBox.Focus();
            Keyboard.Focus(BaseFieldBox);
            return;
        }

        FocusAndSelect(BaseItemBox);
    }

    private static void FocusAndSelect(System.Windows.Controls.TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    private void NumberFormatPresetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var numberFormatId = PivotValueFieldSettingsInputParser.ResolvePresetNumberFormatId(NumberFormatPresetBox.SelectedItem as string);
        NumberFormatBox.Text = numberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = "";
    }

    private void ShowValuesAsBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateBaseFieldState();

    private void FocusInitialKeyboardTarget()
    {
        CustomNameBox.Focus();
        CustomNameBox.SelectAll();
        Keyboard.Focus(CustomNameBox);
    }

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

        if (PivotValueFieldSettingsInputParser.TryResolveBuiltInNumberFormatIdForCode(numberFormat, out var builtInId))
        {
            NumberFormatCodeBox.Text = "";
            NumberFormatBox.Text = builtInId?.ToString(CultureInfo.InvariantCulture) ?? "";
            NumberFormatPresetBox.Text = PivotValueFieldSettingsInputParser.NumberFormatPresets
                .First(preset => preset.NumberFormatId == builtInId && string.Equals(preset.FormatCode, numberFormat, StringComparison.OrdinalIgnoreCase))
                .Label;
            return;
        }

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

        var showValuesAs = PivotValueFieldSettingsDialogPlanner.ShowValuesAsFromIndex(ShowValuesAsBox.SelectedIndex);
        var visible = ShowValuesAsRequiresBaseField(showValuesAs);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BaseFieldPanel.Visibility = visibility;
        BaseFieldPanel.IsEnabled = visible;
        BaseItemPanel.Visibility = visibility;
        BaseItemPanel.IsEnabled = visible;
    }

    public static bool TryValidateShowValuesAs(
        PivotShowValuesAs showValuesAs,
        int? baseFieldIndex,
        string? baseItem,
        out string? error)
        => PivotValueFieldSettingsDialogPlanner.TryValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem, out error);

    public static bool ShowValuesAsRequiresBaseField(PivotShowValuesAs showValuesAs) =>
        PivotValueFieldSettingsDialogPlanner.ShowValuesAsRequiresBaseField(showValuesAs);
}
