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
/// One target tile in a native PowerPoint Summary Zoom. Factors use the OOXML percentage
/// scale where 100000 represents 100 percent.
/// </summary>
public sealed record SummaryZoomTarget(
    string SectionId,
    string Title,
    string Description,
    int OffsetFactorX,
    int OffsetFactorY,
    int ScaleFactorX,
    int ScaleFactorY);

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
/// object — live Slide Zoom and Section Zoom navigation use the target metadata captured below;
/// 3D interaction remains deferred; InkML strokes are replayed when the preserved content part
/// is readable, while the original payload remains authoritative for save/reopen.
/// </summary>
public sealed class PreservedObjectInfo
{
    // ── Classification ────────────────────────────────────────────────────────────

    /// <summary>
    /// Which modern-object kind this is. Used to choose the badge text in the fallback render.
    /// </summary>
    public PreservedObjectKind ObjectKind { get; set; } = PreservedObjectKind.Unknown;

    /// <summary>
    /// Numeric presentation slide id targeted by a Slide Zoom's sldZmObj, when present.
    /// This is distinct from <see cref="Slide.Id"/>, which stores the presentation-level
    /// relationship id used by the rest of the model.
    /// </summary>
    public uint? ZoomTargetSlideNumericId { get; set; }

    /// <summary>Stable section GUID targeted by a Section Zoom, when present.</summary>
    public string? ZoomTargetSectionId { get; set; }

    /// <summary>
    /// All section targets in a Summary Zoom, in native tile order. A Summary Zoom is a
    /// multi-target object; the singular section property above remains for Section Zoom
    /// compatibility and is not used to collapse this list.
    /// </summary>
    public List<SummaryZoomTarget> SummaryZoomTargets { get; } = new();

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

    /// <summary>
    /// The original mc:Choice Requires token (e.g. "p14", "p15", "p159") captured from the
    /// source file's mc:AlternateContent wrapper. The writer re-emits this verbatim so the
    /// correct namespace prefix is declared on the Choice element.
    /// mc:AlternateContent permits a SPACE-SEPARATED list of tokens (e.g. "p14 p15") — this
    /// property holds the full raw value verbatim (do not assume it is a single prefix; split
    /// on whitespace before treating it as a set of xmlns prefixes). See
    /// <see cref="McRequiresNsUris"/> for the per-token namespace URI lookup.
    /// Null when <see cref="WasAlternateContent"/> is false or when the token was not present.
    /// EA3 fix: captures the original token instead of hardcoding "p14".
    /// </summary>
    public string? McRequiresToken { get; set; }

    /// <summary>
    /// The namespace URI corresponding to <see cref="McRequiresToken"/>, as declared on
    /// the original mc:Choice element (e.g. "http://schemas.microsoft.com/office/powerpoint/2010/main"
    /// for p14). Needed so the writer can declare the prefix as xmlns:xxx on the wrapper.
    /// Only meaningful when <see cref="McRequiresToken"/> is a SINGLE token; for multi-token
    /// Requires values, use <see cref="McRequiresNsUris"/> instead (this field holds the URI for
    /// the first token as a best-effort single-value fallback, kept for back-compat).
    /// Null when <see cref="McRequiresToken"/> is null or its URI could not be resolved.
    /// </summary>
    public string? McRequiresNsUri { get; set; }

    /// <summary>
    /// FA2: per-token namespace URI lookup for a (possibly space-separated, multi-token)
    /// <see cref="McRequiresToken"/> value, e.g. {"p14": ".../2010/main", "p15": ".../2012/main"}.
    /// Populated on read by resolving each whitespace-separated token in <see cref="McRequiresToken"/>
    /// against the xmlns declarations in scope on the original mc:Choice element. A token whose URI
    /// could not be resolved is OMITTED (not stored with a null/placeholder value) — the writer must
    /// not guess a URI (e.g. must not fall back to the p14 URI for an unrelated prefix).
    /// Empty when <see cref="WasAlternateContent"/> is false.
    /// </summary>
    public Dictionary<string, string> McRequiresNsUris { get; } = new(StringComparer.Ordinal);

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
