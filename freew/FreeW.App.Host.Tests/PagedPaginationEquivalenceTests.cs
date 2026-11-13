using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W8 (paged-edit performance): the optimised page assignment -- no whole-document footnote reserve
/// pass and the early "does any page need a reserve" decision -- must produce exactly the reference
/// (original) assignment, before and after arbitrary edits. The cloned blocks the print path draws are
/// unchanged, so print output is too.
/// </summary>
public sealed class PagedPaginationEquivalenceTests(ITestOutputHelper output)
{
    [StaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OptimisedAssignment_MatchesReference_AfterRandomEdits(int seed)
    {
        var random = new Random(seed);
        var document = BuildDocument(random);
        var editor = new DocumentView();
        editor.LoadModel(document);
        AssertEquivalent(editor, $"seed {seed} initial");

        for (var round = 0; round < 4; round++)
        {
            ApplyRandomEdit(editor.Model, random);
            editor.LoadModel(editor.Model);
            AssertEquivalent(editor, $"seed {seed} edit {round}");
        }
    }

    /// <summary>
    /// Opt-in corpus sweep: set <c>FREEW_PAGED_CORPUS_DIR</c> to a folder of .docx files.
    /// </summary>
    [StaFact]
    public void OptimisedAssignment_MatchesReference_OnCorpus()
    {
        var directory = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        foreach (var path in Directory.GetFiles(directory, "*.docx").OrderBy(file => new FileInfo(file).Length))
        {
            TextDocument document;
            try
            {
                document = DocxReader.Read(path);
            }
            catch (Exception ex)
            {
                output.WriteLine($"{Path.GetFileName(path)}: unreadable ({ex.GetType().Name})");
                continue;
            }

            var editor = new DocumentView();
            editor.LoadModel(document);
            AssertEquivalent(editor, Path.GetFileName(path));
            var random = new Random(7);
            ApplyRandomEdit(editor.Model, random);
            editor.LoadModel(editor.Model);
            AssertEquivalent(editor, Path.GetFileName(path) + " after edit");
        }
    }

    private void AssertEquivalent(DocumentView editor, string label)
    {
        int[] reference;
        int referencePages;
        PaginationEngine.UseReferencePagination = true;
        try
        {
            reference = PaginationEngine.ComputeBlockPageAssignment(editor, out referencePages, null);
        }
        finally
        {
            PaginationEngine.UseReferencePagination = false;
        }

        var optimised = PaginationEngine.ComputeBlockPageAssignment(editor, out var optimisedPages, null);
        var layoutOnly = PaginationEngine.ComputeBlockPageAssignment(editor, out var layoutOnlyPages, null, layoutOnly: true);
        output.WriteLine($"{label}: blocks={reference.Length} pages={referencePages}");

        optimised.Should().Equal(reference, $"{label}: optimised assignment");
        optimisedPages.Should().Be(referencePages, $"{label}: optimised page count");
        layoutOnly.Should().Equal(reference, $"{label}: layout-only (direct render) assignment");
        layoutOnlyPages.Should().Be(referencePages, $"{label}: layout-only page count");
    }

    private static TextDocument BuildDocument(Random random)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 612;
        document.Page.HeightPt = 500;
        document.Page.MarginLeftPt = 54;
        document.Page.MarginRightPt = 54;
        document.Page.MarginTopPt = 54;
        document.Page.MarginBottomPt = 54;

        var nextNote = 1;
        for (var i = 0; i < 70; i++)
        {
            var paragraph = new Paragraph(Sentence(random, random.Next(3, 70)));
            switch (random.Next(10))
            {
                case 0:
                    document.Footnotes[nextNote] = new Footnote(nextNote, Sentence(random, random.Next(5, 80)));
                    paragraph.Runs.Add(Run.FootnoteReference(nextNote));
                    nextNote++;
                    break;
                case 1:
                case 2:
                    paragraph.Formatting = paragraph.Formatting with { ListKind = ListKind.Bullet };
                    break;
                case 3 when i > 0:
                    paragraph.Formatting = paragraph.Formatting with { PageBreakBefore = true };
                    break;
            }
            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static void ApplyRandomEdit(TextDocument document, Random random)
    {
        var paragraphs = document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.SectionBreak is null && paragraph.Runs.Any(run => run.FootnoteId is null))
            .ToList();
        if (paragraphs.Count == 0)
            return;

        var target = paragraphs[random.Next(paragraphs.Count)];
        var index = document.Blocks.IndexOf(target);
        switch (random.Next(3))
        {
            case 0:
                // Typing that grows a paragraph by several lines.
                document.Blocks[index] = WithAppendedText(target, " " + Sentence(random, random.Next(20, 90)));
                break;
            case 1:
                document.Blocks.Insert(index, new Paragraph(Sentence(random, random.Next(10, 60))));
                break;
            default:
                if (document.Blocks.Count > 2 && target.Runs.All(run => run.FootnoteId is null))
                    document.Blocks.RemoveAt(index);
                else
                    document.Blocks.Insert(index + 1, new Paragraph(Sentence(random, 12)));
                break;
        }
    }

    private static Paragraph WithAppendedText(Paragraph source, string text)
    {
        var paragraph = new Paragraph { Formatting = source.Formatting, StyleId = source.StyleId };
        paragraph.Runs.AddRange(source.Runs);
        paragraph.Runs.Add(new Run(text));
        return paragraph;
    }

    private static string Sentence(Random random, int words)
    {
        string[] vocabulary = ["page", "layout", "editor", "paragraph", "footnote", "measure", "flow", "word", "line", "block", "reflow", "stable"];
        return string.Join(" ", Enumerable.Range(0, words).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
    }
}
