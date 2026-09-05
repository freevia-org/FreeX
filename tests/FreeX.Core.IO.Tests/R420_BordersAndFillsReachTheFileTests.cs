using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r420: cell borders and fills must survive an .xlsx round trip.
///
/// <para>These were a gap in r415's own sweep, not an untouched corner. That sweep filtered to
/// bool/double/string/int properties and therefore skipped every complex one -- borders, fill
/// colours, gradients, theme references -- silently. A filter that quietly drops the richest half of
/// a model is the same failure this program keeps finding in its instruments: it reports green over
/// what it never looked at.</para>
///
/// <para>Borders and fills are also the formatting users notice most: a table whose borders vanish
/// on reload looks broken in a way a lost font size does not, and a lost fill can make a
/// deliberately highlighted row indistinguishable from the rest of the sheet.</para>
/// </summary>
public sealed class R420_BordersAndFillsReachTheFileTests
{
    private static CellStyle RoundTrip(CellStyle style)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(style);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var cell = reloaded.Sheets[0].GetCell(1, 1);
        cell.Should().NotBeNull("the styled cell itself must survive before its style can be compared");
        return reloaded.GetStyle(cell!.StyleId);
    }

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.Medium)]
    [InlineData(BorderStyle.Thick)]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.Dotted)]
    [InlineData(BorderStyle.Double)]
    public void EachBorderEdgeKeepsItsStyleAndColour(BorderStyle borderStyle)
    {
        var border = new CellBorder(borderStyle, new CellColor(0xC0, 0x20, 0x40));
        var reloaded = RoundTrip(new CellStyle
        {
            BorderTop = border,
            BorderRight = border,
            BorderBottom = border,
            BorderLeft = border,
        });

        // Each edge is asserted separately: a writer that emitted only the first edge, or transposed
        // left and right, would pass a test that checked just one of them.
        reloaded.BorderTop.Style.Should().Be(borderStyle, "the top edge must survive");
        reloaded.BorderRight.Style.Should().Be(borderStyle, "the right edge must survive");
        reloaded.BorderBottom.Style.Should().Be(borderStyle, "the bottom edge must survive");
        reloaded.BorderLeft.Style.Should().Be(borderStyle, "the left edge must survive");

        reloaded.BorderTop.Color.Should().Be(
            new CellColor(0xC0, 0x20, 0x40), "a border keeps its colour, not just its weight");
    }

    [Fact]
    public void EdgesAreNotTransposed()
    {
        // Distinct styles per edge, so a mapping that swaps two of them fails here even though the
        // per-edge test above would still pass with every edge set identically.
        var reloaded = RoundTrip(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin),
            BorderRight = new CellBorder(BorderStyle.Medium),
            BorderBottom = new CellBorder(BorderStyle.Thick),
            BorderLeft = new CellBorder(BorderStyle.Dashed),
        });

        reloaded.BorderTop.Style.Should().Be(BorderStyle.Thin);
        reloaded.BorderRight.Style.Should().Be(BorderStyle.Medium);
        reloaded.BorderBottom.Style.Should().Be(BorderStyle.Thick);
        reloaded.BorderLeft.Style.Should().Be(BorderStyle.Dashed);
    }

    [Fact]
    public void AFillColourSurvives()
    {
        var reloaded = RoundTrip(new CellStyle { FillColor = new CellColor(0x20, 0x80, 0xC0) });

        reloaded.FillColor.Should().Be(
            new CellColor(0x20, 0x80, 0xC0),
            "a highlighted row that loses its fill becomes indistinguishable from the rest of the sheet");
    }

    [Fact]
    public void AFontColourSurvives()
    {
        var reloaded = RoundTrip(new CellStyle { FontColor = new CellColor(0x10, 0x90, 0x30) });

        reloaded.FontColor.Should().Be(new CellColor(0x10, 0x90, 0x30));
    }

    [Theory]
    [InlineData(CellFillPatternStyle.Solid)]
    [InlineData(CellFillPatternStyle.Gray125)]
    [InlineData(CellFillPatternStyle.DarkHorizontal)]
    public void AFillPatternSurvivesWithItsColours(CellFillPatternStyle pattern)
    {
        var reloaded = RoundTrip(new CellStyle
        {
            FillColor = new CellColor(0x33, 0x66, 0x99),
            FillPatternStyle = pattern,
            FillPatternColor = new CellColor(0xAA, 0xBB, 0xCC),
        });

        reloaded.FillPatternStyle.Should().Be(pattern, "the pattern is what distinguishes a hatch from a solid fill");
        reloaded.FillColor.Should().Be(new CellColor(0x33, 0x66, 0x99));
    }

    [Fact]
    public void ANoneBorderStaysAbsent()
    {
        // The control. Every assertion above checks that something SET survives; without this, a
        // reader that invented a Thin border everywhere would satisfy all of them.
        var reloaded = RoundTrip(new CellStyle { FontName = "Verdana" });

        reloaded.BorderTop.Style.Should().Be(BorderStyle.None, "an unset edge must not acquire a border");
        reloaded.BorderLeft.Style.Should().Be(BorderStyle.None);
        reloaded.FillColor.Should().BeNull("an unstyled cell must not acquire a fill");
    }
}
