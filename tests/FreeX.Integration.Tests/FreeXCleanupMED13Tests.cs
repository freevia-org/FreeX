using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED13 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED13Tests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    /// <summary>
    /// P66: Excel's "Look in: Values" Find does not return matches inside hidden or
    /// filter-hidden rows. Find must skip a match sitting in a filter-hidden row so its address
    /// is never surfaced to Find Next / Replace All.
    /// </summary>
    [Fact]
    public void Find_ValuesMode_SkipsMatchInFilterHiddenRow()
    {
        var (wb, sheet, _) = Setup();
        var visible = new CellAddress(sheet.Id, 1, 1);
        var hidden = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(visible, new TextValue("draft visible"));
        sheet.SetCell(hidden, new TextValue("draft hidden"));
        sheet.FilterHiddenRows.Add(hidden.Row);

        var results = FindReplaceService.Find(wb, "draft");

        results.Should().ContainSingle();
        results[0].Address.Should().Be(visible);
    }

    /// <summary>
    /// P66: Replace All in Values mode (the default LookIn) must not rewrite cells in
    /// hidden/filter-hidden rows -- those rows are invisible and Excel's Find would never have
    /// surfaced them for editing either.
    /// </summary>
    [Fact]
    public void ReplaceAll_ValuesMode_DoesNotRewriteFilterHiddenRow()
    {
        var (wb, sheet, commandBus) = Setup();
        var visible = new CellAddress(sheet.Id, 1, 1);
        var hidden = new CellAddress(sheet.Id, 50, 1);
        sheet.SetCell(visible, new TextValue("draft visible"));
        sheet.SetCell(hidden, new TextValue("draft hidden"));
        sheet.FilterHiddenRows.Add(hidden.Row);

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "draft", "final");

        count.Should().Be(1);
        sheet.GetCell(visible)!.Value.Should().Be(new TextValue("final visible"));
        sheet.GetCell(hidden)!.Value.Should().Be(new TextValue("draft hidden"), because: "Excel's Values-mode Find cannot see filter-hidden rows");
    }
}
