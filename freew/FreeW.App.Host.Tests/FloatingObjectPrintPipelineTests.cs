using System;
using System.Linq;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// freew-textbox-flow F1: a floating drawing object (a text box, an image with non-inline wrap, a
/// chart, WordArt, SmartArt, or a drawing group) rendered correctly on screen via
/// <c>DocumentView.SyncFloatingObjectsCanvas</c>'s overlay canvas, but that canvas is a Grid sibling the
/// print/preview/PDF/XPS pipeline never touches: <see cref="PrintLayout.BuildPaginator"/> (the single hub
/// behind Print, Print Preview, PDF export, and XPS export -- see <see cref="PrintPreviewWindow"/>,
/// <c>MainWindow.Print</c>, <c>PdfExport</c>, <c>XpsExport</c>) built its printed <see cref="System.Windows.Documents.FlowDocument"/>
/// purely from a clone of the editor's block content, so a floating object was completely and silently
/// absent from every printed/exported page. These tests exercise the real production entry point
/// (<see cref="PrintLayout.BuildPaginator"/> plus the returned paginator's
/// <see cref="System.Windows.Documents.DocumentPaginator.GetPage"/>) and walk the actual rendered
/// <see cref="Visual"/> tree for the floating object's own text, rather than asserting against a helper
/// that supplies it directly.
/// </summary>
public sealed class FloatingObjectPrintPipelineTests
{
    [StaFact]
    public void BuildPaginator_FloatingTextBox_DrawsShapeTextOnPrintedPage()
    {
        var view = MakeEditorWithFloatingTextBox();

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsGlyphRunText(page.Visual, "HELLO FLOATING TEXTBOX"),
            "Print/Print Preview/PDF/XPS must draw a floating text box's own text onto the printed page.");
    }

    [StaFact]
    public void BuildPaginator_PlainDocumentWithNoFloatingObjects_RendersOwnTextUnaffected()
    {
        // Sibling/no-regression: the overwhelming common case, a document with nothing floating at all,
        // must keep rendering exactly as before -- this only proves the new floating-object code path is
        // inert (and does not throw) when there is nothing for it to place.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("ORDINARY BODY TEXT"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);

        var paginator = PrintLayout.BuildPaginator(view);
        var page = paginator.GetPage(0);

        Assert.True(
            ContainsGlyphRunText(page.Visual, "ORDINARY BODY TEXT"),
            "A plain document's own text must still render on the printed page.");
    }

    [StaFact]
    public void BuildPaginator_FloatingObjectAnchoredOnSecondPage_DrawsOnlyOnItsOwnRealPage()
    {
        // The object must land on the REAL page its anchor paragraph is actually paginated onto, not
        // always page 0 (which would just be a different way of silently losing it from every page but
        // the first) and not on every page.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 400;
        doc.Page.MarginLeftPt = 36;
        doc.Page.MarginRightPt = 36;
        doc.Page.MarginTopPt = 36;
        doc.Page.MarginBottomPt = 36;

        for (var i = 1; i <= 40; i++)
            doc.Blocks.Add(new Paragraph($"Filler body line number {i:00} pushing content down the page."));

        var anchorParagraph = new Paragraph();
        var shape = Shape.TextBoxWith("SECOND PAGE TEXTBOX", 150, 50, "FFFF00");
        shape.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Page,
            HorizontalOffsetPt = 20,
            VerticalAnchor = VerticalAnchor.Page,
            VerticalOffsetPt = 20,
        };
        anchorParagraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(anchorParagraph);

        var view = new DocumentView();
        view.LoadModel(doc);

        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        Assert.True(paginator.PageCount >= 2, "test setup must actually force this document onto at least two pages.");

        var lastPageIndex = paginator.PageCount - 1;
        Assert.False(
            ContainsGlyphRunText(paginator.GetPage(0).Visual, "SECOND PAGE TEXTBOX"),
            "A floating object anchored to a paragraph on a later page must not also be drawn on page 1.");
        Assert.True(
            ContainsGlyphRunText(paginator.GetPage(lastPageIndex).Visual, "SECOND PAGE TEXTBOX"),
            $"A floating object anchored to a paragraph on the last real page (index {lastPageIndex}) must be drawn there.");
    }

    private static DocumentView MakeEditorWithFloatingTextBox()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // A tall page keeps everything on one physical page -- this test is about the object appearing
        // at all, not about which page it lands on (covered separately below).
        doc.Page.HeightPt = 5000;

        var intro = new Paragraph();
        intro.Runs.Add(new Run("Intro paragraph."));
        doc.Blocks.Add(intro);

        var anchorParagraph = new Paragraph();
        var shape = Shape.TextBoxWith("HELLO FLOATING TEXTBOX", 200, 60, "FFFF00");
        shape.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Margin,
            HorizontalOffsetPt = 20,
            VerticalAnchor = VerticalAnchor.Paragraph,
            VerticalOffsetPt = 10,
        };
        anchorParagraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(anchorParagraph);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    /// <summary>Finds a glyph run anywhere under <paramref name="visual"/> whose original characters
    /// contain <paramref name="containingText"/> -- the real rendered text, independent of any
    /// approximation this file's production code makes for its position.</summary>
    private static bool ContainsGlyphRunText(Visual? visual, string containingText)
    {
        if (visual is null)
            return false;
        if (visual is DrawingVisual dv
            && VisualTreeHelper.GetDrawing(dv) is { } drawing
            && ContainsGlyphRunText(drawing, containingText))
            return true;

        var count = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is Visual child
                && ContainsGlyphRunText(child, containingText))
                return true;
        }
        return false;
    }

    private static bool ContainsGlyphRunText(Drawing drawing, string containingText)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                return group.Children.Any(child => ContainsGlyphRunText(child, containingText));
            case GlyphRunDrawing { GlyphRun.Characters: { } characters }:
                return new string(characters.ToArray()).Contains(containingText, StringComparison.Ordinal);
            default:
                return false;
        }
    }
}
