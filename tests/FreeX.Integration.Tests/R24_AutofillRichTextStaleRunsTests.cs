using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R24-rich-text-inline-1: AutofillCommand detects a trailing-number list
/// series (e.g. "Item1" -&gt; "Item2", "Item3", ...) and correctly computes each new cell's VALUE,
/// but was copying the source cell's rich-text runs (per-character formatting tied to the literal
/// text "Item1") verbatim onto every series-filled cell via CopyAnnotations, even though those
/// cells' text had changed to "Item2"/"Item3". A rich-text run's <c>Text</c> is the literal
/// substring it applies to (see CellTextRun.Text), so a stale run copied onto a differently-valued
/// cell describes text that cell no longer has -- the exact bug the Avalonia render path exposed by
/// displaying the stale run text instead of the cell's real value. Only the plain pattern-copy path
/// (no detected series -- the destination reproduces the source's exact value) may still carry rich
/// runs forward, matching FillCellsCommand's verbatim-copy behavior.
/// </summary>
public class R24_AutofillRichTextStaleRunsTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void FillListSeries_Down_DropsStaleRichTextRunsFromSeriesFilledCells()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1); // A1 = "Item1"
        sheet.SetCell(source, Cell.FromValue(new TextValue("Item1")));
        sheet.RichTextRuns[source] =
        [
            new CellTextRun("Item", null, null, null, null, null, null, null),
            new CellTextRun("1", true, null, null, null, null, null, null),
        ];

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1)); // A2:A4

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The series correctly advances the VALUE...
        sheet.GetValue(2, 1).Should().Be(new TextValue("Item2"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Item3"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Item4"));

        // ...but must not carry A1's stale "Item"+"1" rich-text runs onto cells whose text is now
        // "Item2"/"Item3"/"Item4" -- those runs describe text these cells no longer have.
        sheet.RichTextRuns.Should().NotContainKey(new CellAddress(sheet.Id, 2, 1));
        sheet.RichTextRuns.Should().NotContainKey(new CellAddress(sheet.Id, 3, 1));
        sheet.RichTextRuns.Should().NotContainKey(new CellAddress(sheet.Id, 4, 1));

        // The source cell itself is untouched.
        sheet.RichTextRuns.Should().ContainKey(source);
    }

    [Fact]
    public void FillPlainCopy_Down_StillCarriesRichTextRunsWhenValueIsUnchanged()
    {
        // Sanity check: when Autofill falls back to a plain pattern copy (no trend/list series
        // detected, e.g. a 2-cell alternating text pattern with no trailing number), the
        // destination reproduces the source's exact text, so its rich-text runs remain valid and
        // must still be carried forward -- this path is not part of the bug.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Alpha")));
        sheet.SetCell(a2, Cell.FromValue(new TextValue("Beta")));
        sheet.RichTextRuns[a1] = [new CellTextRun("Alpha", true, null, null, null, null, null, null)];
        sheet.RichTextRuns[a2] = [new CellTextRun("Beta", null, true, null, null, null, null, null)];

        var sourceRange = new GridRange(a1, a2);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1)); // A3:A4

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var a3 = new CellAddress(sheet.Id, 3, 1);
        var a4 = new CellAddress(sheet.Id, 4, 1);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Beta"));

        sheet.RichTextRuns.Should().ContainKey(a3);
        sheet.RichTextRuns[a3][0].Text.Should().Be("Alpha");
        sheet.RichTextRuns.Should().ContainKey(a4);
        sheet.RichTextRuns[a4][0].Text.Should().Be("Beta");
    }
}
