using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r150 remediation for the images-anchoring F2 scope-audit gap.
///
/// Round 150 taught picture insertion to preserve a picture's native aspect ratio by sniffing its
/// pixel size in <see cref="SlideObjectInsertionPlanner.CreatePicturePayload"/>. That landed on
/// two of the three gestures that insert a picture -- Insert &gt; Pictures and drag-and-drop --
/// but not on Ctrl+V of an image sitting on the OS clipboard, which called
/// <c>EditingSession.InsertPicture</c> with no size at all and so kept stretching every pasted
/// image into the default 3in x 2in (1.5:1) box.
///
/// These tests assert the paste gesture AGREES with the insert gesture rather than asserting a
/// literal EMU size: the defect was two paths disagreeing, so a literal assertion on one of them
/// would not have caught it (and a round-149 fix that asserted a literal substring is exactly how
/// a broken RTF writer reached main).
/// </summary>
public sealed class R150_ClipboardImagePasteAspectRatioTests
{
    [Fact]
    public void ApplyPaste_SystemImage_SizesPictureExactlyLikeTheInsertGesture()
    {
        // A 3:4 portrait image -- deliberately not the 1.5:1 of the default box, so a regression
        // to DefaultShapeBounds() is visible in the ratio and not only in the absolute numbers.
        var png = MakePngHeader(300, 400);
        var expected = SlideObjectInsertionPlanner.CreatePicturePayload(png, ".png");

        var (editor, slide) = CreateEditor();

        var source = PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            new PresentationClipboardContent(PngBytes: png),
            ownCopyIsCurrent: false,
            preferSystemClipboard: true);

        source.Should().Be(PresentationClipboardPasteSource.Image);

        var pasted = slide.Shapes[^1];
        pasted.Kind.Should().Be(SlideShapeKind.Picture);
        pasted.ExtentCxEmu.Should().Be(
            expected.WidthEmu!.Value,
            "Ctrl+V of an image must size the picture exactly as Insert > Pictures does");
        pasted.ExtentCyEmu.Should().Be(expected.HeightEmu!.Value);

        // The ratio is the user-visible symptom: a 3:4 photo must not come back as 3:2.
        ((double)pasted.ExtentCxEmu / pasted.ExtentCyEmu)
            .Should().BeApproximately(300d / 400d, 0.01);
    }

    [Fact]
    public void ApplyPaste_SystemImage_WithUndecodableBytes_StillFallsBackToTheDefaultBox()
    {
        // The sibling no-regression case: when the bytes carry no readable header the planner
        // returns no size, and paste must keep working with the default bounds rather than
        // throwing or inserting a zero-sized shape.
        var (editor, slide) = CreateEditor();

        var source = PresentationClipboardWorkflow.ApplyPaste(
            PresentationClipboardWorkflow.PreparePaste(editor),
            new PresentationClipboardContent(PngBytes: [10, 20, 30]),
            ownCopyIsCurrent: false,
            preferSystemClipboard: true);

        source.Should().Be(PresentationClipboardPasteSource.Image);

        var pasted = slide.Shapes[^1];
        pasted.Kind.Should().Be(SlideShapeKind.Picture);
        pasted.ExtentCxEmu.Should().BeGreaterThan(0);
        pasted.ExtentCyEmu.Should().BeGreaterThan(0);
        pasted.Picture!.Bytes.Should().Equal([10, 20, 30]);
    }

    private static byte[] MakePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Array.Copy(sig, bytes, 8);
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        WriteUInt32BigEndian(bytes, 16, (uint)width);
        WriteUInt32BigEndian(bytes, 20, (uint)height);
        return bytes;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static (EditingSession Editor, Slide Slide) CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        return (
            new EditingSession(presentation, new PresentationCommandBus(presentation)),
            slide);
    }
}
