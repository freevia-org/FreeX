using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for the R98 finding: RemoveSheetCommand.Apply only rejected deletion when
/// ctx.Workbook.Sheets.Count &lt;= 1, never checking whether the sheet being deleted was the last
/// remaining VISIBLE sheet while other sheets survived hidden/very-hidden. Real Excel refuses to
/// delete a worksheet if doing so would leave zero visible sheets in the workbook, even when
/// hidden sheets still exist ("a workbook must contain at least one visible worksheet") -- the
/// exact invariant this codebase's own XlsxWorkbookMetadataWriter.ClampToVisibleSheetIndex assumes
/// on write, and the same invariant SetSheetHiddenCommand's "Cannot hide the only visible sheet."
/// guard already protects for the symmetric Hide operation. The fix mirrors that guard inside
/// RemoveSheetCommand.Apply so every caller (WPF ribbon, WPF tab context menu, Avalonia shell) is
/// protected at the single Core.Commands choke point instead of each UI call site needing to
/// duplicate the visibility check correctly.
/// </summary>
public sealed class R98_RemoveSheetLastVisibleGuardTests
{
    [Fact]
    public void RemoveSheetCommand_RejectsDeletingLastVisibleSheet_WhenHiddenSheetsRemain()
    {
        var workbook = new Workbook("RemoveSheetLastVisibleTest");
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        var ctx = new TestCommandContext(workbook);

        new SetSheetHiddenCommand(hidden.Id, hidden: true).Apply(ctx).Success.Should().BeTrue();

        var outcome = new RemoveSheetCommand(visible.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot delete the only visible sheet.");
        workbook.Sheets.Should().HaveCount(2, "the delete must be rejected before any mutation");
        workbook.Sheets.Should().Contain(s => s.Id == visible.Id);
        workbook.Sheets.Should().Contain(s => s.Id == hidden.Id && s.IsHidden);
    }

    [Fact]
    public void RemoveSheetCommand_RejectsDeletingLastVisibleSheet_WhenOtherSheetIsVeryHidden()
    {
        var workbook = new Workbook("RemoveSheetLastVisibleVeryHiddenTest");
        var visible = workbook.AddSheet("Visible");
        var veryHidden = workbook.AddSheet("VeryHidden");
        veryHidden.IsVeryHidden = true;
        var ctx = new TestCommandContext(workbook);

        var outcome = new RemoveSheetCommand(visible.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot delete the only visible sheet.");
        workbook.Sheets.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveSheetCommand_AllowsDeletingHiddenSheet_WhenAVisibleSheetRemains()
    {
        // Sibling/no-regression case: deleting a HIDDEN sheet is fine as long as another visible
        // sheet survives -- the guard must not over-reject deletes that don't touch visibility.
        var workbook = new Workbook("RemoveSheetKeepsVisibleTest");
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        var ctx = new TestCommandContext(workbook);

        new SetSheetHiddenCommand(hidden.Id, hidden: true).Apply(ctx).Success.Should().BeTrue();

        var outcome = new RemoveSheetCommand(hidden.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle();
        workbook.Sheets.Should().Contain(s => s.Id == visible.Id);
    }

    [Fact]
    public void RemoveSheetCommand_AllowsDeletingVisibleSheet_WhenAnotherVisibleSheetRemains()
    {
        // No-regression case for the ordinary two-visible-sheets delete path that must keep
        // working exactly as before.
        var workbook = new Workbook("RemoveSheetTwoVisibleTest");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var ctx = new TestCommandContext(workbook);

        var outcome = new RemoveSheetCommand(first.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        workbook.Sheets.Should().ContainSingle();
        workbook.Sheets.Should().Contain(s => s.Id == second.Id);
    }

    [Fact]
    public void RemoveSheetCommand_StillRejectsDeletingTheOnlySheet_WithOriginalMessage()
    {
        // No-regression case for the pre-existing "only sheet" guard (distinct message, checked
        // first) -- must remain intact alongside the new visibility guard.
        var workbook = new Workbook("RemoveSheetOnlySheetTest");
        var only = workbook.AddSheet("Only");
        var ctx = new TestCommandContext(workbook);

        var outcome = new RemoveSheetCommand(only.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Cannot delete the only sheet.");
        workbook.Sheets.Should().ContainSingle();
    }
}
