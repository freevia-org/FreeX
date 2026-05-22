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

        // Advanced
        OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter;
        OptAfterEnterDirection.ItemsSource = new[] { "Down", "Right", "Up", "Left" };
        OptAfterEnterDirection.SelectedIndex = _opts.AfterEnterDirection switch
        {
            FreexcelEnterDirection.Right => 1,
            FreexcelEnterDirection.Up => 2,
            FreexcelEnterDirection.Left => 3,
            _ => 0
        };
        OptShowGridlines.IsChecked = _opts.ShowGridlines;
        OptShowHeadings.IsChecked = _opts.ShowHeadings;
        OptObjectsDisplay.ItemsSource = new[] { "All", "Placeholders", "Nothing (hide objects)" };
        OptObjectsDisplay.SelectedIndex = _opts.ObjectsDisplay switch
        {
            FreexcelObjectDisplay.Placeholders => 1,
            FreexcelObjectDisplay.Nothing => 2,
            _ => 0
        };

        // View
        OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar;
        OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded;

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
        var selected = (TabList.SelectedItem as ListBoxItem)?.Content?.ToString() ?? "";
        PanelGeneral.Visibility = selected == "_General" ? Visibility.Visible : Visibility.Collapsed;
        PanelFormulas.Visibility = selected == "_Formulas" ? Visibility.Visible : Visibility.Collapsed;
        PanelProofing.Visibility = selected == "_Proofing" ? Visibility.Visible : Visibility.Collapsed;
        PanelSave.Visibility = selected == "_Save" ? Visibility.Visible : Visibility.Collapsed;
        PanelLanguage.Visibility = selected == "_Language" ? Visibility.Visible : Visibility.Collapsed;
        PanelEaseOfAccess.Visibility = selected == "_Ease of Access" ? Visibility.Visible : Visibility.Collapsed;
        PanelAdvanced.Visibility = selected == "_Advanced" ? Visibility.Visible : Visibility.Collapsed;
        PanelCustomizeRibbon.Visibility = selected == "_Customize Ribbon" ? Visibility.Visible : Visibility.Collapsed;
        PanelQuickAccessToolbar.Visibility = selected == "_Quick Access Toolbar" ? Visibility.Visible : Visibility.Collapsed;
        PanelAddIns.Visibility = selected == "_Add-ins" ? Visibility.Visible : Visibility.Collapsed;
        PanelTrustCenter.Visibility = selected == "_Trust Center" ? Visibility.Visible : Visibility.Collapsed;
        PanelView.Visibility = selected == "_View" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var opts = new FreexcelOptions
        {
            DefaultFontName   = OptDefaultFont.SelectedItem as string ?? _opts.DefaultFontName,
            DefaultFontSize   = OptionsInputParser.ParseDefaultFontSizeOrFallback(OptDefaultFontSize.Text, _opts.DefaultFontSize),
            DefaultSheetCount = OptionsInputParser.ParseDefaultSheetCountOrFallback(OptSheetCount.Text, _opts.DefaultSheetCount),
            UserName          = string.IsNullOrWhiteSpace(OptUserName.Text) ? _opts.UserName : OptUserName.Text.Trim(),
            AutoCalculate     = OptCalcAuto.IsChecked == true,
            UseR1C1ReferenceStyle = OptR1C1.IsChecked == true,
            ShowFormulaBar     = OptShowFormulaBar.IsChecked == true,
            FormulaBarExpanded = OptFormulaBarExpanded.IsChecked == true,
            MoveSelectionAfterEnter = OptMoveAfterEnter.IsChecked == true,
            AfterEnterDirection = OptAfterEnterDirection.SelectedIndex switch
            {
                1 => FreexcelEnterDirection.Right,
                2 => FreexcelEnterDirection.Up,
                3 => FreexcelEnterDirection.Left,
                _ => FreexcelEnterDirection.Down
            },
            ShowGridlines = OptShowGridlines.IsChecked == true,
            ShowHeadings = OptShowHeadings.IsChecked == true,
            ObjectsDisplay = OptObjectsDisplay.SelectedIndex switch
            {
                1 => FreexcelObjectDisplay.Placeholders,
                2 => FreexcelObjectDisplay.Nothing,
                _ => FreexcelObjectDisplay.All
            },
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
