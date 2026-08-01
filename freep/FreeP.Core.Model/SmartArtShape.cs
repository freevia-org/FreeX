namespace FreeP.Core.Model;

// ── SmartArt live-layout data model (Theme 17) ────────────────────────────────────────────────

/// <summary>
/// SmartArt diagram family — classifies the layout uniqueId into a rendering strategy.
/// The live layout engine supports common bounded families; Unknown falls back to cached drawing.
/// </summary>
public enum SmartArtFamily
{
    Unknown   = 0,
    Process   = 1,   // horizontal row of boxes + arrow connectors
    List      = 2,   // vertical (or horizontal) stack of boxes
    Cycle     = 3,   // boxes on a circle with arrow connectors
    Hierarchy = 4,   // tree (root top, children below, connector lines)
    Matrix    = 5,   // two-column grid with additional rows
    Relationship = 6 // overlapping or relationship diagrams such as Venn
}

/// <summary>
/// One logical node in the SmartArt data model (maps to a dgm:pt type="node" or "asst").
/// Children are built from the parOf connection graph in data1.xml.
/// </summary>
public sealed class SmartArtNode
{
    /// <summary>dgm:pt @modelId (a GUID string used in cxnLst to wire the tree).</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Display text assembled from dgm:t/a:p/a:r/a:t, new-line per paragraph.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Zero-based depth level (root = 0).</summary>
    public int Level { get; set; }

    /// <summary>Ordered child nodes.</summary>
    public List<SmartArtNode> Children { get; } = new();

    /// <summary>True when the node is typed "asst" (assistant box in org-chart).</summary>
    public bool IsAssistant { get; set; }

    /// <summary>
    /// Optional node-level image for bounded picture-backed SmartArt layouts.
    /// Populated only when the reader can deterministically map cached diagram pictures
    /// to parsed data nodes; null keeps the cached drawing fallback path in control.
    /// </summary>
    public ImagePart? Picture { get; set; }
}

/// <summary>
/// Parsed result of data1.xml + layout1.xml — the logical content the live layout engine
/// needs to build positioned shapes.
/// Stored on <see cref="SmartArtShape"/> alongside the verbatim part bytes.
/// </summary>
public sealed class SmartArtData
{
    /// <summary>
    /// The SmartArt layout family determined from the layout1.xml uniqueId.
    /// </summary>
    public SmartArtFamily Family { get; set; } = SmartArtFamily.Unknown;

    /// <summary>
    /// Raw uniqueId from the layoutDef/@uniqueId attribute (preserved for diagnostics).
    /// </summary>
    public string LayoutUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// True when the parsed layout ID is covered by FreeP's bounded live layout planner.
    /// False keeps the cached dsp:drawing fallback as the render source while still
    /// preserving the parsed family and raw diagram parts for future slices.
    /// </summary>
    public bool IsLiveLayoutSupported { get; set; } = true;

    /// <summary>
    /// Root-level nodes of the diagram tree.  For flat families (List, Process, Cycle)
    /// all visible nodes sit at Level 0 here.  For Hierarchy the root is Nodes[0] with
    /// its children nested inside it.
    /// </summary>
    public List<SmartArtNode> Nodes { get; } = new();
}

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
/// Bounded metadata parsed from a SmartArt quickStyle diagram part.
/// Raw quickStyle bytes remain preserved in <see cref="SmartArtShape.Parts"/>.
/// </summary>
public sealed class SmartArtQuickStyleLabelMetadata
{
    public string Name { get; set; } = string.Empty;

    public int? LineReferenceIndex { get; set; }

    public int? FillReferenceIndex { get; set; }

    public int? EffectReferenceIndex { get; set; }

    public string? FontReferenceIndex { get; set; }
}

public sealed class SmartArtQuickStyleMetadata
{
    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public List<string> StyleLabels { get; } = new();

    /// <summary>
    /// Native DrawingML style references carried by each <c>dgm:styleLbl</c>.
    /// These are semantic metadata only; the raw quickStyle part remains authoritative
    /// for any style content not represented here.
    /// </summary>
    public List<SmartArtQuickStyleLabelMetadata> StyleLabelMetadata { get; } = new();
}

/// <summary>
/// Bounded metadata parsed from a SmartArt colors diagram part.
/// Raw colors bytes remain preserved in <see cref="SmartArtShape.Parts"/>.
/// </summary>
public sealed class SmartArtColorMetadata
{
    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public List<string> ColorLabels { get; } = new();

    public List<ThemeAwareColor> Palette { get; } = new();
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
    // ── Live layout data (Theme 17) ────────────────────────────────────────────────

    /// <summary>
    /// Parsed data model — node tree + family classification — built from data1.xml + layout1.xml.
    /// Null when neither part was loadable (e.g. in legacy archives or the parts were missing).
    /// When non-null and <see cref="SmartArtData.Family"/> is a supported family, the compositor
    /// runs the <see cref="SmartArtLayoutEngine"/> instead of the cached drawing.
    /// </summary>
    public SmartArtData? Data { get; set; }

    /// <summary>
    /// Parsed quickStyle hints from the SmartArt qs part. Null when absent/unreadable.
    /// </summary>
    public SmartArtQuickStyleMetadata? QuickStyle { get; set; }

    /// <summary>
    /// Parsed color-style hints from the SmartArt cs part. Null when absent/unreadable.
    /// </summary>
    public SmartArtColorMetadata? Colors { get; set; }

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
