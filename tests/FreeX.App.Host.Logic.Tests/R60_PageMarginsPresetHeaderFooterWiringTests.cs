using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R60-meta-1: PageLayoutRibbonActionPlanner.PlanMarginsPreset computes the Excel-correct
/// HeaderMargin/FooterMargin for a Margins preset (Wide -> 0.5"/0.5", Normal/Narrow -> 0.3"/0.3"),
/// but nothing ever applied those computed values to the sheet -- SetPageMarginsCommand only ever
/// touched Sheet.PageMargins. Clicking Ribbon &gt; Page Layout &gt; Margins &gt; Wide had zero effect
/// on Sheet.HeaderMargin/FooterMargin. The fix threads optional headerMargin/footerMargin through
/// SetPageMarginsCommand and PageLayoutRibbonCommandPlanner.BuildMarginsCommand, and the WPF host's
/// ApplyPageMarginsPreset (MainWindow.PageLayout.cs) now forwards plan.HeaderMargin/plan.FooterMargin.
/// </summary>
public sealed class R60_PageMarginsPresetHeaderFooterWiringTests
{
    [Fact]
    public void SetPageMarginsCommand_WithHeaderFooterMargins_AppliesAndRevertsBothSheetFields()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;

        var margins = new WorksheetPageMargins(1, 1, 1, 1);
        var command = new SetPageMarginsCommand(sheet.Id, margins, headerMargin: 0.5, footerMargin: 0.5);

        // Pre-fix, SetPageMarginsCommand had no headerMargin/footerMargin parameters at all, so this
        // scenario (a Margins-preset click actually moving the header/footer distance) was impossible
        // to express, let alone pass -- this is the concrete bug the WPF ribbon caller hit.
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PageMargins.Should().Be(margins);
        sheet.HeaderMargin.Should().Be(0.5, "Wide preset must move the header distance to 0.5in like real Excel");
        sheet.FooterMargin.Should().Be(0.5, "Wide preset must move the footer distance to 0.5in like real Excel");

        command.Revert(new TestCommandContext(workbook));
        sheet.HeaderMargin.Should().Be(0.3, "undo must restore the prior header margin");
        sheet.FooterMargin.Should().Be(0.3, "undo must restore the prior footer margin");
    }

    [Fact]
    public void SetPageMarginsCommand_WithoutHeaderFooterMargins_LeavesHeaderFooterUntouched()
    {
        // Sibling no-regression case: every pre-existing caller of SetPageMarginsCommand (Page Setup
        // dialog custom margins, print-preview drag handles, etc.) never passed header/footer values
        // and must keep behaving exactly as before -- only Sheet.PageMargins changes.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var originalMargins = sheet.PageMargins;
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;

        var margins = new WorksheetPageMargins(2, 2, 2, 2);
        var command = new SetPageMarginsCommand(sheet.Id, margins);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PageMargins.Should().Be(margins);
        sheet.HeaderMargin.Should().Be(0.3);
        sheet.FooterMargin.Should().Be(0.3);

        command.Revert(new TestCommandContext(workbook));
        sheet.PageMargins.Should().Be(originalMargins, "undo must restore the prior page margins");
        sheet.HeaderMargin.Should().Be(0.3, "header margin was never touched by this command, so undo must leave it alone");
        sheet.FooterMargin.Should().Be(0.3, "footer margin was never touched by this command, so undo must leave it alone");
    }

    [Fact]
    public void PlanMarginsPreset_WidePreset_BuildsCommandThatMovesHeaderFooterMarginsToHalfInch()
    {
        // End-to-end through the exact planner + command-builder pair the WPF ribbon click uses
        // (PageLayoutRibbonActionPlanner.PlanMarginsPreset -> PageLayoutRibbonCommandPlanner.BuildMarginsCommand).
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;

        var plan = PageLayoutRibbonActionPlanner.PlanMarginsPreset(PageLayoutMarginPreset.Wide);
        var command = PageLayoutRibbonCommandPlanner.BuildMarginsCommand(
            sheet.Id, plan.Value, plan.HeaderMargin, plan.FooterMargin);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.HeaderMargin.Should().Be(0.5, "Wide preset must actually reach the Sheet, matching Excel");
        sheet.FooterMargin.Should().Be(0.5);
    }
}
