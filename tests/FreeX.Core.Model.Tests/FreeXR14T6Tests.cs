using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Focused regression tests for round-14 bucket T6 findings.
/// </summary>
public sealed class FreeXR14T6Tests
{
    // R14-text-to-columns-dedup-1: ConsolidationLabelPlanBuilder collapses row/column labels
    // case-insensitively (ConsolidationRules.AddUnique) but previously keyed the per-cell value
    // buckets with default (case-sensitive) tuple equality, so a source cell whose label differed
    // only in case landed in an orphan bucket that BuildWrites never read — silently dropping its
    // value from the aggregate. Excel merges "Apples" and "apples" into a single category and sums
    // both source values.
    [Fact]
    public void ConsolidateCommand_MergesRowLabelsCaseInsensitivelyIncludingBucketedValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var destination = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apples"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apples"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var command = new ConsolidateCommand(
            [source],
            destination,
            ConsolidateFunction.Sum,
            useLeftColumnLabels: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // Only one merged "Apples" row/label is produced ...
        sheet.GetValue(destination).Should().Be(new TextValue("Apples"));
        sheet.GetCell(new CellAddress(sheet.Id, 6, 1)).Should().BeNull("apples must merge into the same row as Apples, not spawn a second row");
        // ... and its value is the SUM of both differently-cased source rows (10 + 20 = 30), not
        // just the first-seen casing's own value (10).
        sheet.GetValue(new CellAddress(sheet.Id, 5, 2)).Should().Be(new NumberValue(30));
    }
}
