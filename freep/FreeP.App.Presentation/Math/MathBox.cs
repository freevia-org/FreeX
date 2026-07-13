namespace FreeP.App.Compositor.MathLayout;

// ── Math layout box tree (Theme 27) ───────────────────────────────────────────
//
// The MathLayoutEngine converts a MathNode tree into a MathBox tree of
// positioned primitives.  All coordinates are in DIP relative to the top-left
// of the overall math expression bounding box.
//
// A MathBox.Container has a list of child boxes (positioned relative to the
// container's origin).  The primitives are:
//   GlyphBox   — a piece of text (letter/operator/digit)
//   LineBox    — a horizontal rule (fraction bar, overline, radical overline)
//   BracketBox — an auto-sized bracket glyph (open or close delimiter)
//   RadicalBox — the v sign with a specified height
//
// Renderers walk the box tree and draw each primitive using the corresponding
// framework drawing calls.

/// <summary>
/// Overall dimensions of a laid-out math box.
/// The layout engine fills Width, Height, and Baseline for every box.
/// </summary>
public sealed class MathBoxMetrics
{
    /// <summary>Total width in DIP.</summary>
    public double Width { get; set; }

    /// <summary>Total height in DIP (ascent + descent).</summary>
    public double Height { get; set; }

    /// <summary>
    /// Ascent: distance from the top of the bounding box to the math baseline.
    /// Baseline = top + Ascent.
    /// </summary>
    public double Ascent { get; set; }

    /// <summary>Descent from the baseline to the bottom of the bounding box.</summary>
    public double Descent => Height - Ascent;
}

/// <summary>Abstract base for all positioned math rendering primitives.</summary>
public abstract class MathBox
{
    private MathBox() { }

    /// <summary>X position in DIP, relative to the container's top-left origin.</summary>
    public double X { get; set; }

    /// <summary>Y position in DIP, relative to the container's top-left origin.</summary>
    public double Y { get; set; }

    /// <summary>Metrics of this box (set by the layout engine).</summary>
    public MathBoxMetrics Metrics { get; } = new();

    // ── Glyph (text) ────────────────────────────────────────────────────────

    /// <summary>A rendered piece of text: one or more characters at a given size.</summary>
    public sealed class Glyph : MathBox
    {
        /// <summary>Text to draw (a letter, digit, operator, or short string).</summary>
        public string Text { get; }

        /// <summary>Font family for the glyph (usually the math-italic or main font).</summary>
        public string FontFamily { get; }

        /// <summary>Font size in points.</summary>
        public double FontSizePt { get; }

        /// <summary>True = italic style (single-letter math variables).</summary>
        public bool IsItalic { get; }

        /// <summary>True = bold style.</summary>
        public bool IsBold { get; }

        public Glyph(string text, string fontFamily, double fontSizePt, bool isItalic, bool isBold = false)
        {
            Text = text;
            FontFamily = fontFamily;
            FontSizePt = fontSizePt;
            IsItalic = isItalic;
            IsBold = isBold;
        }
    }

    // ── Horizontal rule / line ───────────────────────────────────────────────

    /// <summary>A horizontal line: fraction bar, overline, or underline.</summary>
    public sealed class HRule : MathBox
    {
        /// <summary>Width of the line in DIP (extends rightward from X).</summary>
        public double LineWidth { get; set; }

        /// <summary>Line thickness in DIP.</summary>
        public double Thickness { get; set; }
    }

    // ── Bracket ─────────────────────────────────────────────────────────────

    /// <summary>A straight line segment, used for renderer-neutral math borders.</summary>
    public sealed class Line : MathBox
    {
        /// <summary>X endpoint in DIP relative to this box origin.</summary>
        public double X2 { get; set; }

        /// <summary>Y endpoint in DIP relative to this box origin.</summary>
        public double Y2 { get; set; }

        /// <summary>Line thickness in DIP.</summary>
        public double Thickness { get; set; }
    }

    /// <summary>
    /// An auto-sized bracket glyph (open or close delimiter): (, ), [, ], {, } etc.
    /// The glyph is scaled vertically to the content height.
    /// </summary>
    public sealed class Bracket : MathBox
    {
        /// <summary>The bracket character to draw.</summary>
        public string Character { get; }

        /// <summary>Scaled height in DIP.</summary>
        public double ScaledHeight { get; set; }

        public Bracket(string character) { Character = character; }
    }

    // ── Radical sign ─────────────────────────────────────────────────────────

    /// <summary>
    /// The v radical sign (the check-mark part that scales to the radicand height)
    /// plus the horizontal overline that spans the radicand width.
    /// The Glyph box for the v character is nested here.
    /// </summary>
    public sealed class Radical : MathBox
    {
        /// <summary>Width of the overline portion (= radicand width) in DIP.</summary>
        public double OverlineWidth { get; set; }

        /// <summary>Thickness of the overline in DIP.</summary>
        public double OverlineThick { get; set; }

        /// <summary>Width of the v check-mark portion in DIP.</summary>
        public double SignWidth { get; set; }
    }

    // ── Container (composite) ───────────────────────────────────────────────

    /// <summary>
    /// A container that holds child boxes with absolute (X, Y) positions relative
    /// to this container's origin.  The compositor/renderer simply recurses into children.
    /// </summary>
    public sealed class Container : MathBox
    {
        public List<MathBox> Children { get; } = new();
    }
}

