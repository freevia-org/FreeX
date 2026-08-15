using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class WorkbookTests
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
    public void IndexOfSheet_ReturnsPositionOrMinusOne()
    {
        var wb = new Workbook();
        var first = wb.AddSheet("First");
        var second = wb.AddSheet("Second");

        wb.IndexOfSheet(first.Id).Should().Be(0);
        wb.IndexOfSheet(second.Id).Should().Be(1);
        wb.IndexOfSheet(SheetId.New()).Should().Be(-1);
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

    [Fact]
    public void RemoveSheet_RemovesNamedRangesOnDeletedSheet()
    {
        var wb = new Workbook();
        var keep = wb.AddSheet("Keep");
        var remove = wb.AddSheet("Remove");
        wb.DefineNamedRange("KeepRange", new GridRange(
            new CellAddress(keep.Id, 1, 1),
            new CellAddress(keep.Id, 2, 1)));
        wb.DefineNamedRange("RemoveRange", new GridRange(
            new CellAddress(remove.Id, 1, 1),
            new CellAddress(remove.Id, 2, 1)));

        wb.RemoveSheet(remove.Id).Should().BeTrue();

        wb.NamedRanges.Should().ContainKey("KeepRange");
        wb.NamedRanges.Should().NotContainKey("RemoveRange");

        // R104: real Excel keeps a defined name's Name-Manager metadata (Hidden/Comment) intact
        // when the sheet its range refers to is deleted - only the range text is converted to
        // "#REF!" (now living in NamedFormulas instead of NamedRanges). The metadata entry must
        // therefore survive, not be dropped alongside the range.
        wb.NamedFormulas.Should().ContainKey("RemoveRange");
        wb.NamedFormulas["RemoveRange"].Should().Be("#REF!");
        wb.NamedRangeMetadataByName.Should().ContainKey("RemoveRange");
    }

    [Fact]
    public void RemoveSheet_AdjustsWorkbookViewSheetIndexes()
    {
        var wb = new Workbook();
        wb.AddSheet("First");
        var middle = wb.AddSheet("Middle");
        wb.AddSheet("Last");
        wb.ActiveSheetIndex = 2;
        wb.FirstVisibleSheetIndex = 1;

        wb.RemoveSheet(middle.Id).Should().BeTrue();

        wb.ActiveSheetIndex.Should().Be(1);
        wb.FirstVisibleSheetIndex.Should().Be(1);
    }

    [Fact]
    public void RemoveSheet_ClearsWorkbookViewSheetIndexesWhenLastSheetIsRemoved()
    {
        var wb = new Workbook();
        var only = wb.AddSheet("Only");
        wb.ActiveSheetIndex = 0;
        wb.FirstVisibleSheetIndex = 0;

        wb.RemoveSheet(only.Id).Should().BeTrue();

        wb.ActiveSheetIndex.Should().BeNull();
        wb.FirstVisibleSheetIndex.Should().BeNull();
    }

    [Fact]
    public void MoveSheet_RemapsWorkbookViewIndexesForMovedSheets()
    {
        var wb = new Workbook();
        var first = wb.AddSheet("First");
        var second = wb.AddSheet("Second");
        var third = wb.AddSheet("Third");
        wb.ActiveSheetIndex = 0;
        wb.FirstVisibleSheetIndex = 2;

        wb.MoveSheet(0, 2);

        wb.Sheets.Select(sheet => sheet.Id).Should().Equal(second.Id, third.Id, first.Id);
        wb.ActiveSheetIndex.Should().Be(2);
        wb.FirstVisibleSheetIndex.Should().Be(1);
    }

    [Fact]
    public void MoveSheet_RemapsWorkbookViewIndexesWhenAnotherSheetMovesAcrossThem()
    {
        var wb = new Workbook();
        var first = wb.AddSheet("First");
        var second = wb.AddSheet("Second");
        var third = wb.AddSheet("Third");
        var fourth = wb.AddSheet("Fourth");
        wb.ActiveSheetIndex = 1;
        wb.FirstVisibleSheetIndex = 2;

        wb.MoveSheet(3, 0);

        wb.Sheets.Select(sheet => sheet.Id).Should().Equal(fourth.Id, first.Id, second.Id, third.Id);
        wb.ActiveSheetIndex.Should().Be(2);
        wb.FirstVisibleSheetIndex.Should().Be(3);
    }
}
