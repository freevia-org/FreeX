using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxClosedXmlCellMapperColorTests
{
    [Theory]
    [InlineData(255, 1, 2, 3)]
    [InlineData(0, 250, 251, 252)]
    public void MapColor_ReadsConcreteArgbColor(int alpha, int red, int green, int blue)
    {
        var xlColor = XLColor.FromArgb(alpha, red, green, blue);

        var color = XlsxClosedXmlCellMapper.MapColor(xlColor, WorkbookTheme.Office);

        color.Should().Be(new CellColor((byte)red, (byte)green, (byte)blue));
    }

    [Fact]
    public void MapColor_ResolvesThemeColors()
    {
        var xlColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.4);

        var color = XlsxClosedXmlCellMapper.MapColor(xlColor, WorkbookTheme.Office);

        color.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4));
    }

    [Fact]
    public void MapColor_FallsBackToBlackForUnsupportedClosedXmlColors()
    {
        XlsxClosedXmlCellMapper.MapColor(XLColor.NoColor, WorkbookTheme.Office).Should().Be(CellColor.Black);
        XlsxClosedXmlCellMapper.MapColor(XLColor.FromIndex(3), WorkbookTheme.Office).Should().Be(CellColor.Black);
    }
}
