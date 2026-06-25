namespace FreeP.Core.Model;

/// <summary>
/// Raw diagram part bytes for one diagram OPC part (data, layout, quickStyle, colors, or drawing).
/// Stored verbatim so the writer can round-trip the parts without understanding their XML schemas.
/// </summary>
public sealed class DiagramPart
{
    /// <summary>OPC content type for this part (e.g. ".../diagramData+xml").</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Absolute OPC path within the archive (e.g. "ppt/diagrams/data1.xml").</summary>
    public string PartPath { get; set; } = string.Empty;

    /// <summary>Raw UTF-8 XML bytes for this part.</summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// Payload for a SmartArt graphic (SlideShapeKind.SmartArt).
///
/// SmartArt is stored as a p:graphicFrame whose graphicData URI is the diagram namespace
/// (.../drawingml/2006/diagram). The PPTX spec includes four named diagram sub-parts
/// (data, layout, quickStyle, colors) plus a rendering cache part (dsp:drawing).
///
/// FreeP renders SmartArt by parsing the cached dsp:drawing part into ordinary SlideShape
/// objects (FallbackShapes) using the same spPr/txBody pipeline as regular shapes.
/// All five raw parts are stored verbatim for lossless round-trip.
/// </summary>
public sealed class SmartArtShape
{
    // ── Fallback rendering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fallback shapes parsed from the dsp:drawing cache part. Each is a fully-resolved
    /// SlideShape (AutoShape kind with fill/outline/text) positioned in slide-coordinate space.
    /// Empty when the drawing cache part is absent in the source file.
    /// </summary>
    public List<SlideShape> FallbackShapes { get; } = new();

    // ── Round-trip preservation ────────────────────────────────────────────────────

    /// <summary>
    /// Relationship type strings for the four graphicFrame r: attributes:
    /// r:dm (data), r:lo (layout), r:qs (quickStyle), r:cs (colors).
    /// Preserved verbatim so the writer can re-emit the exact graphicFrame element.
    /// Keys: "dm", "lo", "qs", "cs".
    /// </summary>
    public Dictionary<string, string> DiagramRelIds { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// All raw diagram OPC parts indexed by their absolute OPC path.
    /// Includes data, layout, quickStyle, colors, and drawing (dsp:drawing) parts.
    /// These are written back verbatim on round-trip.
    /// </summary>
    public Dictionary<string, DiagramPart> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rels for each diagram part, stored as raw rel XML bytes keyed by the part path
    /// whose rels file they represent (e.g. "ppt/diagrams/data1.xml" -> data1.xml.rels bytes).
    /// Only populated when a rels file existed in the source archive.
    /// </summary>
    public Dictionary<string, byte[]> PartRels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The drawing part OPC path (dsp:drawing), resolved from the data part's rels.
    /// Null when no drawing cache part exists (graceful degradation).
    /// </summary>
    public string? DrawingPartPath { get; set; }
}
