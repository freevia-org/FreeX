using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R15-protection-ui-edge-1: ProtectWorkbookCommand must honor the caller's structure-protection
/// choice instead of unconditionally setting IsStructureProtected = true, so that unchecking
/// "Structure" in the Protect Workbook dialog actually leaves the workbook's structure unprotected.
/// </summary>
public sealed class R15_protection_Tests
{
    [Fact]
    public void ProtectWorkbookCommand_StructureFalse_LeavesStructureUnprotectedAndAllowsStructuralEdits()
    {
        var wb = new Workbook("test");
        wb.AddSheet("One");
        var ctx = new TestCommandContext(wb);

        var command = new ProtectWorkbookCommand("secret", structureProtected: false);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeFalse();

        // A structural edit (add a sheet, rename a sheet) must be allowed when structure protection
        // was explicitly declined, even though a password was supplied.
        var addOutcome = new AddSheetCommand("Two").Apply(ctx);
        addOutcome.Success.Should().BeTrue();
        wb.SheetCount.Should().Be(2);

        var renameOutcome = new RenameSheetCommand(wb.GetSheetAt(0).Id, "Renamed").Apply(ctx);
        renameOutcome.Success.Should().BeTrue();
        wb.GetSheetAt(0).Name.Should().Be("Renamed");
    }

    [Fact]
    public void ProtectWorkbookCommand_StructureTrue_ProtectsStructureAndRejectsStructuralEdits()
    {
        var wb = new Workbook("test");
        var s1 = wb.AddSheet("One");
        var ctx = new TestCommandContext(wb);

        var command = new ProtectWorkbookCommand("secret", structureProtected: true);

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeTrue();

        var addOutcome = new AddSheetCommand("Two").Apply(ctx);
        addOutcome.Success.Should().BeFalse();
        wb.SheetCount.Should().Be(1);

        var renameOutcome = new RenameSheetCommand(s1.Id, "Renamed").Apply(ctx);
        renameOutcome.Success.Should().BeFalse();
        wb.GetSheetAt(0).Name.Should().Be("One");

        command.Revert(ctx);
        wb.IsStructureProtected.Should().BeFalse();
    }

    [Fact]
    public void ProtectWorkbookCommand_DefaultConstructor_StillProtectsStructure()
    {
        // Backward-compat: callers that don't pass the new parameter (e.g. parity capture, the
        // review-workflow planner) must keep the pre-fix "always protect structure" behavior.
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);

        var command = new ProtectWorkbookCommand("secret");

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeTrue();
    }
}
