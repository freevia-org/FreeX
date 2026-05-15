using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class WorkbookProtectionCommandTests
{
    [Fact]
    public void ProtectWorkbookCommand_ProtectsStructureAndUndoRestores()
    {
        var wb = new Workbook("test");
        var ctx = new SimpleCtx(wb);

        var command = new ProtectWorkbookCommand("secret");

        command.Apply(ctx).Success.Should().BeTrue();
        wb.IsStructureProtected.Should().BeTrue();
        wb.StructureProtectionPassword.Should().Be("secret");

        command.Revert(ctx);

        wb.IsStructureProtected.Should().BeFalse();
        wb.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void AddSheetCommand_RejectsWhenWorkbookStructureProtected()
    {
        var wb = new Workbook("test");
        wb.IsStructureProtected = true;
        var ctx = new SimpleCtx(wb);

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
        var ctx = new SimpleCtx(wb);

        new RenameSheetCommand(s1.Id, "Renamed").Apply(ctx).Success.Should().BeFalse();
        new RemoveSheetCommand(s1.Id).Apply(ctx).Success.Should().BeFalse();
        new MoveSheetCommand(0, 1).Apply(ctx).Success.Should().BeFalse();

        wb.GetSheetAt(0).Name.Should().Be("One");
        wb.SheetCount.Should().Be(2);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
