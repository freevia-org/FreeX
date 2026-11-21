using FreeW.App.Host;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A header/footer layout table wider than the text area (Word lets it hang into the margin) must not be
/// clipped at the band edge: the overflow is reported so the band can widen its page and, for a
/// right-to-left table that anchors at the right margin, shift left.
/// </summary>
public sealed class HeaderFooterBandTableOverflowTests
{
    private static Table TableOfWidth(double widthPt)
    {
        var table = new Table();
        table.ColumnWidthsPt.AddRange([widthPt / 2, widthPt / 2]);
        return table;
    }

    [Fact]
    public void TableOverflow_IsTheAuthoredWidthBeyondTheBand()
    {
        var bandDip = 492.5 * 96.0 / 72.0;

        var overflow = HeaderFooterBandRenderer.TableOverflowDip(TableOfWidth(503.3), bandDip);

        Assert.Equal((503.3 - 492.5) * 96.0 / 72.0, overflow, 3);
    }

    [Fact]
    public void TableOverflow_IsZeroForATableThatFitsOrNoTable()
    {
        Assert.Equal(0, HeaderFooterBandRenderer.TableOverflowDip(TableOfWidth(400), 492.5 * 96.0 / 72.0));
        Assert.Equal(0, HeaderFooterBandRenderer.TableOverflowDip(null, 600));
    }
}
