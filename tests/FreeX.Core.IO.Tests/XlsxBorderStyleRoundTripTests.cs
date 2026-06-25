using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for border styles added in Wave B: Hair, SlantDashDot and other extended OOXML styles.
/// </summary>
public sealed class XlsxBorderStyleRoundTripTests
{
    [Theory]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.SlantDashDot)]
    [InlineData(BorderStyle.MediumDashed)]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashDot)]
    [InlineData(BorderStyle.DashDotDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    public void XlsxAdapter_ExtendedBorderStyle_RoundTrips(BorderStyle borderStyle)
    {
        // Arrange: cell with the given border style on all four edges
        var workbook = new Workbook($"BorderStyle_{borderStyle}");
        var sheet = workbook.AddSheet("Sheet1");
        var border = new CellBorder(borderStyle, CellColor.Black);
        var style = new CellStyle
        {
            BorderTop = border,
            BorderRight = border,
            BorderBottom = border,
            BorderLeft = border,
        };
        var styleId = workbook.RegisterStyle(style);
        sheet.SetStyleOnly(1, 1, styleId);

        // Act — save → reload
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        // Assert
        var loadedStyle = reloaded.GetStyle(reloaded.GetSheetAt(0)!.GetStyleOnly(1, 1)!.Value);
        loadedStyle.BorderTop.Style.Should().Be(borderStyle, $"{borderStyle} top must survive XLSX round-trip");
        loadedStyle.BorderRight.Style.Should().Be(borderStyle, $"{borderStyle} right must survive XLSX round-trip");
        loadedStyle.BorderBottom.Style.Should().Be(borderStyle, $"{borderStyle} bottom must survive XLSX round-trip");
        loadedStyle.BorderLeft.Style.Should().Be(borderStyle, $"{borderStyle} left must survive XLSX round-trip");
    }
}
