using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class SheetCloneTests
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
