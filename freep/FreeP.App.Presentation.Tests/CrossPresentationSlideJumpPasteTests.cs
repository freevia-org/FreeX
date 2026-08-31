using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// The two shell-level paste paths must orphan an internal slide jump the destination deck
/// cannot resolve, matching what the in-canvas editors already do.
/// <para>
/// The shape path is the sharper case: shapes arriving from another FreeP instance were decoded
/// from a one-slide clipboard package, so their jump targets name slides in that package's id
/// space and match nothing here -- the link is not merely stale, it points at an id that exists
/// in no open deck.
/// </para>
/// </summary>
public sealed class CrossPresentationSlideJumpPasteTests
{
    [Fact]
    public void PasteExternalShapes_OrphansASlideJumpTheDestinationDeckCannotResolve()
    {
        var (destination, editor) = Destination();
        var shape = LinkedShape("foreign-slide-id");

        editor.PasteExternalShapes(new[] { shape });

        var link = SoleHyperlink(destination);
        link.TargetSlideId.Should().BeNull();
        link.Tooltip.Should().Be("jump");
    }

    [Fact]
    public void PasteExternalShapes_KeepsAJumpToASlideTheDestinationDeckHas()
    {
        var (destination, editor) = Destination();
        var shape = LinkedShape(destination.Slides[1].Id);

        editor.PasteExternalShapes(new[] { shape });

        SoleHyperlink(destination).TargetSlideId.Should().Be(destination.Slides[1].Id);
    }

    [Fact]
    public void PasteExternalShapes_OrphansAShapeLevelClickActionToo()
    {
        var (destination, editor) = Destination();
        var shape = LinkedShape("foreign-slide-id");
        shape.TextBody = null;
        shape.Hyperlink = new Hyperlink { TargetSlideId = "foreign-slide-id" };

        editor.PasteExternalShapes(new[] { shape });

        var pasted = destination.Slides[0].Shapes.Single();
        pasted.Hyperlink!.TargetSlideId.Should().BeNull();
    }

    [Fact]
    public void PasteExternalShapes_ReachesALinkNestedInsideAGroupedShape()
    {
        var (destination, editor) = Destination();
        var group = new SlideShape { Id = 20, Name = "Group", Kind = SlideShapeKind.Group };
        group.Children.Add(LinkedShape("foreign-slide-id"));

        editor.PasteExternalShapes(new[] { group });

        SoleHyperlink(destination).TargetSlideId.Should().BeNull();
    }

    [Fact]
    public void PasteShapes_WithinTheSameDeckKeepsItsSlideJump()
    {
        var (destination, editor) = Destination();
        var shape = LinkedShape(destination.Slides[1].Id);
        destination.Slides[0].Shapes.Add(shape);
        editor.SelectSlide(0);
        editor.Select(shape.Id);
        editor.CopySelectedShapes();

        editor.PasteShapes();

        destination.Slides[0].Shapes
            .SelectMany(s => s.TextBody!.Paragraphs)
            .SelectMany(p => p.Runs)
            .Select(r => r.Hyperlink)
            .Where(h => h is not null)
            .Should().OnlyContain(h => h!.TargetSlideId == destination.Slides[1].Id);
    }

    [Fact]
    public void SlideLevelRichPaste_OrphansAJumpTheDestinationDeckCannotResolve()
    {
        var (destination, editor) = Destination();

        ApplyRichPaste(editor, HyperlinkedPayload("foreign-slide-id"));

        var link = SoleHyperlink(destination);
        link.TargetSlideId.Should().BeNull();
        link.Url.Should().Be("https://example.test");
        link.Tooltip.Should().Be("jump");
    }

    [Fact]
    public void SlideLevelRichPaste_KeepsAJumpToASlideTheDestinationDeckHas()
    {
        var (destination, editor) = Destination();

        ApplyRichPaste(editor, HyperlinkedPayload(destination.Slides[1].Id));

        SoleHyperlink(destination).TargetSlideId.Should().Be(destination.Slides[1].Id);
    }

    [Fact]
    public void SlideLevelRichPaste_LeavesThePayloadRepastable()
    {
        var (_, editor) = Destination();
        var payload = HyperlinkedPayload("foreign-slide-id");

        ApplyRichPaste(editor, payload);

        payload.Body.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Hyperlink)
            .Should().ContainSingle().Which!.TargetSlideId.Should().Be("foreign-slide-id");
    }

    private static void ApplyRichPaste(
        EditingSession editor,
        InCanvasRichClipboardPayload payload) =>
        PresentationClipboardWorkflow.ApplyPaste(
            new PresentationClipboardPasteRequest(editor, 0),
            new PresentationClipboardContent
            {
                Text = payload.PlainText,
                RichTextBytes = InCanvasRichClipboardPlanner.Serialize(payload),
            },
            ownCopyIsCurrent: false);

    private static (Presentation Deck, EditingSession Editor) Destination()
    {
        var deck = Presentation.CreateEmpty();
        while (deck.Slides.Count < 2)
            deck.Slides.Add(new Slide { LayoutId = deck.Slides[0].LayoutId });
        deck.Slides[0].Shapes.Clear();
        var editor = new EditingSession(deck, new PresentationCommandBus(deck));
        editor.SelectSlide(0);
        return (deck, editor);
    }

    private static SlideShape LinkedShape(string targetSlideId)
    {
        var shape = new SlideShape
        {
            Id = 7,
            Name = "Linked",
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 500000,
            TextBody = new TextBody(),
        };
        shape.TextBody!.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Jump",
                    Hyperlink = new Hyperlink { TargetSlideId = targetSlideId, Tooltip = "jump" },
                },
            },
        });
        return shape;
    }

    private static InCanvasRichClipboardPayload HyperlinkedPayload(string targetSlideId)
    {
        var body = InCanvasRichClipboardPayload.FromPlainText("Jump").Body;
        body.Paragraphs[0].Runs[0].Hyperlink = new Hyperlink
        {
            Url = "https://example.test",
            TargetSlideId = targetSlideId,
            Tooltip = "jump",
        };
        return InCanvasRichClipboardPlanner.Capture(
            body,
            new InCanvasEditorTextSelection(0, body.Paragraphs[0].Runs[0].Text.Length));
    }

    private static Hyperlink SoleHyperlink(Presentation deck) =>
        SlideHyperlinkTraversal
            .EnumerateHyperlinks(deck.Slides.SelectMany(slide => slide.Shapes))
            .Should().ContainSingle().Subject;
}
