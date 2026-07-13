using System.Collections.Generic;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.MathLayout;

// ── Math rendering plan (Theme 27) ────────────────────────────────────────────
//
// The MathBoxRenderPlanner converts a MathBox tree (with its relative coordinates)
// into a flat list of renderer-neutral draw instructions (MathDrawOp) at absolute
// DIP positions.
//
// Renderers (WPF + Avalonia SlideCanvas) call
//   MathBoxRenderPlanner.Plan(layout, x, y, color)
// and iterate the resulting ops, dispatching each variant to the correct
// framework drawing call.  ALL math layout and draw-op computation lives here
// in the shared Presentation layer; renderers only call the framework primitives.

/// <summary>A single renderer-neutral math drawing instruction.</summary>
public abstract class MathDrawOp
{
    private MathDrawOp() { }

    /// <summary>Draw a piece of text (glyph) at the given position.</summary>
    public sealed class DrawGlyph : MathDrawOp
    {
        public string Text { get; }
        public string FontFamily { get; }
        public double FontSizePt { get; }
        public bool IsItalic { get; }
        public bool IsBold { get; }
        public SrgbColor Color { get; }
        /// <summary>X position in DIP (slide-space).</summary>
        public double X { get; }
        /// <summary>Y position in DIP (slide-space) — top of the glyph bounding box.</summary>
        public double Y { get; }

        public DrawGlyph(string text, string fontFamily, double fontSizePt, bool isItalic,
                         SrgbColor color, double x, double y)
            : this(text, fontFamily, fontSizePt, isItalic, false, color, x, y)
        {
        }

        public DrawGlyph(string text, string fontFamily, double fontSizePt, bool isItalic, bool isBold,
                         SrgbColor color, double x, double y)
        {
            Text = text; FontFamily = fontFamily; FontSizePt = fontSizePt;
            IsItalic = isItalic; IsBold = isBold; Color = color; X = x; Y = y;
        }
    }

    /// <summary>Draw a horizontal rule (fraction bar or overline).</summary>
    public sealed class DrawHRule : MathDrawOp
    {
        /// <summary>Left edge X in DIP (slide-space).</summary>
        public double X { get; }
        /// <summary>Y center of the rule in DIP (slide-space).</summary>
        public double Y { get; }
        /// <summary>Width in DIP.</summary>
        public double Width { get; }
        /// <summary>Thickness in DIP.</summary>
        public double Thickness { get; }
        public SrgbColor Color { get; }

        public DrawHRule(double x, double y, double width, double thickness, SrgbColor color)
        { X = x; Y = y; Width = width; Thickness = thickness; Color = color; }
    }

    /// <summary>Draw a straight line segment.</summary>
    public sealed class DrawLine : MathDrawOp
    {
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Thickness { get; }
        public SrgbColor Color { get; }

        public DrawLine(double x1, double y1, double x2, double y2, double thickness, SrgbColor color)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Thickness = thickness; Color = color;
        }
    }

    /// <summary>Draw a scaled bracket character at the given position.</summary>
    public sealed class DrawBracket : MathDrawOp
    {
        public string Character { get; }
        public string FontFamily { get; }
        public double BaseFontSizePt { get; }
        public double ScaledHeight { get; }
        public SrgbColor Color { get; }
        public double X { get; }
        public double Y { get; }

        public DrawBracket(string character, string fontFamily, double baseFontSizePt,
                           double scaledHeight, SrgbColor color, double x, double y)
        {
            Character = character; FontFamily = fontFamily; BaseFontSizePt = baseFontSizePt;
            ScaledHeight = scaledHeight; Color = color; X = x; Y = y;
        }
    }

    /// <summary>Draw the v radical sign (check-mark + overline) at the given position.</summary>
    public sealed class DrawRadical : MathDrawOp
    {
        /// <summary>X of the radical sign in DIP (slide-space).</summary>
        public double X { get; }
        /// <summary>Y (top) of the radical box in DIP (slide-space).</summary>
        public double Y { get; }
        /// <summary>Total height of the radical sign (including radicand).</summary>
        public double Height { get; }
        /// <summary>Width of the v check-mark portion.</summary>
        public double SignWidth { get; }
        /// <summary>Width of the overline that spans the radicand.</summary>
        public double OverlineWidth { get; }
        /// <summary>Thickness of the overline stroke.</summary>
        public double OverlineThickness { get; }
        public SrgbColor Color { get; }

        public DrawRadical(double x, double y, double height, double signWidth,
                           double overlineWidth, double overlineThickness, SrgbColor color)
        {
            X = x; Y = y; Height = height; SignWidth = signWidth;
            OverlineWidth = overlineWidth; OverlineThickness = overlineThickness; Color = color;
        }
    }
}

/// <summary>
/// Converts a <see cref="MathBox.Container"/> into a flat list of
/// <see cref="MathDrawOp"/> at absolute slide-space DIP coordinates.
/// </summary>
public static class MathBoxRenderPlanner
{
    /// <summary>
    /// Plan all drawing operations for the given math layout at the given
    /// slide-space origin (top-left of the layout bounding box).
    /// </summary>
    public static IReadOnlyList<MathDrawOp> Plan(
        MathBox.Container layout,
        double originX,
        double originY,
        SrgbColor color,
        string defaultFontFamily)
    {
        var ops = new List<MathDrawOp>();
        WalkBox(layout, originX, originY, color, defaultFontFamily, ops);
        return ops;
    }

    private static void WalkBox(
        MathBox box,
        double parentX, double parentY,
        SrgbColor color, string fontFamily,
        List<MathDrawOp> ops)
    {
        double absX = parentX + box.X;
        double absY = parentY + box.Y;

        switch (box)
        {
            case MathBox.Glyph g:
                if (!string.IsNullOrEmpty(g.Text))
                    ops.Add(new MathDrawOp.DrawGlyph(
                        g.Text, g.FontFamily, g.FontSizePt, g.IsItalic, g.IsBold, color, absX, absY));
                break;

            case MathBox.HRule hr:
                ops.Add(new MathDrawOp.DrawHRule(
                    absX, absY + hr.Thickness / 2.0, hr.LineWidth, hr.Thickness, color));
                break;

            case MathBox.Line line:
                ops.Add(new MathDrawOp.DrawLine(
                    absX, absY, absX + line.X2, absY + line.Y2, line.Thickness, color));
                break;

            case MathBox.Bracket br:
                ops.Add(new MathDrawOp.DrawBracket(
                    br.Character, fontFamily, 0,
                    br.ScaledHeight, color, absX, absY));
                break;

            case MathBox.Radical rad:
                ops.Add(new MathDrawOp.DrawRadical(
                    absX, absY, rad.Metrics.Height,
                    rad.SignWidth, rad.OverlineWidth, rad.OverlineThick, color));
                break;

            case MathBox.Container c:
                foreach (var child in c.Children)
                    WalkBox(child, absX, absY, color, fontFamily, ops);
                break;
        }
    }
}

