using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class ZoomDialogPolicySourceGuardTests
{
    [Fact]
    public void ZoomDialog_UsesSharedPresentationPlannerForPolicy()
    {
        var source = ReadAvaloniaSource("ZoomDialog.cs");
        var mainWindow = ReadAvaloniaSource("MainWindow.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("new ZoomDialogSession(currentScale)");
        source.Should().Contain("_session.PlanAcceptance(_fitFactors)");
        source.Should().Contain("ZoomDialogFitFactors fitFactors");
        source.Should().NotContain("DefaultFitFactors");
        mainWindow.Should().Contain("new ZoomDialog(_zoomScale, ComputeZoomFitFactors())");
        mainWindow.Should().Contain("ZoomDialogPlanner.BuildFitFactors(page, viewportWidth, viewportHeight)");
        source.Should().Contain("acceptance.Validation.FocusTarget");
        source.Should().Contain("ZoomDialogPlanner.Text");
        source.Should().Contain("ZoomDialogPlanner.FormatPresetLabel(preset.Percent)");
        source.Should().NotContain("Preset(\"Page width\")");
        source.Should().NotContain("Title = \"Zoom\"");
        source.Should().NotContain("new ZoomDialogSelectionRequest(");
        source.Should().NotContain("GetSelectedFitOption");
        source.Should().NotContain("GetSelectedPresetPercent");
        source.Should().Contain("ZoomDialogFitFactors");
        source.Should().NotContain("NumericUpDown");
        source.Should().NotContain("currentScale * 100");
        source.Should().NotContain("switch (pct)");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("ZoomLevels.FromPercent(");
    }

    [Fact]
    public void ZoomDialog_UsesSharedAvaloniaCompactDialogChromeForChrome()
    {
        var source = ReadAvaloniaSource("ZoomDialog.cs");

        source.Should().Contain("using Free.Shared.Shell.Avalonia;");
        source.Should().Contain("AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyTextBox(_percentBox, DialogChromeStyle)");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true)");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72)");
        source.Should().Contain("AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(button, DialogChromeStyle)");
        source.Should().NotContain("Foreground = new SolidColorBrush(Color.FromRgb(0x80");
        source.Should().NotContain("new StackPanel\r\n        {\r\n            Orientation = Orientation.Horizontal,\r\n            HorizontalAlignment = HorizontalAlignment.Right,");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
