namespace FreeP.Core.Model;

// ── OLE embedded-object model (Theme 21) ──────────────────────────────────────────────────────

/// <summary>
/// Payload for an OLE embedded object (SlideShapeKind.Ole).
///
/// FreeP preserves the raw embedded binary + its OPC relationship verbatim so that the
/// .pptx file round-trips cleanly and PowerPoint can still open the embedded object.
/// The fallback preview image (what PowerPoint shows when the object is not activated) is
/// stored in the parent SlideShape.Picture field, exactly like a regular Picture shape.
///
/// The fallback image is rendered in the slide. The WPF host may replace that
/// surface with a registered in-place OLE server for an unrotated object; hosts
/// without an in-place site continue to use external activation.
/// </summary>
public sealed class OleObjectInfo
{
    /// <summary>Original package filename hint used when the payload is opened externally.</summary>
    public string FileName { get; set; } = "Embedded.bin";

    // ── Embedded object part ───────────────────────────────────────────────────────

    /// <summary>
    /// Raw bytes of the embedded object part (e.g. the .xlsx or .bin stored inside
    /// ppt/embeddings/). May be empty when the shape was constructed synthetically.
    /// </summary>
    public byte[] EmbeddedBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// MIME / OPC content type of the embedded object part, e.g.
    /// "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" for .xlsx,
    /// or "application/vnd.ms-office.activeX+xml" for ActiveX.
    /// Used to register the correct Override entry in [Content_Types].xml on write.
    /// </summary>
    public string EmbeddedContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// ProgId string from p:oleObj @progId (e.g. "Excel.Sheet.12", "Word.Document.8").
    /// Preserved verbatim for round-trip.
    /// </summary>
    public string ProgId { get; set; } = string.Empty;

    /// <summary>
    /// OLE relationship type used in the slide rels, e.g.
    /// "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package"
    /// or the MS-proprietary oleObject type.
    /// </summary>
    public string RelType { get; set; } =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    // ── Verbatim XML round-trip ────────────────────────────────────────────────────

    /// <summary>
    /// The raw inner XML of the p:oleObj element (excluding the fallback pic child, which is
    /// reconstructed from SlideShape.Picture on write). Preserved so that attributes we do
    /// not model (spid, drawAspect, type, etc.) survive round-trip.
    /// </summary>
    public string OleObjXml { get; set; } = string.Empty;

    /// <summary>
    /// True when the original element was wrapped in mc:AlternateContent. The writer must
    /// re-wrap the emitted p:graphicFrame in mc:AlternateContent when this is true.
    /// </summary>
    public bool WasAlternateContent { get; set; }

    // ── Optional: verbatim fallback image rels ─────────────────────────────────────

    /// <summary>
    /// The file-extension hint for the embedded part (e.g. "xlsx", "bin").
    /// Derived from the content type on read; used to choose the archive path on write.
    /// </summary>
    public string EmbeddedExtension { get; set; } = "bin";
}
