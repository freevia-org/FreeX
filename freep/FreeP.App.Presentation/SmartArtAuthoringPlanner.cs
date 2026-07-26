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
    MonochromaticAccent2,
    MonochromaticAccent3,
    MonochromaticAccent4,
    MonochromaticAccent5,
    MonochromaticAccent6,
    Grayscale,
}

/// <summary>Bounded SmartArt layout choices whose live layout engine can regenerate the cache.</summary>
public enum SmartArtLayoutPreset
{
    BasicProcess,
    BasicTimeline,
    ContinuousBlockProcess,
    SegmentedProcess,
    ChevronProcess,
    BasicChevronProcess,
    ClosedChevronProcess,
    BendingProcess,
    AlternatingProcess,
    ArrowRibbon,
    CircleProcess,
    FunnelProcess,
    VerticalProcess,
    VerticalBoxList,
    VerticalBulletList,
    BasicCycle,
    ContinuousCycle,
    GearCycle,
    TextCycle,
    BlockCycle,
    NonDirectionalCycle,
    BasicList,
    BasicBlockList,
    StackedList,
    DescendingBlockList,
    BasicPyramid,
    PyramidList,
    RadialCycle,
    RadialList,
    BasicMatrix,
    TitledMatrix,
    GridMatrix,
    BasicVenn,
    RadialVenn,
    TargetList,
    StackedVenn,
    BasicHierarchy,
    Hierarchy3,
    HorizontalHierarchy,
    OrgChart,
    PictureCaptionList,
    LabeledHierarchy,
    TableHierarchy,
}

/// <summary>Bounded PowerPoint SmartArt Quick Style choices.</summary>
public enum SmartArtQuickStylePreset
{
    Simple,
    Moderate,
    Intense,
    Subtle,
    SoftEdge,
    Insert,
    Cartoon,
    Powder,
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
    public const string MonochromaticAccent2CommandId = "freep.smartart.colors.monochromatic-accent-2";
    public const string MonochromaticAccent3CommandId = "freep.smartart.colors.monochromatic-accent-3";
    public const string MonochromaticAccent4CommandId = "freep.smartart.colors.monochromatic-accent-4";
    public const string MonochromaticAccent5CommandId = "freep.smartart.colors.monochromatic-accent-5";
    public const string MonochromaticAccent6CommandId = "freep.smartart.colors.monochromatic-accent-6";
    public const string GrayscaleCommandId = "freep.smartart.colors.grayscale";
    public const string BasicProcessLayoutCommandId = "freep.smartart.layout.basic-process";
    public const string BasicTimelineLayoutCommandId = "freep.smartart.layout.basic-timeline";
    public const string ContinuousBlockProcessLayoutCommandId = "freep.smartart.layout.continuous-block-process";
    public const string SegmentedProcessLayoutCommandId = "freep.smartart.layout.segmented-process";
    public const string ChevronProcessLayoutCommandId = "freep.smartart.layout.chevron-process";
    public const string BasicChevronProcessLayoutCommandId = "freep.smartart.layout.basic-chevron-process";
    public const string ClosedChevronProcessLayoutCommandId = "freep.smartart.layout.closed-chevron-process";
    public const string BendingProcessLayoutCommandId = "freep.smartart.layout.bending-process";
    public const string AlternatingProcessLayoutCommandId = "freep.smartart.layout.alternating-process";
    public const string ArrowRibbonLayoutCommandId = "freep.smartart.layout.arrow-ribbon";
    public const string CircleProcessLayoutCommandId = "freep.smartart.layout.circle-process";
    public const string FunnelProcessLayoutCommandId = "freep.smartart.layout.funnel-process";
    public const string VerticalProcessLayoutCommandId = "freep.smartart.layout.vertical-process";
    public const string VerticalBoxListLayoutCommandId = "freep.smartart.layout.vertical-box-list";
    public const string VerticalBulletListLayoutCommandId = "freep.smartart.layout.vertical-bullet-list";
    public const string BasicCycleLayoutCommandId = "freep.smartart.layout.basic-cycle";
    public const string ContinuousCycleLayoutCommandId = "freep.smartart.layout.continuous-cycle";
    public const string GearCycleLayoutCommandId = "freep.smartart.layout.gear-cycle";
    public const string TextCycleLayoutCommandId = "freep.smartart.layout.text-cycle";
    public const string BlockCycleLayoutCommandId = "freep.smartart.layout.block-cycle";
    public const string NonDirectionalCycleLayoutCommandId = "freep.smartart.layout.non-directional-cycle";
    public const string BasicListLayoutCommandId = "freep.smartart.layout.basic-list";
    public const string BasicBlockListLayoutCommandId = "freep.smartart.layout.basic-block-list";
    public const string StackedListLayoutCommandId = "freep.smartart.layout.stacked-list";
    public const string DescendingBlockListLayoutCommandId = "freep.smartart.layout.descending-block-list";
    public const string BasicPyramidLayoutCommandId = "freep.smartart.layout.basic-pyramid";
    public const string PyramidListLayoutCommandId = "freep.smartart.layout.pyramid-list";
    public const string RadialCycleLayoutCommandId = "freep.smartart.layout.radial-cycle";
    public const string RadialListLayoutCommandId = "freep.smartart.layout.radial-list";
    public const string BasicMatrixLayoutCommandId = "freep.smartart.layout.basic-matrix";
    public const string TitledMatrixLayoutCommandId = "freep.smartart.layout.titled-matrix";
    public const string GridMatrixLayoutCommandId = "freep.smartart.layout.grid-matrix";
    public const string BasicVennLayoutCommandId = "freep.smartart.layout.basic-venn";
    public const string RadialVennLayoutCommandId = "freep.smartart.layout.radial-venn";
    public const string TargetListLayoutCommandId = "freep.smartart.layout.target-list";
    public const string StackedVennLayoutCommandId = "freep.smartart.layout.stacked-venn";
    public const string BasicHierarchyLayoutCommandId = "freep.smartart.layout.basic-hierarchy";
    public const string Hierarchy3LayoutCommandId = "freep.smartart.layout.hierarchy-3";
    public const string HorizontalHierarchyLayoutCommandId = "freep.smartart.layout.horizontal-hierarchy";
    public const string OrgChartLayoutCommandId = "freep.smartart.layout.org-chart";
    public const string PictureCaptionListLayoutCommandId = "freep.smartart.layout.picture-caption-list";
    public const string LabeledHierarchyLayoutCommandId = "freep.smartart.layout.labeled-hierarchy";
    public const string TableHierarchyLayoutCommandId = "freep.smartart.layout.table-hierarchy";
    public const string SimpleQuickStyleCommandId = "freep.smartart.style.simple";
    public const string ModerateQuickStyleCommandId = "freep.smartart.style.moderate";
    public const string IntenseQuickStyleCommandId = "freep.smartart.style.intense";
    public const string SubtleQuickStyleCommandId = "freep.smartart.style.subtle";
    public const string SoftEdgeQuickStyleCommandId = "freep.smartart.style.soft-edge";
    public const string InsertQuickStyleCommandId = "freep.smartart.style.insert";
    public const string CartoonQuickStyleCommandId = "freep.smartart.style.cartoon";
    public const string PowderQuickStyleCommandId = "freep.smartart.style.powder";
    public const string ConvertToShapesCommandId = "freep.smartart.convert-to-shapes";

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
            SmartArtQuickStylePreset.Subtle =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/subtle1",
            SmartArtQuickStylePreset.SoftEdge =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/subtle2",
            SmartArtQuickStylePreset.Insert =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/intense2",
            SmartArtQuickStylePreset.Cartoon =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d1",
            SmartArtQuickStylePreset.Powder =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d2",
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
            SmartArtQuickStylePreset.Subtle => "Subtle",
            SmartArtQuickStylePreset.SoftEdge => "Soft Edge",
            SmartArtQuickStylePreset.Insert => "Insert",
            SmartArtQuickStylePreset.Cartoon => "Cartoon",
            SmartArtQuickStylePreset.Powder => "Powder",
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

        if (preset == SmartArtLayoutPreset.PictureCaptionList &&
            smartArt.Data.Nodes.Any(node => node.Picture?.Bytes is not { Length: > 0 }))
        {
            return NotAppliedLayout("Picture Caption List requires image content for every SmartArt node.");
        }

        var layoutPart = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("layout", StringComparison.OrdinalIgnoreCase));
        if (layoutPart is null || layoutPart.Bytes.Length == 0)
            return NotAppliedLayout("The SmartArt graphic has no native layout definition.");

        var (layoutId, family) = preset switch
        {
            SmartArtLayoutPreset.BasicProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BasicTimeline =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicTimeline", SmartArtFamily.Process),
            SmartArtLayoutPreset.ContinuousBlockProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/continuousBlockProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.SegmentedProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/segmentedProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ChevronProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/chevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BasicChevronProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicChevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.ClosedChevronProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/closedChevronProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BendingProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/bendingProcess", SmartArtFamily.Process),
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
            SmartArtLayoutPreset.VerticalBulletList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.BasicCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.ContinuousCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/continuousCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.GearCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/gearCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.TextCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/textCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BlockCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/blockCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.NonDirectionalCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/nonDirectionalCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/list1", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicBlockList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.StackedList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/stackedList", SmartArtFamily.List),
            SmartArtLayoutPreset.DescendingBlockList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/descendingBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicPyramid =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicPyramid", SmartArtFamily.List),
            SmartArtLayoutPreset.PyramidList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pyramidList", SmartArtFamily.List),
            SmartArtLayoutPreset.RadialCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.RadialList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialList", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.TitledMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.GridMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix", SmartArtFamily.Matrix),
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
            SmartArtLayoutPreset.Hierarchy3 =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.OrgChart =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/orgChart", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.PictureCaptionList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList", SmartArtFamily.List),
            SmartArtLayoutPreset.LabeledHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.TableHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy", SmartArtFamily.Hierarchy),
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

        var monochromaticSlot = preset switch
        {
            SmartArtColorPreset.SingleAccent => accents[0],
            SmartArtColorPreset.MonochromaticAccent2 => accents[1],
            SmartArtColorPreset.MonochromaticAccent3 => accents[2],
            SmartArtColorPreset.MonochromaticAccent4 => accents[3],
            SmartArtColorPreset.MonochromaticAccent5 => accents[4],
            SmartArtColorPreset.MonochromaticAccent6 => accents[5],
            _ => (ThemeColorSlot?)null,
        };
        if (monochromaticSlot is { } slot)
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
