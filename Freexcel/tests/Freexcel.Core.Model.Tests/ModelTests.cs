using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class CellAddressTests
{
    [Fact]
    public void Parse_A1_ReturnsCorrectRowAndCol()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("A1", sheet);
        addr.Row.Should().Be(1);
        addr.Col.Should().Be(1);
    }

    [Fact]
    public void Parse_B7_ReturnsCorrectRowAndCol()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("B7", sheet);
        addr.Row.Should().Be(7);
        addr.Col.Should().Be(2);
    }

    [Fact]
    public void Parse_AA1_ReturnsColumn27()
    {
        var sheet = SheetId.New();
        var addr = CellAddress.Parse("AA1", sheet);
        addr.Col.Should().Be(27);
    }

    [Fact]
    public void ColumnNameToNumber_A_Returns1()
    {
        CellAddress.ColumnNameToNumber("A").Should().Be(1);
    }

    [Fact]
    public void ColumnNameToNumber_Z_Returns26()
    {
        CellAddress.ColumnNameToNumber("Z").Should().Be(26);
    }

    [Fact]
    public void ColumnNameToNumber_AA_Returns27()
    {
        CellAddress.ColumnNameToNumber("AA").Should().Be(27);
    }

    [Fact]
    public void NumberToColumnName_RoundTrips()
    {
        for (uint i = 1; i <= 100; i++)
        {
            var name = CellAddress.NumberToColumnName(i);
            var number = CellAddress.ColumnNameToNumber(name);
            number.Should().Be(i);
        }
    }

    [Fact]
    public void ToA1_FormatsCorrectly()
    {
        var sheet = SheetId.New();
        var addr = new CellAddress(sheet, 7, 2);
        addr.ToA1().Should().Be("B7");
    }
}

public class WorkbookTests
{
    [Fact]
    public void NewWorkbook_HasNoSheets()
    {
        var wb = new Workbook();
        wb.SheetCount.Should().Be(0);
    }

    [Fact]
    public void AddSheet_IncreasesSheetCount()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        wb.SheetCount.Should().Be(1);
        sheet.Name.Should().Be("Sheet1");
    }

    [Fact]
    public void GetSheet_ByName_IsCaseInsensitive()
    {
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        wb.GetSheet("sheet1").Should().NotBeNull();
        wb.GetSheet("SHEET1").Should().NotBeNull();
    }

    [Fact]
    public void AddSheet_DuplicateName_Throws()
    {
        var wb = new Workbook();
        wb.AddSheet("Sheet1");

        var act = () => wb.AddSheet("sheet1");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bad/Name")]
    [InlineData("Bad\\Name")]
    [InlineData("Bad?Name")]
    [InlineData("Bad*Name")]
    [InlineData("Bad[Name]")]
    [InlineData("Bad:Name")]
    [InlineData("12345678901234567890123456789012")]
    public void AddSheet_InvalidExcelSheetName_Throws(string name)
    {
        var wb = new Workbook();

        var act = () => wb.AddSheet(name);

        act.Should().Throw<ArgumentException>();
    }
}

public class SheetTests
{
    [Fact]
    public void SetCell_GetValue_Roundtrips()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(42));
        sheet.GetValue(addr).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void GetValue_EmptyCell_ReturnsBlank()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
    }
}

public class CellAddressBoundsTests
{
    [Fact]
    public void CellAddress_Parse_ThrowsForRowZero()
    {
        var sheet = SheetId.New();
        Action act = () => CellAddress.Parse("A0", sheet);
        act.Should().Throw<FormatException>("row 0 is below the valid range");
    }

    [Fact]
    public void CellAddress_Parse_ThrowsForRowAboveMax()
    {
        var sheet = SheetId.New();
        Action act = () => CellAddress.Parse("A1048577", sheet);
        act.Should().Throw<FormatException>("row 1048577 exceeds MaxRow");
    }

    [Fact]
    public void CellAddress_Parse_ThrowsForColumnAboveMax()
    {
        var sheet = SheetId.New();
        // XFE is column 16385, one past the maximum XFD (16384)
        Action act = () => CellAddress.Parse("XFE1", sheet);
        act.Should().Throw<FormatException>("column XFE exceeds MaxCol");
    }
}

public class CellStyleTests
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
    public void StyleRegistry_GetStyle_ReturnsCopy()
    {
        var wb = new Workbook();
        var style = wb.GetStyle(StyleId.Default);
        style.FontName = "Mutated";
        wb.GetStyle(StyleId.Default).FontName.Should().Be("Calibri");
    }

    [Fact]
    public void Workbook_DefaultCalculationMode_IsAutomatic()
    {
        var wb = new Workbook("test");

        wb.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
    }
}
