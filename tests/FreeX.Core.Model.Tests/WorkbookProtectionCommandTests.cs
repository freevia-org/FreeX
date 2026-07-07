using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class WorkbookProtectionCommandTests
{
    [Fact]
    public void ProtectWorkbookCommand_ProtectsStructureAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new TestCommandContext(wb);

        var command = new ProtectWorkbookCommand("secret");

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeTrue();
        // N57: ProtectWorkbookCommand hashes the typed password at Apply-time instead of storing
        // it raw, so verify the stored value round-trips via the helper rather than equals plaintext.
        wb.StructureProtectionPassword.Should().NotBe("secret");
        ProtectionPasswordHelper.VerifyStoredPassword(wb.StructureProtectionPassword, "secret").Should().BeTrue();

        command.Revert(ctx);

        wb.IsStructureProtected.Should().BeFalse();
        wb.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void UnprotectWorkbookCommand_RequiresMatchingPassword()
    {
        var wb = new Workbook("test")
        {
            IsStructureProtected = true,
            StructureProtectionPassword = "secret"
        };
        var ctx = new TestCommandContext(wb);

        var wrong = new UnprotectWorkbookCommand("wrong").Apply(ctx);

        wrong.Success.Should().BeFalse();
        wb.IsStructureProtected.Should().BeTrue();
        wb.StructureProtectionPassword.Should().Be("secret");

        var correctCommand = new UnprotectWorkbookCommand("secret");
        var correct = correctCommand.Apply(ctx);

        correct.Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeFalse();
        wb.StructureProtectionPassword.Should().BeNull();

        correctCommand.Revert(ctx);

        wb.IsStructureProtected.Should().BeTrue();
        wb.StructureProtectionPassword.Should().Be("secret");
    }

    [Fact]
    public void AddSheetCommand_RejectsWhenWorkbookStructureProtected()
    {
        var wb = new Workbook("test");
        wb.IsStructureProtected = true;
        var ctx = new TestCommandContext(wb);

        var outcome = new AddSheetCommand("Blocked").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("workbook");
        wb.SheetCount.Should().Be(0);
    }

    [Fact]
    public void StructuralSheetCommands_RejectWhenWorkbookStructureProtected()
    {
        var wb = new Workbook("test");
        var s1 = wb.AddSheet("One");
        wb.AddSheet("Two");
        wb.IsStructureProtected = true;
        var ctx = new TestCommandContext(wb);

        new RenameSheetCommand(s1.Id, "Renamed").Apply(ctx).Success.Should().BeFalse();
        new RemoveSheetCommand(s1.Id).Apply(ctx).Success.Should().BeFalse();
        new MoveSheetCommand(0, 1).Apply(ctx).Success.Should().BeFalse();

        wb.GetSheetAt(0).Name.Should().Be("One");
        wb.SheetCount.Should().Be(2);
    }
}
