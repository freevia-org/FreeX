namespace FreeW.Core.Model.Tests;

public sealed class TextDocumentStoryTraversalAdoptionTests
{
    [Fact]
    public void AuditedParagraphConsumers_DelegateToTheSharedTraversalWithExplicitScopeContracts()
    {
        var inspector = ReadSource("freew", "FreeW.Core.Model", "DocumentInspector.cs");
        var readAloud = ReadSource("freew", "FreeW.Core.Model", "ReadAloud.cs");
        var revisions = ReadSource("freew", "FreeW.Core.Model", "RevisionList.cs");
        var crossReferences = ReadSource("freew", "FreeW.Core.Model", "CrossReferenceCommands.cs");
        var equations = ReadSource("freew", "FreeW.App.Presentation", "DocumentView", "EquationVisualPlanner.cs");
        var numbering = ReadSource("freew", "FreeW.App.Presentation", "DocumentView", "PreservedNumberingMarkerPlanner.cs");
        var compatibility = ReadSource("freew", "FreeW.App.Presentation", "Shell", "DocumentSaveCompatibilityPlanner.cs");

        foreach (var source in new[]
                 {
                     inspector, readAloud, revisions, crossReferences, equations, numbering, compatibility,
                 })
        {
            source.Should().Contain("TextDocumentStoryTraversal.Enumerate");
            source.Should().Contain("TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs");
        }

        readAloud.Should().Contain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        crossReferences.Should().Contain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        revisions.Should().Contain("TextDocumentStorySubset.All")
            .And.Contain("TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes")
            .And.Contain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        inspector.Should().Contain("TextDocumentStorySubset.Body")
            .And.Contain("TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes")
            .And.Contain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        compatibility.Should().Contain("comment => comment.ThreadInOrder()")
            .And.Contain("comment => comment.Content");

        foreach (var bodyOnlyWithoutTextBoxes in new[]
                 {
                     readAloud, crossReferences, equations, numbering,
                 })
        {
            bodyOnlyWithoutTextBoxes.Should().NotContain("TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes")
                .And.NotContain("TextDocumentStoryTraversalOptions.IncludeTextBoxes");
        }

        equations.Should().NotContain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        numbering.Should().NotContain("TextDocumentStoryTraversalOptions.IncludeNestedTables");
        compatibility.Should().NotContain("TextDocumentStoryTraversalOptions.IncludeNestedTables")
            .And.NotContain("TextDocumentStoryTraversalOptions.IncludeShapeTextBoxes")
            .And.NotContain("TextDocumentStoryTraversalOptions.IncludeTextBoxes");

        readAloud.Should().NotContain("private static IEnumerable<Paragraph> EnumerateParagraphs");
        crossReferences.Should().NotContain("private static IEnumerable<Paragraph> EnumerateBodyParagraphs");
        equations.Should().NotContain("private static IEnumerable<Paragraph> EnumerateParagraphs");
        numbering.Should().NotContain("private static IEnumerable<Paragraph> EnumerateParagraphs");
        compatibility.Should().NotContain("private static IEnumerable<Paragraph> EnumerateBodyParagraphs")
            .And.NotContain("private static IEnumerable<Paragraph> EnumerateHeaderFooterParagraphs");

        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        File.Exists(Path.Combine(root, "freew", "FreeW.Core.Model", "BodyParagraphWalk.cs"))
            .Should().BeFalse("TextDocumentStoryTraversal now owns the audited paragraph walks");
    }

    /// <summary>
    /// r176: this test previously asserted the OPPOSITE of its last line -- that a comment anchored inside
    /// a text box embedded in a header SURVIVED RemoveComments with its CommentId intact. That was never an
    /// invariant worth keeping: RemoveComments clears TextDocument.Comments first, so a surviving CommentId
    /// is a DANGLING reference, and the docx writer still emits its w:commentRangeStart/End/
    /// w:commentReference for it -- a package Word must repair, which RemoveComments' own doc comment
    /// promises never to produce. The assertion was a characterization of the traversal scope as it stood
    /// during the TextDocumentStoryTraversal adoption, and r174 then cited it as the reason not to widen
    /// the removal walk while widening the prune's. Both walks are now one, so the anchor is cleared
    /// wherever it lives.
    /// </summary>
    [Fact]
    public void DocumentInspector_ClearsCommentAnchorsInHeaderTextBoxes()
    {
        var bodyShapeParagraph = CommentParagraph(1);
        var headerShapeParagraph = CommentParagraph(2);
        var directHeaderParagraph = CommentParagraph(3);
        directHeaderParagraph.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { headerShapeParagraph } },
        });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ShapeHost(bodyShapeParagraph));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(directHeaderParagraph);
        document.Comments[1] = new Comment(1, "body shape", "A", "A");
        document.Comments[2] = new Comment(2, "header shape", "A", "A");
        document.Comments[3] = new Comment(3, "header", "A", "A");

        DocumentInspector.RemoveComments(document);

        bodyShapeParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        directHeaderParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        headerShapeParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference,
            "a comment anchored in a header text box must be cleared too -- Comments was already emptied, " +
            "so leaving its CommentId behind is a dangling w:commentReference the writer still emits");
        document.Comments.Should().BeEmpty();
    }

    [Fact]
    public void DeleteComment_PreservesNestedTableReachWithoutExpandingTextBoxes()
    {
        var nestedParagraph = CommentParagraph(7);
        var shapeParagraph = CommentParagraph(7);
        var outer = Table.Create(1, 1);
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = nestedParagraph;
        outer.Rows[0].Cells[0].NestedTables.Add(nested);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(outer);
        document.Blocks.Add(ShapeHost(shapeParagraph));
        document.Comments[7] = new Comment(7, "comment", "A", "A");

        new DocumentCommandBus(new Context(document)).Execute(new DeleteCommentCommand(7));

        nestedParagraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference);
        shapeParagraph.Runs.Should().Contain(run => run.CommentId == 7);
    }

    [Fact]
    public void DeleteNote_ExpandsShapeTextBoxesButExcludesNoteContentStories()
    {
        var bodyShapeParagraph = NoteParagraph(4);
        var headerShapeParagraph = NoteParagraph(4);
        var endnoteParagraph = NoteParagraph(4);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ShapeHost(bodyShapeParagraph));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(ShapeHost(headerShapeParagraph));
        document.Footnotes[4] = new Footnote(4, "deleted");
        var endnote = new Endnote(9);
        endnote.Content.Add(endnoteParagraph);
        document.Endnotes[9] = endnote;

        new DocumentCommandBus(new Context(document)).Execute(new DeleteNoteCommand(4, footnote: true));

        bodyShapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
        headerShapeParagraph.Runs.Should().NotContain(run => run.FootnoteId == 4);
        endnoteParagraph.Runs.Should().Contain(run => run.FootnoteId == 4);
    }

    /// <summary>
    /// r176, stated as the invariant rather than as a traversal detail: after RemoveComments no run
    /// anywhere in the document may still carry a CommentId, because Comments has been emptied and any
    /// surviving id is a dangling w:commentReference the docx writer still serialises -- a package Word
    /// must repair. Covers every store the walk reaches, including the text boxes embedded in a header
    /// and in a footnote, which is where the split walks used to let one through.
    /// </summary>
    [Fact]
    public void RemoveComments_LeavesNoDanglingCommentIdInAnyStore()
    {
        var bodyShape = CommentParagraph(1);
        var headerShape = CommentParagraph(2);
        var footnoteShape = CommentParagraph(3);
        var directFooter = CommentParagraph(4);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(ShapeHost(bodyShape));
        document.Header = new HeaderFooter();
        document.Header.Paragraphs.Add(ShapeHost(headerShape));
        document.Footer = new HeaderFooter();
        document.Footer.Paragraphs.Add(directFooter);
        var footnote = new Footnote(8, "note");
        footnote.Content.Add(ShapeHost(footnoteShape));
        document.Footnotes[8] = footnote;

        for (var id = 1; id <= 4; id++)
            document.Comments[id] = new Comment(id, $"c{id}", "A", "A");

        DocumentInspector.RemoveComments(document);

        document.Comments.Should().BeEmpty();
        foreach (var paragraph in new[] { bodyShape, headerShape, footnoteShape, directFooter })
        {
            paragraph.Runs.Should().OnlyContain(run => run.CommentId == null && !run.IsCommentReference,
                "every comment anchor must be cleared wherever it lives, or the writer emits a " +
                "w:commentReference with no matching comment entry");
        }
    }

    private static Paragraph ShapeHost(Paragraph content)
    {
        var host = new Paragraph();
        host.Runs.Add(new Run(string.Empty)
        {
            Shape = new Shape { TextParagraphs = { content } },
        });
        return host;
    }

    private static Paragraph CommentParagraph(int id)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text") { CommentId = id });
        paragraph.Runs.Add(Run.CommentReference(id));
        return paragraph;
    }

    private static Paragraph NoteParagraph(int id)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text"));
        paragraph.Runs.Add(Run.FootnoteReference(id));
        return paragraph;
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
