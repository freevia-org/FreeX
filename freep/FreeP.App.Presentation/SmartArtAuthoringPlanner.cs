using System.Text;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>PowerPoint-style SmartArt color authoring presets.</summary>
public enum SmartArtColorPreset
{
    ThemeAccents,
    SingleAccent,
    Grayscale,
}

/// <summary>Bounded SmartArt layout choices whose live layout engine can regenerate the cache.</summary>
public enum SmartArtLayoutPreset
{
    BasicProcess,
    AlternatingProcess,
    ArrowRibbon,
    CircleProcess,
    FunnelProcess,
    VerticalProcess,
    VerticalBoxList,
    BasicCycle,
    BasicBlockList,
    StackedList,
    DescendingBlockList,
    BasicPyramid,
    RadialCycle,
    BasicMatrix,
    BasicVenn,
    RadialVenn,
    TargetList,
    StackedVenn,
    BasicHierarchy,
    HorizontalHierarchy,
    OrgChart,
}

/// <summary>Bounded PowerPoint SmartArt Quick Style choices.</summary>
public enum SmartArtQuickStylePreset
{
    Simple,
    Moderate,
    Intense,
}

public sealed record SmartArtColorApplyResult(
    bool Applied,
    string Message,
    string? PartPath,
    int ColorCount);

public sealed record SmartArtLayoutApplyResult(
    bool Applied,
    string Message,
    string? PartPath,
    string? LayoutUniqueId,
    SmartArtFamily Family);

public sealed record SmartArtQuickStyleApplyResult(
    bool Applied,
    string Message,
    string? PartPath,
    string? StyleUniqueId);

/// <summary>
/// Applies SmartArt Change Colors operations to both the live model and the native diagram
/// colors part. Keeping the native part authoritative makes the edit survive save/reopen.
/// </summary>
public static class SmartArtAuthoringPlanner
{
    private static readonly XNamespace Diagram = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public const string ThemeAccentsCommandId = "freep.smartart.colors.theme-accents";
    public const string SingleAccentCommandId = "freep.smartart.colors.single-accent";
    public const string GrayscaleCommandId = "freep.smartart.colors.grayscale";
    public const string BasicProcessLayoutCommandId = "freep.smartart.layout.basic-process";
    public const string AlternatingProcessLayoutCommandId = "freep.smartart.layout.alternating-process";
    public const string ArrowRibbonLayoutCommandId = "freep.smartart.layout.arrow-ribbon";
    public const string CircleProcessLayoutCommandId = "freep.smartart.layout.circle-process";
    public const string FunnelProcessLayoutCommandId = "freep.smartart.layout.funnel-process";
    public const string VerticalProcessLayoutCommandId = "freep.smartart.layout.vertical-process";
    public const string VerticalBoxListLayoutCommandId = "freep.smartart.layout.vertical-box-list";
    public const string BasicCycleLayoutCommandId = "freep.smartart.layout.basic-cycle";
    public const string BasicBlockListLayoutCommandId = "freep.smartart.layout.basic-block-list";
    public const string StackedListLayoutCommandId = "freep.smartart.layout.stacked-list";
    public const string DescendingBlockListLayoutCommandId = "freep.smartart.layout.descending-block-list";
    public const string BasicPyramidLayoutCommandId = "freep.smartart.layout.basic-pyramid";
    public const string RadialCycleLayoutCommandId = "freep.smartart.layout.radial-cycle";
    public const string BasicMatrixLayoutCommandId = "freep.smartart.layout.basic-matrix";
    public const string BasicVennLayoutCommandId = "freep.smartart.layout.basic-venn";
    public const string RadialVennLayoutCommandId = "freep.smartart.layout.radial-venn";
    public const string TargetListLayoutCommandId = "freep.smartart.layout.target-list";
    public const string StackedVennLayoutCommandId = "freep.smartart.layout.stacked-venn";
    public const string BasicHierarchyLayoutCommandId = "freep.smartart.layout.basic-hierarchy";
    public const string HorizontalHierarchyLayoutCommandId = "freep.smartart.layout.horizontal-hierarchy";
    public const string OrgChartLayoutCommandId = "freep.smartart.layout.org-chart";
    public const string SimpleQuickStyleCommandId = "freep.smartart.style.simple";
    public const string ModerateQuickStyleCommandId = "freep.smartart.style.moderate";
    public const string IntenseQuickStyleCommandId = "freep.smartart.style.intense";

    public static SmartArtQuickStyleApplyResult ApplyQuickStylePreset(
        SmartArtShape? smartArt,
        SmartArtQuickStylePreset preset)
    {
        if (smartArt is null)
            return NotAppliedQuickStyle("No SmartArt graphic is available.");

        var styleId = preset switch
        {
            SmartArtQuickStylePreset.Simple =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1",
            SmartArtQuickStylePreset.Moderate =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/moderate1",
            SmartArtQuickStylePreset.Intense =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/intense1",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

        var part = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramStyle", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("quickStyle", StringComparison.OrdinalIgnoreCase));
        XDocument document;
        if (part is null)
        {
            if (!smartArt.Parts.Values.Any(candidate =>
                    candidate.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase)))
            {
                return NotAppliedQuickStyle("The SmartArt graphic has no native data part for a Quick Style definition.");
            }

            part = CreateQuickStylePart(smartArt);
            document = CreateEmptyQuickStyleDefinition();
        }
        else
        {
            if (part.Bytes.Length == 0)
                return NotAppliedQuickStyle("The native SmartArt Quick Style part is empty.");

            try
            {
                document = ParseXml(part.Bytes);
            }
            catch (Exception ex) when (ex is FormatException or XmlException)
            {
                return NotAppliedQuickStyle("The native SmartArt Quick Style part is not valid XML.");
            }
        }

        var styleDefinition = document.Root;
        if (styleDefinition is null || styleDefinition.Name != Diagram + "styleDef")
            return NotAppliedQuickStyle("The native SmartArt Quick Style definition is missing.");

        styleDefinition.SetAttributeValue("uniqueId", styleId);
        var title = preset switch
        {
            SmartArtQuickStylePreset.Simple => "Simple",
            SmartArtQuickStylePreset.Moderate => "Moderate",
            SmartArtQuickStylePreset.Intense => "Intense",
            _ => preset.ToString(),
        };
        var titleElement = styleDefinition.Elements(Diagram + "title").FirstOrDefault();
        if (titleElement is null)
            styleDefinition.AddFirst(new XElement(Diagram + "title", new XAttribute("val", title)));
        else
            titleElement.SetAttributeValue("val", title);

        part.Bytes = Serialize(document);
        smartArt.QuickStyle ??= new SmartArtQuickStyleMetadata();
        smartArt.QuickStyle.UniqueId = styleId;
        smartArt.QuickStyle.Title = title;

        return new SmartArtQuickStyleApplyResult(
            true,
            $"SmartArt Quick Style changed to {preset}.",
            part.PartPath,
            styleId);
    }

    public static SmartArtLayoutApplyResult ApplyLayoutPreset(
        SmartArtShape? smartArt,
        SmartArtLayoutPreset preset)
    {
        if (smartArt?.Data is null)
            return NotAppliedLayout("No SmartArt data model is available.");

        var layoutPart = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("layout", StringComparison.OrdinalIgnoreCase));
        if (layoutPart is null || layoutPart.Bytes.Length == 0)
            return NotAppliedLayout("The SmartArt graphic has no native layout definition.");

        var (layoutId, family) = preset switch
        {
            SmartArtLayoutPreset.BasicProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.AlternatingProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/alternatingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ArrowRibbon =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/arrowRibbon", SmartArtFamily.Process),
            SmartArtLayoutPreset.CircleProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/circleProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.FunnelProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/funnelProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.VerticalProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.VerticalBoxList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicBlockList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.StackedList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/stackedList", SmartArtFamily.List),
            SmartArtLayoutPreset.DescendingBlockList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicPyramid =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid", SmartArtFamily.List),
            SmartArtLayoutPreset.RadialCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.BasicVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.RadialVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.TargetList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/targetList", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.StackedVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.BasicHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.OrgChart =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/orgChart", SmartArtFamily.Hierarchy),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };

        XDocument document;
        try
        {
            document = ParseXml(layoutPart.Bytes);
        }
        catch (Exception ex) when (ex is FormatException or XmlException)
        {
            return NotAppliedLayout("The native SmartArt layout part is not valid XML.");
        }

        var layoutDefinition = document
            .Descendants(Diagram + "layoutDef")
            .FirstOrDefault();
        if (layoutDefinition is null)
            return NotAppliedLayout("The native SmartArt layout definition is missing.");

        layoutDefinition.SetAttributeValue("uniqueId", layoutId);
        layoutPart.Bytes = Serialize(document);
        smartArt.Data.LayoutUniqueId = layoutId;
        smartArt.Data.Family = family;
        smartArt.Data.IsLiveLayoutSupported = true;
        smartArt.FallbackShapes.Clear();

        return new SmartArtLayoutApplyResult(
            true,
            $"SmartArt layout changed to {preset}.",
            layoutPart.PartPath,
            layoutId,
            family);
    }

    public static SmartArtColorApplyResult ApplyColorPreset(
        SmartArtShape? smartArt,
        SmartArtColorPreset preset,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (smartArt is null)
            return NotApplied("No SmartArt graphic is available.");

        var part = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramColors", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("colors", StringComparison.OrdinalIgnoreCase));

        XDocument document;
        if (part is null)
        {
            if (!smartArt.Parts.Values.Any(candidate =>
                    candidate.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase)))
            {
                return NotApplied("The SmartArt graphic has no native data part for a new colors definition.");
            }

            part = CreateColorsPart(smartArt);
            document = CreateEmptyColorsDefinition();
        }
        else
        {
            if (part.Bytes.Length == 0)
                return NotApplied("The SmartArt colors part is empty.");

            try
            {
                document = ParseXml(part.Bytes);
            }
            catch (Exception ex) when (ex is FormatException or XmlException)
            {
                return NotApplied("The native SmartArt colors part is not valid XML.");
            }
        }

        var fillLists = document
            .Descendants(Diagram + "fillClrLst")
            .ToList();
        var firstPalette = fillLists.FirstOrDefault()?
            .Elements()
            .Where(IsColorElement)
            .ToList();
        if (firstPalette is null || firstPalette.Count == 0)
            return NotApplied("The SmartArt colors part has no node fill palette.");

        var appliedColors = BuildColors(preset, firstPalette.Count, theme, effectiveClrMap);
        foreach (var fillList in fillLists)
        {
            var colors = fillList.Elements().Where(IsColorElement).ToList();
            for (var index = 0; index < colors.Count; index++)
            {
                var color = appliedColors[index % appliedColors.Count];
                colors[index].ReplaceWith(BuildColorElement(color, colors[index]));
            }
        }

        part.Bytes = Serialize(document);
        smartArt.Colors ??= new SmartArtColorMetadata();
        smartArt.Colors.Palette.Clear();
        smartArt.Colors.Palette.AddRange(appliedColors.Select(color => color.ModelColor));

        return new SmartArtColorApplyResult(
            true,
            $"SmartArt colors changed to the {preset} preset.",
            part.PartPath,
            appliedColors.Count);
    }

    private static DiagramPart CreateColorsPart(SmartArtShape smartArt)
    {
        var dataPartPath = smartArt.Parts.Values
            .FirstOrDefault(part => part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase))
            ?.PartPath;
        if (string.IsNullOrWhiteSpace(dataPartPath))
            throw new InvalidOperationException("A SmartArt data part is required to create a colors part.");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataPartPath)))
            .ToLowerInvariant()[..8];
        var directory = dataPartPath[..(dataPartPath.LastIndexOf('/') + 1)];
        var part = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            PartPath = $"{directory}colors-freep-{digest}.xml",
            Bytes = Array.Empty<byte>(),
        };

        smartArt.Parts[part.PartPath] = part;
        smartArt.DiagramRelIds["cs"] = "rIdFreePColors";
        return part;
    }

    private static DiagramPart CreateQuickStylePart(SmartArtShape smartArt)
    {
        var dataPartPath = smartArt.Parts.Values
            .FirstOrDefault(part => part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase))
            ?.PartPath;
        if (string.IsNullOrWhiteSpace(dataPartPath))
            throw new InvalidOperationException("A SmartArt data part is required to create a Quick Style part.");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataPartPath)))
            .ToLowerInvariant()[..8];
        var directory = dataPartPath[..(dataPartPath.LastIndexOf('/') + 1)];
        var part = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            PartPath = $"{directory}quickStyle-freep-{digest}.xml",
            Bytes = Array.Empty<byte>(),
        };

        smartArt.Parts[part.PartPath] = part;
        smartArt.DiagramRelIds["qs"] = "rIdFreePQuickStyle";
        return part;
    }

    private static XDocument CreateEmptyQuickStyleDefinition() =>
        new(new XElement(
            Diagram + "styleDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName)));

    private static XDocument CreateEmptyColorsDefinition()
    {
        var fillColors = Enumerable.Range(0, 6)
            .Select(_ => new XElement(Drawing + "schemeClr", new XAttribute("val", "accent1")));
        return new XDocument(
            new XElement(
                Diagram + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName),
                new XElement(
                    Diagram + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(Diagram + "fillClrLst", fillColors))));
    }

    private static bool IsColorElement(XElement element) =>
        element.Name.Namespace == Drawing &&
        (element.Name.LocalName is "schemeClr" or "srgbClr" or "sysClr");

    private static XElement BuildColorElement(PaletteColor color, XElement previous)
    {
        var name = color.SchemeRole is null ? "srgbClr" : "schemeClr";
        var attributes = previous.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName is not "val" and not "lastClr")
            .ToList();
        attributes.Add(new XAttribute("val", color.SchemeRole ?? color.Resolved.ToString()[1..]));
        return new XElement(Drawing + name, attributes, previous.Nodes());
    }

    private static IReadOnlyList<PaletteColor> BuildColors(
        SmartArtColorPreset preset,
        int count,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var accents = new[]
        {
            ThemeColorSlot.Accent1,
            ThemeColorSlot.Accent2,
            ThemeColorSlot.Accent3,
            ThemeColorSlot.Accent4,
            ThemeColorSlot.Accent5,
            ThemeColorSlot.Accent6,
        };

        if (preset == SmartArtColorPreset.Grayscale)
        {
            var grays = new[] { 0x404040, 0x666666, 0x808080, 0x999999, 0xB3B3B3, 0xD9D9D9 };
            return Enumerable.Range(0, count)
                .Select(index =>
                {
                    var resolved = SrgbColor.FromRgb(grays[index % grays.Length]);
                    return new PaletteColor(resolved, null, new ThemeAwareColor(resolved));
                })
                .ToArray();
        }

        var slot = accents[0];
        if (preset == SmartArtColorPreset.SingleAccent)
        {
            var single = CreateSchemeColor(slot, theme, effectiveClrMap);
            return Enumerable.Repeat(single, count).ToArray();
        }

        return Enumerable.Range(0, count)
            .Select(index => CreateSchemeColor(accents[index % accents.Length], theme, effectiveClrMap))
            .ToArray();
    }

    private static PaletteColor CreateSchemeColor(
        ThemeColorSlot slot,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var role = $"accent{(int)slot - (int)ThemeColorSlot.Accent1 + 1}";
        var reference = new SchemeColorRef { RoleName = role, Slot = slot };
        var modelColor = new ThemeAwareColor(
            ThemeColorResolver.Resolve(new ThemeAwareColor(theme.ColorScheme[slot], reference), theme, effectiveClrMap),
            reference);
        return new PaletteColor(modelColor.Resolved, role, modelColor);
    }

    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static XDocument ParseXml(byte[] bytes) =>
        XDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), LoadOptions.PreserveWhitespace);

    private static SmartArtColorApplyResult NotApplied(string message) =>
        new(false, message, null, 0);

    private static SmartArtLayoutApplyResult NotAppliedLayout(string message) =>
        new(false, message, null, null, SmartArtFamily.Unknown);

    private static SmartArtQuickStyleApplyResult NotAppliedQuickStyle(string message) =>
        new(false, message, null, null);

    private sealed record PaletteColor(SrgbColor Resolved, string? SchemeRole, ThemeAwareColor ModelColor);
}
