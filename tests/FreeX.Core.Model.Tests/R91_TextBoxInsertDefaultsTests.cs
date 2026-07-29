using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-commands-insert-object-5-1: Excel's Insert &gt; Text Box always creates a transparent,
/// borderless box (No Fill, No Line) until the user explicitly adds one. Before this fix,
/// <see cref="AddTextBoxCommand"/> relied on <see cref="TextBoxModel"/>'s always-bordered class
/// defaults (HasFill=true, no line-suppression field existed at all), so a freshly inserted text
/// box always got a solid white fill and a gray border it should never have had. These tests go
/// through the real insert entry point (AddTextBoxCommand.Apply), not a hand-built model.
/// </summary>
public sealed class R91_TextBoxInsertDefaultsTests
{
    [Fact]
    public void AddTextBoxCommand_NewInsert_HasNoFillAndNoLine_MatchesExcelDefault()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = new AddTextBoxCommand(sheet.Id, anchor, "Notes");

        command.Apply(ctx).Success.Should().BeTrue();

        var textBox = TextBoxModel.FindById(sheet.TextBoxes, command.TextBoxId);
        textBox.Should().NotBeNull();
        textBox!.HasFill.Should().BeFalse("Excel's Insert > Text Box defaults to No Fill");
        textBox.OutlineHasNoFill.Should().BeTrue("Excel's Insert > Text Box defaults to No Line");
    }

    /// <summary>No-regression sibling: an imported/loaded text box (never touched by
    /// AddTextBoxCommand) must keep its own authored fill/line -- the class's safe defaults
    /// (HasFill=true, OutlineHasNoFill=false) must be untouched by the new-insert fix.</summary>
    [Fact]
    public void TextBoxModel_BareConstruction_KeepsPriorAlwaysBorderedDefaults()
    {
        var textBox = new TextBoxModel();

        textBox.HasFill.Should().BeTrue("existing/imported text boxes that don't set HasFill explicitly must keep showing their fill");
        textBox.OutlineHasNoFill.Should().BeFalse("existing/imported text boxes that don't set this explicitly must keep showing their line");
    }
}
