using Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class AvaloniaWindowArrangementTests
{
    [Fact]
    public void Build_tiles_one_window_to_the_full_working_area_in_dips()
    {
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(800, 450, 1, maxColumns: 3);
        var tiles = AvaloniaWindowBoundsTranslator.Translate(
            new PixelRect(40, 80, 1600, 900), 2, bounds);

        tiles.Should().ContainSingle().Which.Should().Be(
            new AvaloniaWindowTile(new PixelPoint(40, 80), 800, 450));
    }

    [Fact]
    public void Build_tiles_windows_row_first_with_three_columns()
    {
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(1200, 800, 5, maxColumns: 3);
        var tiles = AvaloniaWindowBoundsTranslator.Translate(
            new PixelRect(100, 200, 1200, 800), 1, bounds);

        tiles.Should().HaveCount(5);
        tiles[0].Should().Be(new AvaloniaWindowTile(new PixelPoint(100, 200), 400, 400));
        tiles[1].Should().Be(new AvaloniaWindowTile(new PixelPoint(500, 200), 400, 400));
        tiles[2].Should().Be(new AvaloniaWindowTile(new PixelPoint(900, 200), 400, 400));
        tiles[3].Should().Be(new AvaloniaWindowTile(new PixelPoint(100, 600), 400, 400));
        tiles[4].Should().Be(new AvaloniaWindowTile(new PixelPoint(500, 600), 400, 400));
    }

    [Fact]
    public void Build_preserves_negative_origin_scaling_and_remainder_pixels()
    {
        var scaling = 1.25;
        var workArea = new PixelRect(-1600, -900, 1001, 701);
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(
            workArea.Width / scaling,
            workArea.Height / scaling,
            3,
            maxColumns: 3);
        var tiles = AvaloniaWindowBoundsTranslator.Translate(workArea, scaling, bounds);

        tiles.Should().HaveCount(3);
        tiles[0].Position.Should().Be(new PixelPoint(-1600, -900));
        tiles[1].Position.Should().Be(new PixelPoint(-1266, -900));
        tiles[2].Position.Should().Be(new PixelPoint(-933, -900));
        tiles.Select(tile => tile.Width * 1.25).Select(width => Math.Round(width)).Should()
            .Equal(334, 333, 334);
        tiles.Select(tile => tile.Height * 1.25).Should().AllBeEquivalentTo(701);
    }

    [Fact]
    public void Build_returns_no_tiles_for_empty_or_invalid_inputs()
    {
        AvaloniaWindowBoundsTranslator.Translate(
                new PixelRect(0, 0, 100, 100),
                1,
                Array.Empty<ShellRect>())
            .Should().BeEmpty();
        ArrangeAllLayoutPlanner.ArrangeRowFirst(100, 100, 2, maxColumns: 0)
            .Should().BeEmpty();
    }

    [Fact]
    public void MainWindow_arrange_all_filters_visible_windows_and_uses_the_active_screen()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var translatorSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "AvaloniaWindowBoundsTranslator.cs"));

        source.Should().Contain("desktop.Windows");
        source.Should().Contain("OfType<MainWindow>()");
        source.Should().Contain("Where(window => window.IsVisible)");
        source.Should().Contain("Screens.ScreenFromWindow(this) ?? Screens.Primary");
        source.Should().Contain("window.WindowState = WindowState.Normal");
        source.Should().Contain("window.Position = tile.Position");
        source.Should().Contain("window.Width = tile.Width");
        source.Should().Contain("window.Height = tile.Height");
        source.Should().Contain("ArrangeAllLayoutPlanner.ArrangeRowFirst(");
        source.Should().Contain("FreeWAvaloniaWindowBoundsTranslator.Translate(");
        source.Should().NotContain("FreeWAvaloniaWindowArrangement");
        translatorSource.Should().Contain(
            "global using FreeWAvaloniaWindowBoundsTranslator = Free.Shared.Shell.Avalonia.AvaloniaWindowBoundsTranslator;");
        translatorSource.Should().NotContain("Math.Round(");
        translatorSource.Should().NotContain("for (");
    }
}
