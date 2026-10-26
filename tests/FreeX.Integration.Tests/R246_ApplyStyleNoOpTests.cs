using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r246: ApplyStyleCommand, the single-sheet twin of the command r239 did. Pressing Bold on
/// already-bold text, or re-picking the style the gallery already highlights, is the gesture -- and
/// it is probably the single most frequent no-op in a spreadsheet editor.
/// </summary>
public sealed class R246_ApplyStyleNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("y"));
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

    [Fact]
    public void ApplyingBoldToAlreadyBoldText_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ApplyStyleCommand(sheet.Id, Range(sheet), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first application is a real edit");

        new ApplyStyleCommand(sheet.Id, Range(sheet), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ApplyingADifferentStyle_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        new ApplyStyleCommand(sheet.Id, Range(sheet), new StyleDiff(Bold: true)).Apply(ctx);

        new ApplyStyleCommand(sheet.Id, Range(sheet), new StyleDiff(Italic: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void OneUnstyledCellInTheRange_IsStillARealEdit()
    {
        // The batch argument again: the first cell already carries the style, the second does not,
        // so the range application is a real edit even though half of it changes nothing.
        var (sheet, ctx) = Fixture();
        var firstOnly = new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        new ApplyStyleCommand(sheet.Id, firstOnly, new StyleDiff(Bold: true)).Apply(ctx);

        new ApplyStyleCommand(sheet.Id, Range(sheet), new StyleDiff(Bold: true)).Apply(ctx)
            .IsNoOp.Should().BeFalse("the second cell has not been styled yet");
    }
}
