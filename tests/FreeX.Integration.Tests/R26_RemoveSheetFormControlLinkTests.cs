using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression coverage for R26-sheet-lifecycle-deep-1: RemoveSheetCommand.Apply never touched
/// FormControlModel.LinkedCell/ListFillRange for controls whose cross-sheet ref named the deleted
/// sheet, unlike the parallel PivotCacheModel.SourceSheetName / SlicerModel.SourceSheetName /
/// PictureModel.LinkedSourceSheetName / TimelineModel.SourceSheetName "clear on delete" blocks and
/// unlike RenameSheetCommand's own P81 block, which already rewrites these same two fields on
/// rename. Left unrewritten, a checkbox/spinner/list-box's stale "Sheet2!$D$3" LinkedCell would
/// silently reattach to an unrelated new sheet later created/renamed "Sheet2"
/// (FormControlInteractionService.TryResolveLinkedCell would resolve it). The fix mirrors the
/// existing X3 CF/DV delete-sheet pass: rewrite via FormulaRewriter + DeleteSheetOp, producing
/// "#REF!" text — the same #REF! outcome Excel itself gives for any other reference to a
/// deleted sheet — rather than leaving the stale sheet name in place.
/// </summary>
public sealed class R26_RemoveSheetFormControlLinkTests
{
    [Fact]
    public void RemoveSheetCommand_RewritesFormControlLinkedCellAndListFillRangeToRef_AndUndoRestores()
    {
        var workbook = new Workbook("RemoveSheetFormControlTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Name = "Check Box 1",
            LinkedCell = "Data!$D$3",
            ListFillRange = "Data!$A$1:$A$5",
        };
        report.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be("#REF!",
            because: "the linked cell's host sheet is gone, so it must go stale like any other " +
                     "reference to a deleted sheet, not keep saying the dead sheet's name forever");
        control.ListFillRange.Should().Be("#REF!");

        command.Revert(ctx);

        control.LinkedCell.Should().Be("Data!$D$3");
        control.ListFillRange.Should().Be("Data!$A$1:$A$5");
    }

    [Fact]
    public void RemoveSheetCommand_DoesNotReattachAfterFormerlyDeletedSheetNameIsReused()
    {
        // The concrete failure scenario from the finding: delete 'Data' (whose D3 was linked),
        // then add a brand-new sheet and rename it back to 'Data'. Before the fix, the control's
        // never-cleared "Data!$D$3" LinkedCell would silently resolve against the NEW sheet's D3.
        var workbook = new Workbook("RemoveSheetFormControlReattachTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "Data!$D$3",
        };
        report.FormControls.Add(control);

        new RemoveSheetCommand(data.Id).Apply(ctx: new TestCommandContext(workbook)).Success
            .Should().BeTrue();

        var recreated = workbook.AddSheet("DataRecreated");
        new RenameSheetCommand(recreated.Id, "Data").Apply(new TestCommandContext(workbook)).Success
            .Should().BeTrue();

        control.LinkedCell.Should().Be("#REF!",
            because: "a cleared/#REF!'d link must never silently reattach just because a later " +
                     "sheet happens to be renamed back to the deleted sheet's old name");
    }

    [Fact]
    public void RemoveSheetCommand_LeavesFormControlLinkedCellUntouched_WhenItReferencesASurvivingSheet()
    {
        // Sibling already-working case: a control's link naming a DIFFERENT, still-alive sheet
        // must not be disturbed by deleting an unrelated sheet.
        var workbook = new Workbook("RemoveSheetFormControlUnrelatedTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        var scratch = workbook.AddSheet("Scratch");

        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            LinkedCell = "Report!$D$3",
        };
        report.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(scratch.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be("Report!$D$3",
            because: "deleting an unrelated sheet must not touch a control's link to a surviving sheet");

        command.Revert(ctx);
        control.LinkedCell.Should().Be("Report!$D$3");
    }

    [Fact]
    public void RemoveSheetCommand_LeavesUnqualifiedFormControlLinkedCellUntouched()
    {
        // A LinkedCell with no sheet qualifier belongs to whichever sheet hosts the control — it
        // must not be corrupted by deleting a *different* sheet just because that sheet's own
        // deletion pass runs a rewrite over every control in the workbook.
        var workbook = new Workbook("RemoveSheetFormControlUnqualifiedTest");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");

        var control = new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            LinkedCell = "$D$3",
        };
        report.FormControls.Add(control);

        var ctx = new TestCommandContext(workbook);
        var command = new RemoveSheetCommand(data.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        control.LinkedCell.Should().Be("$D$3");
    }
}
