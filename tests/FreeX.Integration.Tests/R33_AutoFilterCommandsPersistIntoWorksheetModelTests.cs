using System.IO;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R33-commands-autofilter-slicer-1: interactively-applied worksheet-level AutoFilter criteria
/// (value list, Top 10/Bottom N, Above/Below Average) used to mutate ONLY session-only state
/// (sheet.ActiveValueFilterColumns / sheet.ColumnFilterOwnedRows), never sheet.AutoFilter.FilterColumns
/// -- the model XlsxWorksheetAutoFilterXmlMapper.Save actually serializes into the worksheet's
/// &lt;autoFilter&gt;/&lt;filterColumn&gt; XML. On save+reload, the criterion vanished and every value
/// showed as checked again. FilterCommand/TopBottomFilterCommand/AverageFilterCommand now also mirror
/// the criterion into sheet.AutoFilter.FilterColumns (via WorksheetAutoFilterColumnSync) whenever
/// _range matches a plain worksheet-level AutoFilter range (as opposed to a structured table's own
/// filter, which FilterCommand.ApplyToStructuredTableIfMatched already handled -- see H18).
/// </summary>
public sealed class R33_AutoFilterCommandsPersistIntoWorksheetModelTests
{
    [Fact]
    public void FilterCommand_ValueFilter_WorksheetAutoFilter_PersistsAcrossSaveReload()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["North"]);
        filter.Apply(ctx).Success.Should().BeTrue();

        // Bug: previously sheet.AutoFilter.FilterColumns stayed empty even though the value filter
        // was visibly applied (rows hidden) -- the model XlsxWorksheetAutoFilterXmlMapper serializes
        // never saw the criterion.
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var column = sheet.AutoFilter.FilterColumns[0];
        column.ColumnId.Should().Be(0);
        column.Values.Should().BeEquivalentTo(["North"]);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedSheet = reloaded.Sheets[0];

        reloadedSheet.AutoFilter.Should().NotBeNull();
        reloadedSheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var reloadedColumn = reloadedSheet.AutoFilter.FilterColumns[0];
        reloadedColumn.ColumnId.Should().Be(0);
        reloadedColumn.Values.Should().BeEquivalentTo(["North"]);

        // Undo must restore the AutoFilter model exactly as it was (empty), not just the hidden rows.
        filter.Revert(ctx);
        sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
        sheet.AutoFilter.Reference.Should().Be(range.ToString());
    }

    [Fact]
    public void FilterCommand_ValueFilter_StructuredTableRange_StillOnlyWritesTableFilterColumns()
    {
        // Sibling/already-working case (H18): a structured table's own filter dropdown must keep
        // mirroring into table.FilterColumns exactly as before, and must NOT spuriously create/touch
        // a worksheet-level sheet.AutoFilter (tables carry their own <autoFilter> inside the table
        // part, not the worksheet's).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Region") }
        };
        sheet.StructuredTables.Add(table);

        var filter = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["North"]);
        filter.Apply(ctx).Success.Should().BeTrue();

        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        sheet.AutoFilter.Should().BeNull();
    }

    [Fact]
    public void TopBottomFilterCommand_WorksheetAutoFilter_Top10PersistsAndClears()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(80));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var topBottom = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        topBottom.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        var top10 = sheet.AutoFilter.FilterColumns[0].Top10;
        top10.Should().NotBeNull();
        top10!.Top.Should().BeTrue();
        top10.Percent.Should().BeFalse();
        top10.Value.Should().Be(2);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedTop10 = reloaded.Sheets[0].AutoFilter!.FilterColumns.Single().Top10;
        reloadedTop10.Should().NotBeNull();
        reloadedTop10!.Top.Should().BeTrue();
        reloadedTop10.Value.Should().Be(2);

        // Clearing (count: 0) must remove the filterColumn from the model too.
        var clear = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 0, top: true);
        clear.Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter.FilterColumns.Should().BeEmpty();
    }

    [Fact]
    public void AverageFilterCommand_WorksheetAutoFilter_DynamicFilterPersists()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var above = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);
        above.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        sheet.AutoFilter.FilterColumns[0].DynamicFilter?.Type.Should().Be("aboveAverage");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        reloaded.Sheets[0].AutoFilter!.FilterColumns.Single().DynamicFilter?.Type.Should().Be("aboveAverage");
    }
}
