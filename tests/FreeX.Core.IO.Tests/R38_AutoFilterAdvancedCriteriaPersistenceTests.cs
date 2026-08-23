using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-38 findings R38-commands-autofilter-advanced-2-{1,2,3}.
/// </summary>
public sealed class R38_AutoFilterAdvancedCriteriaPersistenceTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, GridRange Range) MakeSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(1));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        return (wb, sheet, ctx, range);
    }

    // -----------------------------------------------------------------------------------------
    // R38-commands-autofilter-advanced-2-1: clearing/replacing a value-list filter on a column
    // must release rows a Top10/Average/custom-criterion/color filter on that SAME column owns.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FilterCommand_ClearingColumn_ReleasesRowsOwnedByPriorTop10FilterOnSameColumn()
    {
        var (_, sheet, ctx, range) = MakeSheet();

        // Apply Top 2 (keeps rows with 100 and 90; hides row 4 with value 1) via ColumnFilterOwnedRows.
        var topBottom = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        topBottom.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        var filterCol = range.Start.Col;
        sheet.ColumnFilterOwnedRows.Should().ContainKey(filterCol);
        sheet.ColumnFilterOwnedRows[filterCol].Should().Contain(4u);

        // Now "Clear Filter From Score" runs FilterCommand with an empty allowed-values list, exactly
        // as MainWindow.DataFilterCommands/MainWindow.AutoFilter's AutoFilterDialogAction.ClearFilter
        // handler does. Before the fix, row 4 stayed hidden forever because ColumnFilterOwnedRows
        // for this column was never released.
        var clear = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEmpty();
        (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned) || owned.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void FilterCommand_ReplacingColumnWithNewValueList_ReleasesRowsOwnedByPriorAverageFilter()
    {
        var (_, sheet, ctx, range) = MakeSheet();

        // Above-average keeps rows > average(100,90,1)=63.67 -> keeps rows 2,3 (100,90), hides row 4.
        var average = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);
        average.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        var filterCol = range.Start.Col;
        sheet.ColumnFilterOwnedRows[filterCol].Should().Contain(4u);

        // Replace with a plain value-list selection of "90" only (should now hide rows 2 and 4,
        // keep row 3). The old Average-owned row 4 must not linger stuck from the old mechanism --
        // it should be governed solely by the new value-list criterion afterward.
        var replace = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["90"]);
        replace.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        // The stale Average ownership entry for this column must be cleared -- row 4 is now hidden
        // purely because the value-list filter excludes it, not because of a leftover Average claim.
        (!sheet.ColumnFilterOwnedRows.TryGetValue(filterCol, out var owned) || owned.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void FilterCommand_NoRegression_ClearingColumnWithNoOwnedRows_IsNoOp()
    {
        var (_, sheet, ctx, range) = MakeSheet();

        var apply = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["100"]);
        apply.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);

        var clear = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []);
        clear.Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------------------------
    // R38-commands-autofilter-advanced-2-2: custom AutoFilter criteria (comparisons, AND/OR,
    // wildcards, date bounds) must persist into sheet.AutoFilter.FilterColumns and round-trip
    // through save/reload.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FilterConditionCommand_SimpleComparison_PersistsAndRoundTripsThroughSave()
    {
        var (wb, sheet, ctx, range) = MakeSheet();

        var criterion = new NumberGreaterThanFilterCriterion(50);
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);
        command.Apply(ctx).Success.Should().BeTrue();

        // In-session hiding still works: row 4 (value 1) fails ">50".
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var column = sheet.AutoFilter.FilterColumns[0];
        column.CustomFilters.Should().ContainSingle();
        column.CustomFilters[0].Operator.Should().Be("greaterThan");
        column.CustomFilters[0].Value.Should().Be("50");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedColumn = reloaded.Sheets[0].AutoFilter!.FilterColumns.Single();
        reloadedColumn.CustomFilters.Should().ContainSingle();
        reloadedColumn.CustomFilters[0].Operator.Should().Be("greaterThan");
        reloadedColumn.CustomFilters[0].Value.Should().Be("50");
    }

    [Fact]
    public void FilterConditionCommand_CompositeAndCriterion_PersistsBothOperandsWithAndFlag()
    {
        var (wb, sheet, ctx, range) = MakeSheet();

        var criterion = new CompositeFilterCriterion(
            new NumberGreaterThanFilterCriterion(10),
            new NumberLessThanFilterCriterion(100),
            UseAnd: true);
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var column = sheet.AutoFilter.FilterColumns[0];
        column.CustomFiltersAnd.Should().BeTrue();
        column.CustomFilters.Should().HaveCount(2);
        column.CustomFilters.Should().Contain(f => f.Operator == "greaterThan" && f.Value == "10");
        column.CustomFilters.Should().Contain(f => f.Operator == "lessThan" && f.Value == "100");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedColumn = reloaded.Sheets[0].AutoFilter!.FilterColumns.Single();
        reloadedColumn.CustomFiltersAnd.Should().BeTrue();
        reloadedColumn.CustomFilters.Should().HaveCount(2);
    }

    [Fact]
    public void FilterConditionCommand_WildcardContains_PersistsAsteriskWrappedValue()
    {
        var (_, sheet, ctx, range) = MakeSheet();

        var criterion = new TextContainsFilterCriterion("abc");
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var column = sheet.AutoFilter.FilterColumns[0];
        column.CustomFilters.Should().ContainSingle();
        column.CustomFilters[0].Value.Should().Be("*abc*");
    }

    [Fact]
    public void XlsxLoad_InlineStringEqualityCustomFilter_ReappliesWithoutRowHiddenBits()
    {
        var workbook = new Workbook("inline-equality");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 2, 1, "North");
        SetText(sheet, 3, 1, "South");
        SetText(sheet, 4, 1, "West");
        SetText(sheet, 5, 1, "East");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        new FilterConditionCommand(sheet.Id, range, 0, new TextEqualsFilterCriterion("East"))
            .Apply(new TestCommandContext(workbook))
            .Success.Should().BeTrue();

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        RemoveFilteredRowBitsAndUseInlineStrings(stream, ["Region", "North", "South", "West", "East"]);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.Sheets.Single();

        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u]);
        reloadedSheet.FilterHiddenRows.Should().NotContain(5u);
        reloadedSheet.GetValue(5, 1).Should().BeOfType<TextValue>().Which.Value.Should().Be("East");
    }

    [Fact]
    public void XlsxLoad_InlineStringBeginsWithCustomFilter_ReappliesWithoutRowHiddenBits()
    {
        var workbook = new Workbook("inline-begins");
        var sheet = workbook.AddSheet("Sheet1");
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 2, 1, "North");
        SetText(sheet, 3, 1, "Northwest");
        SetText(sheet, 4, 1, "South");
        SetText(sheet, 5, 1, "East");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        new FilterConditionCommand(sheet.Id, range, 0, new TextBeginsWithFilterCriterion("North"))
            .Apply(new TestCommandContext(workbook))
            .Success.Should().BeTrue();

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        RemoveFilteredRowBitsAndUseInlineStrings(stream, ["Region", "North", "Northwest", "South", "East"]);

        stream.Position = 0;
        var reloadedSheet = adapter.Load(stream).Sheets.Single();
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u]);
        reloadedSheet.FilterHiddenRows.Should().NotContain(2u);
        reloadedSheet.FilterHiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void XlsxLoad_NumericCustomFilter_ReappliesWithoutRowHiddenBits()
    {
        var (workbook, sheet, _, range) = MakeSheet();
        new FilterConditionCommand(sheet.Id, range, 0, new NumberGreaterThanFilterCriterion(50))
            .Apply(new TestCommandContext(workbook))
            .Success.Should().BeTrue();

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        RemoveFilteredRowBits(stream);

        stream.Position = 0;
        var reloadedSheet = adapter.Load(stream).Sheets.Single();
        reloadedSheet.FilterHiddenRows.Should().ContainSingle().Which.Should().Be(4u);
    }

    [Fact]
    public void FilterConditionCommand_Undo_RestoresPreviousAutoFilterColumns()
    {
        var (_, sheet, ctx, range) = MakeSheet();

        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(50));
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();

        command.Revert(ctx);
        sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void FilterConditionCommand_NoRegression_BlankCriterion_StillHidesRowsButLeavesModelUntouched()
    {
        // Blank/NonBlank criteria have no faithful <customFilter> representation in Excel's schema,
        // so the model is intentionally left alone (no misleading customFilters entry) -- but the
        // in-session hidden-row behavior (the pre-existing contract) must be unaffected.
        var (_, sheet, ctx, range) = MakeSheet();

        var criterion = new NonBlankFilterCriterion();
        var command = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);
        command.Apply(ctx).Success.Should().BeTrue();

        // All 3 data rows have non-blank numeric values, so none are hidden.
        sheet.FilterHiddenRows.Should().BeEmpty();
        sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
    }

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static void RemoveFilteredRowBits(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument document;
        using (var reader = new StreamReader(entry.Open()))
            document = XDocument.Parse(reader.ReadToEnd());

        var worksheetNs = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        foreach (var row in document.Root!.Element(worksheetNs + "sheetData")!.Elements(worksheetNs + "row"))
            row.Attribute("hidden")?.Remove();

        entry.Delete();
        var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var writer = new StreamWriter(replacement.Open());
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static void RemoveFilteredRowBitsAndUseInlineStrings(MemoryStream stream, IReadOnlyList<string> values)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument document;
            using (var reader = new StreamReader(entry.Open()))
                document = XDocument.Parse(reader.ReadToEnd());

            var worksheetNs = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            foreach (var row in document.Root!.Element(worksheetNs + "sheetData")!.Elements(worksheetNs + "row"))
            {
                row.Attribute("hidden")?.Remove();
                var rowNumber = int.Parse(row.Attribute("r")!.Value);
                var cell = row.Element(worksheetNs + "c");
                if (cell is null || rowNumber > values.Count)
                    continue;

                cell.Attribute("t")?.Remove();
                cell.Element(worksheetNs + "v")?.Remove();
                cell.Add(new XElement(
                    worksheetNs + "is",
                    new XElement(worksheetNs + "t", values[rowNumber - 1])));
                cell.SetAttributeValue("t", "inlineStr");
            }

            entry.Delete();
            var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(replacement.Open());
            document.Save(writer, SaveOptions.DisableFormatting);
        }
    }
}
