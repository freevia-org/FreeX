using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.App.UI;

/// <summary>
/// Renders emoji runs (e.g. 👍 👎 👌) to small COLOR bitmaps for use as chart data-label glyphs.
///
/// Background: OxyPlot.Wpf draws annotation text through a monochrome <c>GlyphRun</c> path, so emoji
/// in "Value From Cells" data labels render as flat black/gray silhouettes. The obvious fix —
/// rendering the emoji with WPF <see cref="FormattedText"/> + the color font "Segoe UI Emoji" — does
/// NOT help: WPF's text stack never adopted DirectWrite color-glyph (COLR/CPAL) rendering, so even a
/// direct <c>DrawText</c> to a <see cref="RenderTargetBitmap"/> comes out fully monochrome (verified:
/// max channel spread = 0).
///
/// So we draw a faithful COLORED approximation of the handful of emoji Excel actually uses in
/// "Value From Cells" thumbs/OK labels (👍 👎 👌) as small amber vector glyphs — matching Excel's
/// yellow-hand emoji palette — rasterize them to a transparent PNG, and feed that back into OxyPlot
/// as an image annotation (drawn via WPF <c>DrawImage</c>, which preserves color). Unknown emoji fall
/// back to the original monochrome text path so nothing is lost. The percent/text remainder of the
/// label keeps using OxyPlot's normal (monochrome-but-fine) text path.
/// </summary>
internal static class ChartEmojiGlyphs
{
    // Excel renders its emoji as the classic yellow/amber "hand" palette. These approximate that look.
    private static readonly Color HandFill = Color.FromRgb(0xFF, 0xC8, 0x3D);   // amber/yellow hand
    private static readonly Color HandStroke = Color.FromRgb(0xD6, 0x9A, 0x16); // darker amber outline
    private static readonly Color OkRing = Color.FromRgb(0xF1, 0x9A, 0x33);     // orange ring for 👌

    private const int ThumbsUp = 0x1F44D;   // 👍
    private const int ThumbsDown = 0x1F44E; // 👎
    private const int OkHand = 0x1F44C;     // 👌

    // Cache rendered emoji PNGs keyed by (run, integer pixel size) so repeated categories/charts
    // don't re-rasterize the same glyph.
    private static readonly ConcurrentDictionary<(string Run, int PixelSize), byte[]> PngCache = new();

    /// <summary>
    /// Splits a "Value From Cells" label into a leading emoji run and the trailing text remainder.
    /// Excel's thumbs-up idiom puts the emoji first (e.g. "👍 30%"), so we peel the leading emoji /
    /// pictographic code points (plus any immediately-trailing spaces) off the front.
    /// </summary>
    /// <returns>
    /// <c>Emoji</c> = the leading emoji run (empty if the label starts with non-emoji text);
    /// <c>Text</c> = the remainder with leading whitespace trimmed.
    /// </returns>
    internal static (string Emoji, string Text) SplitLeadingEmoji(string label)
    {
        if (string.IsNullOrEmpty(label))
            return (string.Empty, label ?? string.Empty);

        var i = 0;
        var sawEmoji = false;
        while (i < label.Length)
        {
            var advance = NextCodePoint(label, i, out var codePoint);
            if (IsEmojiCodePoint(codePoint))
            {
                sawEmoji = true;
                i += advance;
                continue;
            }

            // Variation selectors / zero-width joiners / skin-tone modifiers belong to a preceding
            // emoji run; absorb them only if we've already seen an emoji.
            if (sawEmoji && IsEmojiModifier(codePoint))
            {
                i += advance;
                continue;
            }

            break;
        }

        if (!sawEmoji)
            return (string.Empty, label);

        var emoji = label.Substring(0, i);
        var rest = label.Substring(i).TrimStart();
        return (emoji, rest);
    }

    /// <summary>True when the label begins with at least one color-emoji code point.</summary>
    internal static bool HasLeadingEmoji(string label) => SplitLeadingEmoji(label).Emoji.Length > 0;

    /// <summary>
    /// Returns the leading emoji run only when EVERY emoji in it is one we can draw a faithful colored
    /// approximation for (👍 👎 👌). If the run contains any emoji we can't color, returns empty so the
    /// caller leaves the whole label on OxyPlot's normal text path (no half-colored / partially-dropped
    /// labels).
    /// </summary>
    internal static (string Emoji, string Text) SplitLeadingDrawableEmoji(string label)
    {
        var (emoji, text) = SplitLeadingEmoji(label);
        if (emoji.Length == 0)
            return (string.Empty, label);

        var i = 0;
        while (i < emoji.Length)
        {
            var advance = NextCodePoint(emoji, i, out var cp);
            if (!IsEmojiModifier(cp) && !IsDrawableEmoji(cp))
                return (string.Empty, label); // contains an emoji we can't color → don't split
            i += advance;
        }

        return (emoji, text);
    }

    private static bool IsDrawableEmoji(int cp) => cp is ThumbsUp or ThumbsDown or OkHand;

    /// <summary>
    /// Renders <paramref name="emoji"/> to a tightly-cropped, transparent-background COLOR PNG sized so the
    /// glyph is roughly <paramref name="fontSize"/> tall at <paramref name="renderScale"/> device scale.
    /// Returns the PNG bytes plus the device pixel dimensions, or <c>null</c> when nothing was drawn.
    /// </summary>
    internal static EmojiBitmap? RenderEmojiPng(string emoji, double fontSize, double renderScale)
    {
        if (string.IsNullOrEmpty(emoji))
            return null;

        var pixelSize = Math.Max(8, (int)Math.Round(fontSize * Math.Max(1.0, renderScale)));
        var png = PngCache.GetOrAdd((emoji, pixelSize), key => RenderPngCore(key.Run, key.PixelSize));
        if (png.Length == 0)
            return null;

        // Decode dimensions cheaply from the cached bytes (PNG IHDR holds width/height at fixed offset).
        var (w, h) = ReadPngDimensions(png);
        if (w <= 0 || h <= 0)
            return null;

        return new EmojiBitmap(png, w, h, renderScale);
    }

    private static byte[] RenderPngCore(string emoji, int pixelSize)
    {
        try
        {
            // Lay out one square cell per drawable code point, side by side.
            var codePoints = new List<int>();
            var i = 0;
            while (i < emoji.Length)
            {
                var advance = NextCodePoint(emoji, i, out var cp);
                if (IsDrawableEmoji(cp))
                    codePoints.Add(cp);
                i += advance;
            }

            if (codePoints.Count == 0)
                return [];

            var cell = pixelSize;
            var width = cell * codePoints.Count;
            var height = cell;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (var c = 0; c < codePoints.Count; c++)
                    DrawGlyph(dc, codePoints[c], new Rect(c * cell, 0, cell, cell));
            }

            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            // Best-effort; on any failure fall back to no image (OxyPlot keeps drawing the original
            // monochrome text including the emoji).
            return [];
        }
    }

    /// <summary>Draws a faithful amber colored approximation of a single emoji code point into <paramref name="r"/>.</summary>
    private static void DrawGlyph(DrawingContext dc, int codePoint, Rect r)
    {
        switch (codePoint)
        {
            case ThumbsUp:
                DrawThumb(dc, r, pointingUp: true);
                break;
            case ThumbsDown:
                DrawThumb(dc, r, pointingUp: false);
                break;
            case OkHand:
                DrawOkHand(dc, r);
                break;
        }
    }

    private static void DrawThumb(DrawingContext dc, Rect r, bool pointingUp)
    {
        var fill = new SolidColorBrush(HandFill); fill.Freeze();
        var pen = new Pen(new SolidColorBrush(HandStroke), Math.Max(1.0, r.Height * 0.05));
        pen.Brush.Freeze(); pen.Freeze();

        // Build a thumbs-up in a normalized 0..1 box, then flip vertically for thumbs-down.
        // Fist (rounded rectangle) on the lower-left, thumb sticking up on the right.
        var s = Math.Min(r.Width, r.Height);
        var ox = r.X + (r.Width - s) / 2;
        var oy = r.Y + (r.Height - s) / 2;
        Point P(double nx, double ny)
        {
            var y = pointingUp ? ny : (1.0 - ny);
            return new Point(ox + nx * s, oy + y * s);
        }

        // Fist body
        var fistRadius = 0.10 * s;
        var fist = new RectangleGeometry(
            new Rect(P(0.10, 0.40).X, Math.Min(P(0.40, 0.40).Y, P(0.40, 0.95).Y),
                     0.55 * s, Math.Abs(P(0.40, 0.95).Y - P(0.40, 0.40).Y)),
            fistRadius, fistRadius);
        dc.DrawGeometry(fill, pen, fist);

        // Thumb: a rounded vertical capsule rising above the fist on the right.
        var thumb = new StreamGeometry();
        using (var ctx = thumb.Open())
        {
            ctx.BeginFigure(P(0.45, 0.40), isFilled: true, isClosed: true);
            ctx.LineTo(P(0.45, 0.20), true, true);
            ctx.ArcTo(P(0.70, 0.20), new Size(0.125 * s, 0.16 * s), 0, false,
                pointingUp ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(P(0.70, 0.45), true, true);
            ctx.LineTo(P(0.45, 0.45), true, true);
        }
        thumb.Freeze();
        dc.DrawGeometry(fill, pen, thumb);
    }

    private static void DrawOkHand(DrawingContext dc, Rect r)
    {
        var fill = new SolidColorBrush(HandFill); fill.Freeze();
        var ring = new Pen(new SolidColorBrush(OkRing), Math.Max(1.5, r.Height * 0.12));
        ring.Brush.Freeze(); ring.Freeze();
        var pen = new Pen(new SolidColorBrush(HandStroke), Math.Max(1.0, r.Height * 0.05));
        pen.Brush.Freeze(); pen.Freeze();

        var s = Math.Min(r.Width, r.Height);
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;

        // The "OK" thumb-index ring on the lower-left.
        dc.DrawEllipse(Brushes.Transparent, ring, new Point(cx - 0.12 * s, cy + 0.12 * s), 0.22 * s, 0.22 * s);

        // The three extended fingers as rounded amber bars fanning to the upper-right.
        for (var f = 0; f < 3; f++)
        {
            var fx = cx + (0.10 + f * 0.13) * s;
            var top = cy - 0.30 * s;
            var bar = new RectangleGeometry(new Rect(fx, top, 0.10 * s, 0.40 * s), 0.05 * s, 0.05 * s);
            dc.DrawGeometry(fill, pen, bar);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] png)
    {
        // PNG: 8-byte signature, then IHDR chunk: 4-byte length, 4-byte type "IHDR",
        // then 4-byte width, 4-byte height (big-endian). Width starts at offset 16.
        if (png.Length < 24)
            return (0, 0);
        int Read32(int o) => (png[o] << 24) | (png[o + 1] << 16) | (png[o + 2] << 8) | png[o + 3];
        return (Read32(16), Read32(20));
    }

    private static int NextCodePoint(string s, int index, out int codePoint)
    {
        var c = s[index];
        if (char.IsHighSurrogate(c) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
        {
            codePoint = char.ConvertToUtf32(c, s[index + 1]);
            return 2;
        }

        codePoint = c;
        return 1;
    }

    private static bool IsEmojiCodePoint(int cp) =>
        cp is >= 0x1F300 and <= 0x1FAFF   // Misc symbols & pictographs, emoticons, transport, supplemental, symbols-and-pictographs-extended-A
        || cp is >= 0x2600 and <= 0x27BF  // Misc symbols + Dingbats
        || cp is >= 0x1F000 and <= 0x1F0FF // Mahjong/Domino/Playing cards
        || cp is >= 0x1F1E6 and <= 0x1F1FF; // Regional indicator symbols (flags)

    private static bool IsEmojiModifier(int cp) =>
        cp == 0xFE0F            // VARIATION SELECTOR-16 (emoji presentation)
        || cp == 0xFE0E         // VARIATION SELECTOR-15 (text presentation)
        || cp == 0x200D         // ZERO WIDTH JOINER
        || cp is >= 0x1F3FB and <= 0x1F3FF; // skin-tone modifiers
}

/// <summary>A rendered color-emoji PNG plus its device pixel size and the scale it was rendered at.</summary>
internal readonly record struct EmojiBitmap(byte[] Png, int PixelWidth, int PixelHeight, double RenderScale)
{
    /// <summary>Logical (DIP) width = device pixels / render scale.</summary>
    internal double LogicalWidth => PixelWidth / Math.Max(1.0, RenderScale);

    /// <summary>Logical (DIP) height = device pixels / render scale.</summary>
    internal double LogicalHeight => PixelHeight / Math.Max(1.0, RenderScale);
}
