namespace FreeW.Core.Model.Tests;

public sealed class NestedRevisionCloneLifecycleTests
{
    private const string InnerDate = "2026-10-01T10:00:00Z";
    private const string OuterDate = "2026-10-02T11:00:00Z";

    [Theory]
    [InlineData("body", RevisionClonePolicy.Preserve)]
    [InlineData("body", RevisionClonePolicy.Strip)]
    [InlineData("cell", RevisionClonePolicy.Preserve)]
    [InlineData("cell", RevisionClonePolicy.Strip)]
    [InlineData("nested-cell", RevisionClonePolicy.Preserve)]
    [InlineData("nested-cell", RevisionClonePolicy.Strip)]
    [InlineData("shape", RevisionClonePolicy.Preserve)]
    [InlineData("shape", RevisionClonePolicy.Strip)]
    [InlineData("grouped-shape", RevisionClonePolicy.Preserve)]
    [InlineData("grouped-shape", RevisionClonePolicy.Strip)]
    [InlineData("section-header", RevisionClonePolicy.Preserve)]
    [InlineData("section-header", RevisionClonePolicy.Strip)]
    public void CloneDocumentBlocksKeepsOrStripsTheWholeRepresentableRevisionPair(
        string location, RevisionClonePolicy policy)
    {
        var (block, findRun) = CreateBlock(location);
        var source = new TextDocument();
        source.Blocks.Add(block);

        var clonedBlock = DocumentModelCloner.CloneBlocks(source, policy).Single();
        var sourceRun = findRun(block);
        var clonedRun = findRun(clonedBlock);

        clonedBlock.Should().NotBeSameAs(block);
        clonedRun.Should().NotBeSameAs(sourceRun);
        clonedRun.Text.Should().Be("abcdef");
        AssertRevisionPolicy(clonedRun, policy);
        // The record is immutable: replacing either graph's value must not mutate the other.
        sourceRun.NestedRevision = sourceRun.NestedRevision! with { Author = "Changed source reviewer" };
        clonedRun.Text = "independent clone";
        sourceRun.Text.Should().Be("abcdef");
        AssertRevisionPolicy(clonedRun, policy);
    }

    [Theory]
    [InlineData(RevisionClonePolicy.Preserve)]
    [InlineData(RevisionClonePolicy.Strip)]
    public void CloneSectionStoriesPreservesPolicyInAllSixSlots(RevisionClonePolicy policy)
    {
        var slots = new SectionHeadersFooters
        {
            Header = Region(), Footer = Region(), FirstHeader = Region(), FirstFooter = Region(),
            EvenHeader = Region(), EvenFooter = Region(),
        };

        var clone = DocumentModelCloner.CloneSectionHeadersFooters(slots, policy);

        foreach (var region in new[] { clone.Header, clone.Footer, clone.FirstHeader, clone.FirstFooter, clone.EvenHeader, clone.EvenFooter })
        {
            region.Should().NotBeNull();
            AssertRevisionPolicy(region!.Paragraphs.Single().Runs.Single(), policy);
        }
        clone.Header!.Paragraphs[0].Runs[0].NestedRevision = null;
        AssertPair(slots.Header!.Paragraphs[0].Runs[0]);
    }

    [Theory]
    [InlineData(false, RevisionClonePolicy.Preserve)]
    [InlineData(false, RevisionClonePolicy.Strip)]
    [InlineData(true, RevisionClonePolicy.Preserve)]
    [InlineData(true, RevisionClonePolicy.Strip)]
    public void CloneNotesPreservesPolicyAndDetachedRunIdentity(bool endnote, RevisionClonePolicy policy)
    {
        var paragraph = ParagraphWithPair();
        Paragraph cloned;
        if (endnote)
        {
            var source = new Endnote(1);
            source.Content.Add(paragraph);
            cloned = DocumentModelCloner.CloneEndnote(source, policy).Content.Single();
        }
        else
        {
            var source = new Footnote(1);
            source.Content.Add(paragraph);
            cloned = DocumentModelCloner.CloneFootnote(source, policy).Content.Single();
        }

        cloned.Should().NotBeSameAs(paragraph);
        AssertRevisionPolicy(cloned.Runs.Single(), policy);
        cloned.Runs[0].NestedRevision = null;
        AssertPair(paragraph.Runs.Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeCloneAndReplacementRetainBothWrappersOnSelectedAndOutsideFragments(bool reversed)
    {
        var source = ParagraphWithPair();
        var clone = DocumentModelCloner.CloneParagraphTextRange(source, reversed ? 4 : 2,
            reversed ? 2 : 4, RevisionClonePolicy.Preserve,
            formatting => formatting with { Bold = true }, preserveUnselectedText: true);

        clone.Runs.Select(run => run.Text).Should().Equal("ab", "cd", "ef");
        clone.Runs.Select(run => run.Formatting.Bold).Should().Equal(false, true, false);
        clone.Runs.Should().AllSatisfy(run => AssertPair(run));
        RevisionEditPlanner.TryReplacePlainTextRange(clone, 2, 2, "XY").Should().BeTrue();
        clone.PlainText.Should().Be("abXYef");
        clone.Runs.Should().AllSatisfy(run => AssertPair(run));
        source.PlainText.Should().Be("abcdef");
        source.Runs.Should().ContainSingle();
        AssertPair(source.Runs.Single());
    }

    [Fact]
    public void TypingInsideAPendingNestedRevisionPreservesBothSidesWithoutMarkingNewText()
    {
        var paragraph = ParagraphWithPair();

        RevisionEditPlanner.InsertText(paragraph, 3, "X", RunFormatting.Default).Should().Be(4);

        paragraph.Runs.Select(run => run.Text).Should().Equal("abc", "X", "def");
        AssertPair(paragraph.Runs[0]);
        AssertPair(paragraph.Runs[2]);
        paragraph.Runs[1].Revision.Should().Be(RevisionKind.None);
        paragraph.Runs[1].NestedRevision.Should().BeNull();
    }

    [Fact]
    public void ParagraphBreakAndTrackedFormattingKeepBothWrappersOnEveryFragment()
    {
        var paragraph = ParagraphWithPair();
        var tail = RevisionEditPlanner.SplitParagraph(paragraph, 3);
        paragraph.PlainText.Should().Be("abc");
        tail.PlainText.Should().Be("def");
        var document = new TextDocument { TrackRevisions = true };

        RevisionEditPlanner.ApplyFormattingRange(tail, 1, 2,
            formatting => formatting with { Italic = true }, document, "Formatting reviewer", InnerDate).Should().BeTrue();

        paragraph.Runs.Concat(tail.Runs).Should().AllSatisfy(run => AssertPair(run));
        tail.Runs.Select(run => run.Text).Should().Equal("d", "e", "f");
        tail.Runs[1].FormatRevision.Should().NotBeNull();
        tail.Runs[1].Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void UnicodeRangeDeletionPreservesTheNestedRevisionOfRemainingText()
    {
        var paragraph = ParagraphWithPair("a😀b");
        var range = TextElementDeletionPlanner.Plan(paragraph.PlainText, 3, backward: true)!.Value;

        TextElementDeletionPlanner.Apply(paragraph, range, trackChanges: false, "Editing reviewer", InnerDate);

        paragraph.PlainText.Should().Be("ab");
        AssertPair(paragraph.Runs.Single());
    }

    [Theory]
    [InlineData("header")]
    [InlineData("footer")]
    [InlineData("footnote")]
    [InlineData("endnote")]
    [InlineData("shape")]
    public void StorySnapshotUndoRedoRetainsTheOriginalNestedPairAfterInPlaceEdits(string location)
    {
        var document = CreateStoryDocument(location);
        var story = DocumentStories.Enumerate(document).Single();
        story.Paragraph.Runs.Clear();
        story.Paragraph.Runs.Add(PairedRun());
        var bus = new DocumentCommandBus(new Context(document));
        bus.Execute(new EditStoryParagraphCommand(story.Address, paragraph =>
        {
            paragraph.Runs[0].Text = "edited";
            paragraph.Runs[0].NestedRevision = paragraph.Runs[0].NestedRevision! with { Author = "Edited outer reviewer" };
        }));
        story.Paragraph.PlainText.Should().Be("edited");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            bus.Undo().Should().BeTrue();
            story.Paragraph.PlainText.Should().Be("abcdef");
            AssertPair(story.Paragraph.Runs.Single());
            bus.Redo().Should().BeTrue();
            story.Paragraph.PlainText.Should().Be("edited");
            story.Paragraph.Runs[0].NestedRevision!.Author.Should().Be("Edited outer reviewer");
        }
    }

    [Fact]
    public void ShapeParagraphSplitAndMergeKeepRevisionPairsThroughUndoRedo()
    {
        var shape = Shape.TextBoxWith("abcdef", 120, 40);
        shape.TextParagraphs[0].Runs.Clear();
        shape.TextParagraphs[0].Runs.Add(PairedRun());
        var document = new TextDocument();
        var host = new Paragraph();
        host.Runs.Add(Run.FromShape(shape));
        document.Blocks.Add(host);
        var bus = new DocumentCommandBus(new Context(document));

        bus.Execute(new InsertShapeTextParagraphBreakCommand(0, 0, 0, 0, 3));
        AssertSplit();
        bus.Undo().Should().BeTrue();
        shape.TextParagraphs.Should().ContainSingle();
        AssertPair(shape.TextParagraphs[0].Runs.Single());
        bus.Redo().Should().BeTrue();
        AssertSplit();
        bus.Execute(new MergeShapeTextParagraphWithPreviousCommand(0, 0, 1));
        AssertMerged();
        bus.Undo().Should().BeTrue();
        AssertSplit();
        bus.Redo().Should().BeTrue();
        AssertMerged();

        void AssertSplit()
        {
            shape.TextParagraphs.Select(paragraph => paragraph.PlainText).Should().Equal("abc", "def");
            shape.TextParagraphs.SelectMany(paragraph => paragraph.Runs).Should().AllSatisfy(run => AssertPair(run));
            host.Runs[0].Text.Should().Be(shape.PlainText);
        }
        void AssertMerged()
        {
            shape.TextParagraphs.Should().ContainSingle();
            shape.PlainText.Should().Be("abcdef");
            shape.TextParagraphs[0].Runs.Should().AllSatisfy(run => AssertPair(run));
            host.Runs[0].Text.Should().Be(shape.PlainText);
        }
    }

    private static (Block Block, Func<Block, Run> FindRun) CreateBlock(string location)
    {
        var paragraph = ParagraphWithPair();
        switch (location)
        {
            case "body": return (paragraph, block => ((Paragraph)block).Runs.Single());
            case "cell":
            case "nested-cell":
                var table = Table.Create(1, 1);
                var cell = table.Rows[0].Cells[0];
                cell.Paragraphs.Clear();
                if (location == "cell")
                {
                    cell.Paragraphs.Add(paragraph);
                    return (table, block => ((Table)block).Rows[0].Cells[0].Paragraphs.Single().Runs.Single());
                }
                var nested = Table.Create(1, 1);
                nested.Rows[0].Cells[0].Paragraphs.Clear();
                nested.Rows[0].Cells[0].Paragraphs.Add(paragraph);
                cell.NestedTables.Add(nested);
                return (table, block => ((Table)block).Rows[0].Cells[0].NestedTables.Single().Rows[0].Cells[0].Paragraphs.Single().Runs.Single());
            case "shape":
            case "grouped-shape":
                var shape = Shape.TextBoxWith("abcdef", 120, 40);
                shape.TextParagraphs.Clear();
                shape.TextParagraphs.Add(paragraph);
                var host = new Paragraph();
                if (location == "shape")
                {
                    host.Runs.Add(Run.FromShape(shape));
                    return (host, block => ((Paragraph)block).Runs.Single().Shape!.TextParagraphs.Single().Runs.Single());
                }
                var group = new DrawingGroup();
                group.Children.Add(shape);
                host.Runs.Add(Run.FromDrawingGroup(group));
                return (host, block => ((Shape)((Paragraph)block).Runs.Single().DrawingGroup!.Children.Single()).TextParagraphs.Single().Runs.Single());
            case "section-header":
                var sectionHost = new Paragraph("section body") { SectionBreak = new Section(new PageSettings()) };
                sectionHost.SectionBreak.HeadersFooters.Header = Region();
                return (sectionHost, block => ((Paragraph)block).SectionBreak!.HeadersFooters.Header!.Paragraphs.Single().Runs.Single());
            default: throw new ArgumentOutOfRangeException(nameof(location));
        }
    }

    private static TextDocument CreateStoryDocument(string location)
    {
        var document = new TextDocument();
        switch (location)
        {
            case "header": document.Header = new HeaderFooter("abcdef"); break;
            case "footer": document.Footer = new HeaderFooter("abcdef"); break;
            case "footnote": document.Footnotes[1] = new Footnote(1, "abcdef"); break;
            case "endnote": document.Endnotes[1] = new Endnote(1, "abcdef"); break;
            case "shape":
                var host = new Paragraph();
                host.Runs.Add(Run.FromShape(Shape.TextBoxWith("abcdef", 120, 40)));
                document.Blocks.Add(host);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(location));
        }
        return document;
    }

    private static Run PairedRun(string text = "abcdef") => new(text)
    {
        Revision = RevisionKind.Deleted,
        RevisionAuthor = "Inner reviewer",
        RevisionDateXml = InnerDate,
        NestedRevision = new NestedRevisionInfo(RevisionKind.Inserted, "Outer reviewer", OuterDate),
    };

    private static Paragraph ParagraphWithPair(string text = "abcdef")
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(PairedRun(text));
        return paragraph;
    }

    private static HeaderFooter Region()
    {
        var region = new HeaderFooter();
        region.Paragraphs.Add(ParagraphWithPair());
        return region;
    }

    private static void AssertRevisionPolicy(Run run, RevisionClonePolicy policy)
    {
        if (policy == RevisionClonePolicy.Preserve) AssertPair(run);
        else
        {
            run.Revision.Should().Be(RevisionKind.None);
            run.RevisionAuthor.Should().BeNull();
            run.RevisionDateXml.Should().BeNull();
            run.NestedRevision.Should().BeNull();
        }
    }

    private static void AssertPair(Run run)
    {
        run.Revision.Should().Be(RevisionKind.Deleted);
        run.RevisionAuthor.Should().Be("Inner reviewer");
        run.RevisionDateXml.Should().Be(InnerDate);
        run.NestedRevision.Should().Be(new NestedRevisionInfo(RevisionKind.Inserted, "Outer reviewer", OuterDate));
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }
}
