using Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaWindowBoundsTranslatorTests
{
    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Translate_batch_preserves_adjacent_edges_and_full_pixel_width(double scaling)
    {
        var workArea = new PixelRect(-1600, -900, 1001, 701);
        var dipWidth = AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Width, scaling);
        var dipHeight = AvaloniaWindowBoundsTranslator.PixelsToDips(workArea.Height, scaling);
        var bounds = new[]
        {
            new ShellRect(0, 0, dipWidth / 3, dipHeight),
            new ShellRect(dipWidth / 3, 0, dipWidth / 3, dipHeight),
            new ShellRect(dipWidth * 2 / 3, 0, dipWidth / 3, dipHeight),
        };

        var tiles = AvaloniaWindowBoundsTranslator.Translate(workArea, scaling, bounds);

        tiles.Should().HaveCount(3);
        tiles[0].Position.Should().Be(new PixelPoint(workArea.X, workArea.Y));
        PixelRight(tiles[0], scaling).Should().Be(tiles[1].Position.X);
        PixelRight(tiles[1], scaling).Should().Be(tiles[2].Position.X);
        PixelRight(tiles[2], scaling).Should().Be(workArea.Right);
        tiles.Sum(tile => PixelWidth(tile, scaling)).Should().Be(workArea.Width);
        tiles.Should().OnlyContain(tile => PixelHeight(tile, scaling) == workArea.Height);
    }

    [Fact]
    public void Translate_rounds_each_edge_away_from_zero_before_deriving_size()
    {
        var tile = AvaloniaWindowBoundsTranslator.Translate(
            new PixelRect(-2000, 100, 1000, 800),
            1.25,
            new ShellRect(0.4, 1.2, 0.8, 2.4));

        tile.Position.Should().Be(new PixelPoint(-1999, 102));
        tile.Width.Should().Be(0.8);
        tile.Height.Should().Be(2.4);
        PixelRight(tile, 1.25).Should().Be(-1998);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Translate_invalid_scaling_falls_back_to_one(double scaling)
    {
        var tile = AvaloniaWindowBoundsTranslator.Translate(
            new PixelRect(-100, -50, 800, 600),
            scaling,
            new ShellRect(1.5, 2.5, 3, 4));

        tile.Should().Be(new AvaloniaWindowTile(new PixelPoint(-98, -47), 3, 4));
        AvaloniaWindowBoundsTranslator.PixelsToDips(125, scaling).Should().Be(125);
    }

    [Fact]
    public void Translate_empty_batch_returns_no_tiles()
    {
        AvaloniaWindowBoundsTranslator.Translate(
                new PixelRect(0, 0, 100, 100),
                1.5,
                Array.Empty<ShellRect>())
            .Should().BeEmpty();
    }

    private static int PixelRight(AvaloniaWindowTile tile, double scaling) =>
        tile.Position.X + PixelWidth(tile, scaling);

    private static int PixelWidth(AvaloniaWindowTile tile, double scaling) =>
        (int)Math.Round(tile.Width * scaling, MidpointRounding.AwayFromZero);

    private static int PixelHeight(AvaloniaWindowTile tile, double scaling) =>
        (int)Math.Round(tile.Height * scaling, MidpointRounding.AwayFromZero);
}
