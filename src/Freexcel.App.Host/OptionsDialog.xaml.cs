using System.Windows;
using System.Windows.Controls;
using System.IO;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public partial class OptionsDialog : Window
{
    private readonly FreexcelOptions _opts;
    private readonly HashSet<string> _disabledFormulaErrorCodes;
    private readonly Dictionary<string, CheckBox> _errorRuleBoxes = new(StringComparer.OrdinalIgnoreCase);
    public FreexcelOptions Result { get; private set; }
    public IReadOnlySet<string> DisabledFormulaErrorCodesResult { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] Fonts =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    private static readonly string[] Sizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public OptionsDialog(FreexcelOptions opts, IEnumerable<string>? disabledFormulaErrorCodes = null)
    {
        _opts = opts;
        _disabledFormulaErrorCodes = new HashSet<string>(disabledFormulaErrorCodes ?? [], StringComparer.OrdinalIgnoreCase);
        DisabledFormulaErrorCodesResult = new HashSet<string>(_disabledFormulaErrorCodes, StringComparer.OrdinalIgnoreCase);
        Result = opts;
        InitializeComponent();
        Loaded += (_, _) => Populate();
        TabList.SelectedIndex = 0;
    }

    private void Populate()
    {
        // General
        OptDefaultFont.ItemsSource = Fonts;
        OptDefaultFont.SelectedItem = Fonts.Contains(_opts.DefaultFontName)
            ? _opts.DefaultFontName : "Calibri";

        OptDefaultFontSize.ItemsSource = Sizes;
        OptDefaultFontSize.Text = _opts.DefaultFontSize.ToString();

        OptSheetCount.Text = _opts.DefaultSheetCount.ToString();
        OptUserName.Text   = _opts.UserName;
        OptShowScreenTips.IsChecked = true;

        // Formulas
        OptCalcAuto.IsChecked   =  _opts.AutoCalculate;
        OptCalcManual.IsChecked = !_opts.AutoCalculate;
        OptR1C1.IsChecked = _opts.UseR1C1ReferenceStyle;
        OptFormulasAutocomplete.IsChecked = true;
        PopulateErrorCheckingRules();

        // Save
        OptDefaultFormat.ItemsSource = new[] { "Excel Workbook (.xlsx)", "Freexcel JSON (.json)" };
        OptDefaultFormat.SelectedIndex = _opts.DefaultFormat == ".json" ? 1 : 0;

        OptRecentFilesPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Freexcel", "recent.json");
    }

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabList.SelectedIndex < 0) return;
        PanelGeneral.Visibility  = TabList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelFormulas.Visibility = TabList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelSave.Visibility     = TabList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var opts = new FreexcelOptions
        {
            DefaultFontName   = OptDefaultFont.SelectedItem as string ?? _opts.DefaultFontName,
            DefaultFontSize   = int.TryParse(OptDefaultFontSize.Text, out var fs) && fs > 0 ? fs : _opts.DefaultFontSize,
            DefaultSheetCount = int.TryParse(OptSheetCount.Text, out var sc) && sc is >= 1 and <= 255 ? sc : _opts.DefaultSheetCount,
            UserName          = string.IsNullOrWhiteSpace(OptUserName.Text) ? _opts.UserName : OptUserName.Text.Trim(),
            AutoCalculate     = OptCalcAuto.IsChecked == true,
            UseR1C1ReferenceStyle = OptR1C1.IsChecked == true,
            DefaultFormat     = OptDefaultFormat.SelectedIndex == 1 ? ".json" : ".xlsx",
        };
        opts.Save();
        Result = opts;
        DisabledFormulaErrorCodesResult = CollectDisabledFormulaErrorCodes();
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void PopulateErrorCheckingRules()
    {
        OptErrorCheckingRules.Children.Clear();
        _errorRuleBoxes.Clear();

        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var checkBox = new CheckBox
            {
                Content = rule.Label,
                ToolTip = rule.Description,
                IsChecked = !_disabledFormulaErrorCodes.Contains(rule.ErrorCode),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = rule.ErrorCode
            };
            _errorRuleBoxes[rule.ErrorCode] = checkBox;
            OptErrorCheckingRules.Children.Add(checkBox);
        }
    }

    private IReadOnlySet<string> CollectDisabledFormulaErrorCodes()
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            if (_errorRuleBoxes.TryGetValue(rule.ErrorCode, out var box) &&
                box.IsChecked != true)
            {
                disabled.Add(rule.ErrorCode);
            }
        }

        return disabled;
    }
}
