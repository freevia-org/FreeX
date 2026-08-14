using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichTextEditSessionTests
{
    [Fact]
    public void ShapeSessionOwnsFormattingHyperlinkAndCommitDecision()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 7,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            TextBody = BodyWithText("Alpha"),
        };
        slide.Shapes.Add(shape);
        var start = InCanvasTextEditPlanner.BeginShapeEdit(
            0,
            presentation,
            slide,
            shape.Id,
            SlideTransformCore.Identity,
            40,
            20,
            InCanvasTextEditKind.RichText);
        var session = InCanvasRichTextEditSession.BeginShape(start);
        var selection = new InCanvasEditorTextSelection(0, 5);
        var hyperlink = new Hyperlink { Url = "https://example.com" };

        session.ToggleTextFormat(TableCellTextFormatKind.Bold, selection).Should().BeTrue();
        session.ApplyHyperlink(hyperlink, selection).Should().BeTrue();
        session.GetSelectedRunHyperlink(selection)!.Url.Should().Be(hyperlink.Url);

        var decision = session.Commit();

        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command!);
        shape.TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        shape.TextBody.Paragraphs[0].Runs[0].Hyperlink!.Url.Should().Be(hyperlink.Url);
    }

    [Fact]
    public void TableCellSessionOwnsNavigationAndCommitDecision()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = TwoCellTableShape();
        slide.Shapes.Add(shape);
        var start = TableCellEditPlanner.BeginEdit(
            0,
            slide,
            shape.Id,
            0,
            0,
            SlideTransformCore.Identity,
            30,
            18);
        var session = InCanvasRichTextEditSession.BeginTableCell(start);

        var navigation = session.PlanTableCellNavigation(
            slide,
            [shape.Id],
            (0, 0),
            TableCellNavigationDirection.Next);
        session.ReplacePlainText("Edited");
        var decision = session.Commit();

        navigation.IsReady.Should().BeTrue();
        navigation.Row.Should().Be(0);
        navigation.Col.Should().Be(1);
        decision.Outcome.Should().Be(InCanvasTextEditOutcome.Commit);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(decision.Command!);
        InCanvasTextEditPlanner.ExtractPlainText(
            shape.Table!.Rows[0].Cells[0].TextBody).Should().Be("Edited");
    }

    [Fact]
    public void NativeDocumentSynchronizationPreservesWholeBodySelectionSemantics()
    {
        var body = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "One" },
                        new Run { Text = "Two" },
                    },
                },
            },
        };
        var session = InCanvasRichTextEditSession.Create(body);

        session.ToggleTextFormat(TableCellTextFormatKind.Italic, selection: null)
            .Should().BeTrue();

        session.Body.Paragraphs[0].Runs.Should().OnlyContain(run => run.Italic);
    }

    [Fact]
    public void CancelCompletesTransactionWithoutACommand()
    {
        var session = InCanvasRichTextEditSession.Create(BodyWithText("Keep"));

        session.Cancel().Outcome.Should().Be(InCanvasTextEditOutcome.Canceled);
        session.Commit().Outcome.Should().Be(InCanvasTextEditOutcome.Unchanged);
        var mutate = () => session.SynchronizeBody(BodyWithText("Too late"));
        mutate.Should().Throw<InvalidOperationException>();
    }

    private static SlideShape TwoCellTableShape()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400);
        table.ColumnWidthsEmu.Add(914400);
        table.Rows.Add(new TableRow
        {
            HeightEmu = 457200,
            Cells =
            {
                new TableCell { TextBody = BodyWithText("First") },
                new TableCell { TextBody = BodyWithText("Second") },
            },
        });
        return new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Table,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 457200,
            Table = table,
        };
    }

    private static TextBody BodyWithText(string text) => new()
    {
        Paragraphs =
        {
            new Paragraph { Runs = { new Run { Text = text, FontFamily = "Aptos" } } },
        },
    };
}
