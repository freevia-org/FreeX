using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R34-commands-paste-special-3-2: Paste Special &gt; Comments and &gt; Validation never tiled their
/// copied content across a destination selection that is a whole multiple of the copied source range,
/// unlike every other Paste Special content mode (Values/Formulas/Formats/All), which repeats the
/// source across the whole selected destination. Copying a 1x1 cell's comment/validation rule and
/// pasting onto a 1x3 destination selection only filled the first (top-left) cell; the rest of the
/// selection silently got nothing. PasteCommentsCommand/PasteDataValidationCommand now accept an
/// overload taking the full destination GridRange (not just its anchor) and tile across it, mirroring
/// PasteCommandFactory.CreateInternalPasteCommand's shouldTileDestinationRange/period-based tiling.
/// </summary>
public sealed class R34_PasteCommentsValidationTileTests
{
    [Fact]
    public void PasteCommentsCommand_TilesSingleCellCommentAcrossLargerDestinationSelection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Comments[source] = "Note";

        // Select C1:C3 (a 1x3 destination, an exact multiple of the 1x1 source) and Paste Special >
        // Comments.
        var destinationStart = new CellAddress(sheet.Id, 1, 3);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 3, 3));

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destinationRange,
            transpose: false);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Real Excel tiles the comment into every cell of the selection, like every other Paste
        // Special content kind.
        sheet.Comments[new CellAddress(sheet.Id, 1, 3)].Should().Be("Note");
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)].Should().Be("Note");
        sheet.Comments[new CellAddress(sheet.Id, 3, 3)].Should().Be("Note");

        command.Revert(ctx);

        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 1, 3)).Should().BeFalse();
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 2, 3)).Should().BeFalse();
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 3, 3)).Should().BeFalse();
    }

    /// <summary>
    /// Regression guard for the sibling case the fix must not break: the existing single-cell
    /// (1x1-source -&gt; 1x1-destination anchor) constructor overload keeps behaving exactly as before.
    /// </summary>
    [Fact]
    public void PasteCommentsCommand_SingleCellAnchorOverload_StillPastesJustOneCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.Comments[source] = "Note";
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[new CellAddress(sheet.Id, 1, 3)].Should().Be("Note");
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 2, 3)).Should().BeFalse();
        sheet.Comments.ContainsKey(new CellAddress(sheet.Id, 3, 3)).Should().BeFalse();
    }

    [Fact]
    public void PasteDataValidationCommand_TilesSingleCellRuleAcrossLargerDestinationSelection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });

        // Select C1:C3 (a 1x3 destination, an exact multiple of the 1x1 source) and Paste Special >
        // Validation.
        var destinationStart = new CellAddress(sheet.Id, 1, 3);
        var destinationRange = new GridRange(destinationStart, new CellAddress(sheet.Id, 3, 3));

        var command = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            destinationRange,
            transpose: false);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Real Excel tiles the rule into every cell of the selection, one rule per destination cell,
        // like every other Paste Special content kind.
        sheet.DataValidations.Should().HaveCount(4); // original + 3 pasted tiles
        for (uint row = 1; row <= 3; row++)
        {
            sheet.DataValidations.Should().ContainSingle(rule =>
                rule.AppliesTo == new GridRange(
                    new CellAddress(sheet.Id, row, 3),
                    new CellAddress(sheet.Id, row, 3)) &&
                rule.Formula1 == "1" &&
                rule.Formula2 == "10");
        }

        command.Revert(ctx);

        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == new GridRange(source, source));
    }

    /// <summary>
    /// Regression guard for the sibling case the fix must not break: the existing single-cell
    /// (1x1-source -&gt; 1x1-destination anchor) constructor overload keeps behaving exactly as before.
    /// </summary>
    [Fact]
    public void PasteDataValidationCommand_SingleCellAnchorOverload_StillPastesJustOneRule()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });
        var destination = new CellAddress(sheet.Id, 1, 3); // C1

        var command = new PasteDataValidationCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().HaveCount(2); // original + one pasted rule, no tiling
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == new GridRange(destination, destination));
    }
}
