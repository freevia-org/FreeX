using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-border-accent-swatch-F1: Home ▸ Borders ▸ Line Color ▸ "Accent n" applies
/// <c>_workbook.Theme.GetColor(Accent1/Accent2)</c> (MainWindow.HomeFormatting.cs), but
/// <see cref="BorderMenuIcon"/> painted the swatch from hard-coded constants that matched no theme:
/// Accent 1 drew (15,109,140) against an applied (21,96,130), and Accent 2 drew a TEAL (45,125,154)
/// against an applied ORANGE (233,113,50). The menu advertised one color and painted another.
/// </summary>
public sealed class R177_BorderMenuAccentSwatchTests
{
    private static readonly CellColor SwappedAccent1 = new(7, 200, 111);

    [Theory]
    [InlineData(BorderMenuIconKind.ColorAccent1, WorkbookThemeColorSlot.Accent1)]
    [InlineData(BorderMenuIconKind.ColorAccent2, WorkbookThemeColorSlot.Accent2)]
    public void AccentSwatch_PaintsTheColorTheCommandApplies(BorderMenuIconKind kind, WorkbookThemeColorSlot slot)
    {
        StaTestRunner.Run(() =>
        {
            var theme = WorkbookTheme.Office;

            // Ground truth is the same call the click handler makes, not a hard-coded RGB.
            var applied = theme.GetColor(slot);
            var painted = DominantSwatchColor(kind, theme);

            painted.Should().Be(Color.FromRgb(applied.R, applied.G, applied.B),
                "the swatch must show exactly what clicking the entry applies");
        });
    }

    [Fact]
    public void AccentSwatch_FollowsASwappedTheme()
    {
        StaTestRunner.Run(() =>
        {
            var office = WorkbookTheme.Office;
            var swapped = office.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

            var before = DominantSwatchColor(BorderMenuIconKind.ColorAccent1, office);
            var after = DominantSwatchColor(BorderMenuIconKind.ColorAccent1, swapped);

            after.Should().Be(Color.FromRgb(SwappedAccent1.R, SwappedAccent1.G, SwappedAccent1.B),
                "the swatch is theme-backed, so a theme swap must repaint it");
            after.Should().NotBe(before);
        });
    }

    [Fact]
    public void ToolGlyph_KeepsItsFixedChromeAccent_NoRegression()
    {
        StaTestRunner.Run(() =>
        {
            // The Draw Border pencil depicts a TOOL, not a color the command applies, so it must NOT
            // follow the theme -- guards against over-applying the fix to every accent-colored glyph.
            var office = WorkbookTheme.Office;
            var swapped = office.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

            RenderToBytes(BorderMenuIconKind.DrawBorder, swapped)
                .Should().Equal(RenderToBytes(BorderMenuIconKind.DrawBorder, office));
        });
    }

    /// <summary>Most frequent fully-opaque pixel color — the swatch fill, outvoting its border/AA pixels.</summary>
    private static Color DominantSwatchColor(BorderMenuIconKind kind, WorkbookTheme theme)
    {
        var pixels = RenderToBytes(kind, theme);
        var counts = new Dictionary<Color, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 250)
                continue;
            var color = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
            counts[color] = counts.GetValueOrDefault(color) + 1;
        }

        // The 18x18 glyph draws a light grid behind everything; the swatch fill is the next-largest
        // block, so drop the single most common color (the grid/background) before picking.
        counts.Should().NotBeEmpty();
        return counts.OrderByDescending(entry => entry.Value)
            .Where(entry => entry.Key != Color.FromRgb(196, 196, 196))
            .Select(entry => entry.Key)
            .First();
    }

    private static byte[] RenderToBytes(BorderMenuIconKind kind, WorkbookTheme theme)
    {
        var icon = new BorderMenuIcon { Kind = kind, Theme = theme };
        icon.Measure(new Size(18, 18));
        icon.Arrange(new Rect(0, 0, 18, 18));
        icon.UpdateLayout();

        var bitmap = new RenderTargetBitmap(18, 18, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(icon);
        var pixels = new byte[18 * 18 * 4];
        bitmap.CopyPixels(pixels, 18 * 4, 0);
        return pixels;
    }
}
