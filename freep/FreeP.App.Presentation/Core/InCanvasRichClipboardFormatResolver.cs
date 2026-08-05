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

        return content.Text is null
            ? new InCanvasRichClipboardResolution(null, PresentationClipboardPasteSource.Nothing)
            : new InCanvasRichClipboardResolution(
                InCanvasRichClipboardPayload.FromPlainText(content.Text),
                PresentationClipboardPasteSource.Text);
    }
}
