using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class ColorHexParserSourceTests
{
    [Fact]
    public void AvaloniaRgbAdaptersUseSharedDrawingMlParser()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var project = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        var view = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));
        var effects = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "AvaloniaImageAdjustHelper.Effects.cs"));
        var window = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        project.Should().Contain("Free.Shared.Drawing\\Free.Shared.Drawing.csproj");
        view.Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var color)");
        view.Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var parsed)");
        view.Should().NotContain("NumberStyles.HexNumber");
        view.Should().NotContain("byte.TryParse(");
        effects.Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var color)");
        effects.Should().NotContain("Color.Parse(");
        window.Should().Contain("DrawingMlRgbColor.TryParseHexRgb(hex, out var color)");
    }
}
