using System.Globalization;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Reads DrawingML color elements (a:solidFill, a:gradFill) from PresentationML XML,
/// resolving scheme colors against a <see cref="PresentationColorScheme"/>.
/// </summary>
internal static class PptxColorReader
{
    internal static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Tries to read a ThemeAwareColor from any color-bearing container element that contains
    /// a:srgbClr or a:schemeClr. Returns null when no supported color child is found.
    /// </summary>
    public static ThemeAwareColor? TryReadColor(XElement? colorContainer, PresentationColorScheme scheme)
    {
        if (colorContainer is null) return null;

        // a:srgbClr val="RRGGBB"
        var srgbEl = colorContainer.Element(A + "srgbClr");
        var srgb = srgbEl?.Attribute("val")?.Value;
        if (!string.IsNullOrWhiteSpace(srgb))
        {
            var rgb = ParseHexColor(srgb);
            return rgb.HasValue ? new ThemeAwareColor(rgb.Value, ReadAlpha(srgbEl)) : null;
        }

        // a:schemeClr val="dk1|lt1|..."
        var schemeClr = colorContainer.Element(A + "schemeClr");
        if (schemeClr is not null)
        {
            var val = schemeClr.Attribute("val")?.Value;
            if (TryMapSchemeColor(val, out var slot))
            {
                var lumMod = ReadPercentage(schemeClr.Element(A + "lumMod")?.Attribute("val")?.Value) ?? 1.0;
                var lumOff = ReadPercentage(schemeClr.Element(A + "lumOff")?.Attribute("val")?.Value) ?? 0.0;

                // a:tint val: blend toward white; val=100000=original, val=0=white
                var tintRaw = ReadPercentage(schemeClr.Element(A + "tint")?.Attribute("val")?.Value);
                double tintFraction = tintRaw.HasValue ? tintRaw.Value : 1.0;

                // a:shade val: blend toward black; val=100000=original, val=0=black
                var shadeRaw = ReadPercentage(schemeClr.Element(A + "shade")?.Attribute("val")?.Value);
                double shadeFraction = shadeRaw.HasValue ? shadeRaw.Value : 1.0;

                var resolved = ThemeColorTransform.Apply(
                    scheme[slot],
                    lumMod,
                    lumOff,
                    tintFraction,
                    shadeFraction);

                // Store the raw role name (val) so ThemeColorResolver can apply clrMap indirection
                // at render time (master's clrMap may remap tx1→lt1, bg1→dk1, etc.).
                return new ThemeAwareColor(resolved, new SchemeColorRef
                {
                    RoleName = val!.Trim(),
                    Slot     = slot,
                    LumMod   = lumMod,
                    LumOff   = lumOff,
                    Tint     = tintFraction,
                    Shade    = shadeFraction,
                }, ReadAlpha(schemeClr));
            }
        }

        // a:sysClr lastClr="RRGGBB" (window/windowText)
        var sysClr = colorContainer.Element(A + "sysClr");
        if (sysClr is not null)
        {
            var last = sysClr.Attribute("lastClr")?.Value;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var rgb = ParseHexColor(last);
                return rgb.HasValue ? new ThemeAwareColor(rgb.Value, ReadAlpha(sysClr)) : null;
            }
        }

        return null;
    }

    /// <summary>Reads a ShapeFill from the parent element (spPr or similar). Returns null if no fill element found.</summary>
    /// <param name="resolveBlip">
    /// Optional delegate to resolve a blip embed rId to (imageBytes, contentType).
    /// When null, a:blipFill elements are skipped.
    /// </param>
    public static ShapeFill? TryReadFill(
        XElement spPr,
        PresentationColorScheme scheme,
        Func<string, (byte[] bytes, string contentType)?>? resolveBlip = null)
    {
        // a:noFill
        if (spPr.Element(A + "noFill") is not null)
            return ShapeFill.None.Instance;

        // a:solidFill
        var solidFill = spPr.Element(A + "solidFill");
        if (solidFill is not null)
        {
            var color = TryReadColor(solidFill, scheme);
            return color is not null ? new ShapeFill.Solid(color) : null;
        }

        // a:gradFill — read ALL stops
        var gradFill = spPr.Element(A + "gradFill");
        if (gradFill is not null)
            return TryReadGradFill(gradFill, scheme);

        // a:blipFill — picture fill on a shape
        var blipFill = spPr.Element(A + "blipFill");
        if (blipFill is not null && resolveBlip is not null)
            return TryReadBlipFill(blipFill, resolveBlip);

        // a:pattFill — pattern fill
        var pattFill = spPr.Element(A + "pattFill");
        if (pattFill is not null)
            return TryReadPattFill(pattFill, scheme);

        return null;
    }

    /// <summary>
    /// Parses a:gradFill into a <see cref="ShapeFill.Gradient"/> with all stops.
    /// </summary>
    internal static ShapeFill.Gradient? TryReadGradFill(XElement gradFill, PresentationColorScheme scheme)
    {
        var gsLst = gradFill.Element(A + "gsLst");
        var gsElements = gsLst?.Elements(A + "gs")
            .OrderBy(g => ParseInt(g.Attribute("pos")?.Value))
            .ToList();

        if (gsElements is not { Count: >= 1 })
            return null;

        var stops = new List<GradientStop>(gsElements.Count);
        foreach (var gs in gsElements)
        {
            // pos is in 1/1000 % (0..100000)
            int posRaw = ParseInt(gs.Attribute("pos")?.Value);
            double position = posRaw / 100000.0;

            // Each gs contains either a:solidFill, a:schemeClr, a:srgbClr, or a:sysClr directly.
            var color = TryReadColor(gs.Element(A + "solidFill") ?? gs, scheme);
            if (color is null) continue;

            stops.Add(new GradientStop(position, color));
        }

        if (stops.Count < 1)
            return null;

        // Ensure 2-stop minimum by duplicating single stop
        if (stops.Count == 1)
            stops.Add(new GradientStop(1.0, stops[0].Color));

        // Gradient kind: a:lin → linear, a:path → radial/rect
        var linEl  = gradFill.Element(A + "lin");
        var pathEl = gradFill.Element(A + "path");

        if (pathEl is not null)
        {
            // Radial (circle or rect)
            return new ShapeFill.Gradient(stops, GradientKind.Radial, 0);
        }

        // Linear: read angle
        double angleDeg = 90; // default top→bottom
        if (linEl is not null)
        {
            var angAttr = linEl.Attribute("ang")?.Value;
            if (long.TryParse(angAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var angRaw))
                angleDeg = angRaw / 60000.0;
        }

        return new ShapeFill.Gradient(stops, GradientKind.Linear, angleDeg);
    }

    /// <summary>
    /// Parses a:blipFill into a <see cref="ShapeFill.Picture"/>.
    /// </summary>
    internal static ShapeFill.Picture? TryReadBlipFill(
        XElement blipFill,
        Func<string, (byte[] bytes, string contentType)?> resolveBlip)
    {
        var R_ns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var blip = blipFill.Element(A + "blip");
        var embedId = blip?.Attribute(XName.Get("embed", R_ns))?.Value;
        if (string.IsNullOrWhiteSpace(embedId)) return null;

        var resolved = resolveBlip(embedId);
        if (resolved is null) return null;

        // Check for tile mode (a:tile) vs stretch (a:stretch/a:fillRect)
        bool tile = blipFill.Element(A + "tile") is not null;

        return new ShapeFill.Picture(resolved.Value.bytes, resolved.Value.contentType, tile);
    }

    /// <summary>
    /// Parses a:pattFill into a <see cref="ShapeFill.Pattern"/>.
    /// </summary>
    internal static ShapeFill.Pattern? TryReadPattFill(XElement pattFill, PresentationColorScheme scheme)
    {
        var preset = pattFill.Attribute("prst")?.Value;
        if (string.IsNullOrWhiteSpace(preset)) preset = "pct50";

        var fgClr = pattFill.Element(A + "fgClr");
        var bgClr = pattFill.Element(A + "bgClr");

        var fg = TryReadColor(fgClr, scheme) ?? ThemeAwareColor.Black;
        var bg = TryReadColor(bgClr, scheme) ?? ThemeAwareColor.White;

        return new ShapeFill.Pattern(preset, fg, bg);
    }

    /// <summary>Reads a ShapeOutline from an a:ln element. Returns null if element missing.</summary>
    public static ShapeOutline? TryReadOutline(XElement? lnElement, PresentationColorScheme scheme)
    {
        if (lnElement is null) return null;

        // PowerPoint emits an empty line element for some WordArt material
        // routes. It carries no width, fill, or line-end semantics and is a
        // no-outline marker rather than the default black line.
        if (!lnElement.HasAttributes && !lnElement.Elements().Any())
            return ShapeOutline.None.Instance;

        // a:noFill inside the line = no outline
        if (lnElement.Element(A + "noFill") is not null)
            return ShapeOutline.None.Instance;

        // A zero-width line is an explicit no-line marker. Treating it as an
        // omitted width used to synthesize the default 0.75 pt black outline.
        if (long.TryParse(lnElement.Attribute("w")?.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var explicitWidthEmu) && explicitWidthEmu == 0)
            return ShapeOutline.None.Instance;

        // w attribute in EMU; convert to points
        var wAttr = lnElement.Attribute("w")?.Value;
        double widthPt = 0.75;
        if (long.TryParse(wAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wEmu) && wEmu > 0)
            widthPt = DrawingMlCoordinateUnits.EmuToPoints(wEmu);

        // a:prstDash
        var dashVal = lnElement.Element(A + "prstDash")?.Attribute("val")?.Value;
        var dash = MapDash(dashVal);
        var beginLineEnd = TryReadLineEnd(lnElement.Element(A + "tailEnd"));
        var endLineEnd = TryReadLineEnd(lnElement.Element(A + "headEnd"));

        // Wave 22B: a:gradFill → gradient outline
        var gradFill = lnElement.Element(A + "gradFill");
        if (gradFill is not null)
        {
            var gradient = TryReadGradFill(gradFill, scheme);
            if (gradient is not null)
                return new ShapeOutline.GradientVisible(gradient, widthPt, dash, beginLineEnd, endLineEnd);
        }

        var solidFill = lnElement.Element(A + "solidFill");
        var color = solidFill is not null ? TryReadColor(solidFill, scheme) : null;
        color ??= ThemeAwareColor.Black; // fallback

        return new ShapeOutline.Visible(color, widthPt, dash, beginLineEnd, endLineEnd);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    internal static bool TryMapSchemeColor(string? value, out ThemeColorSlot slot)
        => ThemeColorSlotMapper.TryMapRole(value, out slot);

    internal static string ToSchemeColorString(ThemeColorSlot slot) =>
        ThemeColorSlotMapper.ToSchemeColorString(slot);

    private static SrgbColor? ParseHexColor(string? hex)
    {
        return DrawingMlRgbColor.TryParseHexRgb(hex, out var rgb)
            ? new SrgbColor(rgb.R, rgb.G, rgb.B)
            : null;
    }

    private static double? ReadPercentage(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v / 100000.0, 0, 2.0) // lumMod can exceed 1.0
            : null;

    private static byte ReadAlpha(XElement? colorElement)
    {
        var alpha = ReadPercentage(colorElement?.Element(A + "alpha")?.Attribute("val")?.Value);
        return alpha.HasValue
            ? (byte)Math.Round(Math.Clamp(alpha.Value, 0.0, 1.0) * 255.0)
            : byte.MaxValue;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static OutlineDash MapDash(string? val) =>
        val?.ToLowerInvariant() switch
        {
            "dash" => OutlineDash.Dash,
            "dot" or "sysdot" => OutlineDash.Dot,
            "dashdot" => OutlineDash.DashDot,
            "lgdash" or "lgdashdot" or "lgdashdotdot" => OutlineDash.LongDash,
            "sysdash" => OutlineDash.SystemDash,
            "sysdashDot" => OutlineDash.SystemDashDot,
            _ => OutlineDash.Solid
        };

    private static ShapeLineEnd? TryReadLineEnd(XElement? lineEndElement) =>
        lineEndElement?.Attribute("type")?.Value?.Trim().ToLowerInvariant() switch
        {
            "triangle" => new ShapeLineEnd(ShapeLineEndKind.Triangle),
            _ => null
        };
}
