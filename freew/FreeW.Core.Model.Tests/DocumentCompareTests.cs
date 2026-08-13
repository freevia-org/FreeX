using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.Model.Tests;

public class DocumentCompareTests
{
    private const string Author = "Reviewer";
    private const string DateXml = "2026-06-17T12:00:00Z";

    private static TextDocument DocWith(params string[] paragraphs)
    {
        var doc = new TextDocument();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static void AddPreservedSafetyShell(TextDocument doc, string marker)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        doc.Preserved.OriginalSettings = new XElement(w + "settings", new XElement(w + "proofState"));
        doc.Preserved.OriginalCustomProperties = new XElement(
            cp + "Properties",
            new XElement(cp + "property", new XAttribute("name", marker), new XElement(vt + "lpwstr", marker)));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/customXml/review-safety.xml",
            Encoding.UTF8.GetBytes(marker),
            "application/xml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"));
        doc.Preserved.ContentTypeDefaults["xml"] = "application/xml";
    }

    [Fact]
    public void IdenticalDocuments_ProduceNoRevisions()
    {
        var original = DocWith("Hello world", "Second paragraph");
        var revised = DocWith("Hello world", "Second paragraph");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        TrackChanges.HasRevisions(result).Should().BeFalse();
        result.Paragraphs.Select(p => p.PlainText)
            .Should().Equal("Hello world", "Second paragraph");
    }

    [Fact]
    public void IdenticalDocuments_PreserveSpanningFieldOwnership()
    {
        var original = DocWith("A", "Alpha, 1");
        var revised = DocWith("A", "Alpha, 1");
        var revisedParagraphs = revised.Paragraphs.ToArray();
        var field = new ComplexField(" INDEX \\h \"A\" ");
        revisedParagraphs[0].SpanningFieldStart = field;
        revisedParagraphs[0].SpanningFieldOwner = field;
        revisedParagraphs[1].SpanningFieldOwner = field;
        revisedParagraphs[1].EndsSpanningField = true;

        var result = DocumentCompare.Compare(original, revised, Author, DateXml).Paragraphs.ToArray();

        result[0].SpanningFieldStart.Should().Be(revisedParagraphs[0].SpanningFieldStart);
        result.Should().OnlyContain(paragraph => paragraph.SpanningFieldOwner == field);
        result[1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void InsertedFloatingTable_PreservesCompleteTableShell()
    {
        var original = new TextDocument();
        var revised = new TextDocument();
        var table = Table.Create(1, 1);
        table.TableStyleId = "TableGrid";
        table.PreferredWidthPt = 240;
        table.Alignment = TableAlignment.Right;
        table.IndentFromLeftPt = 12;
        table.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Page,
            VerticalAnchor: TableVerticalAnchor.Margin,
            HorizontalAlignment: TableHorizontalPositionAlignment.Outside,
            VerticalOffsetPt: 18);
        table.FloatingTableAllowsOverlap = false;
        table.CellSpacingPt = 2;
        revised.Blocks.Add(table);

        var cloned = DocumentCompare.Compare(original, revised, Author, DateXml)
            .Blocks.OfType<Table>().Single();

        cloned.TableStyleId.Should().Be("TableGrid");
        cloned.PreferredWidthPt.Should().Be(240);
        cloned.Alignment.Should().Be(TableAlignment.Right);
        cloned.IndentFromLeftPt.Should().Be(12);
        cloned.FloatingPosition.Should().Be(table.FloatingPosition);
        cloned.FloatingTableAllowsOverlap.Should().BeFalse();
        cloned.CellSpacingPt.Should().Be(2);
    }

    [Fact]
    public void InsertedNestedTable_PreservesFieldsAndStripsIncomingRevisions()
    {
        var revised = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].RowRevision = RevisionKind.Inserted;
        var paragraph = table.Rows[0].Cells[0].Paragraphs.Single();
        paragraph.MarkRevision = RevisionKind.Deleted;
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            ParagraphFormatting.Default,
            "Prior reviewer",
            DateXml);
        var fieldRun = Run.ComplexFieldRun(" REF Total ", "42");
        fieldRun.Revision = RevisionKind.Inserted;
        fieldRun.MoveRevisionId = 8;
        fieldRun.FormatRevision = new FormatRevision(RunFormatting.Default, "Prior reviewer", DateXml);
        paragraph.Runs.Add(fieldRun);

        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs.Single().Runs.Add(
            Run.TableFormulaFieldRun(new TableFormulaField("=SUM(ABOVE)"), "42"));
        table.Rows[0].Cells[0].NestedTables.Add(nested);
        revised.Blocks.Add(table);

        var cloned = DocumentCompare.Compare(new TextDocument(), revised, Author, DateXml)
            .Blocks.OfType<Table>().Single();

        cloned.Should().NotBeSameAs(table);
        cloned.Rows[0].RowRevision.Should().Be(RevisionKind.None);
        var clonedParagraph = cloned.Rows[0].Cells[0].Paragraphs.Single();
        clonedParagraph.MarkRevision.Should().Be(RevisionKind.None);
        clonedParagraph.ParagraphFormatRevision.Should().BeNull();
        clonedParagraph.Runs.Single().ComplexField!.Instruction.Should().Be(" REF Total ");
        clonedParagraph.Runs.Single().Revision.Should().Be(RevisionKind.None);
        clonedParagraph.Runs.Single().MoveRevisionId.Should().BeNull();
        clonedParagraph.Runs.Single().FormatRevision.Should().BeNull();
        cloned.Rows[0].Cells[0].NestedTables.Single()
            .Rows[0].Cells[0].Paragraphs.Single().Runs.Single().TableFormula
            .Should().Be(new TableFormulaField("=SUM(ABOVE)"));
    }

    [Fact]
    public void InsertedTableWithNestedTable_PreservesNestedTable()
    {
        var original = new TextDocument();
        var revised = new TextDocument();

        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0] = new TableCell("nested cell text");
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        // Word requires a cell that hosts a table to still carry a trailing paragraph.
        outerTable.Rows[0].Cells[0].Paragraphs.Add(new Paragraph(string.Empty));
        revised.Blocks.Add(outerTable);

        var cloned = DocumentCompare.Compare(original, revised, Author, DateXml)
            .Blocks.OfType<Table>().Single();

        var clonedCell = cloned.Rows[0].Cells[0];
        clonedCell.NestedTables.Should().ContainSingle();
        var clonedNestedTable = clonedCell.NestedTables[0];
        clonedNestedTable.Should().NotBeSameAs(nestedTable);
        clonedNestedTable.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("nested cell text");
    }

    [Fact]
    public void IdenticalDocuments_PreserveSharedBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var original = DocWith("References", "Entry");
        var revised = DocWith("References", "Entry");
        foreach (var block in revised.Blocks)
            block.BlockContentControl = control;

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.Blocks.Should().HaveCount(2);
        result.Blocks[0].BlockContentControl.Should().Be(control);
        ReferenceEquals(result.Blocks[1].BlockContentControl, result.Blocks[0].BlockContentControl).Should().BeTrue();
    }

    [Fact]
    public void InsertedParagraph_IsMarkedInserted()
    {
        var original = DocWith("Keep this", "Tail");
        var revised = DocWith("Keep this", "Brand new line", "Tail");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        var paragraphs = result.Paragraphs.ToList();
        paragraphs.Select(p => p.PlainText).Should().Equal("Keep this", "Brand new line", "Tail");

        // The unchanged paragraphs carry no marks.
        paragraphs[0].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        paragraphs[2].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);

        // The inserted paragraph is entirely tracked as an insertion, stamped with author + date.
        paragraphs[1].Runs.Should().NotBeEmpty();
        paragraphs[1].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Inserted);
        paragraphs[1].Runs.Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);
    }

    [Fact]
    public void DeletedParagraph_IsKeptAndMarkedDeleted()
    {
        var original = DocWith("Keep this", "Doomed paragraph", "Tail");
        var revised = DocWith("Keep this", "Tail");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        var paragraphs = result.Paragraphs.ToList();
        // The deleted paragraph is kept in the result (struck-through), in its original position.
        paragraphs.Select(p => p.PlainText).Should().Equal("Keep this", "Doomed paragraph", "Tail");

        paragraphs[1].Runs.Should().NotBeEmpty();
        paragraphs[1].Runs.Should().OnlyContain(r => r.Revision == RevisionKind.Deleted);
        paragraphs[1].Runs.Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);

        // Accepting the comparison drops the deletion's text (an empty paragraph stays behind, since
        // run-level accept does not remove the paragraph container) and leaves the surviving text.
        TrackChanges.AcceptAll(result);
        result.Paragraphs.Select(p => p.PlainText).Where(t => t.Length > 0)
            .Should().Equal("Keep this", "Tail");
    }

    [Fact]
    public void WordLevelChange_MarksOnlyChangedWords()
    {
        var original = DocWith("the quick brown fox");
        var revised = DocWith("the quick red fox");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        var paragraph = result.Paragraphs.Single();

        // Unchanged words stay ordinary; "brown" is deleted, "red" is inserted.
        var deleted = paragraph.Runs.Where(r => r.Revision == RevisionKind.Deleted).Select(r => r.Text.Trim());
        var inserted = paragraph.Runs.Where(r => r.Revision == RevisionKind.Inserted).Select(r => r.Text.Trim());
        var normal = paragraph.Runs.Where(r => r.Revision == RevisionKind.None).Select(r => r.Text.Trim());

        deleted.Should().Equal("brown");
        inserted.Should().Equal("red");
        normal.Should().Contain(new[] { "the", "quick", "fox" });

        // Every revision run is attributed; accepting yields exactly the revised text.
        paragraph.Runs.Where(r => r.Revision != RevisionKind.None)
            .Should().OnlyContain(r => r.RevisionAuthor == Author && r.RevisionDateXml == DateXml);

        TrackChanges.AcceptAll(result);
        result.Paragraphs.Single().PlainText.Should().Be("the quick red fox");
    }

    [Fact]
    public void WordLevelChange_MapsRevisedBookmarkAroundSurvivingText()
    {
        var original = DocWith("A old C");
        var revised = new TextDocument();
        var revisedParagraph = new Paragraph();
        revisedParagraph.Runs.Add(new Run("A "));
        revisedParagraph.Runs.Add(new Run("new "));
        revisedParagraph.Runs.Add(new Run("C"));
        revisedParagraph.BookmarkNames.Add("Target");
        revisedParagraph.BookmarkBoundaries.Add(new BookmarkBoundary("8", BookmarkBoundaryKind.Start, 2, "Target"));
        revisedParagraph.BookmarkBoundaries.Add(new BookmarkBoundary("8", BookmarkBoundaryKind.End, 3));
        revised.Blocks.Add(revisedParagraph);

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        var paragraph = result.Paragraphs.Single();
        paragraph.BookmarkBoundaries.Should().HaveCount(2);
        var start = paragraph.BookmarkBoundaries[0];
        var end = paragraph.BookmarkBoundaries[1];
        paragraph.Runs[start.RunIndex].Text.Should().Be("C");
        end.RunIndex.Should().Be(start.RunIndex + 1);
    }

    [Fact]
    public void Compare_DoesNotMutateInputs()
    {
        var original = DocWith("alpha beta");
        var revised = DocWith("alpha gamma");

        DocumentCompare.Compare(original, revised, Author, DateXml);

        TrackChanges.HasRevisions(original).Should().BeFalse();
        TrackChanges.HasRevisions(revised).Should().BeFalse();
        original.Paragraphs.Single().PlainText.Should().Be("alpha beta");
        revised.Paragraphs.Single().PlainText.Should().Be("alpha gamma");
    }

    [Fact]
    public void RejectingComparison_RestoresOriginalText()
    {
        var original = DocWith("one two three");
        var revised = DocWith("one four three");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        TrackChanges.RejectAll(result);
        result.Paragraphs.Single().PlainText.Should().Be("one two three");
    }

    // --- CompareSettings depth ---------------------------------------------------

    [Fact]
    public void CompareSettings_Default_AllChangeTypesEnabled()
    {
        // All flags must default to true so passing no settings preserves existing behaviour.
        var settings = CompareSettings.Default;
        settings.Insertions.Should().BeTrue();
        settings.Deletions.Should().BeTrue();
        settings.Moves.Should().BeTrue();
        settings.Comments.Should().BeTrue();
        settings.Formatting.Should().BeTrue();
        settings.CaseChanges.Should().BeTrue();
        settings.Whitespace.Should().BeTrue();
        settings.ShowChangesIn.Should().Be(CompareShowChangesIn.NewDocument);
    }

    [Fact]
    public void Compare_WithDefaultSettings_MatchesNoSettingsOverload()
    {
        // The two-argument and five-argument Compare calls must produce identical results.
        var original = DocWith("alpha beta");
        var revised = DocWith("alpha gamma");

        var withDefaults  = DocumentCompare.Compare(original, revised, Author, DateXml, CompareSettings.Default);
        var withoutSettings = DocumentCompare.Compare(original, revised, Author, DateXml);

        // Same paragraph count; same revision kinds on matching tokens.
        withDefaults.Paragraphs.Count().Should().Be(withoutSettings.Paragraphs.Count());
        withDefaults.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Revision != RevisionKind.None).Count()
            .Should().Be(withoutSettings.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Revision != RevisionKind.None).Count());
    }

    [Fact]
    public void Compare_InsertionsSuppressed_InsertedTokensAreOrdinaryRuns()
    {
        var original = DocWith("the brown fox");
        var revised  = DocWith("the quick red fox");

        var settings = new CompareSettings { Insertions = false };
        var result = DocumentCompare.Compare(original, revised, Author, DateXml, settings);

        var paragraph = result.Paragraphs.Single();
        // No inserted revision marks; deleted tokens are still marked.
        paragraph.Runs.Should().NotContain(r => r.Revision == RevisionKind.Inserted,
            "insertions flag is false — no inserted marks must appear");
        paragraph.Runs.Should().Contain(r => r.Revision == RevisionKind.Deleted,
            "deletions flag is true — deleted marks must still appear");
    }

    [Fact]
    public void Compare_DeletionsSuppressed_DeletedTokensAreDropped()
    {
        var original = DocWith("the brown fox");
        var revised  = DocWith("the red fox");

        var settings = new CompareSettings { Deletions = false };
        var result = DocumentCompare.Compare(original, revised, Author, DateXml, settings);

        var paragraph = result.Paragraphs.Single();
        // Deleted tokens are dropped entirely; inserted tokens are still marked.
        paragraph.Runs.Should().NotContain(r => r.Revision == RevisionKind.Deleted,
            "deletions flag is false — no deleted marks must appear");
        paragraph.Runs.Should().Contain(r => r.Revision == RevisionKind.Inserted,
            "insertions flag is true — inserted marks must still appear");
    }

    [Fact]
    public void Compare_BothSuppressed_ProducesNoRevisionMarks()
    {
        var original = DocWith("hello world");
        var revised  = DocWith("goodbye universe");

        var settings = new CompareSettings { Insertions = false, Deletions = false };
        var result = DocumentCompare.Compare(original, revised, Author, DateXml, settings);

        result.Paragraphs.SelectMany(p => p.Runs)
            .Should().NotContain(r => r.Revision != RevisionKind.None,
                "both insertion and deletion tracking suppressed — result must contain no revision marks");
    }

    [Fact]
    public void Compare_CaseChangesSuppressed_PreservesRevisedCasingWithoutRevisionMarks()
    {
        var original = DocWith("Status: Draft");
        var revised = DocWith("status: draft");

        var result = DocumentCompare.Compare(
            original,
            revised,
            Author,
            DateXml,
            new CompareSettings { CaseChanges = false });

        result.Paragraphs.Single().PlainText.Should().Be("status: draft");
        TrackChanges.HasRevisions(result).Should().BeFalse();
    }

    [Fact]
    public void Compare_WhitespaceSuppressed_PreservesRevisedWhitespaceWithoutRevisionMarks()
    {
        var original = DocWith("alpha beta\tgamma");
        var revised = DocWith("alpha   beta gamma");

        var result = DocumentCompare.Compare(
            original,
            revised,
            Author,
            DateXml,
            new CompareSettings { Whitespace = false });

        result.Paragraphs.Single().PlainText.Should().Be("alpha   beta gamma");
        TrackChanges.HasRevisions(result).Should().BeFalse();
    }

    [Fact]
    public void Compare_CaseAndWhitespaceSuppressed_StillTracksSubstantiveWordChanges()
    {
        var original = DocWith("Alpha beta");
        var revised = DocWith("alpha   delta");

        var result = DocumentCompare.Compare(
            original,
            revised,
            Author,
            DateXml,
            new CompareSettings { CaseChanges = false, Whitespace = false });

        var paragraph = result.Paragraphs.Single();
        paragraph.Runs.Where(run => run.Revision == RevisionKind.Deleted)
            .Select(run => run.Text.Trim()).Should().Equal("beta");
        paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted)
            .Select(run => run.Text.Trim()).Should().Equal("delta");
    }

    [Fact]
    public void Compare_DefaultSettings_TracksCaseAndWhitespaceChanges()
    {
        var original = DocWith("Alpha beta");
        var revised = DocWith("alpha  beta");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        TrackChanges.HasRevisions(result).Should().BeTrue();
    }

    [Fact]
    public void Compare_ShowChangesIn_StoredOnSettings()
    {
        // The ShowChangesIn flag is a dialog/presentation concept; the engine doesn't act on it, but
        // it must round-trip through the CompareSettings so the calling command can use it.
        var settings = new CompareSettings { ShowChangesIn = CompareShowChangesIn.Original };
        settings.ShowChangesIn.Should().Be(CompareShowChangesIn.Original);
    }

    [Fact]
    public void Compare_FormatOnlyRunChange_TracksPreviousFormatting()
    {
        var original = new TextDocument();
        var originalParagraph = new Paragraph();
        originalParagraph.Runs.Add(new Run("plain"));
        originalParagraph.Runs.Add(new Run(" keeps its format", new RunFormatting { Italic = true }));
        original.Blocks.Add(originalParagraph);

        var revised = new TextDocument();
        var revisedParagraph = new Paragraph();
        revisedParagraph.Runs.Add(new Run("plain", new RunFormatting { Bold = true }));
        revisedParagraph.Runs.Add(new Run(" keeps its format", new RunFormatting { Italic = true }));
        revised.Blocks.Add(revisedParagraph);

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);
        var runs = result.Paragraphs.Single().Runs;

        runs[0].Formatting.Bold.Should().BeTrue();
        runs[0].FormatRevision.Should().Be(new FormatRevision(RunFormatting.Default, Author, DateXml));
        runs[1].FormatRevision.Should().BeNull();
        TrackChanges.HasRevisions(result).Should().BeTrue();

        TrackChanges.RejectAll(result);
        result.Paragraphs.Single().Runs[0].Formatting.Bold.Should().BeFalse();
        TrackChanges.HasRevisions(result).Should().BeFalse();
    }

    [Fact]
    public void Compare_FormatOnlyRunChange_CanBeSuppressed()
    {
        var original = new TextDocument();
        var originalParagraph = new Paragraph();
        originalParagraph.Runs.Add(new Run("format me"));
        original.Blocks.Add(originalParagraph);

        var revised = new TextDocument();
        var revisedParagraph = new Paragraph();
        revisedParagraph.Runs.Add(new Run("format me", new RunFormatting { Underline = true }));
        revised.Blocks.Add(revisedParagraph);

        var result = DocumentCompare.Compare(
            original,
            revised,
            Author,
            DateXml,
            new CompareSettings { Formatting = false });

        result.Paragraphs.Single().Runs.Single().Formatting.Underline.Should().BeTrue();
        result.Paragraphs.Single().Runs.Single().FormatRevision.Should().BeNull();
        TrackChanges.HasRevisions(result).Should().BeFalse();
    }

    [Fact]
    public void Compare_RevisedDocumentDoNotTrackFormatting_KeepsFormattingWithoutRevisionAndPreservesPolicy()
    {
        var original = DocWith("same text");
        var revised = DocWith("same text");
        revised.Paragraphs.Single().Runs.Single().Formatting = RunFormatting.Default with { Underline = true };
        revised.DoNotTrackFormatting = true;

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);
        var run = result.Paragraphs.Single().Runs.Single();

        result.DoNotTrackFormatting.Should().BeTrue();
        run.Formatting.Underline.Should().BeTrue();
        run.FormatRevision.Should().BeNull();
        TrackChanges.HasRevisions(result).Should().BeFalse();
    }

    [Fact]
    public void Compare_UniqueUnchangedParagraphMove_UsesPairedMoveRevisionId()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);
        var moved = result.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.MoveRevisionId != null)
            .ToList();

        moved.Should().HaveCount(2);
        moved.Should().ContainSingle(run => run.Text == "Alpha" && run.Revision == RevisionKind.Deleted);
        moved.Should().ContainSingle(run => run.Text == "Alpha" && run.Revision == RevisionKind.Inserted);
        moved.Select(run => run.MoveRevisionId).Distinct().Should().ContainSingle().Which.Should().Be(1);
        moved.Should().OnlyContain(run => run.RevisionAuthor == Author && run.RevisionDateXml == DateXml);

        TrackChanges.AcceptAll(result);
        result.Paragraphs.Where(paragraph => paragraph.PlainText.Length > 0)
            .Select(paragraph => paragraph.PlainText).Should().Equal("Bravo", "Alpha", "Charlie");
    }

    [Fact]
    public void Compare_UniqueUnchangedParagraphMove_CanBeRejectedToOriginalOrder()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        TrackChanges.RejectAll(result);
        result.Paragraphs.Where(paragraph => paragraph.PlainText.Length > 0)
            .Select(paragraph => paragraph.PlainText).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void Compare_MovesSuppressed_UsesOrdinaryInsertionAndDeletion()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");

        var result = DocumentCompare.Compare(
            original,
            revised,
            Author,
            DateXml,
            new CompareSettings { Moves = false });

        result.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.MoveRevisionId != null);
        result.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.Revision == RevisionKind.Deleted)
            .And.Contain(run => run.Revision == RevisionKind.Inserted);
    }

    [Fact]
    public void Compare_RevisedDocumentDoNotTrackMoves_UsesOrdinaryRevisionPairsAndPreservesPolicy()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revised = DocWith("Bravo", "Alpha", "Charlie");
        revised.DoNotTrackMoves = true;

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.DoNotTrackMoves.Should().BeTrue();
        result.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.MoveRevisionId != null);
        result.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.Revision == RevisionKind.Deleted)
            .And.Contain(run => run.Revision == RevisionKind.Inserted);
    }

    [Fact]
    public void Compare_DuplicateParagraphMove_FallsBackToOrdinaryRevisionPairs()
    {
        var original = DocWith("Repeat", "Repeat", "Tail");
        var revised = DocWith("Repeat", "Tail", "Repeat");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.MoveRevisionId != null);
    }

    [Fact]
    public void Compare_CopiesRevisedPreservedPackageSafetyShell()
    {
        var original = DocWith("baseline review text");
        var revised = DocWith("updated review text");
        AddPreservedSafetyShell(revised, "compare-retained");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.Preserved.OriginalSettings.Should().NotBeNull();
        result.Preserved.OriginalSettings.Should().NotBeSameAs(revised.Preserved.OriginalSettings);
        result.Preserved.OriginalCustomProperties.Should().NotBeNull();
        result.Preserved.Parts.Should().ContainSingle(part =>
            part.PartName == "/customXml/review-safety.xml" &&
            Encoding.UTF8.GetString(part.Bytes) == "compare-retained");
        result.Preserved.Parts.Single().Bytes.Should().NotBeSameAs(revised.Preserved.Parts.Single().Bytes);
        result.Preserved.ContentTypeDefaults.Should().ContainKey("xml");
    }

    [Fact]
    public void Compare_CopiesRevisedCommentThreadForRetainedAnchors()
    {
        var original = DocWith("Annotated text");
        var revised = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Annotated text") { CommentId = 5 });
        paragraph.Runs.Add(Run.CommentReference(5));
        revised.Blocks.Add(paragraph);

        var comment = new Comment(5, "Please verify", "Alice", "A")
        {
            DateXml = "2026-07-24T12:00:00Z",
            Resolved = true,
        };
        comment.AddReply(6, "Verified", "Bob", "B").DateXml = "2026-07-24T13:00:00Z";
        revised.Comments[5] = comment;

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.Paragraphs.Single().Runs.Should().Contain(run => run.CommentId == 5 && !run.IsCommentReference)
            .And.Contain(run => run.CommentId == 5 && run.IsCommentReference);
        result.Comments.Should().ContainKey(5);
        result.Comments[5].Should().NotBeSameAs(comment);
        result.Comments[5].PlainText.Should().Be("Please verify");
        result.Comments[5].Resolved.Should().BeTrue();
        result.Comments[5].Replies.Should().ContainSingle();
        result.Comments[5].Replies[0].PlainText.Should().Be("Verified");

        comment.Content[0].Runs[0].Text = "Mutated source";
        result.Comments[5].PlainText.Should().Be("Please verify");
    }

    [Fact]
    public void Compare_DeletedCommentedParagraph_CarriesOriginalCommentThread()
    {
        var original = DocWith("Keep this", "Doomed paragraph", "Tail");
        var doomed = original.Paragraphs.ElementAt(1);
        doomed.Runs[0].CommentId = 5;
        doomed.Runs.Add(Run.CommentReference(5));
        var comment = new Comment(5, "Remove this note", "Alice", "A") { Resolved = true };
        comment.AddReply(6, "Acknowledged", "Bob", "B");
        original.Comments[5] = comment;

        var result = DocumentCompare.Compare(original, DocWith("Keep this", "Tail"), Author, DateXml);
        var deletedRuns = result.Paragraphs.ElementAt(1).Runs;

        deletedRuns.Should().Contain(run => run.CommentId == 5 && !run.IsCommentReference);
        deletedRuns.Should().Contain(run => run.CommentId == 5 && run.IsCommentReference);
        result.Comments.Should().ContainKey(5);
        result.Comments[5].PlainText.Should().Be("Remove this note");
        result.Comments[5].Resolved.Should().BeTrue();
        result.Comments[5].Replies.Should().ContainSingle(reply => reply.Id == 6 && reply.PlainText == "Acknowledged");
    }

    [Fact]
    public void Compare_DeletedCommentCollision_RemapsOnlyDeletedAnchorThread()
    {
        var original = DocWith("Keep this", "Doomed paragraph", "Tail");
        var doomed = original.Paragraphs.ElementAt(1);
        doomed.Runs[0].CommentId = 5;
        doomed.Runs.Add(Run.CommentReference(5));
        original.Comments[5] = new Comment(5, "Original note", "Alice", "A");

        var revised = DocWith("Keep this", "Tail", "Current note");
        var current = revised.Paragraphs.ElementAt(2);
        current.Runs[0].CommentId = 5;
        current.Runs.Add(Run.CommentReference(5));
        revised.Comments[5] = new Comment(5, "Revised note", "Bob", "B");

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);
        var deletedId = result.Paragraphs.ElementAt(1).Runs.First(run => !run.IsCommentReference).CommentId;

        deletedId.Should().NotBe(5);
        result.Comments[5].PlainText.Should().Be("Revised note");
        result.Comments[deletedId!.Value].PlainText.Should().Be("Original note");
        result.Paragraphs.Last().Runs.Should().Contain(run => run.CommentId == 5 && run.IsCommentReference);
    }

    [Fact]
    public void Compare_CommentsDisabled_RemovesDeletedSideAnchors()
    {
        var original = DocWith("Keep this", "Doomed paragraph", "Tail");
        var doomed = original.Paragraphs.ElementAt(1);
        doomed.Runs[0].CommentId = 5;
        doomed.Runs.Add(Run.CommentReference(5));
        original.Comments[5] = new Comment(5, "Remove this note", "Alice", "A");

        var result = DocumentCompare.Compare(
            original,
            DocWith("Keep this", "Tail"),
            Author,
            DateXml,
            new CompareSettings { Comments = false });

        result.Paragraphs.ElementAt(1).Runs.Should().NotContain(run => run.CommentId != null);
        result.Comments.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Style catalog for whole-paragraph deletions (r134 MED: dangling StyleId reference)
    // -----------------------------------------------------------------------

    [Fact]
    public void Compare_WholeParagraphDeletion_CarriesOriginalOnlyStyleIntoResultCatalog()
    {
        // "Quote" exists only in the original document — revised dropped the style entirely (e.g. the
        // author removed it while cleaning up the style catalog). The deleted paragraph still references
        // it, so the result's style catalog must define it or the saved document has a dangling style id.
        var original = DocWith("Doomed paragraph", "Tail");
        original.Styles["Quote"] = new DocumentStyle { Id = "Quote", Name = "Quote" };
        original.Paragraphs.First().StyleId = "Quote";

        var revised = DocWith("Tail"); // revised never had "Quote" in its catalog at all

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        var deletedParagraph = result.Paragraphs.Single(paragraph =>
            paragraph.Runs.Any(run => run.Revision == RevisionKind.Deleted));

        deletedParagraph.StyleId.Should().Be("Quote");
        result.Styles.Should().ContainKey("Quote");
    }

    [Fact]
    public void Compare_RevisedRedefinesSameStyleId_KeepsRevisedDefinitionNotOriginals()
    {
        // Sibling no-regression: when both documents define the same style id, revised's own definition
        // must win in the result catalog — the original-style backfill must never overwrite it.
        var original = DocWith("Doomed paragraph", "Tail");
        original.Styles["Quote"] = new DocumentStyle { Id = "Quote", Name = "Original Quote Name" };
        original.Paragraphs.First().StyleId = "Quote";

        var revised = DocWith("Tail");
        revised.Styles["Quote"] = new DocumentStyle { Id = "Quote", Name = "Revised Quote Name" };

        var result = DocumentCompare.Compare(original, revised, Author, DateXml);

        result.Styles["Quote"].Name.Should().Be("Revised Quote Name");
    }
}
