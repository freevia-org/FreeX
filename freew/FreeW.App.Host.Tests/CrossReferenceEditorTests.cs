using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Editor-level coverage for <see cref="DocumentView.InsertCrossReference"/> (Word's References &gt;
/// Cross-reference). Runs on STA because it drives the real WPF <see cref="DocumentView"/>. An inserted
/// cross-reference must materialise as a model <see cref="Run.CrossReference"/> field carrying the chosen
/// kind/insert-as/hyperlink, must auto-bookmark a body target that lacks an anchor (so REF/PAGEREF
/// resolves), and must point a NOTEREF at the note id for foot/endnotes.
/// </summary>
public sealed class CrossReferenceEditorTests
{
    private static TextDocument HeadingModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter One") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body where the reference goes."));
        return doc;
    }

    private static Run InsertedField(DocumentView view) =>
        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.CrossReference is not null);

    [StaFact]
    public void InsertCrossReference_Heading_WritesRefFieldAndAutoBookmarksTarget()
    {
        var view = new DocumentView();
        view.LoadModel(HeadingModel());

        var target = CrossReferences.Targets(view.Model, CrossRefType.Heading).Single();
        view.InsertCrossReference(CrossRefType.Heading, target, CrossRefInsertAs.Text, hyperlink: true);
        view.CommitToModel();

        var field = InsertedField(view).CrossReference!;
        field.Kind.Should().Be(CrossRefFieldKind.Ref);
        field.InsertAs.Should().Be(CrossRefInsertAs.Text);
        field.Hyperlink.Should().BeTrue();

        // The heading paragraph (which had no bookmark) gets an auto "_Ref…" anchor the field targets.
        var headingParagraph = (Paragraph)view.Model.Blocks[0];
        headingParagraph.BookmarkName.Should().NotBeNullOrEmpty();
        field.Target.Should().Be(headingParagraph.BookmarkName);
    }

    [StaFact]
    public void InsertCrossReference_WpfMutationPreservesExistingBookmarksAndCachesText()
    {
        var doc = HeadingModel();
        var heading = (Paragraph)doc.Blocks[0];
        heading.BookmarkNames.Add("chapter");
        heading.BookmarkNames.Add("_Ref2");
        ((Paragraph)doc.Blocks[1]).BookmarkName = "_Ref1";

        var view = new DocumentView();
        view.LoadModel(doc);
        view.InsertCrossReference(
            CrossRefType.Heading,
            new CrossRefTarget("Chapter One", Anchor: null, BlockIndex: 0),
            CrossRefInsertAs.Text,
            hyperlink: false);
        view.CommitToModel();

        var field = InsertedField(view);
        field.Text.Should().Be("Chapter One");
        field.CrossReference!.Target.Should().Be("_Ref3");
        ((Paragraph)view.Model.Blocks[0]).BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");

        view.Undo();
        ((Paragraph)view.Model.Blocks[0]).BookmarkNames.Should().Equal("chapter", "_Ref2");
        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.CrossReference != null);

        view.Redo();
        ((Paragraph)view.Model.Blocks[0]).BookmarkNames.Should().Equal("chapter", "_Ref2", "_Ref3");
        InsertedField(view).CrossReference!.Target.Should().Be("_Ref3");
    }

    [StaFact]
    public void InsertCrossReference_PageNumber_WritesPageRefField()
    {
        var view = new DocumentView();
        view.LoadModel(HeadingModel());

        var target = CrossReferences.Targets(view.Model, CrossRefType.Heading).Single();
        view.InsertCrossReference(CrossRefType.Heading, target, CrossRefInsertAs.PageNumber, hyperlink: false);
        view.CommitToModel();

        InsertedField(view).CrossReference!.Kind.Should().Be(CrossRefFieldKind.PageRef);
    }

    [StaFact]
    public void InsertCrossReference_Footnote_WritesNoteRefOverNoteId()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body."));
        doc.Footnotes[1] = new Footnote(1, "the note");

        var view = new DocumentView();
        view.LoadModel(doc);

        var target = CrossReferences.Targets(view.Model, CrossRefType.Footnote).Single();
        view.InsertCrossReference(CrossRefType.Footnote, target, CrossRefInsertAs.Text, hyperlink: true);
        view.CommitToModel();

        var field = InsertedField(view).CrossReference!;
        field.Kind.Should().Be(CrossRefFieldKind.NoteRef);
        field.Target.Should().Be("1");
    }

    [StaFact]
    public void UpdateFields_CrossReference_RefreshesCachedHeadingText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1", BookmarkName = "_Ref1" });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("See "),
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.Ref, "_Ref1", CrossRefInsertAs.Text, Hyperlink: true),
                    "Chapter One")
            }
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        var fieldRun = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Single(r => r.CrossReference is not null);
        fieldRun.Text.Should().Be("Chapter Two");
    }

    [StaFact]
    public void UpdateFields_PageReferenceUsesTargetPhysicalPage()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("See page "),
                Run.CrossReferenceFieldRun(
                    new CrossReferenceField(CrossRefFieldKind.PageRef, "_Ref2", CrossRefInsertAs.PageNumber, Hyperlink: false),
                    "9"),
                new Run(" and imported "),
                Run.ComplexFieldRun(" PAGEREF _Ref2 ", "9")
                }
        });
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(new Paragraph("Target")
        {
            BookmarkName = "_Ref2",
        });
        doc.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        doc.Page.PageNumberStartAt = 4;

        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        InsertedField(view).Text.Should().Be("V");
        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "PAGEREF")
            .Text.Should().Be("V");
    }
}
