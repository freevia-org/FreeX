namespace FreeP.App.Compositor;

public sealed record InCanvasRichClipboardResolution(
    InCanvasRichClipboardPayload? Payload,
    PresentationClipboardPasteSource Source)
{
    public bool HasPayload => Payload is not null;
}

/// <summary>Chooses the richest portable in-canvas payload exposed by a native clipboard.</summary>
public static class InCanvasRichClipboardFormatResolver
{
    public static InCanvasRichClipboardResolution Resolve(PresentationClipboardContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var payload = InCanvasRichClipboardPlanner.Deserialize(content.RichTextBytes);
        if (payload is not null)
        {
            return new InCanvasRichClipboardResolution(
                payload,
                PresentationClipboardPasteSource.RichText);
        }

        payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(content.XamlPackageBytes);
        if (payload is not null)
        {
            return new InCanvasRichClipboardResolution(
                payload,
                PresentationClipboardPasteSource.XamlPackage);
        }

        payload = ExternalRichTextClipboardPlanner.TryParseRtf(content.RtfBytes);
        if (payload is not null)
        {
            return new InCanvasRichClipboardResolution(
                payload,
                PresentationClipboardPasteSource.RichText);
        }

        if (content.Text is not null)
        {
            return new InCanvasRichClipboardResolution(
                InCanvasRichClipboardPayload.FromPlainText(content.Text),
                PresentationClipboardPasteSource.Text);
        }

        // Last resort, and deliberately below text: an image tool leaves a bare bitmap with
        // nothing else, while an app that offers a bitmap *and* text (a copied spreadsheet
        // range, say) means the text. Resolving the bitmap here is what keeps a screenshot
        // paste inside the payload path -- WPF used to get this only by declining the key and
        // letting its RichTextBox paste the bitmap itself, which bypassed every other rule the
        // payload path applies, and Avalonia, which never declines, silently did nothing.
        if (content.PngBytes is { Length: > 0 } png)
        {
            return new InCanvasRichClipboardResolution(
                InCanvasRichClipboardPayload.FromInlineImage(png, "image/png"),
                PresentationClipboardPasteSource.Image);
        }

        return new InCanvasRichClipboardResolution(null, PresentationClipboardPasteSource.Nothing);
    }
}
