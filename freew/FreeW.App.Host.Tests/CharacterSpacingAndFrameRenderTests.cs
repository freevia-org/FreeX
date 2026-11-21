using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using WpfDocs = System.Windows.Documents;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Page renders paint expanded/condensed character spacing (w:spacing) with per-word spaced text boxes and draw
/// one border box around consecutive paragraphs of the same frame; the editor keeps the plain, editable runs.
/// </summary>
public sealed class CharacterSpacingAndFrameRenderTests
{
    private static DocumentView MakeSpacedView(bool pageOnly, double spacingPt)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Spaced words")
        {
            Formatting = RunFormatting.Default with { CharacterSpacingPt = spacingPt, FontSizePt = 12 }
        });
        doc.Blocks.Add(paragraph);
        var view = new DocumentView { RenderPageBreakMarkers = !pageOnly };
        view.LoadModel(doc);
        return view;
    }

    private static System.Collections.Generic.IEnumerable<WpfDocs.Inline> Flatten(WpfDocs.Inline inline) =>
        inline is WpfDocs.Span span ? span.Inlines.SelectMany(Flatten).Prepend(inline) : [inline];

    private static System.Collections.Generic.List<SpacedTextBlock> SpacedBoxes(DocumentView view) => view.Document.Blocks
        .OfType<WpfDocs.Paragraph>()
        .SelectMany(paragraph => paragraph.Inlines.SelectMany(Flatten))
        .OfType<WpfDocs.InlineUIContainer>()
        .Select(container => container.Child)
        .OfType<SpacedTextBlock>()
        .ToList();

    [StaFact]
    public void PageRender_ExpandedSpacing_UsesSpacedBoxesCarryingThePerCharacterAdvance()
    {
        var boxes = SpacedBoxes(MakeSpacedView(pageOnly: true, spacingPt: 3));

        Assert.Equal(2, boxes.Count);
        Assert.Equal("Spaced words", string.Concat(boxes.Select(box => box.Text)));
        Assert.All(boxes, box => Assert.Equal(3 * 96.0 / 72.0, box.CharacterAdvance, 6));
    }

    [StaFact]
    public void PageRender_NoSpacing_KeepsPlainRuns_AndTheEditorKeepsTheEditableRun()
    {
        Assert.Empty(SpacedBoxes(MakeSpacedView(pageOnly: true, spacingPt: 0)));

        var editor = MakeSpacedView(pageOnly: false, spacingPt: 3);
        Assert.Empty(SpacedBoxes(editor));
        Assert.Contains(
            editor.Document.Blocks.OfType<WpfDocs.Paragraph>().SelectMany(p => p.Inlines.OfType<WpfDocs.Run>()),
            run => run.Text == "Spaced words");
    }

    [StaFact]
    public void SpacedTextBlock_ExpandedAndCondensed_ChangeTheMeasuredWidthByTheAdvancePerCharacter()
    {
        static double Measure(double advance)
        {
            var box = new SpacedTextBlock { Text = "abcd", FontSize = 16, CharacterAdvance = advance };
            box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return box.DesiredSize.Width;
        }

        var natural = Measure(0);
        Assert.Equal(natural + 4 * 5, Measure(5), 1);
        Assert.Equal(natural - 4 * 1, Measure(-1), 1);
    }

    [StaFact]
    public void PageRender_CondensedSpacing_NarrowsTheLineAndExpandedWidensIt()
    {
        double Width(double spacingPt)
        {
            var width = 0.0;
            foreach (var box in SpacedBoxes(MakeSpacedView(pageOnly: true, spacingPt)))
            {
                box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width += box.DesiredSize.Width;
            }

            return width;
        }

        Assert.True(Width(2) > Width(0.5));
        Assert.True(Width(-1) < Width(0.5));
    }

    [StaFact]
    public void SpaceInlineCharacters_LeavesRunsWithTabsOrLineBreaksAlone()
    {
        var run = new WpfDocs.Run("a\tb");
        Assert.Same(run, DocumentView.SpaceInlineCharacters(run, 4, 16));
    }

    private static DocumentView MakeFramedView(bool pageOnly, bool sameFrame)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var frame = ParagraphFrame.FromAttributes([new("wrap", "around"), new("hAnchor", "text"), new("y", "1")]);
        var other = ParagraphFrame.FromAttributes([new("wrap", "around"), new("hAnchor", "text"), new("y", "9")]);
        for (var i = 0; i < 3; i++)
        {
            var paragraph = new Paragraph
            {
                Frame = i == 2 && !sameFrame ? other : frame,
                Formatting = ParagraphFormatting.Default with
                {
                    Border = new ParagraphBorder("#000000", 0.75),
                    SpaceAfterPt = 0,
                    SpaceAfterIsSet = true,
                    IndentLeftPt = 51,
                    FirstLineIndentPt = -51
                }
            };
            paragraph.Runs.Add(new Run("line " + i));
            doc.Blocks.Add(paragraph);
        }

        var view = new DocumentView { RenderPageBreakMarkers = !pageOnly };
        view.LoadModel(doc);
        return view;
    }

    private static WpfDocs.Paragraph[] Paragraphs(DocumentView view) =>
        view.Document.Blocks.OfType<WpfDocs.Paragraph>().ToArray();

    [StaFact]
    public void PageRender_ParagraphsOfOneFrame_ShareASingleBorderBox()
    {
        var paragraphs = Paragraphs(MakeFramedView(pageOnly: true, sameFrame: true));

        Assert.Equal(3, paragraphs.Length);
        Assert.True(paragraphs[0].BorderThickness.Top > 0);
        Assert.Equal(0, paragraphs[0].BorderThickness.Bottom);
        Assert.Equal(0, paragraphs[1].BorderThickness.Top);
        Assert.Equal(0, paragraphs[1].BorderThickness.Bottom);
        Assert.Equal(0, paragraphs[2].BorderThickness.Top);
        Assert.True(paragraphs[2].BorderThickness.Bottom > 0);
        Assert.All(paragraphs, paragraph => Assert.True(paragraph.BorderThickness.Left > 0));
    }

    [StaFact]
    public void PageRender_ADifferentFrame_StartsANewBox()
    {
        var paragraphs = Paragraphs(MakeFramedView(pageOnly: true, sameFrame: false));

        Assert.True(paragraphs[1].BorderThickness.Bottom > 0);
        Assert.True(paragraphs[2].BorderThickness.Top > 0);
    }

    [StaFact]
    public void PageRender_HangingFirstLine_IsEnclosedByTheLeftEdge()
    {
        var joined = Paragraphs(MakeFramedView(pageOnly: true, sameFrame: true))[1];
        var editor = Paragraphs(MakeFramedView(pageOnly: false, sameFrame: true))[1];

        Assert.Equal(0, joined.Margin.Left, 3);
        Assert.Equal(51 * 96.0 / 72.0, editor.Margin.Left, 3);
        // Text stays where it was: the margin moved into the padding.
        Assert.Equal(editor.Margin.Left + editor.Padding.Left, joined.Margin.Left + joined.Padding.Left, 3);
    }

    [StaFact]
    public void Editor_FramedParagraphs_KeepSeparateBoxes_AndTheFrameSurvivesCommit()
    {
        var view = MakeFramedView(pageOnly: false, sameFrame: true);
        var paragraphs = Paragraphs(view);
        Assert.True(paragraphs[0].BorderThickness.Bottom > 0);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().ToList();
        Assert.All(committed, paragraph => Assert.Equal("wrap=around;hAnchor=text;y=1".Length, paragraph.Frame!.Signature.Length));
        Assert.Equal(committed[0].Frame, committed[1].Frame);
    }
}
