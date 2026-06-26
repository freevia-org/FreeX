namespace FreeP.Core.Model;

// ── Modern-object and generic preserved-object model (Wave 25A) ──────────────────────────

/// <summary>
/// Tags the preserved object kind for diagnostic/render purposes.
/// </summary>
public enum PreservedObjectKind
{
    /// <summary>Slide zoom / summary zoom (p:graphicFrame, zoom namespace).</summary>
    Zoom    = 0,

    /// <summary>Ink annotation (p:contentPart, InkML part).</summary>
    Ink     = 1,

    /// <summary>3D model (p:graphicFrame, am3d namespace).</summary>
    Model3d = 2,

    /// <summary>Any other unrecognized graphicFrame or contentPart not matched by a known URI.</summary>
    Unknown = 3,
}

/// <summary>
/// Payload for a preserved modern object (SlideShapeKind.Zoom / Ink / Model3d / PreservedObject).
///
/// All three modern objects (slide zoom, ink, 3D model) and any UNKNOWN graphicFrame or
/// contentPart are stored using this carrier. The verbatim raw XML of the element is stored
/// together with any referenced OPC parts (bytes + content types) and their rels, so the
/// writer can reconstruct the original structure byte-for-byte and PowerPoint reopens the
/// file intact.
///
/// Rendering: the fallback/preview image (if any) is stored in the parent
/// <see cref="SlideShape.Picture"/> field and drawn by the compositor exactly like an OLE
/// object — no live zoom navigation / 3D interaction / ink replay (all deferred).
/// </summary>
public sealed class PreservedObjectInfo
{
    // ── Classification ────────────────────────────────────────────────────────────

    /// <summary>
    /// Which modern-object kind this is. Used to choose the badge text in the fallback render.
    /// </summary>
    public PreservedObjectKind ObjectKind { get; set; } = PreservedObjectKind.Unknown;

    // ── Verbatim XML round-trip ───────────────────────────────────────────────────

    /// <summary>
    /// The raw outer XML of the p:graphicFrame or p:contentPart element as it appeared in
    /// the slide XML (before any mc:AlternateContent unwrapping). This is re-emitted verbatim
    /// on write, with rel-id attributes patched to match freshly written part paths.
    /// </summary>
    public string RawXml { get; set; } = string.Empty;

    /// <summary>
    /// True when the original element was wrapped in mc:AlternateContent. The writer must
    /// re-wrap the emitted element in mc:AlternateContent when this is true.
    /// </summary>
    public bool WasAlternateContent { get; set; }

    // ── Referenced OPC parts ──────────────────────────────────────────────────────

    /// <summary>
    /// All OPC parts referenced by this object, indexed by their absolute OPC path.
    /// Key: absolute OPC path (e.g. "ppt/media/3dModel1.glb", "ppt/ink/ink1.xml").
    /// Value: raw bytes of the part.
    /// </summary>
    public Dictionary<string, byte[]> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Content types for each referenced part (key = absolute OPC path).
    /// Used to register Override entries in [Content_Types].xml.
    /// </summary>
    public Dictionary<string, string> PartContentTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The rels for each referenced part (key = part path, value = raw rels XML bytes).
    /// Only populated when a .rels file existed for the part.
    /// </summary>
    public Dictionary<string, byte[]> PartRels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Relationship entries from the slide rels that this object needs re-emitted
    /// (key = original rId, value = (RelType, TargetPath)).
    /// The writer allocates fresh rIds and patches them into RawXml on output.
    /// </summary>
    public Dictionary<string, (string RelType, string TargetPath)> SlideRels { get; }
        = new(StringComparer.Ordinal);
}
