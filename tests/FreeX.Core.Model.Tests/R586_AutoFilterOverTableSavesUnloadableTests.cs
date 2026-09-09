using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r586: FreeX could SAVE a workbook it then refused to LOAD.
///
/// <para>Found by accident, which is worth recording: an r586 mutation-probe fixture put a worksheet
/// AutoFilter over the same range as a structured table. Every one of its 530 mutants "failed", and
/// the uniformity is what gave it away -- attributes that had loaded perfectly in r585 were suddenly
/// aborting. The baseline check said why: the UNMUTATED fixture would not load either, with
/// <c>InvalidOperationException: The range Sheet1!A1:D6 overlaps with the worksheet's autofilter</c>.
/// The probe was inert, and its 525 "findings" were an artefact.</para>
///
/// <para>But the artefact was itself a defect. <see cref="ToggleWorksheetAutoFilterCommand"/> guards
/// the range's validity and the sheet's protection, and nothing else: it will happily set a
/// worksheet AutoFilter over a range a structured table already occupies. The save then writes both,
/// and the reload throws -- so a user who selects a table and clicks Filter can produce a file this
/// application cannot reopen.</para>
///
/// <para>Excel does not have this state: clicking Filter inside a table toggles the TABLE's own
/// filter, and a worksheet AutoFilter overlapping a table cannot be created. Rejecting the command is
/// therefore the aligned minimum, and it is what the guard now does.</para>
/// </summary>
public sealed class R586_AutoFilterOverTableSavesUnloadableTests
{
    private static (Workbook Workbook, Sheet Sheet, GridRange TableRange) WorkbookWithTable()
    {
        var workbook = new Workbook("AutoFilterOverTable");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T1",
            DisplayName = "T1",
            Range = range,
        };
        for (var i = 1; i <= 4; i++)
            table.Columns.Add(new StructuredTableColumnModel(i, $"H{i}"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet, range);
    }

    [Fact]
    public void TogglingAWorksheetAutoFilterOverATablesRange_IsRejected()
    {
        var (workbook, sheet, range) = WorkbookWithTable();
        var ctx = new TestCommandContext(workbook);

        var outcome = new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse(
            "a worksheet AutoFilter over a table's range produces a workbook FreeX cannot reload");
        sheet.AutoFilter.Should().BeNull();
    }

    [Fact]
    public void AWorkbookFreeXSaves_MustAlwaysReload()
    {
        // The consequence assertion, independent of which guard stops it: whatever the command does,
        // what lands on disk has to be readable. This is the assertion that would have caught the
        // defect without knowing where it lived.
        var (workbook, sheet, range) = WorkbookWithTable();
        var ctx = new TestCommandContext(workbook);
        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reload = () => new XlsxFileAdapter().Load(saved);

        reload.Should().NotThrow("FreeX must never write a file it cannot itself open");
    }

    [Fact]
    public void AWorksheetAutoFilterAwayFromAnyTable_StillWorks()
    {
        // Non-vacuity: the guard must reject only the overlapping case. Without this, a guard that
        // refused every AutoFilter would pass both tests above.
        var (workbook, sheet, _) = WorkbookWithTable();
        for (uint r = 10; r <= 13; r++)
        for (uint c = 1; c <= 2; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 10 ? new TextValue($"F{c}") : new NumberValue(r * c));

        var ctx = new TestCommandContext(workbook);
        var away = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 13, 2));

        new ToggleWorksheetAutoFilterCommand(sheet.Id, away).Apply(ctx)
            .Success.Should().BeTrue("a filter that touches no table is ordinary and must still work");
        sheet.AutoFilter.Should().NotBeNull();
    }

    [Fact]
    public void CreatingATableOverAnExistingWorksheetAutoFilter_MustAlsoReload()
    {
        // The SAME unloadable state, reached from the other direction: filter first, table second.
        // CreateStructuredTableCommand already guards against another table, a merged region and a
        // spill range, and its own comment describes the merge rule as "enforced from the other
        // direction" -- so the missing AutoFilter half is a gap by this file's own standard.
        var workbook = new Workbook("TableOverAutoFilter");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var ctx = new TestCommandContext(workbook);

        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter.Should().NotBeNull("the filter is created before the table, on a bare range");

        new CreateStructuredTableCommand(sheet.Id, range, firstRowHasHeaders: true).Apply(ctx);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var reload = () => new XlsxFileAdapter().Load(saved);
        reload.Should().NotThrow("FreeX must never write a file it cannot itself open");
    }

    [Fact]
    public void UndoingTheTableRestoresTheWorksheetAutoFilterItCleared()
    {
        // Apply CLEARS the overlapping worksheet filter, so Revert has to put it back. Removing the
        // table without unwinding the state its creation changed is the "undo restores the value but
        // not the structure" class, and it is silent -- the user sees the table vanish and the filter
        // stay gone.
        var workbook = new Workbook("UndoRestoresFilter");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4));
        var ctx = new TestCommandContext(workbook);
        new ToggleWorksheetAutoFilterCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();
        var filterRangeBefore = sheet.AutoFilter!.Reference;

        var create = new CreateStructuredTableCommand(sheet.Id, range, firstRowHasHeaders: true);
        create.Apply(ctx).Success.Should().BeTrue();
        sheet.AutoFilter.Should().BeNull("the table takes over filtering, as it does in Excel");

        create.Revert(ctx);

        sheet.StructuredTables.Should().BeEmpty();
        sheet.AutoFilter.Should().NotBeNull("undo must restore the filter the table replaced");
        sheet.AutoFilter!.Reference.Should().Be(filterRangeBefore);
    }
}
