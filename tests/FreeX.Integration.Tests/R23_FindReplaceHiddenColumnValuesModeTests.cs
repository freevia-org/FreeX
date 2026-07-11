using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R23-find-replace-2: Excel's "Look in: Values" Find/Replace does not surface hidden data --
/// it already skips manually hidden ROWS (<see cref="Sheet.IsRowEffectivelyHidden"/>), but was
/// missing the symmetric hidden-COLUMN check (<see cref="Sheet.IsColEffectivelyHidden"/>), so
/// Replace All would silently rewrite cells in a manually hidden or group-collapsed column that
/// the user could never have reached via Find Next.
/// </summary>
public class R23_FindReplaceHiddenColumnValuesModeTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void Find_ValuesMode_SkipsManuallyHiddenColumn()
    {
        var (wb, sheet, _) = Setup();
        var b5 = new CellAddress(sheet.Id, 5, 2); // column B, hidden
        sheet.SetCell(b5, new TextValue("Q3 Budget"));
        sheet.HiddenCols.Add(2);

        var results = FindReplaceService.Find(wb, "Q3", new FindOptions(LookIn: FindLookIn.Values));

        results.Should().BeEmpty();
    }

    [Fact]
    public void Find_ValuesMode_SkipsGroupCollapsedColumn()
    {
        var (wb, sheet, _) = Setup();
        var c3 = new CellAddress(sheet.Id, 3, 3); // column C, collapsed via outline group
        sheet.SetCell(c3, new TextValue("needle"));
        sheet.GroupHiddenCols.Add(3);

        var results = FindReplaceService.Find(wb, "needle", new FindOptions(LookIn: FindLookIn.Values));

        results.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAll_ValuesMode_DoesNotRewriteHiddenColumnCell()
    {
        var (wb, sheet, commandBus) = Setup();
        var b5 = new CellAddress(sheet.Id, 5, 2); // hidden column B
        var a5 = new CellAddress(sheet.Id, 5, 1); // visible column A
        sheet.SetCell(b5, new TextValue("Q3 Budget"));
        sheet.SetCell(a5, new TextValue("Q3 Actuals"));
        sheet.HiddenCols.Add(2);

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "Q3",
            "Q4",
            new FindOptions(LookIn: FindLookIn.Values));

        count.Should().Be(1);
        sheet.GetCell(a5)!.Value.Should().Be(new TextValue("Q4 Actuals"));
        sheet.GetCell(b5)!.Value.Should().Be(new TextValue("Q3 Budget"));
    }

    [Fact]
    public void Find_FormulasMode_StillMatchesHiddenColumn()
    {
        // Only Values-mode is scoped by hidden-row/column visibility; Formulas mode must be
        // unaffected (mirrors the existing hidden-row behavior for this mode).
        var (wb, sheet, _) = Setup();
        var b1 = new CellAddress(sheet.Id, 1, 2); // hidden column B
        sheet.SetFormula(b1, "SUM(A1:A5)");
        sheet.HiddenCols.Add(2);

        var results = FindReplaceService.Find(wb, "SUM", new FindOptions(LookIn: FindLookIn.Formulas));

        results.Should().HaveCount(1);
        results[0].Address.Should().Be(b1);
    }
}
