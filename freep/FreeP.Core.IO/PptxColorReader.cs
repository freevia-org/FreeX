using System.Globalization;
using System.Xml.Linq;
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
        var srgb = colorContainer.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (!string.IsNullOrWhiteSpace(srgb))
        {
            var rgb = ParseHexColor(srgb);
            return rgb.HasValue ? new ThemeAwareColor(rgb.Value) : null;
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

                var baseColor = scheme[slot];
                var resolved = ApplyLumModOff(baseColor, lumMod, lumOff);
                if (tintFraction < 1.0)
                    resolved = ApplyTint(resolved, tintFraction);
                if (shadeFraction < 1.0)
                    resolved = ApplyShade(resolved, shadeFraction);

                return new ThemeAwareColor(resolved, new SchemeColorRef
                {
                    Slot    = slot,
                    LumMod  = lumMod,
                    LumOff  = lumOff,
                    Tint    = tintFraction,
                    Shade   = shadeFraction,
                });
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
                return rgb.HasValue ? new ThemeAwareColor(rgb.Value) : null;
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

        // a:noFill inside the line = no outline
        if (lnElement.Element(A + "noFill") is not null)
            return ShapeOutline.None.Instance;

        var solidFill = lnElement.Element(A + "solidFill");
        var color = solidFill is not null ? TryReadColor(solidFill, scheme) : null;
        color ??= ThemeAwareColor.Black; // fallback

        // w attribute in EMU; convert to points
        var wAttr = lnElement.Attribute("w")?.Value;
        double widthPt = 0.75;
        if (long.TryParse(wAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wEmu) && wEmu > 0)
            widthPt = wEmu / 12700.0;

        // a:prstDash
        var dashVal = lnElement.Element(A + "prstDash")?.Attribute("val")?.Value;
        var dash = MapDash(dashVal);

        return new ShapeOutline.Visible(color, widthPt, dash);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    internal static bool TryMapSchemeColor(string? value, out ThemeColorSlot slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        slot = value.Trim().ToLowerInvariant() switch
        {
            "dk1" or "tx1" => ThemeColorSlot.Dk1,
            "lt1" or "bg1" => ThemeColorSlot.Lt1,
            "dk2" or "tx2" => ThemeColorSlot.Dk2,
            "lt2" or "bg2" => ThemeColorSlot.Lt2,
            "accent1" => ThemeColorSlot.Accent1,
            "accent2" => ThemeColorSlot.Accent2,
            "accent3" => ThemeColorSlot.Accent3,
            "accent4" => ThemeColorSlot.Accent4,
            "accent5" => ThemeColorSlot.Accent5,
            "accent6" => ThemeColorSlot.Accent6,
            "hlink" => ThemeColorSlot.HLink,
            "folhlink" => ThemeColorSlot.FolHLink,
            _ => default
        };

        return value.Trim().ToLowerInvariant() is
            "dk1" or "tx1" or
            "lt1" or "bg1" or
            "dk2" or "tx2" or
            "lt2" or "bg2" or
            "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or
            "hlink" or "folhlink";
    }

    internal static string ToSchemeColorString(ThemeColorSlot slot) =>
        slot switch
        {
            ThemeColorSlot.Dk1 => "dk1",
            ThemeColorSlot.Lt1 => "lt1",
            ThemeColorSlot.Dk2 => "dk2",
            ThemeColorSlot.Lt2 => "lt2",
            ThemeColorSlot.Accent1 => "accent1",
            ThemeColorSlot.Accent2 => "accent2",
            ThemeColorSlot.Accent3 => "accent3",
            ThemeColorSlot.Accent4 => "accent4",
            ThemeColorSlot.Accent5 => "accent5",
            ThemeColorSlot.Accent6 => "accent6",
            ThemeColorSlot.HLink => "hlink",
            ThemeColorSlot.FolHLink => "folHlink",
            _ => "dk1"
        };

    internal static SrgbColor ApplyLumModOff(SrgbColor baseColor, double lumMod, double lumOff)
    {
        if (lumMod == 1.0 && lumOff == 0.0) return baseColor;

        // Convert to HLS, apply, convert back
        RgbToHls(baseColor, out var h, out var l, out var s);

        l = Math.Clamp(l * lumMod + lumOff, 0.0, 1.0);

        return HlsToRgb(h, l, s);
    }

    /// <summary>
    /// Apply DrawingML tint. OOXML tint val=100000 = original color (no tinting);
    /// tint val=0 = fully white. tintFraction = val/100000 in [0,1].
    /// Formula (in sRGB): R_new = R * tintFraction + 255 * (1 - tintFraction)
    /// </summary>
    internal static SrgbColor ApplyTint(SrgbColor baseColor, double tintFraction)
    {
        if (tintFraction >= 1.0) return baseColor;
        if (tintFraction <= 0.0) return new SrgbColor(255, 255, 255);
        double r = baseColor.R * tintFraction + 255.0 * (1.0 - tintFraction);
        double g = baseColor.G * tintFraction + 255.0 * (1.0 - tintFraction);
        double b = baseColor.B * tintFraction + 255.0 * (1.0 - tintFraction);
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(r), 0, 255),
            (byte)Math.Clamp(Math.Round(g), 0, 255),
            (byte)Math.Clamp(Math.Round(b), 0, 255));
    }

    /// <summary>
    /// Apply DrawingML shade. OOXML shade val=100000 = original color (no shading);
    /// shade val=0 = fully black. shadeFraction = val/100000 in [0,1].
    /// Formula (in sRGB): R_new = R * shadeFraction
    /// </summary>
    internal static SrgbColor ApplyShade(SrgbColor baseColor, double shadeFraction)
    {
        if (shadeFraction >= 1.0) return baseColor;
        if (shadeFraction <= 0.0) return new SrgbColor(0, 0, 0);
        double r = baseColor.R * shadeFraction;
        double g = baseColor.G * shadeFraction;
        double b = baseColor.B * shadeFraction;
        return new SrgbColor(
            (byte)Math.Clamp(Math.Round(r), 0, 255),
            (byte)Math.Clamp(Math.Round(g), 0, 255),
            (byte)Math.Clamp(Math.Round(b), 0, 255));
    }

    private static void RgbToHls(SrgbColor c, out double h, out double l, out double s)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta < 1e-10) { h = 0; s = 0; return; }

        s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

        if (max == r) h = ((g - b) / delta % 6.0) / 6.0;
        else if (max == g) h = ((b - r) / delta + 2.0) / 6.0;
        else h = ((r - g) / delta + 4.0) / 6.0;

        if (h < 0) h += 1.0;
    }

    private static SrgbColor HlsToRgb(double h, double l, double s)
    {
        if (s < 1e-10)
        {
            var v = (byte)Math.Clamp(Math.Round(l * 255), 0, 255);
            return new SrgbColor(v, v, v);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        double p = 2.0 * l - q;

        return new SrgbColor(
            HueToRgb(p, q, h + 1.0 / 3.0),
            HueToRgb(p, q, h),
            HueToRgb(p, q, h - 1.0 / 3.0));
    }

    private static byte HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6.0 ? p + (q - p) * 6.0 * t
            : t < 1.0 / 2.0 ? q
            : t < 2.0 / 3.0 ? p + (q - p) * (2.0 / 3.0 - t) * 6.0
            : p;
        return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }

    private static SrgbColor? ParseHexColor(string hex)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length != 6) return null;
        if (!byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return null;
        if (!byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return null;
        if (!byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return null;
        return new SrgbColor(r, g, b);
    }

    private static double? ReadPercentage(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v / 100000.0, 0, 2.0) // lumMod can exceed 1.0
            : null;

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
}
