using FluentAssertions;

using FreeX.App.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="InsertTextBoxCommandFactory"/>: the placeholder fallback and that
/// the built command adds a text box to the sheet on apply. No running shell required.
/// </summary>
public sealed class InsertTextBoxCommandFactoryTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void Build_BlankText_UsesPlaceholder_AndAddsTextBoxOnApply()
    {
        var workbook = new Workbook("TB");
        var sheet = workbook.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 3, 2);

        var command = InsertTextBoxCommandFactory.Build(sheet.Id, anchor, text: "   ");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.TextBoxes.Should().ContainSingle();
        var box = sheet.TextBoxes[0];
        box.Anchor.Should().Be(anchor);
        box.Text.Should().Be(InsertTextBoxCommandFactory.Placeholder);
        box.Width.Should().Be(InsertTextBoxCommandFactory.DefaultWidth);
        box.Height.Should().Be(InsertTextBoxCommandFactory.DefaultHeight);
    }

    [Fact]
    public void Build_GivenText_IsTrimmedAndUsed()
    {
        var workbook = new Workbook("TB");
        var sheet = workbook.AddSheet("Sheet1");

        var command = InsertTextBoxCommandFactory.Build(
            sheet.Id, new CellAddress(sheet.Id, 1, 1), text: "  Hello  ");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        sheet.TextBoxes[0].Text.Should().Be("Hello");
    }
}
