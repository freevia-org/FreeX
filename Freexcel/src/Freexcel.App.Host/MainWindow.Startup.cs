using System;
using System.Linq;
using System.Windows;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private static readonly (string Label, string Code)[] NumberFormatOptions =
    [
        ("General", "General"),
        ("Number (0.00)", "0.00"),
        ("Currency ($#,##0.00)", "$#,##0.00"),
        ("Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)"),
        ("Percentage (0%)", "0%"),
        ("Fraction (# ?/?)", "# ?/?"),
        ("Scientific (0.00E+00)", "0.00E+00"),
        ("Date (yyyy-MM-dd)", "yyyy-MM-dd"),
        ("Time (HH:mm:ss)", "HH:mm:ss"),
        ("Text (@)", "@")
    ];

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizedContentInset();

        var fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontNameBox.ItemsSource = fonts;
        FontNameBox.SelectedItem = fonts.Contains("Calibri") ? "Calibri" : fonts[0];

        var sizes = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        FontSizeBox.ItemsSource = sizes;
        FontSizeBox.SelectedItem = "11";

        NumberFormatBox.ItemsSource = NumberFormatOptions.Select(option => option.Label).ToArray();
        NumberFormatBox.SelectedIndex = 0;

        PopulateFormatTableGalleryMenu();
        ApplyOptionsToView();
        NormalizeRibbonSurface(forceCompact: true);
        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
        TryStartScreenshotTour();
    }
}
