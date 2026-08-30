using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Pasting hyperlinked rich text between two open presentations must not carry the source
/// deck's internal slide-jump id into the destination: that id names a slide the destination
/// cannot resolve, so the link silently jumps nowhere. The destination orphans it the same way
/// deleting a slide orphans the links that pointed at it -- target cleared, Url and Tooltip kept.
/// </summary>
public sealed class CrossPresentationHyperlinkPasteTests
{
    private const string SourceSlideId = "source-slide-id";
    private const string SharedSlideId = "shared-slide-id";

    [Fact]
    public void Apply_DropsSlideJumpTargetTheDestinationDeckDoesNotContain()
    {
        var payload = HyperlinkedPayload(SourceSlideId);

        var pasted = InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _,
            new[] { SharedSlideId });

        var hyperlink = SoleHyperlink(pasted);
        hyperlink.TargetSlideId.Should().BeNull();
        hyperlink.Url.Should().Be("https://example.test");
        hyperlink.Tooltip.Should().Be("jump");
    }

    [Fact]
    public void Apply_KeepsSlideJumpTargetTheDestinationDeckStillContains()
    {
        var payload = HyperlinkedPayload(SharedSlideId);

        var pasted = InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _,
            new[] { "other-slide-id", SharedSlideId });

        SoleHyperlink(pasted).TargetSlideId.Should().Be(SharedSlideId);
    }

    [Fact]
    public void Apply_WithoutDestinationSlideIdsKeepsTheCapturedTarget()
    {
        var payload = HyperlinkedPayload(SourceSlideId);

        var pasted = InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _);

        SoleHyperlink(pasted).TargetSlideId.Should().Be(SourceSlideId);
    }

    [Fact]
    public void Apply_DoesNotMutateThePayloadSoASecondPasteBackIntoTheSourceDeckStillJumps()
    {
        var payload = HyperlinkedPayload(SourceSlideId);

        InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _,
            new[] { SharedSlideId });

        SoleHyperlink(payload.Body).TargetSlideId.Should().Be(SourceSlideId);

        var backInSource = InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _,
            new[] { SourceSlideId });

        SoleHyperlink(backInSource).TargetSlideId.Should().Be(SourceSlideId);
    }

    [Fact]
    public void Apply_DropsUnresolvableTargetsWhenTheDestinationDeckHasNoSlideIdsAtAll()
    {
        var payload = HyperlinkedPayload(SourceSlideId);

        var pasted = InCanvasRichClipboardPlanner.Apply(
            Body(string.Empty),
            new InCanvasEditorTextSelection(0, 0),
            payload,
            out _,
            Array.Empty<string>());

        SoleHyperlink(pasted).TargetSlideId.Should().BeNull();
    }

    [Fact]
    public void ApplyClipboardPayload_ForwardsDestinationSlideIdsToThePlanner()
    {
        var buffer = new InCanvasRichTextEditBuffer(Body(string.Empty));

        buffer.ApplyClipboardPayload(
            HyperlinkedPayload(SourceSlideId),
            new InCanvasEditorTextSelection(0, 0),
            out _,
            new[] { SharedSlideId });

        SoleHyperlink(buffer.Body).TargetSlideId.Should().BeNull();
    }

    [Fact]
    public void ApplyClipboardPayload_WithoutDestinationSlideIdsKeepsTheCapturedTarget()
    {
        var buffer = new InCanvasRichTextEditBuffer(Body(string.Empty));

        buffer.ApplyClipboardPayload(
            HyperlinkedPayload(SourceSlideId),
            new InCanvasEditorTextSelection(0, 0),
            out _);

        SoleHyperlink(buffer.Body).TargetSlideId.Should().Be(SourceSlideId);
    }

    private static InCanvasRichClipboardPayload HyperlinkedPayload(string targetSlideId)
    {
        var body = Body("Jump");
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

    private static Hyperlink SoleHyperlink(TextBody body) =>
        body.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Hyperlink)
            .Where(link => link is not null)
            .Should().ContainSingle().Subject!;

    private static TextBody Body(string text) =>
        InCanvasRichClipboardPayload.FromPlainText(text).Body;
}
