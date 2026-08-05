namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaWindowBoundsTranslationSourceGuardTests
{
    [Fact]
    public void Window_geometry_callsites_use_the_shared_translator_and_keep_local_window_policy()
    {
        var arrangement = ReadAppSource("MainWindow.WindowManagement.cs");
        var sideBySide = ReadAppSource("MainWindow.SideBySide.cs");
        var ribbon = ReadAppSource("MainWindow.RibbonMenuWires.cs");

        arrangement.Should().Contain("var tiles = AvaloniaWindowBoundsTranslator.Translate(");
        arrangement.Should().Contain("window.WindowState = WindowState.Normal;");
        arrangement.Should().Contain("Math.Max(window.MinWidth, tile.Width)");
        arrangement.Should().Contain("Math.Max(window.MinHeight, tile.Height)");

        sideBySide.Should().Contain("var tiles = AvaloniaWindowBoundsTranslator.Translate(");
        sideBySide.Should().Contain("WindowState = WindowState.Normal;");
        sideBySide.Should().Contain("Math.Max(MinWidth, tile.Width)");
        sideBySide.Should().Contain("Math.Max(MinHeight, tile.Height)");

        ribbon.Should().Contain("var tile = AvaloniaWindowBoundsTranslator.Translate(");
        ribbon.Should().Contain("WindowState = WindowState.Normal;");
    }

    [Fact]
    public void Window_geometry_callsites_do_not_reintroduce_inline_pixel_conversion()
    {
        var sources = new[]
        {
            ReadAppSource("MainWindow.WindowManagement.cs"),
            ReadAppSource("MainWindow.SideBySide.cs"),
            ReadAppSource("MainWindow.RibbonMenuWires.cs"),
        };

        sources.Should().OnlyContain(source =>
            source.Contains("AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Width, scaling)", StringComparison.Ordinal));

        foreach (var source in sources)
        {
            source.Should().NotContain("Position = new PixelPoint(");
            source.Should().NotContain("workArea.X + (int)");
            source.Should().NotContain("workArea.Y + (int)");
        }
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepositoryFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, Path.Combine(relativeSegments));
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativeSegments)}");
    }
}
