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
