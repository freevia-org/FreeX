namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item Y1, basic SmartArt / DrawingML diagram):
// A SmartArt graphic is modelled as an OPTIONAL INLINE RUN MARK (Run.SmartArt), mirroring Run.Chart and
// every other inline run feature (Image / Equation / Shape / WordArt). This lets a diagram flow through the
// existing run sequence, table cells, headers/footers and hyperlink/comment/revision wrapping with zero new
// plumbing, and â€” exactly like a chart â€” it round-trips through docx as separate parts referenced by an
// inline w:drawing. A SmartArt holds a SmartArtKind (List / Process / Hierarchy) and an ordered TREE of
// nodes (each node: text + ordered child nodes). List and Process diagrams are naturally flat (top-level
// nodes, no children); Hierarchy nests children. Size is kept in points to match the rest of the FreeW unit
// model (the writer converts to EMU).
//
// SIMPLIFICATION (Y1 milestone): the diagram DATA part (word/diagrams/dataN.xml â€” dgm:dataModel) carries
// every node's text and the parentâ†’child structure (dgm:cxnLst). The layout / quickStyle / colors parts are
// emitted near-empty-but-valid (stock list/process ids), while the writer also emits a cached dsp:drawing
// geometry part so compatible viewers can display positioned shapes without recomputing the diagram.

/// <summary>
/// The kind of a <see cref="SmartArt"/> diagram. Determines which stock layout id the writer references and
/// how the node tree is interpreted: <see cref="List"/> and <see cref="Process"/> are flat sequences of
/// top-level nodes; <see cref="Hierarchy"/> is an org-chart-style tree where nodes nest children.
/// </summary>
public enum SmartArtKind
{
    /// <summary>A basic block/bulleted list (stock layout urn ...:list1).</summary>
    List,

    /// <summary>A left-to-right process / sequence (stock layout urn ...:process1).</summary>
    Process,

    /// <summary>An org-chart-style hierarchy where nodes nest child nodes (stock layout urn ...:hierarchy1).</summary>
    Hierarchy
}

/// <summary>
/// One node of a <see cref="SmartArt"/> diagram: its display <see cref="Text"/> plus an ordered list of
/// <see cref="Children"/> (used by <see cref="SmartArtKind.Hierarchy"/>; empty for flat List/Process
/// diagrams). The node text is what the writer emits into the diagram data part (dgm:pt/dgm:t) and what the
/// reader recovers, so it round-trips faithfully.
/// </summary>
public sealed class SmartArtNode
{
    /// <summary>The node's display text (the diagram bullet / box label).</summary>
    public string Text { get; set; }

    /// <summary>Ordered child nodes (Hierarchy diagrams); empty for flat List / Process diagrams.</summary>
    public List<SmartArtNode> Children { get; } = [];

    public SmartArtNode() : this(string.Empty) { }

    /// <summary>Creates a node with the given text and optional child nodes.</summary>
    public SmartArtNode(string text, IEnumerable<SmartArtNode>? children = null)
    {
        Text = text;
        if (children is not null)
            Children.AddRange(children);
    }

    /// <summary>Adds a child node carrying <paramref name="text"/> and returns it (fluent tree building).</summary>
    public SmartArtNode AddChild(string text)
    {
        var child = new SmartArtNode(text);
        Children.Add(child);
        return child;
    }
}

/// <summary>
/// A basic SmartArt / DrawingML diagram carried by a <see cref="Run"/> via <see cref="Run.SmartArt"/>. Holds
/// a <see cref="Kind"/> and an ordered tree of <see cref="Nodes"/>. On save it serialises as four diagram
/// parts (<c>word/diagrams/{data,layout,quickStyle,colors}N.xml</c>) referenced by an inline
/// <c>w:drawing</c> whose <c>dgm:relIds</c> holds the four relationship ids; the node texts and hierarchy
/// live in the data part's <c>dgm:dataModel</c>. Modelled as an inline run mark â€” mirroring
/// <see cref="Run.Chart"/> â€” so diagrams round-trip through the existing run flow without a new block type.
/// </summary>
public sealed class SmartArt
{
    /// <summary>The diagram kind (List / Process / Hierarchy).</summary>
    public SmartArtKind Kind { get; set; } = SmartArtKind.List;

    /// <summary>The top-level diagram nodes (in order). Hierarchy nodes may nest <see cref="SmartArtNode.Children"/>.</summary>
    public List<SmartArtNode> Nodes { get; } = [];

    /// <summary>The rendered width in points (converted to EMU on save). Defaults to a Word-typical ~6.5in.</summary>
    public double WidthPt { get; set; } = 468;

    /// <summary>The rendered height in points (converted to EMU on save). Defaults to a Word-typical ~3in.</summary>
    public double HeightPt { get; set; } = 216;

    /// <summary>
    /// Floating-position state. Null (the default) means the diagram is inline.
    /// Set <see cref="FloatingPlacement.Wrapping"/> to any non-Inline value to make it float.
    /// </summary>
    public FloatingPlacement? Placement { get; set; }

    /// <summary>True when this SmartArt diagram is floating (non-null Placement with Wrapping != Inline).</summary>
    public bool IsFloating => Placement?.IsFloating ?? false;

    /// <summary>
    /// Layout preset id (e.g. "list1", "process1", "hierarchy1", "cycle1"). Null means the diagram uses
    /// the stock layout for its <see cref="Kind"/>. Maps to the layout part's <c>uniqueId</c> suffix.
    /// </summary>
    public string? LayoutId { get; set; }

    /// <summary>
    /// Color-scheme preset id (e.g. "colorful1", "accent1", "mono1"). Null means the stock accent-1
    /// palette. Round-trips through a schema-valid extension in the colors part while its native
    /// <c>uniqueId</c> remains the Word gallery identifier.
    /// </summary>
    public string? ColorSchemeId { get; set; }

    /// <summary>
    /// Style preset id (e.g. "flat1", "subtle1", "intense1"). Null means the flat default.
    /// Round-trips through a schema-valid extension in the quickStyle part while its native
    /// <c>uniqueId</c> remains the Word gallery identifier.
    /// </summary>
    public string? StyleId { get; set; }

    public SmartArt() { }

    /// <summary>
    /// Convenience factory: a flat diagram (typically <see cref="SmartArtKind.List"/> or
    /// <see cref="SmartArtKind.Process"/>) from an ordered set of node texts.
    /// </summary>
    public static SmartArt Create(SmartArtKind kind, IEnumerable<string> nodeTexts)
    {
        var smartArt = new SmartArt { Kind = kind };
        foreach (var text in nodeTexts)
            smartArt.Nodes.Add(new SmartArtNode(text));
        return smartArt;
    }
}
