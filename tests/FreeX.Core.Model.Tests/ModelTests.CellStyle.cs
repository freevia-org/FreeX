using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class CellStyleTests
{
    [Fact]
    public void CellStyle_DefaultHasExpectedProperties()
    {
        var s = CellStyle.Default;
        s.FontName.Should().Be("Calibri");
        s.FontSize.Should().Be(11);
        s.Bold.Should().BeFalse();
        s.FillColor.Should().BeNull();
        s.NumberFormat.Should().Be("General");
        s.Locked.Should().BeTrue();
        s.Hidden.Should().BeFalse();
    }

    [Fact]
    public void StyleRegistry_DefaultStyleAlwaysAtIndex0()
    {
        var wb = new Workbook();
        var style = wb.GetStyle(StyleId.Default);
        style.Should().NotBeNull();
        style.FontName.Should().Be("Calibri");
    }

    [Fact]
    public void StyleRegistry_RegisterNewStyle_ReturnsDistinctId()
    {
        var wb = new Workbook();
        var bold = new CellStyle { Bold = true };
        var id = wb.RegisterStyle(bold);
        id.Should().NotBe(StyleId.Default);
    }

    [Fact]
    public void StyleRegistry_RegisterDuplicateStyle_ReturnsSameId()
    {
        var wb = new Workbook();
        var s1 = new CellStyle { FontName = "Arial", FontSize = 14 };
        var s2 = new CellStyle { FontName = "Arial", FontSize = 14 };
        var id1 = wb.RegisterStyle(s1);
        var id2 = wb.RegisterStyle(s2);
        id1.Should().Be(id2);
        wb.StyleCount.Should().Be(2);
    }

    [Fact]
    public void StyleRegistry_GetStyle_ReturnsDefensiveClone()
    {
        var wb = new Workbook();
        var id = wb.RegisterStyle(new CellStyle { Bold = true });
        var first = wb.GetStyle(id);
        first.Bold = false;

        var second = wb.GetStyle(id);

        first.Should().NotBeSameAs(second);
        second.Bold.Should().BeTrue();
    }

    [Fact]
    public void Workbook_DefaultCalculationMode_IsAutomatic()
    {
        var wb = new Workbook("test");

        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
    }

    [Fact]
    public void RegisterStyle_ManyDuplicates_DoesNotGrowRegistry()
    {
        var wb = new Workbook("T");
        for (int i = 0; i < 10_000; i++)
            wb.RegisterStyle(new CellStyle { Bold = true });
        wb.StyleCount.Should().Be(2, "10,000 identical bold styles collapse to one entry (plus Default)");
    }
}
