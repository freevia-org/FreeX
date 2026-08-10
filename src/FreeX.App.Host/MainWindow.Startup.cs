using System.Windows;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizedContentInset();

        // The Home Font / Font Size / Number Format combo items are populated on the *rendered*
        // declarative ribbon combos by PopulateAndWireRenderedHomeCombos (called from
        // TryApplyDeclarativeRibbon below), so there is no stub population here anymore.
        InitializePageLayoutScaleToFitControls();

        PopulateFormatTableGalleryMenu();
        TryApplyDeclarativeRibbon();
        ApplyOptionsToView();
        if (ShouldAdoptSharedWorkbookOnLoad)
        {
            // Secondary window (Excel "New Window"): share the existing workbook rather than
            // replacing it. The first window keeps its CreateNewWorkbook() startup behavior.
            AdoptSharedWorkbook();
        }
        else if (!_parityCaptureWorkbookPrepared)
        {
            CreateNewWorkbook();
        }
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
        RegisterWithWindowRegistry();
        TryStartScreenshotTour();
        TryStartSheetTabVisualTour();
        TryStartSheetTabWorkflowsTour();
        TryStartAccentBarVisualTour();
    }
}
