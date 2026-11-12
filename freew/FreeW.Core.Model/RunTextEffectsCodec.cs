using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// The typed view over <see cref="RunTextEffects"/>. The record stores each effect as its source XML so
/// documents round-trip losslessly; this codec interprets those strings (for rendering and for the
/// Text Effects UI) and serialises an edited effect back to valid <c>w14</c> XML in schema order.
/// <para>
/// Reading is strict about numbers: every attribute is range-checked against the schema's bounds and a
/// value outside them makes the whole effect uninterpretable (null) rather than guessed at. The stored
/// string is untouched either way, so an effect this codec cannot read still round-trips verbatim.
/// </para>
/// <para>
/// Writing produces the same canonical string the DOCX reader stores (element only, unformatted, one
/// trailing <c>xmlns:w14</c> declaration), so an effect set through the UI compares equal to the same
/// effect after a save and reload.
/// </para>
/// </summary>
public static class RunTextEffectsCodec
{
    private static readonly XNamespace W14 = RunTextEffects.Namespace;

    // Schema bounds (ST_PositiveCoordinate, ST_LineWidth, ST_PositiveFixedAngle, ST_FixedAngle,
    // ST_PositiveFixedPercentage / ST_Percentage as Word writes them).
    private const long MaxCoordinateEmu = 27273042316900;
    private const long MaxLineWidthEmu = 20116800;
    private const int MaxAngle = 21600000;
    private const int MaxSkew = 5400000;
    private const int MaxPercentage = 100000;
    private const int MaxScale = 10000000;
    private const int MaxGradientStops = 64;

    private static readonly HashSet<string> Alignments =
        new(StringComparer.Ordinal) { "tl", "t", "tr", "l", "ctr", "r", "bl", "b", "br", "none" };

    /// <summary>
    /// Puts hand-written effect XML (used for effects with no typed form, such as the 3-D pair) into
    /// the canonical stored form. Returns null when it is not a well-formed w14 element.
    /// </summary>
    public static string? Canonicalize(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XElement element;
        try
        {
            element = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        if (element.Name.Namespace != W14)
            return null;

        foreach (var node in element.DescendantsAndSelf())
            node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        element.DescendantNodes().OfType<XText>()
            .Where(text => string.IsNullOrWhiteSpace(text.Value))
            .ToList()
            .ForEach(text => text.Remove());
        return Serialize(element);
    }

    // ---------------------------------------------------------------- reading

    public static TextGlowEffect? ReadGlow(string? xml)
    {
        if (Parse(xml, "glow") is not { } element
            || !TryLong(element, "rad", 0, MaxCoordinateEmu, 0, out var radius)
            || ReadColor(element) is not { } color)
        {
            return null;
        }

        return new TextGlowEffect(radius, color);
    }

    public static TextShadowEffect? ReadShadow(string? xml)
    {
        if (Parse(xml, "shadow") is not { } element
            || !TryLong(element, "blurRad", 0, MaxCoordinateEmu, 0, out var blur)
            || !TryLong(element, "dist", 0, MaxCoordinateEmu, 0, out var distance)
            || !TryInt(element, "dir", 0, MaxAngle, 0, out var direction)
            || !TryInt(element, "sx", -MaxScale, MaxScale, 100000, out var scaleX)
            || !TryInt(element, "sy", -MaxScale, MaxScale, 100000, out var scaleY)
            || !TryInt(element, "kx", -MaxSkew, MaxSkew, 0, out var skewX)
            || !TryInt(element, "ky", -MaxSkew, MaxSkew, 0, out var skewY)
            || !TryAlignment(element, "b", out var alignment)
            || ReadColor(element) is not { } color)
        {
            return null;
        }

        return new TextShadowEffect
        {
            BlurRadiusEmu = blur,
            DistanceEmu = distance,
            DirectionAngle = direction,
            ScaleX = scaleX,
            ScaleY = scaleY,
            SkewX = skewX,
            SkewY = skewY,
            Alignment = alignment,
            Color = color,
        };
    }

    public static TextReflectionEffect? ReadReflection(string? xml)
    {
        if (Parse(xml, "reflection") is not { } element
            || !TryLong(element, "blurRad", 0, MaxCoordinateEmu, 0, out var blur)
            || !TryInt(element, "stA", 0, MaxPercentage, 100000, out var startOpacity)
            || !TryInt(element, "stPos", 0, MaxPercentage, 0, out var startPosition)
            || !TryInt(element, "endA", 0, MaxPercentage, 0, out var endOpacity)
            || !TryInt(element, "endPos", 0, MaxPercentage, 100000, out var endPosition)
            || !TryLong(element, "dist", 0, MaxCoordinateEmu, 0, out var distance)
            || !TryInt(element, "dir", 0, MaxAngle, 0, out var direction)
            || !TryInt(element, "fadeDir", 0, MaxAngle, 5400000, out var fadeDirection)
            || !TryInt(element, "sx", -MaxScale, MaxScale, 100000, out var scaleX)
            || !TryInt(element, "sy", -MaxScale, MaxScale, 100000, out var scaleY)
            || !TryInt(element, "kx", -MaxSkew, MaxSkew, 0, out var skewX)
            || !TryInt(element, "ky", -MaxSkew, MaxSkew, 0, out var skewY)
            || !TryAlignment(element, "b", out var alignment))
        {
            return null;
        }

        return new TextReflectionEffect
        {
            BlurRadiusEmu = blur,
            StartOpacity = startOpacity,
            StartPosition = startPosition,
            EndOpacity = endOpacity,
            EndPosition = endPosition,
            DistanceEmu = distance,
            DirectionAngle = direction,
            FadeDirectionAngle = fadeDirection,
            ScaleX = scaleX,
            ScaleY = scaleY,
            SkewX = skewX,
            SkewY = skewY,
            Alignment = alignment,
        };
    }

    public static TextFillEffect? ReadFill(string? xml) =>
        Parse(xml, "textFill") is { } element ? ReadFillChild(element, required: true) : null;

    public static TextOutlineEffect? ReadOutline(string? xml)
    {
        if (Parse(xml, "textOutline") is not { } element
            || !TryLong(element, "w", 0, MaxLineWidthEmu, 9525, out var width))
        {
            return null;
        }

        TextFillEffect? fill = null;
        if (HasFillChild(element))
        {
            fill = ReadFillChild(element, required: true);
            if (fill is null)
                return null;
        }

        var join = TextOutlineJoin.Unspecified;
        int? miterLimit = null;
        if (element.Element(W14 + "round") is not null)
        {
            join = TextOutlineJoin.Round;
        }
        else if (element.Element(W14 + "bevel") is not null)
        {
            join = TextOutlineJoin.Bevel;
        }
        else if (element.Element(W14 + "miter") is { } miter)
        {
            join = TextOutlineJoin.Miter;
            if (miter.Attribute(W14 + "lim") is not null)
            {
                if (!TryInt(miter, "lim", 0, MaxScale, 0, out var limit))
                    return null;
                miterLimit = limit;
            }
        }

        return new TextOutlineEffect
        {
            WidthEmu = width,
            Cap = Token(element, "cap"),
            Compound = Token(element, "cmpd"),
            Alignment = Token(element, "algn"),
            Fill = fill,
            Dash = element.Element(W14 + "prstDash") is { } dash ? Token(dash, "val") : null,
            Join = join,
            MiterLimit = miterLimit,
        };
    }

    // ---------------------------------------------------------------- writing

    public static string WriteGlow(TextGlowEffect glow)
    {
        ArgumentNullException.ThrowIfNull(glow);
        return Serialize(new XElement(
            W14 + "glow",
            Attribute("rad", Math.Clamp(glow.RadiusEmu, 0, MaxCoordinateEmu)),
            WriteColor(glow.Color)));
    }

    public static string WriteShadow(TextShadowEffect shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        return Serialize(new XElement(
            W14 + "shadow",
            Attribute("blurRad", Math.Clamp(shadow.BlurRadiusEmu, 0, MaxCoordinateEmu)),
            Attribute("dist", Math.Clamp(shadow.DistanceEmu, 0, MaxCoordinateEmu)),
            Attribute("dir", NormalizeAngle(shadow.DirectionAngle)),
            Attribute("sx", Math.Clamp(shadow.ScaleX, -MaxScale, MaxScale)),
            Attribute("sy", Math.Clamp(shadow.ScaleY, -MaxScale, MaxScale)),
            Attribute("kx", Math.Clamp(shadow.SkewX, -MaxSkew, MaxSkew)),
            Attribute("ky", Math.Clamp(shadow.SkewY, -MaxSkew, MaxSkew)),
            Attribute("algn", SafeAlignment(shadow.Alignment, "tl")),
            WriteColor(shadow.Color)));
    }

    public static string WriteReflection(TextReflectionEffect reflection)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        return Serialize(new XElement(
            W14 + "reflection",
            Attribute("blurRad", Math.Clamp(reflection.BlurRadiusEmu, 0, MaxCoordinateEmu)),
            Attribute("stA", Math.Clamp(reflection.StartOpacity, 0, MaxPercentage)),
            Attribute("stPos", Math.Clamp(reflection.StartPosition, 0, MaxPercentage)),
            Attribute("endA", Math.Clamp(reflection.EndOpacity, 0, MaxPercentage)),
            Attribute("endPos", Math.Clamp(reflection.EndPosition, 0, MaxPercentage)),
            Attribute("dist", Math.Clamp(reflection.DistanceEmu, 0, MaxCoordinateEmu)),
            Attribute("dir", NormalizeAngle(reflection.DirectionAngle)),
            Attribute("fadeDir", NormalizeAngle(reflection.FadeDirectionAngle)),
            Attribute("sx", Math.Clamp(reflection.ScaleX, -MaxScale, MaxScale)),
            Attribute("sy", Math.Clamp(reflection.ScaleY, -MaxScale, MaxScale)),
            Attribute("kx", Math.Clamp(reflection.SkewX, -MaxSkew, MaxSkew)),
            Attribute("ky", Math.Clamp(reflection.SkewY, -MaxSkew, MaxSkew)),
            Attribute("algn", SafeAlignment(reflection.Alignment, "bl"))));
    }

    public static string WriteFill(TextFillEffect fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        return Serialize(new XElement(W14 + "textFill", WriteFillChild(fill)));
    }

    public static string WriteOutline(TextOutlineEffect outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        var element = new XElement(
            W14 + "textOutline",
            Attribute("w", Math.Clamp(outline.WidthEmu, 0, MaxLineWidthEmu)));
        AddToken(element, "cap", outline.Cap);
        AddToken(element, "cmpd", outline.Compound);
        AddToken(element, "algn", outline.Alignment);

        // CT_TextOutlineEffect order: fill, dash, join.
        if (outline.Fill is { } fill)
            element.Add(WriteFillChild(fill));
        if (SafeToken(outline.Dash) is { } dash)
            element.Add(new XElement(W14 + "prstDash", new XAttribute(W14 + "val", dash)));
        switch (outline.Join)
        {
            case TextOutlineJoin.Round:
                element.Add(new XElement(W14 + "round"));
                break;
            case TextOutlineJoin.Bevel:
                element.Add(new XElement(W14 + "bevel"));
                break;
            case TextOutlineJoin.Miter:
                var miter = new XElement(W14 + "miter");
                if (outline.MiterLimit is { } limit)
                    miter.Add(Attribute("lim", Math.Clamp(limit, 0, MaxScale)));
                element.Add(miter);
                break;
        }

        return Serialize(element);
    }

    // ---------------------------------------------------------------- shared pieces

    private static XElement? Parse(string? xml, string localName)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var element = XElement.Parse(xml);
            return element.Name == W14 + localName ? element : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string Serialize(XElement element)
    {
        // Same canonical form as the DOCX reader: the prefix declaration is the LAST attribute.
        element.SetAttributeValue(XNamespace.Xmlns + "w14", W14.NamespaceName);
        return element.ToString(SaveOptions.DisableFormatting);
    }

    private static XAttribute Attribute(string name, long value) =>
        new(W14 + name, value.ToString(CultureInfo.InvariantCulture));

    private static XAttribute Attribute(string name, string value) => new(W14 + name, value);

    private static void AddToken(XElement element, string name, string? value)
    {
        if (SafeToken(value) is { } token)
            element.Add(Attribute(name, token));
    }

    /// <summary>Enumeration tokens are short ASCII identifiers; anything else is dropped, never written.</summary>
    private static string? SafeToken(string? value) =>
        value is { Length: > 0 and <= 32 } && value.All(character => char.IsAsciiLetterOrDigit(character))
            ? value
            : null;

    private static string? Token(XElement element, string name) =>
        SafeToken(element.Attribute(W14 + name)?.Value);

    private static string SafeAlignment(string? value, string fallback) =>
        value is not null && Alignments.Contains(value) ? value : fallback;

    private static bool TryAlignment(XElement element, string fallback, out string alignment)
    {
        var raw = element.Attribute(W14 + "algn")?.Value;
        alignment = raw ?? fallback;
        return raw is null || Alignments.Contains(raw);
    }

    private static int NormalizeAngle(int angle)
    {
        var normalized = angle % MaxAngle;
        return normalized < 0 ? normalized + MaxAngle : normalized;
    }

    private static bool TryLong(XElement element, string name, long min, long max, long fallback, out long value)
    {
        value = fallback;
        if (element.Attribute(W14 + name) is not { } attribute)
            return true;

        return long.TryParse(attribute.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            && value >= min
            && value <= max;
    }

    private static bool TryInt(XElement element, string name, int min, int max, int fallback, out int value)
    {
        var ok = TryLong(element, name, min, max, fallback, out var parsed);
        value = ok ? (int)parsed : fallback;
        return ok;
    }

    private static bool HasFillChild(XElement parent) =>
        parent.Element(W14 + "noFill") is not null
        || parent.Element(W14 + "solidFill") is not null
        || parent.Element(W14 + "gradFill") is not null;

    private static TextFillEffect? ReadFillChild(XElement parent, bool required)
    {
        if (parent.Element(W14 + "noFill") is not null)
            return TextFillEffect.NoFill;

        if (parent.Element(W14 + "solidFill") is { } solid)
            return ReadColor(solid) is { } color ? TextFillEffect.Solid(color) : null;

        if (parent.Element(W14 + "gradFill") is { } gradient)
            return ReadGradient(gradient);

        return required ? null : TextFillEffect.NoFill;
    }

    private static TextFillEffect? ReadGradient(XElement gradient)
    {
        var stopElements = gradient.Element(W14 + "gsLst")?.Elements(W14 + "gs").ToList();
        if (stopElements is not { Count: >= 2 and <= MaxGradientStops })
            return null;

        var stops = new List<TextGradientStop>(stopElements.Count);
        foreach (var stopElement in stopElements)
        {
            if (!TryInt(stopElement, "pos", 0, MaxPercentage, 0, out var position)
                || ReadColor(stopElement) is not { } color)
            {
                return null;
            }

            stops.Add(new TextGradientStop(position, color));
        }

        if (gradient.Element(W14 + "lin") is { } linear)
        {
            if (!TryInt(linear, "ang", 0, MaxAngle, 0, out var angle))
                return null;
            return new TextFillEffect
            {
                Kind = TextFillKind.Gradient,
                GradientKind = TextGradientKind.Linear,
                Stops = stops,
                LinearAngle = angle,
                LinearScaled = linear.Attribute(W14 + "scaled")?.Value is "1" or "true",
            };
        }

        if (gradient.Element(W14 + "path") is { } path)
        {
            var kind = path.Attribute(W14 + "path")?.Value switch
            {
                "circle" => TextGradientKind.Circle,
                "rect" => TextGradientKind.Rectangle,
                "shape" => TextGradientKind.Shape,
                _ => (TextGradientKind?)null,
            };
            if (kind is null)
                return null;

            int left = 0, top = 0, right = 0, bottom = 0;
            if (path.Element(W14 + "fillToRect") is { } rect
                && (!TryInt(rect, "l", -MaxScale, MaxScale, 0, out left)
                    || !TryInt(rect, "t", -MaxScale, MaxScale, 0, out top)
                    || !TryInt(rect, "r", -MaxScale, MaxScale, 0, out right)
                    || !TryInt(rect, "b", -MaxScale, MaxScale, 0, out bottom)))
            {
                return null;
            }

            return new TextFillEffect
            {
                Kind = TextFillKind.Gradient,
                GradientKind = kind.Value,
                Stops = stops,
                FillToRectLeft = left,
                FillToRectTop = top,
                FillToRectRight = right,
                FillToRectBottom = bottom,
            };
        }

        // Stops with neither w14:lin nor w14:path: Word draws a linear gradient at angle 0.
        return new TextFillEffect { Kind = TextFillKind.Gradient, GradientKind = TextGradientKind.Linear, Stops = stops };
    }

    private static XElement WriteFillChild(TextFillEffect fill)
    {
        switch (fill.Kind)
        {
            case TextFillKind.None:
                return new XElement(W14 + "noFill");
            case TextFillKind.Gradient when fill.Stops.Count >= 2:
                var gradient = new XElement(
                    W14 + "gradFill",
                    new XElement(
                        W14 + "gsLst",
                        fill.Stops
                            .Take(MaxGradientStops)
                            .Select(stop => new XElement(
                                W14 + "gs",
                                Attribute("pos", Math.Clamp(stop.Position, 0, MaxPercentage)),
                                WriteColor(stop.Color)))));
                if (fill.GradientKind == TextGradientKind.Linear)
                {
                    gradient.Add(new XElement(
                        W14 + "lin",
                        Attribute("ang", NormalizeAngle(fill.LinearAngle)),
                        Attribute("scaled", fill.LinearScaled ? "1" : "0")));
                }
                else
                {
                    gradient.Add(new XElement(
                        W14 + "path",
                        Attribute("path", fill.GradientKind switch
                        {
                            TextGradientKind.Circle => "circle",
                            TextGradientKind.Rectangle => "rect",
                            _ => "shape",
                        }),
                        new XElement(
                            W14 + "fillToRect",
                            Attribute("l", fill.FillToRectLeft),
                            Attribute("t", fill.FillToRectTop),
                            Attribute("r", fill.FillToRectRight),
                            Attribute("b", fill.FillToRectBottom))));
                }

                return gradient;
            default:
                return new XElement(
                    W14 + "solidFill",
                    WriteColor(fill.Color ?? fill.Stops.FirstOrDefault()?.Color ?? TextEffectColor.FromScheme("tx1")));
        }
    }

    private static TextEffectColor? ReadColor(XElement parent)
    {
        XElement? element;
        TextEffectColor color;
        if ((element = parent.Element(W14 + "srgbClr")) is not null)
        {
            var hex = element.Attribute(W14 + "val")?.Value;
            if (hex is not { Length: 6 } || !hex.All(Uri.IsHexDigit))
                return null;
            color = new TextEffectColor { RgbHex = hex.ToUpperInvariant() };
        }
        else if ((element = parent.Element(W14 + "schemeClr")) is not null)
        {
            if (SafeToken(element.Attribute(W14 + "val")?.Value) is not { } scheme)
                return null;
            color = new TextEffectColor { SchemeName = scheme };
        }
        else
        {
            return null;
        }

        var transforms = new List<TextEffectColorTransform>();
        foreach (var child in element.Elements())
        {
            if (child.Name.Namespace != W14 || !TextEffectColor.TransformOrder.Contains(child.Name.LocalName))
                continue;
            if (!TryInt(child, "val", -MaxScale, MaxScale, 0, out var value))
                return null;
            transforms.Add(new TextEffectColorTransform(child.Name.LocalName, value));
        }

        return color with { Transforms = transforms };
    }

    private static XElement WriteColor(TextEffectColor color)
    {
        var element = color.RgbHex is { } hex
            ? new XElement(W14 + "srgbClr", Attribute("val", TextEffectColor.NormalizeHex(hex)))
            : new XElement(W14 + "schemeClr", Attribute("val", SafeToken(color.SchemeName) ?? "tx1"));

        // CT_Color's transform group is a sequence, so emit in schema order whatever order was set.
        foreach (var name in TextEffectColor.TransformOrder)
        {
            foreach (var transform in color.Transforms.Where(transform => transform.Name == name))
                element.Add(new XElement(W14 + name, Attribute("val", Math.Clamp(transform.Value, -MaxScale, MaxScale))));
        }

        return element;
    }
}
