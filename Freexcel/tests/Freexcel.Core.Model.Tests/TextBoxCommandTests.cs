using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class TextBoxCommandTests
{
    [Fact]
    public void AddTextBoxCommand_AddsTextBoxAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = new AddTextBoxCommand(sheet.Id, anchor, "Notes", 180, 80);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.TextBoxes.Should().ContainSingle();
        sheet.TextBoxes[0].Anchor.Should().Be(anchor);
        sheet.TextBoxes[0].Text.Should().Be("Notes");
        sheet.TextBoxes[0].Width.Should().Be(180);
        sheet.TextBoxes[0].Height.Should().Be(80);

        command.Revert(ctx);

        sheet.TextBoxes.Should().BeEmpty();
    }

    [Fact]
    public void AddTextBoxCommand_RejectsTextBoxOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);

        var command = new AddTextBoxCommand(sheet1.Id, new CellAddress(sheet2.Id, 1, 1), "Notes");

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.TextBoxes.Should().BeEmpty();
    }

    [Fact]
    public void AddTextBoxCommand_RejectsInvalidInitialSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        new AddTextBoxCommand(sheet.Id, anchor, "Notes", double.NegativeInfinity, 80)
            .Apply(ctx).Success.Should().BeFalse();
        new AddTextBoxCommand(sheet.Id, anchor, "Notes", 180, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new AddTextBoxCommand(sheet.Id, anchor, "Notes", 180, 0)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.TextBoxes.Should().BeEmpty();
    }

    [Fact]
    public void AddTextBoxCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var outcome = new AddTextBoxCommand(sheet.Id, anchor, "Notes").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.TextBoxes.Should().BeEmpty();
    }

    [Fact]
    public void AddTextBoxCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var outcome = new AddTextBoxCommand(sheet.Id, anchor, "Notes").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.TextBoxes.Should().ContainSingle();
    }

    [Fact]
    public void ResizeTextBoxCommand_SetsSizeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Width = 180, Height = 80 };
        sheet.TextBoxes.Add(textBox);

        var command = new ResizeTextBoxCommand(sheet.Id, textBox.Id, 220, 120);

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.Width.Should().Be(220);
        textBox.Height.Should().Be(120);

        command.Revert(ctx);

        textBox.Width.Should().Be(180);
        textBox.Height.Should().Be(80);
    }

    [Fact]
    public void RotateTextBoxCommand_SetsRotationAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", RotationDegrees = 15 };
        sheet.TextBoxes.Add(textBox);

        var command = new RotateTextBoxCommand(sheet.Id, textBox.Id, -90);

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.RotationDegrees.Should().Be(270);

        command.Revert(ctx);

        textBox.RotationDegrees.Should().Be(15);
    }

    [Fact]
    public void TextBoxFormattingCommands_RejectInvalidNumbers()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Width = 180, Height = 80 };
        sheet.TextBoxes.Add(textBox);

        new ResizeTextBoxCommand(sheet.Id, textBox.Id, 220, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new RotateTextBoxCommand(sheet.Id, textBox.Id, double.NegativeInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        textBox.Width.Should().Be(180);
        textBox.Height.Should().Be(80);
        textBox.RotationDegrees.Should().Be(0);
    }

    [Fact]
    public void ResizeTextBoxCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 1), Text = "Note", Width = 180, Height = 80 };
        sheet.TextBoxes.Add(textBox);
        sheet.IsProtected = true;

        var outcome = new ResizeTextBoxCommand(sheet.Id, textBox.Id, 220, 120).Apply(ctx);

        outcome.Success.Should().BeFalse();
        textBox.Width.Should().Be(180);
        textBox.Height.Should().Be(80);
    }

    [Fact]
    public void SetTextBoxColorsCommand_SetsColorsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Note",
            FillColor = new CellColor(10, 20, 30),
            OutlineColor = new CellColor(40, 50, 60),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25)
        };
        sheet.TextBoxes.Add(textBox);

        var command = new SetTextBoxColorsCommand(
            sheet.Id,
            textBox.Id,
            new CellColor(240, 250, 255),
            new CellColor(70, 80, 90));

        command.Apply(ctx).Success.Should().BeTrue();
        textBox.FillColor.Should().Be(new CellColor(240, 250, 255));
        textBox.OutlineColor.Should().Be(new CellColor(70, 80, 90));
        textBox.FillThemeColor.Should().BeNull();
        textBox.OutlineThemeColor.Should().BeNull();

        command.Revert(ctx);

        textBox.FillColor.Should().Be(new CellColor(10, 20, 30));
        textBox.OutlineColor.Should().Be(new CellColor(40, 50, 60));
        textBox.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        textBox.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
    }

    [Fact]
    public void SetTextBoxColorsCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Text = "Note",
            FillColor = new CellColor(10, 20, 30)
        };
        sheet.TextBoxes.Add(textBox);
        sheet.IsProtected = true;

        var outcome = new SetTextBoxColorsCommand(
            sheet.Id,
            textBox.Id,
            new CellColor(240, 250, 255),
            null).Apply(ctx);

        outcome.Success.Should().BeFalse();
        textBox.FillColor.Should().Be(new CellColor(10, 20, 30));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
