namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item Y2, OLE / embedded objects):
// An embedded or linked OLE object is modelled as an OPTIONAL INLINE RUN MARK (Run.EmbeddedObject), mirroring
// Run.Chart / Run.Image / Run.Equation and every other inline run feature. This lets an OLE object
// flow through the existing run sequence, table cells, headers/footers and hyperlink/comment/revision
// wrapping with zero new plumbing, and — like charts — it round-trips through docx as a separate binary
// part (word/embeddings/oleObjectN.bin) referenced from the run by relationship id.
//
// An EmbeddedObject holds only what a MINIMAL embedded/linked-object round-trip needs:
//   * Payload   — the embedded native data (e.g. the .xlsx/.bin bytes for an Excel.Sheet object).
//   * Link      — alternatively, the external target of a linked object. Linked objects have no payload.
//   * ProgId    — the OLE class/ProgID string (e.g. "Excel.Sheet.12") so consumers know the server type.
//   * Icon      — the on-page presentation image, reusing the existing InlineImage bytes model. Word's
//                 classic w:object renders a VML v:shape whose v:imagedata points at a metafile/png icon;
//                 we reuse the image-emit path for that presentation part. Optional (some objects display
//                 as content rather than an icon), but a FreeW-authored object always carries one.
//   * Size      — WidthPt / HeightPt in points (matching the rest of the FreeW unit model; the writer
//                 converts to the VML style's pt units directly).
//
// SIMPLIFICATIONS (Y2):
//   * Linked targets are retained but not opened, refreshed, or activated by FreeW.
//   * No live OLE activation: the payload bytes + ProgID are preserved verbatim, but FreeW does not
//     launch the OLE server or re-render the object. The icon image is the sole on-page presentation.
//   * The VML presentation is minimised to a single v:shape + v:imagedata (Word fills in richer VML on
//     re-save); this is enough for a lossless FreeW round-trip and for Word to recognise the object.

/// <summary>
/// An embedded or linked OLE object carried by a <see cref="Run"/> via <see cref="Run.EmbeddedObject"/>.
/// Holds either embedded native <see cref="Payload"/> bytes or a <see cref="LinkedTarget"/>, the OLE
/// <see cref="ProgId"/> (e.g. "Excel.Sheet.12"), an optional on-page presentation <see cref="Icon"/> image,
/// and the rendered size in points. Embedded data serialises as a binary package part; linked data serialises
/// as an external relationship referenced from a classic
/// <c>w:object</c>/<c>v:shape</c>/<c>o:OLEObject</c> run, with the icon written as a media part referenced
/// by the shape's <c>v:imagedata</c>. Modelled as an inline run mark — mirroring <see cref="Run.Chart"/>
/// and <see cref="Run.Image"/> — so embedded objects round-trip through the existing run flow without a
/// new block type. See the file header for the design choice and simplifications.
/// </summary>
public sealed class EmbeddedObject
{
    /// <summary>The embedded native object data (the OLE server's persisted bytes), preserved verbatim.</summary>
    public byte[] Payload { get; }

    /// <summary>
    /// The external relationship target for a linked OLE object. Null identifies an embedded object whose
    /// native data is carried by <see cref="Payload"/>; a non-null value serialises as <c>Type="Link"</c>
    /// with <c>TargetMode="External"</c> and no package-local OLE payload part.
    /// </summary>
    public string? LinkedTarget { get; }

    /// <summary>Whether this object references external OLE data instead of carrying embedded bytes.</summary>
    public bool IsLinked => LinkedTarget is not null;

    /// <summary>The OLE ProgID / class string identifying the server type, e.g. <c>"Excel.Sheet.12"</c>.</summary>
    public string ProgId { get; set; }

    /// <summary>
    /// The on-page presentation icon image (reuses the <see cref="InlineImage"/> bytes model). Optional —
    /// null when the object carries no separate presentation image. A FreeW-authored object always sets one.
    /// </summary>
    public InlineImage? Icon { get; set; }

    /// <summary>The rendered width in points (the VML shape's width). Defaults to a Word-typical icon size.</summary>
    public double WidthPt { get; set; } = 96;

    /// <summary>The rendered height in points (the VML shape's height). Defaults to a Word-typical icon size.</summary>
    public double HeightPt { get; set; } = 96;

    /// <summary>Creates an embedded object from its payload bytes and ProgID.</summary>
    public EmbeddedObject(byte[] payload, string progId)
        : this(payload, progId, linkedTarget: null)
    {
    }

    private EmbeddedObject(byte[] payload, string progId, string? linkedTarget)
    {
        Payload = payload;
        ProgId = progId;
        LinkedTarget = linkedTarget;
    }

    /// <summary>
    /// Convenience factory: an embedded object from payload bytes + ProgID with an optional presentation
    /// icon image and an optional explicit size (defaulting to the icon's size when an icon is supplied).
    /// </summary>
    public static EmbeddedObject Create(
        byte[] payload,
        string progId,
        InlineImage? icon = null,
        double? widthPt = null,
        double? heightPt = null)
    {
        var obj = new EmbeddedObject(payload, progId) { Icon = icon };
        obj.WidthPt = widthPt ?? icon?.WidthPt ?? obj.WidthPt;
        obj.HeightPt = heightPt ?? icon?.HeightPt ?? obj.HeightPt;
        return obj;
    }

    /// <summary>
    /// Creates a linked OLE object. FreeW preserves the external relationship and presentation but does not
    /// activate or update the external source.
    /// </summary>
    public static EmbeddedObject CreateLinked(
        string linkedTarget,
        string progId,
        InlineImage? icon = null,
        double? widthPt = null,
        double? heightPt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedTarget);
        var obj = new EmbeddedObject([], progId, linkedTarget) { Icon = icon };
        obj.WidthPt = widthPt ?? icon?.WidthPt ?? obj.WidthPt;
        obj.HeightPt = heightPt ?? icon?.HeightPt ?? obj.HeightPt;
        return obj;
    }

    /// <summary>Creates an independent copy for document merge and undo snapshots.</summary>
    public EmbeddedObject Clone() => new([.. Payload], ProgId, LinkedTarget)
    {
        Icon = Icon?.Clone(),
        WidthPt = WidthPt,
        HeightPt = HeightPt
    };
}
