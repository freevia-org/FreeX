using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FormControlCommandTests
{
    [Theory]
    [InlineData(FormControlKind.CheckBox, 1, 3)]
    [InlineData(FormControlKind.OptionButton, 1, 3)]
    [InlineData(FormControlKind.Button, 1, 3)]
    [InlineData(FormControlKind.DropDown, 1, 3)]
    [InlineData(FormControlKind.ListBox, 5, 3)]
    [InlineData(FormControlKind.Spinner, 2, 1)]
    [InlineData(FormControlKind.ScrollBar, 1, 4)]
    public void AddFormControlCommand_InsertsSupportedControlAtAnchorWithBoundedDefaultSize(
        FormControlKind kind,
        uint rows,
        uint columns)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 5, 7);
        var command = new AddFormControlCommand(sheet.Id, anchor, kind);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var control = sheet.FormControls.Should().ContainSingle().Subject;
        control.Kind.Should().Be(kind);
        control.Anchor.Should().Be(new GridRange(
            anchor,
            new CellAddress(sheet.Id, anchor.Row + rows - 1, anchor.Col + columns - 1)));
        if (kind is FormControlKind.Spinner or FormControlKind.ScrollBar)
        {
            control.Min.Should().Be(0);
            control.Max.Should().Be(100);
            control.Increment.Should().Be(1);
        }
        else
        {
            control.Min.Should().BeNull();
            control.Max.Should().BeNull();
        }
    }

    [Fact]
    public void AddFormControlCommand_DefaultAnchorClampsAtWorksheetEdge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol);

        new AddFormControlCommand(sheet.Id, anchor, FormControlKind.ListBox)
            .Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.FormControls.Should().ContainSingle().Which.Anchor.Should().Be(new GridRange(anchor, anchor));
    }

    [Fact]
    public void AddFormControlCommand_CommandBusUndoRedoRestoresSameInsertedControl()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new TestCommandContext(workbook));
        var command = new AddFormControlCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 3, 4),
            FormControlKind.ScrollBar);

        bus.Execute(workbook.Id, command).Success.Should().BeTrue();
        var inserted = sheet.FormControls.Should().ContainSingle().Subject;

        bus.Undo(workbook.Id).Success.Should().BeTrue();
        sheet.FormControls.Should().BeEmpty();

        bus.Redo(workbook.Id).Success.Should().BeTrue();
        sheet.FormControls.Should().ContainSingle().Which.Should().BeSameAs(inserted);
    }

    [Theory]
    [InlineData(FormControlKind.Unknown)]
    [InlineData(FormControlKind.GroupBox)]
    [InlineData(FormControlKind.Label)]
    public void AddFormControlCommand_RejectsUnsupportedOrDisplayOnlyKinds(FormControlKind kind)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var command = new AddFormControlCommand(sheet.Id, new CellAddress(sheet.Id, 1, 1), kind);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeFalse();
        sheet.FormControls.Should().BeEmpty();
    }

    [Fact]
    public void AddFormControlCommand_RespectsEditObjectsProtection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var command = new AddFormControlCommand(sheet.Id, new CellAddress(sheet.Id, 1, 1), FormControlKind.CheckBox);

        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.FormControls.Should().BeEmpty();
    }
}
