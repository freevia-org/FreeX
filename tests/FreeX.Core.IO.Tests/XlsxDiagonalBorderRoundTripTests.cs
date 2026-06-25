using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for diagonal cell borders (xlDiagonalDown / xlDiagonalUp / OOXML diagonalDown="1" diagonalUp="1").
/// </summary>
public sealed class XlsxDiagonalBorderRoundTripTests
{
    [Fact]
    public void XlsxAdapter_DiagonalDownBorder_RoundTripsStyleAndColor()
    {
        // Arrange
        var workbook = new Workbook("DiagDown");
        var sheet = workbook.AddSheet("Sheet1");
        var border = new CellBorder(BorderStyle.Thin, new CellColor(255, 0, 0));
        var style = new CellStyle { BorderDiagonalDown = border };
        var styleId = workbook.RegisterStyle(style);
        sheet.SetStyleOnly(1, 1, styleId);

        // Act — save → reload
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        // Assert
        var loadedStyle = reloaded.GetStyle(reloaded.GetSheetAt(0)!.GetStyleOnly(1, 1)!.Value);
        loadedStyle.BorderDiagonalDown.Style.Should().Be(BorderStyle.Thin, "diagonal-down style must survive XLSX round-trip");
        loadedStyle.BorderDiagonalDown.Color.Should().Be(new CellColor(255, 0, 0), "diagonal-down color must survive XLSX round-trip");
        loadedStyle.BorderDiagonalUp.Style.Should().Be(BorderStyle.None, "only diagonal-down was set");
    }

    [Fact]
    public void XlsxAdapter_DiagonalUpBorder_RoundTripsStyleAndColor()
    {
        var workbook = new Workbook("DiagUp");
        var sheet = workbook.AddSheet("Sheet1");
        var border = new CellBorder(BorderStyle.Medium, new CellColor(0, 0, 255));
        var style = new CellStyle { BorderDiagonalUp = border };
        var styleId = workbook.RegisterStyle(style);
        sheet.SetStyleOnly(1, 1, styleId);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var loadedStyle = reloaded.GetStyle(reloaded.GetSheetAt(0)!.GetStyleOnly(1, 1)!.Value);
        loadedStyle.BorderDiagonalUp.Style.Should().Be(BorderStyle.Medium, "diagonal-up style must survive XLSX round-trip");
        loadedStyle.BorderDiagonalUp.Color.Should().Be(new CellColor(0, 0, 255), "diagonal-up color must survive XLSX round-trip");
        loadedStyle.BorderDiagonalDown.Style.Should().Be(BorderStyle.None, "only diagonal-up was set");
    }

    [Fact]
    public void XlsxAdapter_BothDiagonalBorders_RoundTrip()
    {
        var workbook = new Workbook("BothDiag");
        var sheet = workbook.AddSheet("Sheet1");
        var diagBorder = new CellBorder(BorderStyle.Thick, new CellColor(0, 128, 0));
        var style = new CellStyle
        {
            BorderDiagonalDown = diagBorder,
            BorderDiagonalUp = diagBorder,
        };
        var styleId = workbook.RegisterStyle(style);
        sheet.SetStyleOnly(2, 3, styleId);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        var loadedStyle = reloaded.GetStyle(reloaded.GetSheetAt(0)!.GetStyleOnly(2, 3)!.Value);
        loadedStyle.BorderDiagonalDown.Style.Should().Be(BorderStyle.Thick);
        loadedStyle.BorderDiagonalUp.Style.Should().Be(BorderStyle.Thick);
    }
}
