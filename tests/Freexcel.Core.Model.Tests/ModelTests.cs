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

    [Fact]
    public void GetMergeRegion_FindsMergeInLargeList()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (uint r = 1; r <= 500; r++)
        {
            var start = new CellAddress(sheet.Id, r * 2, 1);
            var end   = new CellAddress(sheet.Id, r * 2, 2);
            sheet.AddMergedRegion(new GridRange(start, end));
        }
        var target = new CellAddress(sheet.Id, 500, 1);
        var found  = sheet.GetMergeRegion(target);
        found.Should().NotBeNull("cell at row 500 col 1 is inside a merge region");
        found!.Value.Start.Row.Should().Be(500);
    }

    [Fact]
    public void ReplaceMergedRegions_MaterializesLazyProjectionBeforeReplacing()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)));

        sheet.ReplaceMergedRegions(sheet.MergedRegions.Select(region => new GridRange(
            new CellAddress(region.Start.Sheet, region.Start.Row + 1, region.Start.Col),
            new CellAddress(region.End.Sheet, region.End.Row + 1, region.End.Col))));

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2)));
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

    [Fact]
    public void ColumnNameToNumber_SevenLetters_DoesNotOverflow()
    {
        // Seven-letter column names (e.g. ZZZZZZZ) would overflow uint without
        // the early-exit guard; the result must exceed MaxCol but not wrap.
        var result = CellAddress.ColumnNameToNumber("ZZZZZZZ");
        result.Should().BeGreaterThan(CellAddress.MaxCol, "long column names must return a value > MaxCol, not an overflow-wrapped one");
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

    [Fact]
    public void RegisterStyle_ManyDuplicates_DoesNotGrowRegistry()
    {
        var wb = new Workbook("T");
        for (int i = 0; i < 10_000; i++)
            wb.RegisterStyle(new CellStyle { Bold = true });
        wb.StyleCount.Should().Be(2, "10,000 identical bold styles collapse to one entry (plus Default)");
    }
}

public class SheetCloneTests
{
    [Fact]
    public void Sheet_Clone_CopiesBackgroundImage()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        src.BackgroundImage = new WorksheetBackgroundImage(imageBytes, "image/png", "bg.png");

        var copy = src.Clone(SheetId.New(), "Copy");

        copy.BackgroundImage.Should().NotBeNull();
        copy.BackgroundImage!.ContentType.Should().Be("image/png");
        copy.BackgroundImage.FileName.Should().Be("bg.png");
        copy.BackgroundImage.ImageBytes.Should().Equal(imageBytes);
    }

    [Fact]
    public void Sheet_Clone_CopiesOutlineLevels()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        src.RowOutlineLevels[5] = 2;
        src.ColOutlineLevels[3] = 1;
        src.GroupHiddenRows.Add(5);
        src.GroupHiddenCols.Add(3);

        var copy = src.Clone(SheetId.New(), "Copy");

        copy.RowOutlineLevels.Should().ContainKey(5).WhoseValue.Should().Be(2);
        copy.ColOutlineLevels.Should().ContainKey(3).WhoseValue.Should().Be(1);
        copy.GroupHiddenRows.Should().Contain(5u);
        copy.GroupHiddenCols.Should().Contain(3u);
    }
}
