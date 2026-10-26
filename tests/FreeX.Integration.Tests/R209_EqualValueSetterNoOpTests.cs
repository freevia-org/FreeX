using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r209: the first eight of r208's 35 confirmed FreeX no-op-capable commands. All are equal-value
/// setters -- the Alt Text pane pre-populates the current description, Page Setup pre-populates the
/// current orientation and margins, the theme gallery highlights the current theme -- so closing the
/// dialog without editing re-writes what is already there.
/// <para>
/// FreeX signals this with <c>CommandOutcome(true, IsNoOp: true)</c> rather than the
/// <c>HasEffect</c> override its sister apps use; <c>CommandBus</c> then skips the push, and skipping
/// matters because <c>UndoRedoStack.Push</c> clears redo.
/// </para>
/// </summary>
public sealed class R209_EqualValueSetterNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingAPicturesOwnAltText_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AltText = "A quarterly revenue chart",
        };
        sheet.Pictures.Add(picture);

        new SetPictureAltTextCommand(sheet.Id, picture.Id, picture.AltText).Apply(ctx)
            .IsNoOp.Should().BeTrue("the Alt Text pane pre-populates the current description");
    }

    [Fact]
    public void ChangingAPicturesAltText_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            AltText = "Old",
        };
        sheet.Pictures.Add(picture);

        var outcome = new SetPictureAltTextCommand(sheet.Id, picture.Id, "New").Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
        picture.AltText.Should().Be("New");
    }

    [Fact]
    public void ClearingAnAlreadyEmptyAltText_ReportsNoOp()
    {
        // Normalisation matters: null, empty and whitespace all mean "no description", so clearing
        // an already-empty one must not count as a change.
        var (_, sheet, ctx) = Fixture();
        var picture = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1), AltText = null };
        sheet.Pictures.Add(picture);

        new SetPictureAltTextCommand(sheet.Id, picture.Id, "   ").Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingTheSheetsOwnPageOrientation_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;

        new SetPageOrientationCommand(sheet.Id, WorksheetPageOrientation.Landscape).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingThePageOrientation_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;

        var outcome = new SetPageOrientationCommand(sheet.Id, WorksheetPageOrientation.Landscape).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void ReApplyingTheSheetsOwnPaperSize_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.PaperSize = WorksheetPaperSize.A4;

        new SetPaperSizeCommand(sheet.Id, WorksheetPaperSize.A4).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReApplyingTheWorkbooksOwnTheme_ReportsNoOp()
    {
        var (workbook, _, ctx) = Fixture();

        new SetWorkbookThemeCommand(workbook.Theme).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ApplyingADifferentTheme_DoesNotReportNoOp()
    {
        var (_, _, ctx) = Fixture();
        var different = ctx.Workbook.Theme with { Name = "Another" };

        new SetWorkbookThemeCommand(different).Apply(ctx).IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ReApplyingTheSheetsOwnPrintArea_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var area = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        sheet.PrintArea = area;

        new SetPrintAreaCommand(sheet.Id, area).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingThePrintArea_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));

        new SetPrintAreaCommand(
                sheet.Id,
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 3)))
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }
}
