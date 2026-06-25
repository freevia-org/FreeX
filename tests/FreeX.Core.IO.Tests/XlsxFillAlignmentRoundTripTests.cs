using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for HorizontalAlignment.Fill (Excel "fill" alignment).
/// </summary>
public sealed class XlsxFillAlignmentRoundTripTests
{
    [Fact]
    public void XlsxAdapter_FillAlignment_RoundTrips()
    {
        // Arrange
        var workbook = new Workbook("FillAlign");
        var sheet = workbook.AddSheet("Sheet1");
        var style = new CellStyle { HorizontalAlignment = HorizontalAlignment.Fill };
        var styleId = workbook.RegisterStyle(style);
        sheet.SetStyleOnly(1, 1, styleId);

        // Act — save → reload
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(stream);

        // Assert
        var loadedStyle = reloaded.GetStyle(reloaded.GetSheetAt(0)!.GetStyleOnly(1, 1)!.Value);
        loadedStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Fill,
            "Fill alignment must survive XLSX save/reload round-trip");
    }

    [Fact]
    public void CellStyle_FillAlignment_IsDistinctFromOtherAlignments()
    {
        var fill = new CellStyle { HorizontalAlignment = HorizontalAlignment.Fill };
        var left = new CellStyle { HorizontalAlignment = HorizontalAlignment.Left };
        var center = new CellStyle { HorizontalAlignment = HorizontalAlignment.Center };

        fill.Should().NotBe(left, "Fill ≠ Left");
        fill.Should().NotBe(center, "Fill ≠ Center");
        fill.HorizontalAlignment.Should().Be(HorizontalAlignment.Fill);
    }
}
