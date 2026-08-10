namespace FreeW.Core.Model.Tests;

public sealed class DocumentModelClonerTests
{
    private const string DateXml = "2026-08-10T09:00:00Z";

    [Fact]
    public void Preserve_ClonesNestedTablesFieldsAndAllRevisionMetadata()
    {
        var source = CreateRevisionedNestedTable();

        var clone = DocumentModelCloner.CloneBlock(source, RevisionClonePolicy.Preserve)
            .Should().BeOfType<Table>().Subject;

        clone.Should().NotBeSameAs(source);
        clone.Rows[0].Should().NotBeSameAs(source.Rows[0]);
        clone.Rows[0].RowRevision.Should().Be(RevisionKind.Inserted);
        clone.Rows[0].RowRevisionAuthor.Should().Be("Alice");

        var paragraph = clone.Rows[0].Cells[0].Paragraphs.Single();
        paragraph.MarkRevision.Should().Be(RevisionKind.Deleted);
        paragraph.MarkRevisionAuthor.Should().Be("Alice");
        paragraph.ParagraphFormatRevision.Should().Be(
            new ParagraphFormatRevision(ParagraphFormatting.Default, "Alice", DateXml));

        var fieldRun = paragraph.Runs.Single();
        fieldRun.ComplexField!.Instruction.Should().Be(" REF Total ");
        fieldRun.Revision.Should().Be(RevisionKind.Inserted);
        fieldRun.RevisionAuthor.Should().Be("Alice");
        fieldRun.MoveRevisionId.Should().Be(17);
        fieldRun.FormatRevision.Should().Be(new FormatRevision(RunFormatting.Default, "Alice", DateXml));

        var sourceNested = source.Rows[0].Cells[0].NestedTables.Single();
        var nested = clone.Rows[0].Cells[0].NestedTables.Single();
        nested.Should().NotBeSameAs(sourceNested);
        nested.Rows[0].RowRevision.Should().Be(RevisionKind.Deleted);
        nested.Rows[0].Cells[0].Paragraphs.Single().Runs.Single().TableFormula
            .Should().Be(new TableFormulaField("=SUM(ABOVE)"));

        fieldRun.Text = "changed";
        source.Rows[0].Cells[0].Paragraphs.Single().Runs.Single().Text.Should().Be("42");
    }

    [Fact]
    public void Strip_ClonesNestedTablesAndFieldsWithoutAnyIncomingRevisionMetadata()
    {
        var source = CreateRevisionedNestedTable();

        var clone = DocumentModelCloner.CloneBlock(source, RevisionClonePolicy.Strip)
            .Should().BeOfType<Table>().Subject;

        var outerRow = clone.Rows[0];
        outerRow.RowRevision.Should().Be(RevisionKind.None);
        outerRow.RowRevisionAuthor.Should().BeNull();
        outerRow.RowRevisionDateXml.Should().BeNull();

        var paragraph = outerRow.Cells[0].Paragraphs.Single();
        paragraph.MarkRevision.Should().Be(RevisionKind.None);
        paragraph.MarkRevisionAuthor.Should().BeNull();
        paragraph.MarkRevisionDateXml.Should().BeNull();
        paragraph.ParagraphFormatRevision.Should().BeNull();

        var fieldRun = paragraph.Runs.Single();
        fieldRun.ComplexField!.Instruction.Should().Be(" REF Total ");
        fieldRun.Revision.Should().Be(RevisionKind.None);
        fieldRun.RevisionAuthor.Should().BeNull();
        fieldRun.RevisionDateXml.Should().BeNull();
        fieldRun.MoveRevisionId.Should().BeNull();
        fieldRun.FormatRevision.Should().BeNull();

        var nested = outerRow.Cells[0].NestedTables.Single();
        nested.Rows[0].RowRevision.Should().Be(RevisionKind.None);
        var nestedParagraph = nested.Rows[0].Cells[0].Paragraphs.Single();
        nestedParagraph.MarkRevision.Should().Be(RevisionKind.None);
        var formulaRun = nestedParagraph.Runs.Single();
        formulaRun.TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)"));
        formulaRun.Revision.Should().Be(RevisionKind.None);
        formulaRun.FormatRevision.Should().BeNull();
    }

    [Fact]
    public void CloneParagraphTextRange_DeepClonesSelectedRunPayloadsAndPreservesRevisions()
    {
        var source = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
            StyleId = "Caption",
            MarkRevision = RevisionKind.Inserted,
            MarkRevisionAuthor = "Alice",
            MarkRevisionDateXml = DateXml,
            ParagraphFormatRevision = new ParagraphFormatRevision(
                ParagraphFormatting.Default,
                "Alice",
                DateXml)
        };
        source.Runs.Add(new Run("alpha", RunFormatting.Default with { Italic = true })
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = DateXml
        });
        var shapeRun = Run.FromShape(Shape.TextBoxWith("nested", 120, 40));
        shapeRun.Revision = RevisionKind.Inserted;
        shapeRun.RevisionAuthor = "Carol";
        shapeRun.RevisionDateXml = DateXml;
        shapeRun.MoveRevisionId = 27;
        source.Runs.Add(shapeRun);

        var clone = DocumentModelCloner.CloneParagraphTextRange(
            source,
            2,
            source.PlainText.Length,
            RevisionClonePolicy.Preserve);

        clone.Should().NotBeSameAs(source);
        clone.PlainText.Should().Be("phanested");
        clone.Formatting.Should().Be(source.Formatting);
        clone.StyleId.Should().Be("Caption");
        clone.MarkRevision.Should().Be(RevisionKind.Inserted);
        clone.MarkRevisionAuthor.Should().Be("Alice");
        clone.ParagraphFormatRevision.Should().Be(source.ParagraphFormatRevision);
        clone.Runs.Select(run => run.Revision).Should().Equal(
            RevisionKind.Deleted,
            RevisionKind.Inserted);
        clone.Runs[1].MoveRevisionId.Should().Be(27);
        var sourceShape = shapeRun.Shape!;
        var clonedShape = clone.Runs[1].Shape!;
        clonedShape.Should().NotBeSameAs(sourceShape);
        clonedShape.TextParagraphs[0].Should().NotBeSameAs(sourceShape.TextParagraphs[0]);

        clonedShape.TextParagraphs[0].Runs[0].Text = "changed";
        sourceShape.TextParagraphs[0].Runs[0].Text.Should().Be("nested");
    }

    [Fact]
    public void CloneParagraphTextRange_PreservesOutsideTextAndTransformsOnlySelectedFragments()
    {
        var source = new Paragraph();
        source.Runs.Add(new Run("alpha", RunFormatting.Default)
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice"
        });
        source.Runs.Add(new Run("beta", RunFormatting.Default with { Italic = true })
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob"
        });
        source.BookmarkNames.Add("Middle");
        source.BookmarkBoundaries.Add(new BookmarkBoundary(
            "7",
            BookmarkBoundaryKind.Start,
            1,
            "Middle"));

        var clone = DocumentModelCloner.CloneParagraphTextRange(
            source,
            2,
            7,
            RevisionClonePolicy.Preserve,
            formatting => formatting with { Bold = true },
            preserveUnselectedText: true);

        clone.PlainText.Should().Be("alphabeta");
        clone.Runs.Select(run => run.Text).Should().Equal("al", "pha", "be", "ta");
        clone.Runs.Select(run => run.Formatting.Bold).Should().Equal(false, true, true, false);
        clone.Runs.Select(run => run.Formatting.Italic).Should().Equal(false, false, true, true);
        clone.Runs.Select(run => run.Revision).Should().Equal(
            RevisionKind.Inserted,
            RevisionKind.Inserted,
            RevisionKind.Deleted,
            RevisionKind.Deleted);
        clone.BookmarkBoundaries.Should().ContainSingle();
        clone.BookmarkBoundaries[0].RunIndex.Should().Be(2);
    }

    [Fact]
    public void CloneParagraphTextRange_StripRemovesParagraphAndRunRevisionMetadata()
    {
        var source = new Paragraph
        {
            MarkRevision = RevisionKind.Inserted,
            MarkRevisionAuthor = "Alice",
            MarkRevisionDateXml = DateXml,
            ParagraphFormatRevision = new ParagraphFormatRevision(
                ParagraphFormatting.Default,
                "Alice",
                DateXml)
        };
        source.Runs.Add(new Run("tracked")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = DateXml,
            MoveRevisionId = 19,
            FormatRevision = new FormatRevision(RunFormatting.Default, "Bob", DateXml)
        });

        var clone = DocumentModelCloner.CloneParagraphTextRange(
            source,
            0,
            source.PlainText.Length,
            RevisionClonePolicy.Strip);

        clone.MarkRevision.Should().Be(RevisionKind.None);
        clone.MarkRevisionAuthor.Should().BeNull();
        clone.MarkRevisionDateXml.Should().BeNull();
        clone.ParagraphFormatRevision.Should().BeNull();
        clone.Runs.Single().Revision.Should().Be(RevisionKind.None);
        clone.Runs.Single().RevisionAuthor.Should().BeNull();
        clone.Runs.Single().RevisionDateXml.Should().BeNull();
        clone.Runs.Single().MoveRevisionId.Should().BeNull();
        clone.Runs.Single().FormatRevision.Should().BeNull();
    }

    private static Table CreateRevisionedNestedTable()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].RowRevision = RevisionKind.Inserted;
        table.Rows[0].RowRevisionAuthor = "Alice";
        table.Rows[0].RowRevisionDateXml = DateXml;

        var paragraph = table.Rows[0].Cells[0].Paragraphs.Single();
        paragraph.MarkRevision = RevisionKind.Deleted;
        paragraph.MarkRevisionAuthor = "Alice";
        paragraph.MarkRevisionDateXml = DateXml;
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            ParagraphFormatting.Default,
            "Alice",
            DateXml);
        var fieldRun = Run.ComplexFieldRun(" REF Total ", "42", formatting: new RunFormatting { Bold = true });
        fieldRun.Revision = RevisionKind.Inserted;
        fieldRun.RevisionAuthor = "Alice";
        fieldRun.RevisionDateXml = DateXml;
        fieldRun.MoveRevisionId = 17;
        fieldRun.FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", DateXml);
        paragraph.Runs.Add(fieldRun);

        var nested = Table.Create(1, 1);
        nested.Rows[0].RowRevision = RevisionKind.Deleted;
        nested.Rows[0].RowRevisionAuthor = "Bob";
        nested.Rows[0].RowRevisionDateXml = DateXml;
        var nestedParagraph = nested.Rows[0].Cells[0].Paragraphs.Single();
        nestedParagraph.MarkRevision = RevisionKind.Inserted;
        nestedParagraph.MarkRevisionAuthor = "Bob";
        nestedParagraph.MarkRevisionDateXml = DateXml;
        var formulaRun = Run.TableFormulaFieldRun(new TableFormulaField("=SUM(ABOVE)"), "42");
        formulaRun.Revision = RevisionKind.Deleted;
        formulaRun.RevisionAuthor = "Bob";
        formulaRun.RevisionDateXml = DateXml;
        formulaRun.FormatRevision = new FormatRevision(RunFormatting.Default, "Bob", DateXml);
        nestedParagraph.Runs.Add(formulaRun);
        table.Rows[0].Cells[0].NestedTables.Add(nested);

        return table;
    }
}
