using System.Globalization;
using System.Windows;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class PivotValueFieldSettingsDialog : Window
{
    private static readonly IReadOnlyList<PivotValueFieldDisplayOption<string>> SummaryFunctions =
        PivotValueFieldPlanner.GetSummaryFunctions(WpfResourceKeyTextResolver.Instance);
    private static readonly IReadOnlyList<PivotValueFieldDisplayOption<PivotShowValuesAs>> ShowValuesAsOptions =
        PivotValueFieldPlanner.GetShowValuesAsOptions(WpfResourceKeyTextResolver.Instance);
    private static readonly IReadOnlyList<PivotValueNumberFormatDisplayPreset> NumberFormatPresets =
        PivotValueFieldPlanner.GetNumberFormatPresets(WpfResourceKeyTextResolver.Instance);

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
        SummaryFunctionBox.ItemsSource = SummaryFunctions.Select(item => item.Label);
        SummaryFunctionBox.SelectedIndex = PivotValueFieldPlanner.FindSummaryFunctionIndex(field.SummaryFunction);

        ShowValuesAsBox.ItemsSource = ShowValuesAsOptions.Select(item => item.Label);
        ShowValuesAsBox.SelectedIndex = PivotValueFieldPlanner.FindShowValuesAsIndex(field.ShowValuesAs);

        BaseFieldBox.ItemsSource = new[] { PivotValueFieldPlanner.GetAutomaticBaseFieldLabel(WpfResourceKeyTextResolver.Instance) }.Concat(_sourceHeaders).ToList();
        BaseFieldBox.SelectedIndex = PivotValueFieldPlanner.FindBaseFieldIndex(field.BaseFieldIndex, _sourceHeaders.Count);
        BaseItemBox.Text = field.BaseItem ?? "";

        NumberFormatPresetBox.ItemsSource = NumberFormatPresets.Select(preset => preset.Label);
        NumberFormatPresetBox.SelectedIndex = PivotValueFieldPlanner.FindNumberFormatPresetIndex(field.NumberFormatId);
        NumberFormatBox.Text = field.NumberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = field.NumberFormatCode ?? "";
        UpdateBaseFieldState();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PivotValueFieldPlanner.TryParseOptionalNumberFormatId(NumberFormatBox.Text, out var numberFormatId))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("PivotValueFieldSettings_NumberFormatIdWholeNumberMessage"), UiText.Get("PivotValueFieldSettings_ValueFieldSettings"));
            FocusInvalidNumberFormatInput();
            return;
        }

        numberFormatId ??= PivotValueFieldPlanner.ResolvePresetNumberFormatId(
            NumberFormatPresetBox.SelectedItem as string,
            WpfResourceKeyTextResolver.Instance);
        var numberFormatCode = PivotValueFieldPlanner.ResolveOptionalNumberFormatCode(NumberFormatCodeBox.Text);
        numberFormatId = PivotValueFieldPlanner.ResolveNumberFormatIdForCode(numberFormatId, numberFormatCode);

        var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(ShowValuesAsBox.SelectedIndex);
        var baseFieldIndex = PivotValueFieldPlanner.ResolveBaseFieldIndex(showValuesAs, BaseFieldBox.SelectedIndex);
        var baseItem = PivotValueFieldPlanner.ResolveBaseItem(showValuesAs, BaseItemBox.Text);
        if (!TryValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem, out var showValuesAsError))
        {
            DialogMessageHelper.ShowWarning(this, showValuesAsError, UiText.Get("PivotValueFieldSettings_ValueFieldSettings"));
            FocusInvalidShowValuesAsInput(baseFieldIndex);
            return;
        }

        ResultDataField = PivotValueFieldPlanner.CreateResult(
            _initialField,
            _sourceHeaders,
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
        DialogFocus.FocusAndSelect(NumberFormatBox);
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

        DialogFocus.FocusAndSelect(BaseItemBox);
    }

    private void NumberFormatPresetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var numberFormatId = PivotValueFieldPlanner.ResolvePresetNumberFormatId(
            NumberFormatPresetBox.SelectedItem as string,
            WpfResourceKeyTextResolver.Instance);
        NumberFormatBox.Text = numberFormatId?.ToString(CultureInfo.InvariantCulture) ?? "";
        NumberFormatCodeBox.Text = "";
    }

    private void ShowValuesAsBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateBaseFieldState();

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(CustomNameBox);
    }

    private void NumberFormatButton_Click(object sender, RoutedEventArgs e)
    {
        var style = new CellStyle { NumberFormat = CurrentNumberFormatCode() };
        var dialog = new FormatCellsDialog(style, FormatCellsDialogTab.Number)
        {
            Owner = this,
            Title = UiText.Get("PivotValueFieldSettings_FormatCellsTitle")
        };

        if (dialog.ShowDialog() != true || dialog.ResultDiff?.NumberFormat is not { } numberFormat)
            return;

        if (PivotValueFieldPlanner.TryResolveBuiltInNumberFormatIdForCode(numberFormat, out var builtInId))
        {
            NumberFormatCodeBox.Text = "";
            NumberFormatBox.Text = builtInId?.ToString(CultureInfo.InvariantCulture) ?? "";
            NumberFormatPresetBox.Text = NumberFormatPresets
                .First(preset => preset.NumberFormatId == builtInId && string.Equals(preset.FormatCode, numberFormat, StringComparison.OrdinalIgnoreCase))
                .Label;
            return;
        }

        NumberFormatCodeBox.Text = numberFormat;
        NumberFormatBox.Text = PivotValueFieldPlanner.DefaultCustomNumberFormatId.ToString(CultureInfo.InvariantCulture);
        NumberFormatPresetBox.Text = numberFormat;
    }

    private string CurrentNumberFormatCode()
    {
        var customCode = PivotValueFieldPlanner.ResolveOptionalNumberFormatCode(NumberFormatCodeBox.Text);
        if (!string.IsNullOrWhiteSpace(customCode))
            return customCode;

        if (NumberFormatPresetBox.SelectedItem is string selectedPreset &&
            PivotValueFieldPlanner.ResolvePresetNumberFormatCode(selectedPreset, WpfResourceKeyTextResolver.Instance) is { } selectedCode)
        {
            return selectedCode;
        }

        return PivotValueFieldPlanner.ResolvePresetNumberFormatCode(NumberFormatPresetBox.Text, WpfResourceKeyTextResolver.Instance)
            ?? NumberFormatPresetBox.Text
            ?? UiText.Get("PivotValueFieldSettings_GeneralFormat");
    }

    private void UpdateBaseFieldState()
    {
        if (BaseFieldPanel is null || BaseItemPanel is null || ShowValuesAsBox is null)
            return;

        var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(ShowValuesAsBox.SelectedIndex);
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
        => PivotValueFieldPlanner.TryValidateShowValuesAs(
            showValuesAs,
            baseFieldIndex,
            baseItem,
            WpfResourceKeyTextResolver.Instance,
            out error);

    public static bool ShowValuesAsRequiresBaseField(PivotShowValuesAs showValuesAs) =>
        PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(showValuesAs);
}
