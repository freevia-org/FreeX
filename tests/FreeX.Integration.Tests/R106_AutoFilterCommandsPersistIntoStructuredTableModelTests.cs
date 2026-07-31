using System.IO;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R106-commands-autofilter-table-sync-1: TopBottomFilterCommand and FilterConditionCommand (Top 10
/// and Custom Filter criteria) used to mutate ONLY session-only state (sheet.FilterHiddenRows /
/// sheet.ColumnFilterOwnedRows) and, for a plain worksheet-level AutoFilter range,
/// sheet.AutoFilter.FilterColumns via WorksheetAutoFilterColumnSync (see R33) -- but
/// WorksheetAutoFilterColumnSync is a no-op whenever <c>_range</c> is a structured table's own
/// Range, since a table carries its own &lt;autoFilter&gt; inside the table part rather than a
/// worksheet-level one. Applying either criterion kind from a Table's own header dropdown hid/showed
/// rows correctly in the live session, but the table's StructuredTableFilterColumnModel list was
/// never updated, so the criterion was silently dropped from the table's &lt;autoFilter&gt; XML the
/// moment the workbook was saved and reopened -- mirrors the value-list case (finding H18) that
/// FilterCommand.ApplyToStructuredTableIfMatched already covers, tested in R33's
/// FilterCommand_ValueFilter_StructuredTableRange_StillOnlyWritesTableFilterColumns.
///
/// AverageFilterCommand (Above/Below Average) is intentionally NOT fixed here: see its own
/// R106-commands-autofilter-table-sync-1 comment for why a table-level &lt;dynamicFilter&gt;
/// passthrough was attempted and reverted (it crashes ClosedXML on real reload) -- that gap is
/// covered by <see cref="AverageFilterCommand_StructuredTableRange_StillNotPersisted_NoCrash"/> below,
/// which documents (rather than hides) the still-open half of this finding.
/// </summary>
public sealed class R106_AutoFilterCommandsPersistIntoStructuredTableModelTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, GridRange Range) SetUpNumericTable()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
            HasAutoFilter = true,
            Columns = { new StructuredTableColumnModel(1, "Score") }
        };
        sheet.StructuredTables.Add(table);

        return (wb, sheet, ctx, range);
    }

    [Fact]
    public void TopBottomFilterCommand_StructuredTableRange_PersistsAcrossSaveReload()
    {
        var (wb, sheet, ctx, range) = SetUpNumericTable();

        var topBottom = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        topBottom.Apply(ctx).Success.Should().BeTrue();

        // Bug: previously sheet.StructuredTables[0].FilterColumns stayed empty even though the Top-N
        // criterion was visibly applied (rows hidden) -- nothing ever wrote it into the table model,
        // and no worksheet-level sheet.AutoFilter should be spuriously created either (mirrors H18).
        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        var column = sheet.StructuredTables[0].FilterColumns[0];
        column.ColumnId.Should().Be(0);
        column.NativeFilterXmls.Should().ContainSingle(xml => xml.Contains("top10"));

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedTable = reloaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        reloadedTable.FilterColumns[0].NativeFilterXmls.Should().ContainSingle(xml => xml.Contains("top10"));

        // Undo must restore the table's FilterColumns exactly as it was (empty).
        topBottom.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();

        // Clearing (count: 0) must remove the filterColumn from the table model too.
        topBottom.Apply(ctx).Success.Should().BeTrue();
        var clear = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 0, top: true);
        clear.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    /// <summary>
    /// Documents the still-open half of this finding: applying Above/Below Average from a Table's
    /// own header dropdown still doesn't persist into the table's FilterColumns model (unlike
    /// TopBottomFilterCommand/FilterConditionCommand, fixed above) -- but critically, it also must
    /// NOT throw or corrupt the table, and save/reload of the surrounding workbook must keep working.
    /// See AverageFilterCommand's own R106-commands-autofilter-table-sync-1 comment for why: a raw
    /// &lt;dynamicFilter&gt; table passthrough was attempted and reverted because it crashes
    /// ClosedXML.Excel.XLWorkbook.LoadAutoFilterColumns on real reload (a pre-existing Core.IO gap --
    /// XlsxClosedXmlLoadPackageSanitizer's dynamicFilter strip only scans xl/worksheets/*.xml, not
    /// xl/tables/*.xml). This test pins the current (safe, if incomplete) behavior so a future fix
    /// attempt trips it instead of silently reintroducing the crash.
    /// </summary>
    [Fact]
    public void AverageFilterCommand_StructuredTableRange_StillNotPersisted_NoCrash()
    {
        var (wb, sheet, ctx, range) = SetUpNumericTable();

        var above = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);
        above.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedTable = reloaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().BeEmpty();

        above.Revert(ctx);
    }

    [Fact]
    public void FilterConditionCommand_StructuredTableRange_PersistsAcrossSaveReload()
    {
        var (wb, sheet, ctx, range) = SetUpNumericTable();

        var criterion = new NumberGreaterThanFilterCriterion(60);
        var condition = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, criterion);
        condition.Apply(ctx).Success.Should().BeTrue();

        sheet.AutoFilter.Should().BeNull();
        sheet.StructuredTables[0].FilterColumns.Should().ContainSingle();
        var column = sheet.StructuredTables[0].FilterColumns[0];
        column.CustomFilters.Should().ContainSingle();
        column.CustomFilters[0].Operator.Should().Be("greaterThan");
        column.CustomFilters[0].Value.Should().Be("60");

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedTable = reloaded.Sheets[0].StructuredTables.Single();
        reloadedTable.FilterColumns.Should().ContainSingle();
        reloadedTable.FilterColumns[0].CustomFilters.Should().ContainSingle();
        reloadedTable.FilterColumns[0].CustomFilters[0].Operator.Should().Be("greaterThan");
        reloadedTable.FilterColumns[0].CustomFilters[0].Value.Should().Be("60");

        condition.Revert(ctx);
        sheet.StructuredTables[0].FilterColumns.Should().BeEmpty();
    }

    /// <summary>
    /// No-regression sibling: the SAME three commands applied against a plain worksheet-level
    /// AutoFilter range (no structured table at all) must keep behaving exactly as R33 already
    /// covers -- sheet.StructuredTables stays empty and nothing throws when there is no table to
    /// match against.
    /// </summary>
    [Fact]
    public void TopBottomAndAverageAndCondition_WorksheetAutoFilterRange_NoStructuredTableTouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var topBottom = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 1, top: true);
        topBottom.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        sheet.AutoFilter!.FilterColumns.Should().ContainSingle();
        topBottom.Revert(ctx);

        var above = new AverageFilterCommand(sheet.Id, range, filterColOffset: 0, above: true);
        above.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        above.Revert(ctx);

        var condition = new FilterConditionCommand(sheet.Id, range, filterColOffset: 0, new NumberGreaterThanFilterCriterion(10));
        condition.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        condition.Revert(ctx);
    }
}
