using System;
using System.Linq;
using System.Windows;

namespace FreeX.App.Host;

public partial class MainWindow
{
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

        NumberFormatBox.ItemsSource = HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label).ToArray();
        NumberFormatBox.SelectedIndex = HomeNumberFormatDropdownPlanner.DefaultSelectionIndex;

        PopulateFormatTableGalleryMenu();
        ApplyOptionsToView();
        NormalizeRibbonSurface(forceCompact: true);
        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
        TryStartScreenshotTour();
        TryStartSheetTabVisualTour();
        TryStartAccentBarVisualTour();
    }
}
