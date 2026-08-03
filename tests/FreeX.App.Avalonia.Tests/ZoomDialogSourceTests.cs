using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ZoomDialogSourceTests
{
    [Fact]
    public void ZoomDialog_UsesWpfGeometrySharedChromeAndTwoColumnContent()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, zoomDialogChrome);");
        source.Should().Contain("ControlHeight = 20,");
        source.Should().Contain("CompactRadioButtonHeight = 16,");
        source.Should().Contain("TextBoxHeight = ZoomDialogPlanner.CustomPercentBoxHeight,");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyCompactRadioButton(button, zoomDialogChrome);");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(");
        source.Should().Contain("ZoomDialogPlanner.PresetColumnWidth");
        source.Should().Contain("Header = \"Magnification\"");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(magnificationGroup, zoomDialogChrome);");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton])");
        source.Should().Contain("Width = ZoomDialogPlanner.ActionButtonWidth");
    }

    [Fact]
    public void ZoomDialog_RetainsSharedSelectionAndValidationRoutes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ZoomDialogPlanner.TryCreateResult(customBox.Text, out var customResult, out var validationError)");
        source.Should().Contain("selectedZoomPercent = CalculateZoomToSelectionPercent();");
        source.Should().Contain("selectedZoomPercent = zoom;");
        source.Should().Contain("AutomationProperties.SetAutomationId(customBox, \"ZoomCustomPercentBox\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(okButton, \"ZoomOkButton\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancelButton, \"ZoomCancelButton\");");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
