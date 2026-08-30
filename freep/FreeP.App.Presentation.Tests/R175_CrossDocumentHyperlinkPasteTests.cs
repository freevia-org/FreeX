using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r175 remediation for freep-hyperlinks-actions F2.
///
/// <see cref="Hyperlink.TargetSlideId"/> round-trips through the in-canvas rich clipboard
/// verbatim (<see cref="InCanvasRichClipboardPlanner.Serialize"/> /
/// <see cref="InCanvasRichClipboardPlanner.Deserialize"/>). For a slide loaded from a real
/// .pptx that value is the small per-file OOXML relationship id assigned by
/// <c>PptxPackageReader</c> (e.g. "rId4"), not a value unique across documents -- so pasting a
/// hyperlinked run copied from one open presentation into a different open presentation could
/// silently attach the link to whatever unrelated slide happens to carry that same small id in
/// the destination deck, or (if nothing collides) to slide 1 once <c>PptxPackageWriter</c>'s
/// <c>EnsureHlinkRel</c> fallback kicks in on save.
///
/// <see cref="InCanvasRichClipboardPlanner.Apply"/> now takes the destination document's own
/// slide ids and drops (nulls) a pasted hyperlink's <see cref="Hyperlink.TargetSlideId"/> when it
/// does not name one of them, leaving Url/Tooltip untouched -- the same treatment
/// <c>PresentationCommands</c> already gives a hyperlink whose target slide was deleted. Passing
/// no destination ids (the default) preserves the pre-fix behaviour exactly, so the same-document
/// paste path -- the overwhelmingly common case, and the sibling this fix must not regress --
/// keeps working unchanged.
/// </summary>
public sealed class R175_CrossDocumentHyperlinkPasteTests
{
    [Fact]
    public void Apply_DestinationSlideIdsProvided_DropsUnresolvedInternalSlideJumpTarget()
    {
        // Simulates: copy hyperlinked text from presentation A (whose link targets A's
        // slide "rId4"), then paste into presentation B, whose own slides are "rId2"/"rId3".
        var payload = CapturedHyperlinkPayload(targetSlideId: "rId4", url: null);
        var roundTripped = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload))!;

        var destination = InCanvasRichClipboardPayload.FromPlainText("Before After").Body;
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            roundTripped,
            out _,
            destinationSlideIds: new[] { "rId2", "rId3" });

        var run = updated.Paragraphs.SelectMany(p => p.Runs)
            .Should().ContainSingle(r => r.Hyperlink != null).Subject;
        run.Hyperlink!.TargetSlideId.Should().BeNull(
            "the source deck's internal id does not name any slide in the destination document");
    }

    [Fact]
    public void Apply_DestinationSlideIdsProvided_KeepsTargetWhenItResolves()
    {
        // Same shape of paste, but this time the destination genuinely has a slide "rId4" --
        // e.g. pasting back into the very document the fragment was copied from.
        var payload = CapturedHyperlinkPayload(targetSlideId: "rId4", url: null);
        var roundTripped = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload))!;

        var destination = InCanvasRichClipboardPayload.FromPlainText("Before After").Body;
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            roundTripped,
            out _,
            destinationSlideIds: new[] { "rId4", "rId2" });

        var run = updated.Paragraphs.SelectMany(p => p.Runs)
            .Should().ContainSingle(r => r.Hyperlink != null).Subject;
        run.Hyperlink!.TargetSlideId.Should().Be("rId4");
    }

    [Fact]
    public void Apply_NoDestinationSlideIdsSupplied_PreservesTargetUnvalidated()
    {
        // The sibling no-regression case: every existing call site that does not opt in to
        // validation (e.g. InCanvasRichTextEditBuffer's same-document buffer paste) must see
        // byte-for-byte the same behaviour as before this fix.
        var payload = CapturedHyperlinkPayload(targetSlideId: "rId4", url: null);
        var roundTripped = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload))!;

        var destination = InCanvasRichClipboardPayload.FromPlainText("Before After").Body;
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            roundTripped,
            out _);

        var run = updated.Paragraphs.SelectMany(p => p.Runs)
            .Should().ContainSingle(r => r.Hyperlink != null).Subject;
        run.Hyperlink!.TargetSlideId.Should().Be("rId4");
    }

    [Fact]
    public void Apply_DestinationSlideIdsProvided_LeavesExternalUrlHyperlinkUntouched()
    {
        // Sibling case: an external (http) hyperlink has no TargetSlideId at all and must not be
        // affected by slide-id validation regardless of what the destination's slides are.
        var payload = CapturedHyperlinkPayload(targetSlideId: null, url: "https://example.test");
        var roundTripped = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload))!;

        var destination = InCanvasRichClipboardPayload.FromPlainText("Before After").Body;
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            roundTripped,
            out _,
            destinationSlideIds: new[] { "rId2", "rId3" });

        var run = updated.Paragraphs.SelectMany(p => p.Runs)
            .Should().ContainSingle(r => r.Hyperlink != null).Subject;
        run.Hyperlink!.Url.Should().Be("https://example.test");
        run.Hyperlink!.TargetSlideId.Should().BeNull();
    }

    private static InCanvasRichClipboardPayload CapturedHyperlinkPayload(string? targetSlideId, string? url)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Linked",
                    Hyperlink = new Hyperlink { TargetSlideId = targetSlideId, Url = url },
                },
            },
        });
        return InCanvasRichClipboardPlanner.Capture(body, new InCanvasEditorTextSelection(0, 6));
    }
}
