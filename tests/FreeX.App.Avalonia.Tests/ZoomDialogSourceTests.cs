using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ZoomDialogSourceTests
{
    [Fact]
    public void ZoomDialog_UsesWpfGeometrySharedChromeAndTwoColumnContent()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, zoomDialogChrome);");
        source.Should().Contain("ForegroundBrush = Brushes.Black,");
        source.Should().Contain("TextOptions.SetTextRenderingMode(dialog, TextRenderingMode.Antialias);");
        source.Should().Contain("ControlHeight = ZoomDialogVisualMetrics.AvaloniaControlHeight,");
        source.Should().Contain("CompactRadioButtonHeight = ZoomDialogVisualMetrics.AvaloniaCompactRadioButtonHeight,");
        source.Should().Contain("TextBoxHeight = ZoomDialogVisualMetrics.CustomPercentBoxHeight,");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactRadioButton(button, zoomDialogChrome);");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(");
        source.Should().Contain("ZoomDialogVisualMetrics.PresetColumnWidth");
        source.Should().Contain("Header = UiText.Get(\"Zoom_Magnification\")");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(magnificationGroup, zoomDialogChrome);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton])");
        source.Should().Contain("Width = ZoomDialogVisualMetrics.ActionButtonWidth");
    }

    [Fact]
    public void ZoomDialog_RetainsSharedSelectionAndValidationRoutes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ZoomDialogPlanner.TryCreateResult(customBox.Text, out var customResult, out var validationError)");
        source.Should().Contain("selectedZoomPercent = CalculateZoomToSelectionPercent();");
        source.Should().Contain("selectedZoomPercent = zoom;");
        source.Should().Contain("AutomationProperties.SetAutomationId(customBox, ZoomDialogVisualMetrics.CustomPercentBoxAutomationId);");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, ZoomDialogVisualMetrics.OkButtonAutomationId);");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, ZoomDialogVisualMetrics.CancelButtonAutomationId);");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
