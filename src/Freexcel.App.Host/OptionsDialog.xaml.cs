using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace Freexcel.App.Host;

public partial class OptionsDialog : Window
{
    private readonly FreexcelOptions _opts;
    public FreexcelOptions Result { get; private set; }

    private static readonly string[] Fonts =
        ["Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana", "Georgia"];

    private static readonly string[] Sizes =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36"];

    public OptionsDialog(FreexcelOptions opts)
    {
        _opts = opts;
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
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
