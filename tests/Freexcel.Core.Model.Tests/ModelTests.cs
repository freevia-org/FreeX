using Freexcel.Core.Model;
using FluentAssertions;
using System.Diagnostics;

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
        wb.NamedRangeMetadataByName.Should().NotContainKey("RemoveRange");
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

    [Fact]
    public void GetUsedRange_RecomputesAfterBoundaryCellsAreCleared()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("inside"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("edge"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));

        sheet.ClearCell(new CellAddress(sheet.Id, 20, 30));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 3)));
    }

    [Fact]
    public void GetUsedRange_ExpandsAfterCachedRangeIsRead()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("inside"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 3)));

        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("edge"));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));
    }

    [Fact]
    public void GetUsedRange_KeepsCachedBoundsAfterInteriorCellsChange()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("start"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 20), new TextValue("interior"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("end"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));

        sheet.SetCell(new CellAddress(sheet.Id, 10, 20), new TextValue("updated"));
        sheet.ClearCell(new CellAddress(sheet.Id, 10, 20));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));
    }

    [Fact]
    public void GetUsedRange_RepeatedCallsReuseCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Large");
        for (uint row = 1; row <= 200; row++)
        {
            for (uint col = 1; col <= 100; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));
        }

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        GridRange? range = null;
        for (var i = 0; i < repetitions; i++)
            range = sheet.GetUsedRange();
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100)));
        Console.WriteLine(
            $"GetUsedRange cached repeated {repetitions}x over {sheet.CellCount:N0} cells: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void GetUsedRange_InterleavedInteriorWritesReuseCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Large");
        for (uint row = 1; row <= 200; row++)
        {
            for (uint col = 1; col <= 100; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));
        }

        var expected = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100));
        sheet.GetUsedRange().Should().Be(expected);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 10_000;
        var replacement = new NumberValue(123);
        var address = new CellAddress(sheet.Id, 100, 50);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        GridRange? range = null;
        for (var i = 0; i < repetitions; i++)
        {
            sheet.SetCell(address, replacement);
            range = sheet.GetUsedRange();
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        range.Should().Be(expected);
        Console.WriteLine(
            $"GetUsedRange interleaved interior writes {repetitions}x over {sheet.CellCount:N0} cells: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
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
    public void StyleRegistry_GetStyle_ReturnsSameInstance()
    {
        // GetStyle returns the shared registry instance — no defensive clone on every read.
        // Callers that need to modify a style must call .Clone() themselves.
        var wb = new Workbook();
        var id = wb.RegisterStyle(new CellStyle { Bold = true });
        var first = wb.GetStyle(id);
        var second = wb.GetStyle(id);
        first.Should().BeSameAs(second);
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

    [Fact]
    public void Sheet_Clone_CopiesLayoutCollections()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        src.ColumnWidths[2] = 18.5;
        src.RowHeights[4] = 33.25;
        src.HiddenRows.Add(6);
        src.FilterHiddenRows.Add(7);
        src.HiddenCols.Add(3);
        src.RowPageBreaks.Add(20);
        src.ColumnPageBreaks.Add(5);
        src.RowOutlineLevels[5] = 2;
        src.ColOutlineLevels[3] = 1;
        src.GroupHiddenRows.Add(5);
        src.GroupHiddenCols.Add(3);

        var copy = src.Clone(SheetId.New(), "Copy");

        copy.ColumnWidths.Should().ContainKey(2).WhoseValue.Should().Be(18.5);
        copy.RowHeights.Should().ContainKey(4).WhoseValue.Should().Be(33.25);
        copy.HiddenRows.Should().Contain(6u);
        copy.FilterHiddenRows.Should().Contain(7u);
        copy.HiddenCols.Should().Contain(3u);
        copy.RowPageBreaks.Should().Contain(20u);
        copy.ColumnPageBreaks.Should().Contain(5u);
        copy.RowOutlineLevels.Should().ContainKey(5).WhoseValue.Should().Be(2);
        copy.ColOutlineLevels.Should().ContainKey(3).WhoseValue.Should().Be(1);
        copy.GroupHiddenRows.Should().Contain(5u);
        copy.GroupHiddenCols.Should().Contain(3u);
    }

    [Fact]
    public void Sheet_Clone_RemapCellsStylesAndMergedRegionsToNewSheetId()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        var sourceAddress = new CellAddress(src.Id, 2, 3);
        var sourceCell = Cell.FromValue(new TextValue("value"));
        sourceCell.StyleId = new StyleId(7);
        src.SetCell(sourceAddress, sourceCell);
        src.SetStyleOnly(4, 5, new StyleId(8));
        src.AddMergedRegion(new GridRange(
            new CellAddress(src.Id, 6, 1),
            new CellAddress(src.Id, 7, 2)));
        var newId = SheetId.New();

        var copy = src.Clone(newId, "Copy");

        var clonedAddress = new CellAddress(newId, 2, 3);
        copy.GetCell(clonedAddress).Should().NotBeSameAs(sourceCell);
        copy.GetCell(clonedAddress)!.Value.Should().Be(new TextValue("value"));
        copy.GetCell(clonedAddress)!.StyleId.Should().Be(new StyleId(7));
        copy.GetStyleOnly(4, 5).Should().Be(new StyleId(8));
        copy.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(newId, 6, 1),
            new CellAddress(newId, 7, 2)));
    }

    [Fact]
    public void Sheet_Clone_RemapPivotTableRangesAndCopiesFieldLists()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        var pivot = new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 3,
            SourceRange = new GridRange(
                new CellAddress(src.Id, 1, 1),
                new CellAddress(src.Id, 10, 3)),
            TargetRange = new GridRange(
                new CellAddress(src.Id, 12, 1),
                new CellAddress(src.Id, 16, 4)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            StyleName = "PivotStyleMedium9",
            ShowRowStripes = true,
            ShowColumnGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["East", "West"]));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending));
        src.PivotTables.Add(pivot);
        var newId = SheetId.New();

        var copy = src.Clone(newId, "Copy");

        var clonedPivot = copy.PivotTables.Should().ContainSingle().Subject;
        clonedPivot.Should().NotBeSameAs(pivot);
        clonedPivot.SourceRange.Should().Be(new GridRange(
            new CellAddress(newId, 1, 1),
            new CellAddress(newId, 10, 3)));
        clonedPivot.TargetRange.Should().Be(new GridRange(
            new CellAddress(newId, 12, 1),
            new CellAddress(newId, 16, 4)));
        clonedPivot.StyleName.Should().Be("PivotStyleMedium9");
        clonedPivot.ShowColumnGrandTotals.Should().BeFalse();
        clonedPivot.RowFields.Should().Equal(pivot.RowFields);
        clonedPivot.DataFields.Should().Equal(pivot.DataFields);
        clonedPivot.CalculatedFields.Should().Equal(pivot.CalculatedFields);
        clonedPivot.Sorts.Should().Equal(pivot.Sorts);
        clonedPivot.RowFields.Should().NotBeSameAs(pivot.RowFields);
    }

    [Fact]
    public void Sheet_Clone_RemapStructuredTableRangeAndCopiesColumns()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        var nativeAttributes = new Dictionary<string, string> { ["custom"] = "kept" };
        var table = new StructuredTableModel
        {
            Id = 2,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(
                new CellAddress(src.Id, 1, 1),
                new CellAddress(src.Id, 5, 3)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            PackagePart = "xl/tables/table1.xml",
            NativeAttributes = nativeAttributes
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["West"], IncludeBlank: true));
        src.StructuredTables.Add(table);
        var newId = SheetId.New();

        var copy = src.Clone(newId, "Copy");

        var clonedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        clonedTable.Should().NotBeSameAs(table);
        clonedTable.Range.Should().Be(new GridRange(
            new CellAddress(newId, 1, 1),
            new CellAddress(newId, 5, 3)));
        clonedTable.StyleName.Should().Be("TableStyleMedium2");
        clonedTable.TotalsRowShown.Should().BeTrue();
        clonedTable.Columns.Should().Equal(table.Columns);
        clonedTable.FilterColumns.Should().BeEquivalentTo(table.FilterColumns);
        clonedTable.Columns.Should().NotBeSameAs(table.Columns);
        clonedTable.NativeAttributes.Should().BeEquivalentTo(nativeAttributes);
        clonedTable.NativeAttributes.Should().NotBeSameAs(nativeAttributes);
    }

    [Fact]
    public void Sheet_Clone_DropsExistingConditionalFormatX14IdNativeChild()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        src.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 1, 1), new CellAddress(src.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            NativeChildXmls =
            [
                """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext><ext uri="{FUTURE}" /></extLst>""",
                """<future xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""
            ],
            NativePayloadChildXmls = ["""<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />"""]
        });

        var copy = src.Clone(SheetId.New(), "Copy");

        var clonedRule = copy.ConditionalFormats.Should().ContainSingle().Subject;
        clonedRule.AppliesTo.Start.Sheet.Should().Be(copy.Id);
        clonedRule.AppliesTo.End.Sheet.Should().Be(copy.Id);
        clonedRule.Id.Should().NotBe(src.ConditionalFormats.Single().Id);
        clonedRule.NativeChildXmls.Should().HaveCount(2);
        clonedRule.NativeChildXmls.Should().Contain(xml => xml.Contains("{FUTURE}", StringComparison.Ordinal));
        clonedRule.NativeChildXmls.Should().Contain(xml => xml.Contains("future", StringComparison.Ordinal));
        clonedRule.NativeChildXmls.Should().NotContain(xml => xml.Contains("11111111-2222-3333-4444-555555555555", StringComparison.Ordinal));
        clonedRule.NativePayloadChildXmls.Should().BeEquivalentTo(src.ConditionalFormats.Single().NativePayloadChildXmls);
    }

    [Fact]
    public void Sheet_Clone_RemapDataValidationRangeAndPreservesNativeMetadata()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("S");
        var nativeAttributes = new Dictionary<string, string> { ["uid"] = "{validation}" };
        var nativeChildXmls = new[] { """<ext custom="kept" />""" };
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(src.Id, 3, 2),
                new CellAddress(src.Id, 6, 2)),
            Type = DvType.List,
            Formula1 = "\"A,B\"",
            AllowBlank = false,
            ErrorTitle = "Invalid",
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
        validation.AdditionalRanges.Add(new GridRange(
            new CellAddress(src.Id, 8, 4),
            new CellAddress(src.Id, 9, 4)));
        src.DataValidations.Add(validation);
        var newId = SheetId.New();

        var copy = src.Clone(newId, "Copy");

        var clonedValidation = copy.DataValidations.Should().ContainSingle().Subject;
        clonedValidation.Should().NotBeSameAs(src.DataValidations.Single());
        clonedValidation.AppliesTo.Should().Be(new GridRange(
            new CellAddress(newId, 3, 2),
            new CellAddress(newId, 6, 2)));
        clonedValidation.AdditionalRanges.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(newId, 8, 4),
            new CellAddress(newId, 9, 4)));
        clonedValidation.Type.Should().Be(DvType.List);
        clonedValidation.AllowBlank.Should().BeFalse();
        clonedValidation.ErrorTitle.Should().Be("Invalid");
        clonedValidation.NativeAttributes.Should().BeSameAs(nativeAttributes);
        clonedValidation.NativeChildXmls.Should().BeSameAs(nativeChildXmls);
    }
}
