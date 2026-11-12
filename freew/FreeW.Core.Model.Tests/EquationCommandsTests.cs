namespace FreeW.Core.Model.Tests;

public class EquationCommandsTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static Equation Linear(string text, bool display = false, MathJustification justification = MathJustification.CenterGroup)
    {
        var equation = EquationLinearFormat.Parse(text);
        equation.IsDisplayMath = display;
        equation.Justification = justification;
        return equation;
    }

    private static (TextDocument Document, Paragraph Paragraph, Equation Equation) InlineDocument()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("intro"));
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Right },
            StyleId = "Body"
        };
        var equation = Linear("a/b");
        paragraph.Runs.Add(new Run("Before "));
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(new Run(" after"));
        paragraph.BookmarkNames.Add("whole");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("k0", BookmarkBoundaryKind.Start, 0, "lead"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("k0", BookmarkBoundaryKind.End, 1));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("k1", BookmarkBoundaryKind.Start, 2, "tail"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("k1", BookmarkBoundaryKind.End, 3));
        paragraph.SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage);
        document.Blocks.Add(paragraph);
        document.Blocks.Add(new Paragraph("outro"));
        return (document, paragraph, equation);
    }

    private static List<object> Snapshot(TextDocument document)
    {
        var snapshot = new List<object>();
        foreach (var block in document.Blocks)
        {
            snapshot.Add(block);
            if (block is Paragraph paragraph)
            {
                snapshot.Add(paragraph.Formatting);
                snapshot.Add(paragraph.Runs.Count);
                snapshot.AddRange(paragraph.Runs);
                snapshot.AddRange(paragraph.Runs.Select(run => (object?)run.Equation ?? "-"));
                snapshot.AddRange(paragraph.Runs.Select(run => run.Text));
                snapshot.AddRange(paragraph.BookmarkBoundaries);
                snapshot.Add((object?)paragraph.SectionBreak ?? "-");
            }
        }

        return snapshot;
    }

    [Fact]
    public void Apply_ReplacesTheEquation_InPlace()
    {
        var (document, paragraph, equation) = InlineDocument();
        var replacement = Linear("x^2");
        var command = new ReplaceEquationCommand(equation, replacement);

        command.HasEffect(new Context(document)).Should().BeTrue();
        command.Apply(new Context(document));

        document.Blocks.Should().HaveCount(3);
        var edited = document.Blocks[1].Should().BeOfType<Paragraph>().Subject;
        edited.Should().NotBeSameAs(paragraph, "the original paragraph is kept untouched for undo");
        edited.Runs.Select(run => run.Text).Should().Equal("Before ", "x^2", " after");
        edited.Runs[1].Equation.Should().BeSameAs(replacement);
        edited.Runs[0].Should().BeSameAs(paragraph.Runs[0]);
        edited.Formatting.Alignment.Should().Be(TextAlignment.Right);
        edited.StyleId.Should().Be("Body");
        edited.BookmarkBoundaries.Should().Equal(paragraph.BookmarkBoundaries);
        edited.SectionBreak.Should().BeSameAs(paragraph.SectionBreak);
        paragraph.Runs[1].Equation.Should().BeSameAs(equation);
    }

    [Fact]
    public void Revert_RestoresExactlyTheOriginalDocument_AndRedoRepeatsTheEdit()
    {
        var (document, _, equation) = InlineDocument();
        var before = Snapshot(document);
        var command = new ReplaceEquationCommand(equation, Linear("x^2", display: true));
        var context = new Context(document);

        command.Apply(context);
        var applied = document.Blocks.Select(block => ((Paragraph)block).PlainText).ToList();
        command.Revert(context);

        Snapshot(document).Should().Equal(before, "undo must put back the very same objects");

        command.Apply(context);
        document.Blocks.Select(block => ((Paragraph)block).PlainText).Should().Equal(applied);
        command.Revert(context);
        Snapshot(document).Should().Equal(before);
    }

    [Fact]
    public void ChangeToDisplay_GivesTheEquationItsOwnParagraph()
    {
        var (document, paragraph, equation) = InlineDocument();
        var replacement = Linear("a/b", display: true);

        new ReplaceEquationCommand(equation, replacement).Apply(new Context(document));

        document.Blocks.Should().HaveCount(5);
        var before = (Paragraph)document.Blocks[1];
        var display = (Paragraph)document.Blocks[2];
        var after = (Paragraph)document.Blocks[3];

        before.PlainText.Should().Be("Before ");
        display.Runs.Should().ContainSingle().Which.Equation.Should().BeSameAs(replacement);
        after.PlainText.Should().Be(" after");
        EquationEditPlanner.IsDisplayOnly(display).Should().BeTrue();

        // The equation's line follows the equation's justification, not the paragraph's old alignment.
        display.Formatting.Alignment.Should().Be(TextAlignment.Left);
        before.Formatting.Alignment.Should().Be(TextAlignment.Right);
        after.Formatting.Alignment.Should().Be(TextAlignment.Right);
        new[] { before, display, after }.Should().OnlyContain(part => part.StyleId == "Body");

        // Start-of-paragraph properties stay first, end-of-paragraph properties stay last.
        before.BookmarkNames.Should().Equal("whole");
        display.BookmarkNames.Should().BeEmpty();
        after.BookmarkNames.Should().BeEmpty();
        before.SectionBreak.Should().BeNull();
        display.SectionBreak.Should().BeNull();
        after.SectionBreak.Should().BeSameAs(paragraph.SectionBreak);

        // Every bookmark boundary lands in exactly one part, at the matching position.
        before.BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("k0", BookmarkBoundaryKind.Start, 0, "lead"),
            new BookmarkBoundary("k0", BookmarkBoundaryKind.End, 1));
        display.BookmarkBoundaries.Should().Equal(new BookmarkBoundary("k1", BookmarkBoundaryKind.Start, 1, "tail"));
        after.BookmarkBoundaries.Should().Equal(new BookmarkBoundary("k1", BookmarkBoundaryKind.End, 1));
    }

    [Fact]
    public void ChangeToDisplay_SplitsOnlyOnTheSideThatHasContent()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        var equation = Linear("a/b");
        paragraph.Runs.Add(new Run(string.Empty));
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(new Run("tail"));
        document.Blocks.Add(paragraph);

        new ReplaceEquationCommand(equation, Linear("a/b", display: true)).Apply(new Context(document));

        document.Blocks.Should().HaveCount(2);
        ((Paragraph)document.Blocks[0]).Runs.Should().HaveCount(2);
        EquationEditPlanner.IsDisplayOnly((Paragraph)document.Blocks[0]).Should().BeTrue();
        ((Paragraph)document.Blocks[1]).PlainText.Should().Be("tail");
    }

    [Fact]
    public void ChangeToInline_KeepsTheParagraph()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        var equation = Linear("a/b", display: true);
        paragraph.Runs.Add(Run.FromEquation(equation));
        document.Blocks.Add(paragraph);
        var replacement = Linear("a/b");

        new ReplaceEquationCommand(equation, replacement).Apply(new Context(document));

        var edited = document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Subject;
        edited.Runs.Single().Equation.Should().BeSameAs(replacement);
        edited.Formatting.Alignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public void ChangingJustification_ClearsTheDisplayParagraphsOwnAlignment_ButEditingTheMathDoesNot()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Right } };
        var equation = Linear("a/b", display: true);
        paragraph.Runs.Add(Run.FromEquation(equation));
        document.Blocks.Add(paragraph);
        var context = new Context(document);

        var mathOnly = Linear("a/c", display: true);
        new ReplaceEquationCommand(equation, mathOnly).Apply(context);
        ((Paragraph)document.Blocks[0]).Formatting.Alignment.Should().Be(TextAlignment.Right);

        var justified = Linear("a/c", display: true, MathJustification.Left);
        new ReplaceEquationCommand(mathOnly, justified).Apply(context);
        ((Paragraph)document.Blocks[0]).Formatting.Alignment.Should().Be(TextAlignment.Left);
        ((Paragraph)document.Blocks[0]).Runs.Single().Equation!.Justification.Should().Be(MathJustification.Left);
    }

    [Fact]
    public void ReapplyingTheSameInstance_IsolatesAFreshlyInsertedDisplayEquation_Once()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph("text ");
        var equation = Linear("a/b", display: true);
        paragraph.Runs.Add(Run.FromEquation(equation));
        document.Blocks.Add(paragraph);
        var context = new Context(document);

        var command = new ReplaceEquationCommand(equation, equation);
        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);

        document.Blocks.Should().HaveCount(2);
        new ReplaceEquationCommand(equation, equation).HasEffect(context).Should().BeFalse("the equation already has its own line");

        command.Revert(context);
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(paragraph);
    }

    [Fact]
    public void TargetsEquationsInsideTableCells_AndNestedTables()
    {
        var document = new TextDocument();
        var outer = new Table();
        var row = new TableRow();
        var cell = new TableCell("cell");
        var nested = new Table();
        var nestedRow = new TableRow();
        var nestedCell = new TableCell(string.Empty);
        var cellEquation = Linear("x_1");
        var nestedEquation = Linear("y_2");
        cell.Paragraphs[0].Runs.Add(Run.FromEquation(cellEquation));
        nestedCell.Paragraphs.Add(new Paragraph());
        nestedCell.Paragraphs[^1].Runs.Add(Run.FromEquation(nestedEquation));
        nestedRow.Cells.Add(nestedCell);
        nested.Rows.Add(nestedRow);
        cell.NestedTables.Add(nested);
        row.Cells.Add(cell);
        outer.Rows.Add(row);
        document.Blocks.Add(outer);
        var context = new Context(document);

        var cellReplacement = Linear("x_2");
        var nestedReplacement = Linear("y_3");
        var first = new ReplaceEquationCommand(cellEquation, cellReplacement);
        var second = new ReplaceEquationCommand(nestedEquation, nestedReplacement);
        first.Apply(context);
        second.Apply(context);

        EquationLocator.Find(document, cellReplacement).Should().NotBeNull();
        EquationLocator.Find(document, nestedReplacement).Should().NotBeNull();
        EquationLocator.Find(document, cellEquation).Should().BeNull();

        second.Revert(context);
        first.Revert(context);
        EquationLocator.Find(document, cellEquation)!.Container.Should().BeSameAs(cell.Paragraphs);
        EquationLocator.Find(document, nestedEquation)!.Container.Should().BeSameAs(nestedCell.Paragraphs);
    }

    [Fact]
    public void EquationThatIsNoLongerInTheDocument_IsANoOp()
    {
        var (document, _, _) = InlineDocument();
        var before = Snapshot(document);
        var command = new ReplaceEquationCommand(Linear("gone"), Linear("x"));
        var context = new Context(document);

        command.HasEffect(context).Should().BeFalse();
        command.Apply(context);
        command.Revert(context);

        Snapshot(document).Should().Equal(before);
    }

    [Fact]
    public void Revert_LeavesTheDocumentAlone_WhenItsParagraphsWereReplacedSince()
    {
        var (document, _, equation) = InlineDocument();
        var command = new ReplaceEquationCommand(equation, Linear("x"));
        var context = new Context(document);
        command.Apply(context);
        var foreign = new Paragraph("someone else's edit");
        document.Blocks[1] = foreign;

        command.Revert(context);

        document.Blocks[1].Should().BeSameAs(foreign);
        document.Blocks.Should().HaveCount(3);
    }
}
