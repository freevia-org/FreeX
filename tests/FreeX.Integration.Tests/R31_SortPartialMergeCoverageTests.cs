using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-commands-merge-autofit-deep-1: SortCommand's merged-cell "uniform" check only verified
/// mutual consistency among the merges that already overlapped the range, never that EVERY row of
/// the range was covered by an identically-sized merge (or none at all). A single merged header row
/// (e.g. A1:C1) sitting over otherwise-unmerged data rows (A2:C5) passed the check trivially — it
/// was compared only against itself — so the sort proceeded even though Excel refuses this whole
/// class of range ("This operation requires the merged cells to be identically sized"). The fix
/// requires the overlapping-merge count to equal the range's row count (on top of the existing
/// per-merge containment/shape checks), which — combined with the invariant that merged regions
/// never overlap one another — guarantees full, uniform per-row coverage.
/// </summary>
public sealed class R31_sort_partial_merge_coverage_Tests
{
    [Fact]
    public void Sort_OverMergedHeaderRowAboveUnmergedData_IsRejected()
    {
        // A1:C5 with only A1:C1 merged as a header ("Name | Age | City"); rows 2-5 are plain,
        // entirely unmerged cells. This is the exact real-world failure scenario: the single merge
        // trivially satisfied the old "uniform" check (compared only against itself), so the sort
        // proceeded even though Excel refuses to sort a range where not every row is uniformly
        // merged (or none is).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sid, 1, 2), new TextValue("Age"));
        sheet.SetCell(new CellAddress(sid, 1, 3), new TextValue("City"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 1, 3)));

        var keys = new[] { 50d, 10d, 30d, 20d };
        for (uint i = 0; i < 4; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(keys[i]));
        }

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 3)); // A1:C5
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "a merged header row over otherwise-unmerged data rows leaves the range non-uniformly merged, which Excel refuses");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");

        // The data must be untouched — no partial/silent sort should have happened.
        sheet.GetValue(2, 1).Should().Be(new NumberValue(50d));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(10d));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(30d));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(20d));
    }

    [Fact]
    public void Sort_OverFullyUniformlyMergedRange_StillSucceeds()
    {
        // Sibling case: every row of the range (B2:C6) carries an identical 1x2 merge — the
        // already-working "each record spans N cosmetic columns" layout — must still be allowed.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d, 20d, 40d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(keys[i]));
            sheet.SetCell(new CellAddress(sid, row, 3), new TextValue($"tag{keys[i]}"));
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, 2),
                new CellAddress(sid, row, 3)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 3));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue(
            "every row of the range is covered by an identically-sized merge, so the sort must still succeed");

        var expectedKeys = new[] { 10d, 20d, 30d, 40d, 50d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 2).Should().Be(new NumberValue(expectedKeys[i]));
            sheet.GetValue(row, 3).Should().Be(new TextValue($"tag{expectedKeys[i]}"));
        }
    }

    [Fact]
    public void Sort_OverRangeWithNoMerges_StillSucceeds()
    {
        // Sibling case: a plain range with no merges at all must sort normally.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d, 20d, 40d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(keys[i]));
        }

        var range = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 6, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("a range with no merges at all must sort normally");

        var expectedKeys = new[] { 10d, 20d, 30d, 40d, 50d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 1).Should().Be(new NumberValue(expectedKeys[i]));
        }
    }
}
