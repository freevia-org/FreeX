using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word prints the page as displayed: in All Markup every insertion and deletion stays marked up inline
/// (deletions struck through, not dropped) and each changed block gets a "changed lines" rule in the
/// left margin, while a comment's range is shown only together with its margin balloon -- never as a
/// highlight on its own. These exercise the real Print / Print Preview / PDF / XPS entry point,
/// <see cref="PrintLayout.BuildPaginator"/>, and inspect the page it actually renders.
/// </summary>
public sealed class PrintReviewMarkupTests
{
    private static readonly Color CommentHighlight = Color.FromRgb(0xFF, 0xF4, 0xCE);

    [StaFact]
    public void AllMarkup_PrintsDeletionsStruckAndInsertionsUnderlined_WithAChangedLineRule()
    {
        var view = MakeTrackedEditor();
        view.ApplyDisplayForReview(ReviewDisplayMode.AllMarkup);

        var drawings = PageDrawings(PrintLayout.BuildPaginator(view).GetPage(0).Visual);

        var text = GlyphText(drawings);
        Assert.Contains("ADDEDWORDS", text);
        Assert.Contains("REMOVEDWORDS", text);

        // Insertion: author colour + underline. Deletion: author colour + strikethrough. Both draw a
        // horizontal decoration line in a colour that is not the plain black body text.
        var colouredGlyphRuns = drawings.OfType<GlyphRunDrawing>()
            .Where(run => run.ForegroundBrush is SolidColorBrush { Color: var c } && c != Colors.Black)
            .Select(run => new string(run.GlyphRun.Characters.ToArray()))
            .ToList();
        Assert.Contains(colouredGlyphRuns, run => run.Contains("ADDEDWORDS", StringComparison.Ordinal));
        Assert.Contains(colouredGlyphRuns, run => run.Contains("REMOVEDWORDS", StringComparison.Ordinal));

        var (marginLeft, _, _, _) = PageLayout.MarginsDip(view.Model.Page);
        var rule = Assert.Single(ChangedLineRules(drawings));
        Assert.InRange(rule.StartPoint.X, marginLeft / 2 - 1, marginLeft / 2 + 1);
        Assert.True(rule.EndPoint.Y > rule.StartPoint.Y);
    }

    [StaFact]
    public void AllMarkup_UnchangedDocument_PrintsNoChangedLineRule()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("nothing has changed here"));
        var view = new DocumentView();
        view.LoadModel(doc);

        var drawings = PageDrawings(PrintLayout.BuildPaginator(view).GetPage(0).Visual);

        Assert.Empty(ChangedLineRules(drawings));
    }

    [StaFact]
    public void NoMarkup_PrintsNoChangedLineRuleAndNoDeletedText()
    {
        var view = MakeTrackedEditor();
        view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);

        var drawings = PageDrawings(PrintLayout.BuildPaginator(view).GetPage(0).Visual);

        Assert.Empty(ChangedLineRules(drawings));
        Assert.DoesNotContain(
            drawings.OfType<GlyphRunDrawing>(),
            run => run.ForegroundBrush is SolidColorBrush { Color.A: > 0 }
                && new string(run.GlyphRun.Characters.ToArray()).Contains("REMOVEDWORDS", StringComparison.Ordinal));
    }

    [StaFact]
    public void CommentRange_IsNotHighlightedOnThePrintedPage_ButStaysHighlightedInTheEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Comments[1] = new Comment(1, "please check", "Reviewer");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("before "));
        paragraph.Runs.Add(new Run("COMMENTED") { CommentId = 1 });
        paragraph.Runs.Add(new Run(" after"));
        doc.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadModel(doc);
        Assert.True(EditorHasCommentHighlight(view), "test setup: the editor highlights the commented range");

        var drawings = PageDrawings(PrintLayout.BuildPaginator(view).GetPage(0).Visual);

        Assert.Contains("COMMENTED", GlyphText(drawings));
        Assert.DoesNotContain(
            drawings.OfType<GeometryDrawing>(),
            drawing => drawing.Brush is SolidColorBrush { Color: var c } && c == CommentHighlight);
        Assert.True(EditorHasCommentHighlight(view), "printing must leave the live editor's highlight in place");
    }

    private static bool EditorHasCommentHighlight(DocumentView view) =>
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<System.Windows.Documents.Run>())
            .Any(run => run.Background is SolidColorBrush { Color: var c } && c == CommentHighlight);

    private static DocumentView MakeTrackedEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("base "));
        paragraph.Runs.Add(new Run("ADDEDWORDS") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice" });
        paragraph.Runs.Add(new Run(" middle "));
        paragraph.Runs.Add(new Run("REMOVEDWORDS") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(new Paragraph("an untouched paragraph"));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static IEnumerable<LineGeometry> ChangedLineRules(IReadOnlyList<Drawing> drawings) =>
        drawings.OfType<GeometryDrawing>()
            .Where(drawing => drawing.Geometry is LineGeometry
                && drawing.Pen is { Brush: SolidColorBrush { Color: var c } }
                && c == HeaderFooterPaginator.ChangedLineRuleColor)
            .Select(drawing => (LineGeometry)drawing.Geometry)
            .Where(line => Math.Abs(line.StartPoint.X - line.EndPoint.X) < 0.01);

    private static string GlyphText(IReadOnlyList<Drawing> drawings)
    {
        var sb = new StringBuilder();
        foreach (var run in drawings.OfType<GlyphRunDrawing>())
            sb.Append(run.GlyphRun.Characters.ToArray());
        return sb.ToString();
    }

    /// <summary>Every leaf drawing of a rendered page's visual tree.</summary>
    private static IReadOnlyList<Drawing> PageDrawings(Visual root)
    {
        var result = new List<Drawing>();
        Walk(root);
        return result;

        void Walk(Visual visual)
        {
            Flatten(VisualTreeHelper.GetDrawing(visual));
            var count = VisualTreeHelper.GetChildrenCount(visual);
            for (var i = 0; i < count; i++)
                if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                    Walk(child);
        }

        void Flatten(Drawing? drawing)
        {
            switch (drawing)
            {
                case System.Windows.Media.DrawingGroup group:
                    foreach (var child in group.Children)
                        Flatten(child);
                    break;
                case not null:
                    result.Add(drawing);
                    break;
            }
        }
    }
}
