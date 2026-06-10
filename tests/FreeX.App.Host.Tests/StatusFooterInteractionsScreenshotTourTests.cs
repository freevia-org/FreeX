using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StatusFooterInteractionsScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesStatusFooterInteractionEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.StatusFooterInteractions.cs");
        var combinedSource = DialogSourceTestSupport.ReadHostSources(
            "MainWindow.ScreenshotTour.cs",
            "MainWindow.ScreenshotTour.StatusFooterInteractions.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("StatusFooterInteractionsTourOutputDirectoryName = \"status-footer-interactions-tour\"");
        dispatcherSource.Should().Contain("FREEX_STATUS_FOOTER_INTERACTIONS_TOUR");
        dispatcherSource.Should().Contain("statusFooterTour ||");
        dispatcherSource.Should().Contain("CaptureStatusFooterInteractionsTourAsync");

        tourSource.Should().Contain("EnsureStatusFooterTourContext");
        tourSource.Should().Contain("RaiseStatusFooterButtonClickAsync(StatusPageLayoutViewButton)");
        tourSource.Should().Contain("RaiseStatusFooterButtonClickAsync(StatusPageBreakPreviewButton)");
        tourSource.Should().Contain("RaiseStatusFooterButtonClickAsync(StatusNormalViewButton)");
        tourSource.Should().Contain("RaiseStatusFooterButtonClickAsync(StatusZoomOutButton)");
        tourSource.Should().Contain("RaiseStatusFooterButtonClickAsync(StatusZoomInButton)");
        tourSource.Should().Contain("Zoom100Btn_Click(this, new RoutedEventArgs())");
        tourSource.Should().Contain("ZoomDialog.TryCreateResult");
        tourSource.Should().Contain("ZoomSelectionPlanner.CalculateDialogZoomPercent");
        tourSource.Should().Contain("ZoomDialog.ShowDialog timed cancel -> FocusSheetGridIfNeeded");
        tourSource.Should().Contain("ButtonBase.Click events and command/session methods");

        tourSource.Should().Contain("freex_status_footer_interactions_stats_single_number");
        tourSource.Should().Contain("freex_status_footer_interactions_stats_text_only");
        tourSource.Should().Contain("freex_status_footer_interactions_stats_mixed_range");
        tourSource.Should().Contain("freex_status_footer_interactions_view_page_layout_clicked");
        tourSource.Should().Contain("freex_status_footer_interactions_view_page_break_clicked");
        tourSource.Should().Contain("freex_status_footer_interactions_view_normal_clicked");
        tourSource.Should().Contain("freex_status_footer_interactions_zoom_button_out");
        tourSource.Should().Contain("freex_status_footer_interactions_zoom_button_in");
        tourSource.Should().Contain("freex_status_footer_interactions_zoom_100_command");
        tourSource.Should().Contain("freex_status_footer_interactions_zoom_custom_125");
        tourSource.Should().Contain("freex_status_footer_interactions_zoom_dialog_close_focus_return");

        tourSource.Should().Contain("planned-but-blocked");
        tourSource.Should().Contain("foreground mouse drag of StatusZoomSlider");
        tourSource.Should().Contain("foreground Ctrl+mouse-wheel over worksheet grid");
        tourSource.Should().Contain("native UIA RangeValue set on StatusZoomSlider");
        tourSource.Should().Contain("StatusFooterInteractionsTourManifest");
        combinedSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.StatusFooterInteractionsTourManifest");

        catalog.Should().Contain("screenshots/status-footer-interactions-tour/");
        catalog.Should().Contain("status_footer_interactions_tour_manifest.json");
    }
}
