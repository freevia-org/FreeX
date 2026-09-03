using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r243: SetPageSetupCommand -- twenty fields written, twenty snapshots kept, and every pair now
/// compared. Page Setup pre-fills every one of its controls from the sheet, so pressing OK without
/// editing rewrites all twenty with what they already hold.
/// <para>
/// Twenty clauses is past the point where hand-transcription is trustworthy, which is why this one
/// waited for the r237 participation contract: that contract fails if a <c>_previous*</c> field this
/// class declares is not mentioned in the decision, so a missed field is caught rather than shipped.
/// Proved here by deleting a clause and watching the contract name it.
/// </para>
/// </summary>
public sealed class R243_PageSetupNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static SetPageSetupCommand Command(
        Sheet sheet,
        WorksheetPageOrientation orientation = WorksheetPageOrientation.Portrait,
        bool printGridlines = false) =>
        new(
            sheet.Id,
            orientation,
            sheet.PaperSize,
            sheet.PageMargins,
            printGridlines,
            sheet.PrintHeadings,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns);

    [Fact]
    public void PressingOkWithoutEditingAnything_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        Command(sheet, sheet.PageOrientation, sheet.PrintGridlines).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingTheOrientation_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;

        var outcome = Command(sheet, WorksheetPageOrientation.Landscape, sheet.PrintGridlines)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void ChangingOnlyAPrintCheckbox_DoesNotReportNoOp()
    {
        // One of the twenty, chosen because it is nowhere near the first: a comparison that stopped
        // early, or transcribed nineteen of twenty, would report no-op here.
        var (sheet, ctx) = Fixture();
        sheet.PrintGridlines.Should().BeFalse();

        var outcome = Command(sheet, sheet.PageOrientation, printGridlines: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
    }
}
