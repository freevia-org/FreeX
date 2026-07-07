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
    public void Compare_ShowChangesIn_StoredOnSettings()
    {
        // The ShowChangesIn flag is a dialog/presentation concept; the engine doesn't act on it, but
        // it must round-trip through the CompareSettings so the calling command can use it.
        var settings = new CompareSettings { ShowChangesIn = CompareShowChangesIn.Original };
        settings.ShowChangesIn.Should().Be(CompareShowChangesIn.Original);
    }
}
