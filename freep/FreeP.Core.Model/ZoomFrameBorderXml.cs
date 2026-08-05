using System.Xml.Linq;

namespace FreeP.Core.Model;

/// <summary>Mutates supported outline color/width/dash/fill states inside native Zoom <c>zmPr/spPr</c>.</summary>
internal static class ZoomFrameBorderXml
{
    private static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void Set(
        XElement zoomProperties,
        string? color,
        int? widthEmu,
        OutlineDash? dash,
        ZoomFrameBorderGradient? gradient = null,
        ZoomFrameBorderPattern? pattern = null,
        bool? noFill = null,
        ThemeColorSlot? themeColor = null,
        ZoomFrameBorderShadow? shadow = null,
        bool? shadowEnabled = null,
        ZoomFrameBorderGlow? glow = null,
        bool? glowEnabled = null,
        ZoomFrameBorderSoftEdge? softEdge = null,
        bool? softEdgeEnabled = null,
        ZoomFrameBorderReflection? reflection = null,
        bool? reflectionEnabled = null)
    {
        if (noFill == false)
            noFill = null;

        // Null means the model did not understand the native line; preserve it verbatim.
        if (color is null && widthEmu is null && dash is null && gradient is null && pattern is null && noFill is null && themeColor is null
            && shadow is null && shadowEnabled is null
            && glow is null && glowEnabled is null
            && softEdge is null && softEdgeEnabled is null
            && reflection is null && reflectionEnabled is null)
            return;

        var shapeProperties = zoomProperties.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "spPr", StringComparison.OrdinalIgnoreCase));
        if (shapeProperties is null)
            return;

        var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
        if (line is null && (widthEmu is not null || dash is not null || gradient is not null || noFill == true))
        {
            line = new XElement(Drawing + "ln");
            shapeProperties.Add(line);
        }
        if (line is not null && widthEmu is not null)
            line.SetAttributeValue("w", widthEmu.Value);

        if (line is not null && dash is OutlineDash dashValue)
        {
            line.Elements(Drawing + "prstDash").Remove();
            if (dashValue != OutlineDash.Solid)
                line.Add(new XElement(Drawing + "prstDash",
                    new XAttribute("val", ToDashToken(dashValue))));
        }

        SetOuterShadow(shapeProperties, shadow, shadowEnabled);
        SetGlow(shapeProperties, glow, glowEnabled);
        SetSoftEdge(shapeProperties, softEdge, softEdgeEnabled);
        SetReflection(shapeProperties, reflection, reflectionEnabled);

        var solidFill = line?.Elements(Drawing + "solidFill").FirstOrDefault();
        if (gradient is not null && pattern is not null)
            throw new ArgumentException("A Zoom frame border cannot use both gradient and pattern fills.");
        if (themeColor is not null && (color is not null || gradient is not null || pattern is not null || noFill == true))
            throw new ArgumentException("A Zoom frame border cannot combine a theme color with another fill.");
        if (noFill == true && (color is not null || gradient is not null || pattern is not null || themeColor is not null))
            throw new ArgumentException("A Zoom frame border cannot combine no-fill with another fill.");

        if (noFill == true)
        {
            line ??= new XElement(Drawing + "ln");
            RemoveAllFills(line);
            line.AddFirst(new XElement(Drawing + "noFill"));
            if (line.Parent is null)
                shapeProperties.Add(line);
            return;
        }

        if (gradient is not null)
        {
            line ??= new XElement(Drawing + "ln");
            RemoveRecognizedFills(line);
            line.AddFirst(new XElement(Drawing + "gradFill",
                new XElement(Drawing + "gsLst",
                    new XElement(Drawing + "gs",
                        new XAttribute("pos", 0),
                        new XElement(Drawing + "srgbClr",
                            new XAttribute("val", gradient.StartColor))),
                    new XElement(Drawing + "gs",
                        new XAttribute("pos", 100000),
                        new XElement(Drawing + "srgbClr",
                            new XAttribute("val", gradient.EndColor)))),
                new XElement(Drawing + "lin",
                    new XAttribute("ang", gradient.Angle),
                    new XAttribute("scaled", 1))));
            if (line.Parent is null)
                shapeProperties.Add(line);
            return;
        }

        if (pattern is not null)
        {
            line ??= new XElement(Drawing + "ln");
            RemoveRecognizedFills(line);
            line.AddFirst(new XElement(Drawing + "pattFill",
                new XAttribute("prst", pattern.Preset),
                new XElement(Drawing + "fgClr",
                    new XElement(Drawing + "srgbClr",
                        new XAttribute("val", pattern.ForegroundColor))),
                new XElement(Drawing + "bgClr",
                    new XElement(Drawing + "srgbClr",
                        new XAttribute("val", pattern.BackgroundColor)))));
            if (line.Parent is null)
                shapeProperties.Add(line);
            return;
        }

        if (themeColor is ThemeColorSlot themeSlot)
        {
            line ??= new XElement(Drawing + "ln");
            RemoveRecognizedFills(line);
            line.AddFirst(new XElement(Drawing + "solidFill",
                new XElement(Drawing + "schemeClr",
                    new XAttribute("val", ThemeColorSlotMapper.ToSchemeColorString(themeSlot)))));
            if (line.Parent is null)
                shapeProperties.Add(line);
            return;
        }

        if (color is { Length: 0 })
        {
            if (solidFill is null
                && line?.Elements(Drawing + "gradFill").FirstOrDefault() is null
                && line?.Elements(Drawing + "noFill").FirstOrDefault() is null)
                return;

            RemoveRecognizedFills(line!, preserveUnsupportedPattern: true);
            if (line!.Attributes().Count() == 0 && !line.Elements().Any())
                line.Remove();
            return;
        }

        if (color is null)
            return;

        line ??= new XElement(Drawing + "ln");
        RemoveRecognizedFills(line);
        line.AddFirst(new XElement(Drawing + "solidFill",
            new XElement(Drawing + "srgbClr", new XAttribute("val", color))));
        if (line.Parent is null)
            shapeProperties.Add(line);
    }

    private static void RemoveRecognizedFills(
        XElement line,
        bool preserveUnsupportedPattern = false)
    {
        foreach (var fill in line.Elements().Where(element =>
                     element.Name == Drawing + "solidFill"
                     || element.Name == Drawing + "gradFill"
                     || (element.Name == Drawing + "pattFill"
                         && (!preserveUnsupportedPattern || IsSupportedPattern(element)))
                     || element.Name == Drawing + "noFill").ToArray())
            fill.Remove();
    }

    private static bool IsSupportedPattern(XElement pattern)
    {
        var preset = ZoomFrameBorderPatternCatalog.Normalize(pattern.Attribute("prst")?.Value);
        var colors = pattern.Elements()
            .Where(element => element.Name == Drawing + "fgClr" || element.Name == Drawing + "bgClr")
            .Select(element => element.Element(Drawing + "srgbClr")?.Attribute("val")?.Value)
            .ToArray();
        return preset is not null
            && colors.Length == 2
            && colors.All(color => color is { Length: 6 } && color.All(Uri.IsHexDigit));
    }

    private static void RemoveAllFills(XElement line)
    {
        foreach (var fill in line.Elements().Where(element =>
                     element.Name == Drawing + "solidFill"
                     || element.Name == Drawing + "gradFill"
                     || element.Name == Drawing + "pattFill"
                     || element.Name == Drawing + "noFill").ToArray())
            fill.Remove();
    }

    private static void SetOuterShadow(
        XElement shapeProperties,
        ZoomFrameBorderShadow? shadow,
        bool? shadowEnabled)
    {
        if (shadow is null && shadowEnabled is null)
            return;

        var effectList = shapeProperties.Elements(Drawing + "effectLst").FirstOrDefault();
        var outerShadow = effectList?.Elements(Drawing + "outerShdw").FirstOrDefault();
        if (shadowEnabled == false)
        {
            outerShadow?.Remove();
            if (effectList is not null && !effectList.Elements().Any())
                effectList.Remove();
            return;
        }

        if (shadow is null)
            return;

        effectList ??= new XElement(Drawing + "effectLst");
        outerShadow ??= new XElement(Drawing + "outerShdw");
        outerShadow.ReplaceAttributes(
            new XAttribute("blurRad", shadow.BlurRadiusEmu),
            new XAttribute("dist", shadow.DistanceEmu),
            new XAttribute("dir", shadow.Direction));
        outerShadow.Elements().Remove();
        outerShadow.Add(
            new XElement(Drawing + "srgbClr",
                new XAttribute("val", shadow.Color),
                new XElement(Drawing + "alpha",
                    new XAttribute("val", shadow.Alpha))));
        if (outerShadow.Parent is null)
            effectList.Add(outerShadow);
        if (effectList.Parent is null)
        {
            var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
            if (line is null)
                shapeProperties.Add(effectList);
            else
                line.AddAfterSelf(effectList);
        }
    }

    private static void SetGlow(
        XElement shapeProperties,
        ZoomFrameBorderGlow? glow,
        bool? glowEnabled)
    {
        if (glow is null && glowEnabled is null)
            return;

        var effectList = shapeProperties.Elements(Drawing + "effectLst").FirstOrDefault();
        var nativeGlow = effectList?.Elements(Drawing + "glow").FirstOrDefault();
        if (glowEnabled == false)
        {
            nativeGlow?.Remove();
            if (effectList is not null && !effectList.Elements().Any())
                effectList.Remove();
            return;
        }

        if (glow is null)
            return;

        effectList ??= new XElement(Drawing + "effectLst");
        nativeGlow ??= new XElement(Drawing + "glow");
        nativeGlow.SetAttributeValue("rad", glow.RadiusEmu);
        nativeGlow.Elements().Remove();
        nativeGlow.Add(
            new XElement(Drawing + "srgbClr",
                new XAttribute("val", glow.Color),
                new XElement(Drawing + "alpha",
                    new XAttribute("val", glow.Alpha))));
        if (nativeGlow.Parent is null)
            effectList.Add(nativeGlow);
        if (effectList.Parent is null)
        {
            var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
            if (line is null)
                shapeProperties.Add(effectList);
            else
                line.AddAfterSelf(effectList);
        }
    }

    private static void SetSoftEdge(
        XElement shapeProperties,
        ZoomFrameBorderSoftEdge? softEdge,
        bool? softEdgeEnabled)
    {
        if (softEdge is null && softEdgeEnabled is null)
            return;

        var effectList = shapeProperties.Elements(Drawing + "effectLst").FirstOrDefault();
        var nativeSoftEdge = effectList?.Elements(Drawing + "softEdge").FirstOrDefault();
        if (softEdgeEnabled == false)
        {
            nativeSoftEdge?.Remove();
            if (effectList is not null && !effectList.Elements().Any())
                effectList.Remove();
            return;
        }

        if (softEdge is null)
            return;

        effectList ??= new XElement(Drawing + "effectLst");
        nativeSoftEdge ??= new XElement(Drawing + "softEdge");
        nativeSoftEdge.SetAttributeValue("rad", softEdge.RadiusEmu);
        if (nativeSoftEdge.Parent is null)
            effectList.Add(nativeSoftEdge);
        if (effectList.Parent is null)
        {
            var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
            if (line is null)
                shapeProperties.Add(effectList);
            else
                line.AddAfterSelf(effectList);
        }
    }

    private static void SetReflection(
        XElement shapeProperties,
        ZoomFrameBorderReflection? reflection,
        bool? reflectionEnabled)
    {
        if (reflection is null && reflectionEnabled is null)
            return;

        var effectList = shapeProperties.Elements(Drawing + "effectLst").FirstOrDefault();
        var nativeReflection = effectList?.Elements(Drawing + "reflection").FirstOrDefault();
        if (reflectionEnabled == false)
        {
            nativeReflection?.Remove();
            if (effectList is not null && !effectList.Elements().Any())
                effectList.Remove();
            return;
        }

        if (reflection is null)
            return;

        effectList ??= new XElement(Drawing + "effectLst");
        nativeReflection ??= new XElement(Drawing + "reflection");
        nativeReflection.ReplaceAttributes(
            new XAttribute("blurRad", reflection.BlurRadiusEmu),
            new XAttribute("stA", reflection.Alpha),
            new XAttribute("dist", reflection.DistanceEmu),
            new XAttribute("dir", reflection.Direction),
            new XAttribute("sy", reflection.ScaleY),
            new XAttribute("endPos", reflection.EndPosition));
        if (nativeReflection.Parent is null)
            effectList.Add(nativeReflection);
        if (effectList.Parent is null)
        {
            var line = shapeProperties.Elements(Drawing + "ln").FirstOrDefault();
            if (line is null)
                shapeProperties.Add(effectList);
            else
                line.AddAfterSelf(effectList);
        }
    }

    private static string ToDashToken(OutlineDash dash) => dash switch
    {
        OutlineDash.Dash => "dash",
        OutlineDash.Dot => "dot",
        OutlineDash.DashDot => "dashDot",
        OutlineDash.LongDash => "lgDash",
        OutlineDash.LongDashDot => "lgDashDot",
        OutlineDash.LongDashDotDot => "lgDashDotDot",
        OutlineDash.SystemDash => "sysDash",
        OutlineDash.SystemDot => "sysDot",
        OutlineDash.SystemDashDot => "sysDashDot",
        _ => "solid",
    };
}
