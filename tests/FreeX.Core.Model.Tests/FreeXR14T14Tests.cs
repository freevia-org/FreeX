using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-14 bucket T14: Remove Duplicates must compare text case-insensitively, matching
/// Excel's Remove Duplicates behavior (e.g. "MAY" and "may" are treated as duplicates).
/// </summary>
public sealed class FreeXR14T14Tests
{
    [Fact]
    public void R14_text_to_columns_dedup_3_RemoveDuplicates_TreatsMixedCaseTextAsDuplicate()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1="Apple", A2="apple" — Excel's Remove Duplicates is case-insensitive, so these
        // are duplicates and only one row should survive.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("apple"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();

        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Apple"));
        sheet.GetValue(2, 1).Should().BeOfType<BlankValue>();

        // Undo must restore both rows exactly as they were.
        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("Apple"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("apple"));
    }
}
