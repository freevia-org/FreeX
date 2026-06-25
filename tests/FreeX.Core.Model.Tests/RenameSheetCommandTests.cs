using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class RenameSheetCommandTests
{
    [Fact]
    public void RenameSheetCommand_RewritesNamedFormulaSheetReferenceAndUndoRestoresOriginal()
    {
        // A workbook with a sheet named "Data" and a named formula referencing it.
        var workbook = new Workbook("RenameSheetNamedFormulaTest");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["MyName"] = "Data!A1*2";
        var ctx = new TestCommandContext(workbook);

        var command = new RenameSheetCommand(sheet.Id, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Name.Should().Be("Sales");
        workbook.NamedFormulas["MyName"].Should().Be("Sales!A1*2");

        command.Revert(ctx);

        sheet.Name.Should().Be("Data");
        workbook.NamedFormulas["MyName"].Should().Be("Data!A1*2");
    }

    [Fact]
    public void RenameSheetCommand_LeavesUnrelatedNamedFormulasUntouched()
    {
        var workbook = new Workbook("RenameSheetUnrelatedNamedFormulaTest");
        var sheet = workbook.AddSheet("Data");
        workbook.NamedFormulas["Unrelated"] = "1+1";
        workbook.NamedFormulas["Referenced"] = "Data!B2";
        var ctx = new TestCommandContext(workbook);

        var command = new RenameSheetCommand(sheet.Id, "Sales");
        command.Apply(ctx).Success.Should().BeTrue();

        workbook.NamedFormulas["Unrelated"].Should().Be("1+1");
        workbook.NamedFormulas["Referenced"].Should().Be("Sales!B2");

        command.Revert(ctx);

        workbook.NamedFormulas["Unrelated"].Should().Be("1+1");
        workbook.NamedFormulas["Referenced"].Should().Be("Data!B2");
    }
}
