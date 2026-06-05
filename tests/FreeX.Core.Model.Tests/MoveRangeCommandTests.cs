using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class MoveRangeCommandTests
{
    [Fact]
    public void Apply_MovesCellsAndUndoRestoresSourceAndDestination()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("left")));
        var formula = Cell.FromFormula("A1&\"!\"");
        formula.Value = new TextValue("left!");
        sheet.SetCell(sourceEnd, formula);
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(sourceStart).Should().BeNull();
        sheet.GetCell(sourceEnd).Should().BeNull();
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("left"));
        var movedFormula = sheet.GetCell(new CellAddress(sheet.Id, 3, 4))!;
        movedFormula.FormulaText.Should().Be("C3&\"!\"");
        movedFormula.Value.Should().Be(new TextValue("left!"));

        command.Revert(context);

        sheet.GetCell(sourceStart)!.Value.Should().Be(new TextValue("left"));
        sheet.GetCell(sourceEnd)!.FormulaText.Should().Be("A1&\"!\"");
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("old"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 4)).Should().BeNull();
    }

    [Fact]
    public void Apply_KeepsMovedFormulaReferencesOutsideMovedRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var source = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(
            source,
            Cell.FromFormula("A1+$A$1+$A1+A$1+SUM(A1:A2)"));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(source, source),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(destination)!.FormulaText
            .Should()
            .Be("A1+$A$1+$A1+A$1+SUM(A1:A2)");
    }

    [Fact]
    public void Apply_RetargetsFormulaReferencesToCellsMovedWithSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var otherSheet = workbook.AddSheet("Other");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 3, 3);
        var outsideFormula = new CellAddress(sheet.Id, 5, 5);
        var crossSheetFormula = new CellAddress(otherSheet.Id, 1, 1);

        sheet.SetCell(sourceStart, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(
            sourceEnd,
            Cell.FromFormula("A1+$A$1+SUM(A1:B1)"));
        sheet.SetCell(
            outsideFormula,
            Cell.FromFormula("A1+$A$1+SUM(A1:B1)"));
        otherSheet.SetCell(
            crossSheetFormula,
            Cell.FromFormula("Sheet1!A1+$A$1"));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 3, 4))!.FormulaText
            .Should()
            .Be("C3+$C$3+SUM(C3:D3)");
        sheet.GetCell(outsideFormula)!.FormulaText
            .Should()
            .Be("C3+$C$3+SUM(C3:D3)");
        otherSheet.GetCell(crossSheetFormula)!.FormulaText
            .Should()
            .Be("Sheet1!C3+$A$1");

        command.Revert(context);

        sheet.GetCell(sourceEnd)!.FormulaText.Should().Be("A1+$A$1+SUM(A1:B1)");
        sheet.GetCell(outsideFormula)!.FormulaText.Should().Be("A1+$A$1+SUM(A1:B1)");
        otherSheet.GetCell(crossSheetFormula)!.FormulaText.Should().Be("Sheet1!A1+$A$1");
    }

    [Fact]
    public void Apply_InvalidatesMovedFormulaCachedAstEvenWhenTextDoesNotChange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 3);
        var formula = Cell.FromFormula("$B$2");
        formula.CachedAst = new object();
        sheet.SetCell(source, formula);

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(source, source),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        var movedFormula = sheet.GetCell(destination)!;
        movedFormula.FormulaText.Should().Be("$B$2");
        movedFormula.CachedAst.Should().BeNull();
    }

    [Fact]
    public void Apply_HandlesOverlappingMoveWithoutLosingSourcePayloads()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("A1")));
        sheet.SetCell(b1, Cell.FromValue(new TextValue("B1")));
        sheet.SetCell(a2, Cell.FromValue(new TextValue("A2")));
        sheet.SetCell(b2, Cell.FromValue(new TextValue("B2")));

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(a1, b2),
            b2);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(a1).Should().BeNull();
        sheet.GetCell(b1).Should().BeNull();
        sheet.GetCell(a2).Should().BeNull();
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("A1"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 3))!.Value.Should().Be(new TextValue("B1"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2))!.Value.Should().Be(new TextValue("A2"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3))!.Value.Should().Be(new TextValue("B2"));

        command.Revert(context);

        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("A1"));
        sheet.GetCell(b1)!.Value.Should().Be(new TextValue("B1"));
        sheet.GetCell(a2)!.Value.Should().Be(new TextValue("A2"));
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("B2"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 3)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 2)).Should().BeNull();
        sheet.GetCell(new CellAddress(sheet.Id, 3, 3)).Should().BeNull();
    }

    [Fact]
    public void Apply_MovesStyleOnlyCellsCommentsAndHyperlinksAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceStyleOnly = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 4, 4);
        var destinationStyleOnly = new CellAddress(sheet.Id, 4, 5);
        var sourceStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var oldDestinationStyle = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetCell(sourceStart, Cell.FromValue(new TextValue("link")));
        sheet.SetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col, sourceStyle);
        sheet.Hyperlinks[sourceStart] = "https://example.com";
        sheet.HyperlinkMetadata[sourceStart] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Example",
            "");
        sheet.Comments[sourceStart] = "move me";
        sheet.ThreadedComments[sourceStyleOnly] = new ThreadedComment("thread", "Quinn")
        {
            Replies = [new CommentReply("reply", "Codex")]
        };
        sheet.SetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col, oldDestinationStyle);
        sheet.Comments[destination] = "replace me";

        var command = new MoveRangeCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceStyleOnly),
            destination);

        command.Apply(context).Success.Should().BeTrue();

        sheet.GetCell(sourceStart).Should().BeNull();
        sheet.GetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col).Should().BeNull();
        sheet.Hyperlinks.Should().NotContainKey(sourceStart);
        sheet.Comments.Should().NotContainKey(sourceStart);
        sheet.GetCell(destination)!.Value.Should().Be(new TextValue("link"));
        sheet.Hyperlinks[destination].Should().Be("https://example.com");
        sheet.Comments[destination].Should().Be("move me");
        sheet.GetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col).Should().Be(sourceStyle);
        sheet.ThreadedComments[destinationStyleOnly].Replies.Should().Equal(new CommentReply("reply", "Codex"));

        command.Revert(context);

        sheet.GetCell(sourceStart)!.Value.Should().Be(new TextValue("link"));
        sheet.GetStyleOnly(sourceStyleOnly.Row, sourceStyleOnly.Col).Should().Be(sourceStyle);
        sheet.Hyperlinks[sourceStart].Should().Be("https://example.com");
        sheet.Comments[sourceStart].Should().Be("move me");
        sheet.ThreadedComments[sourceStyleOnly].Text.Should().Be("thread");
        sheet.GetCell(destination).Should().BeNull();
        sheet.GetStyleOnly(destinationStyleOnly.Row, destinationStyleOnly.Col).Should().Be(oldDestinationStyle);
        sheet.Comments[destination].Should().Be("replace me");
    }

    [Fact]
    public void Apply_RejectsOutOfBoundsDestination()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var outcome = new MoveRangeCommand(
                sheet.Id,
                source,
                new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol))
            .Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("outside");
    }
}
