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

    Dark1Outline,
    Dark2Outline,
    Dark2Fill,
    ColorfulAccentColors,
    ColorfulRangeAccentColors2To3,
    ColorfulRangeAccentColors3To4,
    ColorfulRangeAccentColors4To5,
    ColorfulRangeAccentColors5To6,
    ColoredOutlineAccent1,
    ColoredFillAccent1,
    GradientRangeAccent1,
    GradientLoopAccent1,
    TransparentGradientRangeAccent1,
    ColoredOutlineAccent2,
    ColoredFillAccent2,
    GradientRangeAccent2,
    GradientLoopAccent2,
    TransparentGradientRangeAccent2,
    ColoredOutlineAccent3,
    ColoredFillAccent3,
    GradientRangeAccent3,
    GradientLoopAccent3,
    TransparentGradientRangeAccent3,
    ColoredOutlineAccent4,
    ColoredFillAccent4,
    GradientRangeAccent4,
    GradientLoopAccent4,
    TransparentGradientRangeAccent4,
    ColoredOutlineAccent5,
    ColoredFillAccent5,
    GradientRangeAccent5,
    GradientLoopAccent5,
    TransparentGradientRangeAccent5,
    ColoredOutlineAccent6,
    ColoredFillAccent6,
    GradientRangeAccent6,
    GradientLoopAccent6,
    TransparentGradientRangeAccent6,
}

/// <summary>Bounded SmartArt layout choices whose live layout engine can regenerate the cache.</summary>
public enum SmartArtLayoutPreset
{
    BasicProcess,
    AccentProcess,
    AscendingProcess,
    DescendingProcess,
    BasicTimeline,
    PhasedProcess,
    CircleAccentTimeline,
    StepDownProcess,
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
    VerticalChevronList,
    VerticalArrowList,
    VerticalBulletList,
    HorizontalBulletList,
    HorizontalBlockList,
    BasicCycle,
    Cycle2,
    ContinuousCycle,
    GearCycle,
    TextCycle,
    BlockCycle,
    NonDirectionalCycle,
    BasicList,
    List2,
    BasicBlockList,
    StackedList,
    DescendingBlockList,
    BasicPyramid,
    PyramidList,
    InvertedPyramid,
    RadialCycle,
    BasicRadial,
    RadialList,
    BasicMatrix,
    TitledMatrix,
    GridMatrix,
    BasicRelationship,
    OpposingIdeas,
    ConvergingRadial,
    BasicVenn,
    RadialVenn,
    TargetList,
    StackedVenn,
    InterlockingRings,
    BasicHierarchy,
    Hierarchy3,
    HorizontalHierarchy,
    OrgChart,
    NameAndTitleOrgChart,
    PictureCaptionList,
    PictureAccentList,
    PictureStack,
    PictureLineup,
    PictureStrips,
    ContinuousPictureList,
    LabeledHierarchy,
    TableHierarchy,
    PictureGrid,
}

/// <summary>PowerPoint SmartArt Quick Style choices exposed by the native gallery.</summary>
public enum SmartArtQuickStylePreset
{
    SimpleFill,
    WhiteOutline,
    SubtleEffect,
    ModerateEffect,
    IntenseEffect,
    Polished,
    Inset,
    Cartoon,
    Powder,
    BrickScene,
    FlatScene,
    MetallicScene,
    SunsetScene,
    BirdsEyeScene,

    // Compatibility aliases for the original FreeP command vocabulary.
    Simple = SimpleFill,
    Moderate = ModerateEffect,
    Intense = IntenseEffect,
    Subtle = SubtleEffect,
    SoftEdge = WhiteOutline,
    Insert = Inset,
}

public sealed record SmartArtColorApplyResult(
    bool Applied,
    string Message,
    string? PartPath,
    int ColorCount);

/// <summary>One PowerPoint Change Colors gallery entry with its native diagram identity.</summary>
public sealed record SmartArtColorGalleryEntry(
    SmartArtColorPreset Preset,
    string CommandId,
    string UniqueId,
    string Title,
    string Category);

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
    public const string SmartArtColorsGalleryCommandId = "freep.smartart.colors.gallery";
    public const string BasicProcessLayoutCommandId = "freep.smartart.layout.basic-process";
    public const string AccentProcessLayoutCommandId = "freep.smartart.layout.accent-process";
    public const string AscendingProcessLayoutCommandId = "freep.smartart.layout.ascending-process";
    public const string DescendingProcessLayoutCommandId = "freep.smartart.layout.descending-process";
    public const string BasicTimelineLayoutCommandId = "freep.smartart.layout.basic-timeline";
    public const string CircleAccentTimelineLayoutCommandId = "freep.smartart.layout.circle-accent-timeline";
    public const string PhasedProcessLayoutCommandId = "freep.smartart.layout.phased-process";
    public const string StepDownProcessLayoutCommandId = "freep.smartart.layout.step-down-process";
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
    public const string VerticalChevronListLayoutCommandId = "freep.smartart.layout.vertical-chevron-list";
    public const string VerticalArrowListLayoutCommandId = "freep.smartart.layout.vertical-arrow-list";
    public const string VerticalBulletListLayoutCommandId = "freep.smartart.layout.vertical-bullet-list";
    public const string HorizontalBulletListLayoutCommandId = "freep.smartart.layout.horizontal-bullet-list";
    public const string HorizontalBlockListLayoutCommandId = "freep.smartart.layout.horizontal-block-list";
    public const string BasicCycleLayoutCommandId = "freep.smartart.layout.basic-cycle";
    public const string Cycle2LayoutCommandId = "freep.smartart.layout.cycle-2";
    public const string ContinuousCycleLayoutCommandId = "freep.smartart.layout.continuous-cycle";
    public const string GearCycleLayoutCommandId = "freep.smartart.layout.gear-cycle";
    public const string TextCycleLayoutCommandId = "freep.smartart.layout.text-cycle";
    public const string BlockCycleLayoutCommandId = "freep.smartart.layout.block-cycle";
    public const string NonDirectionalCycleLayoutCommandId = "freep.smartart.layout.non-directional-cycle";
    public const string BasicListLayoutCommandId = "freep.smartart.layout.basic-list";
    public const string List2LayoutCommandId = "freep.smartart.layout.list-2";
    public const string BasicBlockListLayoutCommandId = "freep.smartart.layout.basic-block-list";
    public const string StackedListLayoutCommandId = "freep.smartart.layout.stacked-list";
    public const string DescendingBlockListLayoutCommandId = "freep.smartart.layout.descending-block-list";
    public const string BasicPyramidLayoutCommandId = "freep.smartart.layout.basic-pyramid";
    public const string PyramidListLayoutCommandId = "freep.smartart.layout.pyramid-list";
    public const string InvertedPyramidLayoutCommandId = "freep.smartart.layout.inverted-pyramid";
    public const string RadialCycleLayoutCommandId = "freep.smartart.layout.radial-cycle";
    public const string BasicRadialLayoutCommandId = "freep.smartart.layout.basic-radial";
    public const string RadialListLayoutCommandId = "freep.smartart.layout.radial-list";
    public const string BasicMatrixLayoutCommandId = "freep.smartart.layout.basic-matrix";
    public const string TitledMatrixLayoutCommandId = "freep.smartart.layout.titled-matrix";
    public const string GridMatrixLayoutCommandId = "freep.smartart.layout.grid-matrix";
    public const string BasicRelationshipLayoutCommandId = "freep.smartart.layout.basic-relationship";
    public const string OpposingIdeasLayoutCommandId = "freep.smartart.layout.opposing-ideas";
    public const string ConvergingRadialLayoutCommandId = "freep.smartart.layout.converging-radial";
    public const string BasicVennLayoutCommandId = "freep.smartart.layout.basic-venn";
    public const string RadialVennLayoutCommandId = "freep.smartart.layout.radial-venn";
    public const string TargetListLayoutCommandId = "freep.smartart.layout.target-list";
    public const string StackedVennLayoutCommandId = "freep.smartart.layout.stacked-venn";
    public const string InterlockingRingsLayoutCommandId = "freep.smartart.layout.interlocking-rings";
    public const string BasicHierarchyLayoutCommandId = "freep.smartart.layout.basic-hierarchy";
    public const string Hierarchy3LayoutCommandId = "freep.smartart.layout.hierarchy-3";
    public const string HorizontalHierarchyLayoutCommandId = "freep.smartart.layout.horizontal-hierarchy";
    public const string OrgChartLayoutCommandId = "freep.smartart.layout.org-chart";
    public const string NameAndTitleOrgChartLayoutCommandId = "freep.smartart.layout.name-and-title-org-chart";
    public const string PictureCaptionListLayoutCommandId = "freep.smartart.layout.picture-caption-list";
    public const string PictureAccentListLayoutCommandId = "freep.smartart.layout.picture-accent-list";
    public const string PictureStackLayoutCommandId = "freep.smartart.layout.picture-stack";
    public const string PictureLineupLayoutCommandId = "freep.smartart.layout.picture-lineup";
    public const string PictureStripsLayoutCommandId = "freep.smartart.layout.picture-strips";
    public const string ContinuousPictureListLayoutCommandId = "freep.smartart.layout.continuous-picture-list";
    public const string LabeledHierarchyLayoutCommandId = "freep.smartart.layout.labeled-hierarchy";
    public const string TableHierarchyLayoutCommandId = "freep.smartart.layout.table-hierarchy";
    public const string PictureGridLayoutCommandId = "freep.smartart.layout.picture-grid";
    public const string SimpleQuickStyleCommandId = "freep.smartart.style.simple";
    public const string ModerateQuickStyleCommandId = "freep.smartart.style.moderate";
    public const string IntenseQuickStyleCommandId = "freep.smartart.style.intense";
    public const string SubtleQuickStyleCommandId = "freep.smartart.style.subtle";
    public const string SoftEdgeQuickStyleCommandId = "freep.smartart.style.soft-edge";
    public const string InsertQuickStyleCommandId = "freep.smartart.style.insert";
    public const string CartoonQuickStyleCommandId = "freep.smartart.style.cartoon";
    public const string PowderQuickStyleCommandId = "freep.smartart.style.powder";
    public const string PolishedQuickStyleCommandId = "freep.smartart.style.polished";
    public const string BrickSceneQuickStyleCommandId = "freep.smartart.style.brick-scene";
    public const string FlatSceneQuickStyleCommandId = "freep.smartart.style.flat-scene";
    public const string MetallicSceneQuickStyleCommandId = "freep.smartart.style.metallic-scene";
    public const string SunsetSceneQuickStyleCommandId = "freep.smartart.style.sunset-scene";
    public const string BirdsEyeSceneQuickStyleCommandId = "freep.smartart.style.birds-eye-scene";
    public const string ConvertToShapesCommandId = "freep.smartart.convert-to-shapes";

    /// <summary>
    /// The complete PowerPoint SmartArt Change Colors catalog observed through the native COM
    /// gallery. Legacy FreeP commands remain separate compatibility routes below.
    /// </summary>
    public static IReadOnlyList<SmartArtColorGalleryEntry> ColorGallery { get; } =
    [
        Gallery(SmartArtColorPreset.Dark1Outline, "dark-1-outline", "accent0_1", "Dark 1 Outline", "mainScheme"),
        Gallery(SmartArtColorPreset.Dark2Outline, "dark-2-outline", "accent0_2", "Dark 2 Outline", "mainScheme"),
        Gallery(SmartArtColorPreset.Dark2Fill, "dark-2-fill", "accent0_3", "Dark 2 Fill", "mainScheme"),
        Gallery(SmartArtColorPreset.ColorfulAccentColors, "colorful-accent-colors", "colorful1", "Colorful - Accent Colors", "colorful"),
        Gallery(SmartArtColorPreset.ColorfulRangeAccentColors2To3, "colorful-range-accent-2-to-3", "colorful2", "Colorful Range - Accent Colors 2 to 3", "colorful"),
        Gallery(SmartArtColorPreset.ColorfulRangeAccentColors3To4, "colorful-range-accent-3-to-4", "colorful3", "Colorful Range - Accent Colors 3 to 4", "colorful"),
        Gallery(SmartArtColorPreset.ColorfulRangeAccentColors4To5, "colorful-range-accent-4-to-5", "colorful4", "Colorful Range - Accent Colors 4 to 5", "colorful"),
        Gallery(SmartArtColorPreset.ColorfulRangeAccentColors5To6, "colorful-range-accent-5-to-6", "colorful5", "Colorful Range - Accent Colors 5 to 6", "colorful"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent1, "colored-outline-accent-1", "accent1_1", "Colored Outline - Accent 1", "accent1"),
        Gallery(SmartArtColorPreset.ColoredFillAccent1, "colored-fill-accent-1", "accent1_2", "Colored Fill - Accent 1", "accent1"),
        Gallery(SmartArtColorPreset.GradientRangeAccent1, "gradient-range-accent-1", "accent1_3", "Gradient Range - Accent 1", "accent1"),
        Gallery(SmartArtColorPreset.GradientLoopAccent1, "gradient-loop-accent-1", "accent1_4", "Gradient Loop - Accent 1", "accent1"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent1, "transparent-gradient-range-accent-1", "accent1_5", "Transparent Gradient Range - Accent 1", "accent1"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent2, "colored-outline-accent-2", "accent2_1", "Colored Outline - Accent 2", "accent2"),
        Gallery(SmartArtColorPreset.ColoredFillAccent2, "colored-fill-accent-2", "accent2_2", "Colored Fill - Accent 2", "accent2"),
        Gallery(SmartArtColorPreset.GradientRangeAccent2, "gradient-range-accent-2", "accent2_3", "Gradient Range - Accent 2", "accent2"),
        Gallery(SmartArtColorPreset.GradientLoopAccent2, "gradient-loop-accent-2", "accent2_4", "Gradient Loop - Accent 2", "accent2"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent2, "transparent-gradient-range-accent-2", "accent2_5", "Transparent Gradient Range - Accent 2", "accent2"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent3, "colored-outline-accent-3", "accent3_1", "Colored Outline - Accent 3", "accent3"),
        Gallery(SmartArtColorPreset.ColoredFillAccent3, "colored-fill-accent-3", "accent3_2", "Colored Fill - Accent 3", "accent3"),
        Gallery(SmartArtColorPreset.GradientRangeAccent3, "gradient-range-accent-3", "accent3_3", "Gradient Range - Accent 3", "accent3"),
        Gallery(SmartArtColorPreset.GradientLoopAccent3, "gradient-loop-accent-3", "accent3_4", "Gradient Loop - Accent 3", "accent3"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent3, "transparent-gradient-range-accent-3", "accent3_5", "Transparent Gradient Range - Accent 3", "accent3"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent4, "colored-outline-accent-4", "accent4_1", "Colored Outline - Accent 4", "accent4"),
        Gallery(SmartArtColorPreset.ColoredFillAccent4, "colored-fill-accent-4", "accent4_2", "Colored Fill - Accent 4", "accent4"),
        Gallery(SmartArtColorPreset.GradientRangeAccent4, "gradient-range-accent-4", "accent4_3", "Gradient Range - Accent 4", "accent4"),
        Gallery(SmartArtColorPreset.GradientLoopAccent4, "gradient-loop-accent-4", "accent4_4", "Gradient Loop - Accent 4", "accent4"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent4, "transparent-gradient-range-accent-4", "accent4_5", "Transparent Gradient Range - Accent 4", "accent4"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent5, "colored-outline-accent-5", "accent5_1", "Colored Outline - Accent 5", "accent5"),
        Gallery(SmartArtColorPreset.ColoredFillAccent5, "colored-fill-accent-5", "accent5_2", "Colored Fill - Accent 5", "accent5"),
        Gallery(SmartArtColorPreset.GradientRangeAccent5, "gradient-range-accent-5", "accent5_3", "Gradient Range - Accent 5", "accent5"),
        Gallery(SmartArtColorPreset.GradientLoopAccent5, "gradient-loop-accent-5", "accent5_4", "Gradient Loop - Accent 5", "accent5"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent5, "transparent-gradient-range-accent-5", "accent5_5", "Transparent Gradient Range - Accent 5", "accent5"),
        Gallery(SmartArtColorPreset.ColoredOutlineAccent6, "colored-outline-accent-6", "accent6_1", "Colored Outline - Accent 6", "accent6"),
        Gallery(SmartArtColorPreset.ColoredFillAccent6, "colored-fill-accent-6", "accent6_2", "Colored Fill - Accent 6", "accent6"),
        Gallery(SmartArtColorPreset.GradientRangeAccent6, "gradient-range-accent-6", "accent6_3", "Gradient Range - Accent 6", "accent6"),
        Gallery(SmartArtColorPreset.GradientLoopAccent6, "gradient-loop-accent-6", "accent6_4", "Gradient Loop - Accent 6", "accent6"),
        Gallery(SmartArtColorPreset.TransparentGradientRangeAccent6, "transparent-gradient-range-accent-6", "accent6_5", "Transparent Gradient Range - Accent 6", "accent6"),
    ];

    private static SmartArtColorGalleryEntry Gallery(
        SmartArtColorPreset preset,
        string commandSlug,
        string nativeSlug,
        string title,
        string category) =>
        new(preset,
            $"freep.smartart.colors.{commandSlug}",
            $"urn:microsoft.com/office/officeart/2005/8/colors/{nativeSlug}",
            title,
            category);

    public static SmartArtQuickStyleApplyResult ApplyQuickStylePreset(
        SmartArtShape? smartArt,
        SmartArtQuickStylePreset preset)
    {
        if (smartArt is null)
            return NotAppliedQuickStyle("No SmartArt graphic is available.");

        var styleId = preset switch
        {
            SmartArtQuickStylePreset.SimpleFill =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1",
            SmartArtQuickStylePreset.WhiteOutline =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple2",
            SmartArtQuickStylePreset.SubtleEffect =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple3",
            SmartArtQuickStylePreset.ModerateEffect =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple4",
            SmartArtQuickStylePreset.IntenseEffect =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple5",
            SmartArtQuickStylePreset.Polished =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d1",
            SmartArtQuickStylePreset.Inset =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d2",
            SmartArtQuickStylePreset.Cartoon =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d3",
            SmartArtQuickStylePreset.Powder =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d4",
            SmartArtQuickStylePreset.BrickScene =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d5",
            SmartArtQuickStylePreset.FlatScene =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d6",
            SmartArtQuickStylePreset.MetallicScene =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d7",
            SmartArtQuickStylePreset.SunsetScene =>
                "urn:microsoft.com/office/officeart/2009/2/quickstyle/3d8",
            SmartArtQuickStylePreset.BirdsEyeScene =>
                "urn:microsoft.com/office/officeart/2005/8/quickstyle/3d9",
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
            SmartArtQuickStylePreset.SimpleFill => "Simple Fill",
            SmartArtQuickStylePreset.WhiteOutline => "White Outline",
            SmartArtQuickStylePreset.SubtleEffect => "Subtle Effect",
            SmartArtQuickStylePreset.ModerateEffect => "Moderate Effect",
            SmartArtQuickStylePreset.IntenseEffect => "Intense Effect",
            SmartArtQuickStylePreset.Polished => "Polished",
            SmartArtQuickStylePreset.Inset => "Inset",
            SmartArtQuickStylePreset.Cartoon => "Cartoon",
            SmartArtQuickStylePreset.Powder => "Powder",
            SmartArtQuickStylePreset.BrickScene => "Brick Scene",
            SmartArtQuickStylePreset.FlatScene => "Flat Scene",
            SmartArtQuickStylePreset.MetallicScene => "Metallic Scene",
            SmartArtQuickStylePreset.SunsetScene => "Sunset Scene",
            SmartArtQuickStylePreset.BirdsEyeScene => "Bird's Eye Scene",
            _ => preset.ToString(),
        };
        var titleElement = styleDefinition.Elements(Diagram + "title").FirstOrDefault();
        if (titleElement is null)
            styleDefinition.AddFirst(new XElement(Diagram + "title", new XAttribute("val", title)));
        else
            titleElement.SetAttributeValue("val", title);

        part.Bytes = Serialize(document);
        EnsureDiagramRelationship(smartArt, "qs", "rIdFreePQuickStyle");
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
        if (smartArt is null)
            return NotAppliedLayout("No SmartArt graphic is available.");

        var pictureLayout = preset is (
            SmartArtLayoutPreset.PictureCaptionList or
            SmartArtLayoutPreset.PictureAccentList or
            SmartArtLayoutPreset.PictureStack or
            SmartArtLayoutPreset.PictureLineup or
            SmartArtLayoutPreset.PictureStrips or
            SmartArtLayoutPreset.ContinuousPictureList or
            SmartArtLayoutPreset.PictureGrid);
        if (pictureLayout && (smartArt.Data is null || smartArt.Data.Nodes.Count == 0))
        {
            return NotAppliedLayout("Picture-based SmartArt layouts require a SmartArt data model with at least one node.");
        }

        var layoutPart = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("layout", StringComparison.OrdinalIgnoreCase));
        if (layoutPart is null)
        {
            var dataPart = smartArt.Parts.Values.FirstOrDefault(candidate =>
                candidate.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase) ||
                candidate.PartPath.Contains("data", StringComparison.OrdinalIgnoreCase));
            if (dataPart is null || dataPart.Bytes.Length == 0)
                return NotAppliedLayout("The SmartArt graphic has no native diagram data part from which to create a layout definition.");

            layoutPart = CreateNativeLayoutPart(smartArt, dataPart.PartPath);
            smartArt.DiagramRelIds["lo"] = "rIdFreePLayout";
        }

        if (layoutPart.Bytes.Length == 0)
            return NotAppliedLayout("The native SmartArt layout part is empty.");

        var (layoutId, family) = preset switch
        {
            SmartArtLayoutPreset.BasicProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.AccentProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/accentProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.AscendingProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/ascendingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.DescendingProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/descendingProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.BasicTimeline =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicTimeline", SmartArtFamily.Process),
            SmartArtLayoutPreset.PhasedProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/phasedProcess", SmartArtFamily.Process),
            SmartArtLayoutPreset.CircleAccentTimeline =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/circleAccentTimeline", SmartArtFamily.Process),
            SmartArtLayoutPreset.StepDownProcess =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/StepDownProcess", SmartArtFamily.Process),
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
            SmartArtLayoutPreset.VerticalChevronList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalChevronList", SmartArtFamily.List),
            SmartArtLayoutPreset.VerticalArrowList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalArrowList", SmartArtFamily.List),
            SmartArtLayoutPreset.VerticalBulletList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/verticalBulletList", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalBulletList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalBulletList", SmartArtFamily.List),
            SmartArtLayoutPreset.HorizontalBlockList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalBlockList", SmartArtFamily.List),
            SmartArtLayoutPreset.BasicCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.Cycle2 =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/cycle2", SmartArtFamily.Cycle),
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
            SmartArtLayoutPreset.List2 =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/list2", SmartArtFamily.List),
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
            SmartArtLayoutPreset.InvertedPyramid =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/invertedPyramid", SmartArtFamily.List),
            SmartArtLayoutPreset.RadialCycle =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialCycle", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicRadial =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radial1", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.RadialList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialList", SmartArtFamily.Cycle),
            SmartArtLayoutPreset.BasicMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.TitledMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/titledMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.GridMatrix =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix", SmartArtFamily.Matrix),
            SmartArtLayoutPreset.BasicRelationship =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/relationship1", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.OpposingIdeas =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/opposingIdeas", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.ConvergingRadial =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/convergingRadial", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.BasicVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.RadialVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/radialVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.TargetList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/targetList", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.StackedVenn =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/stackedVenn", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.InterlockingRings =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/interlockingRings", SmartArtFamily.Relationship),
            SmartArtLayoutPreset.BasicHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/basicHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.Hierarchy3 =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.HorizontalHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/horizontalHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.OrgChart =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/orgChart", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.NameAndTitleOrgChart =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/nameAndTitleOrgChart", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.PictureCaptionList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureAccentList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureAccentList", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureStack =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureStack", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureLineup =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureLineup", SmartArtFamily.List),
            SmartArtLayoutPreset.PictureStrips =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureStrips", SmartArtFamily.List),
            SmartArtLayoutPreset.ContinuousPictureList =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/continuousPictureList", SmartArtFamily.List),
            SmartArtLayoutPreset.LabeledHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/labeledHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.TableHierarchy =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/tableHierarchy", SmartArtFamily.Hierarchy),
            SmartArtLayoutPreset.PictureGrid =>
                ("urn:microsoft.com/office/officeart/2005/8/layout/pictureGrid", SmartArtFamily.List),
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
        EnsureDiagramRelationship(smartArt, "lo", "rIdFreePLayout");
        if (smartArt.Data is { } data)
        {
            data.LayoutUniqueId = layoutId;
            data.Family = family;
            data.IsLiveLayoutSupported = true;
            smartArt.FallbackShapes.Clear();
        }

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

        var gallery = ResolveGalleryEntry(preset);
        var appliedColors = BuildColors(gallery, firstPalette.Count, theme, effectiveClrMap);
        var root = document.Root!;
        root.SetAttributeValue("uniqueId", gallery.UniqueId);
        var titleElement = root.Descendants(Diagram + "title").FirstOrDefault();
        if (titleElement is null)
            root.AddFirst(new XElement(Diagram + "title", new XAttribute("val", gallery.Title)));
        else
            titleElement.SetAttributeValue("val", gallery.Title);
        var categoryElement = root.Descendants(Diagram + "cat").FirstOrDefault();
        if (categoryElement is null)
            root.Add(new XElement(Diagram + "cat", new XAttribute("type", gallery.Category)));
        else
            categoryElement.SetAttributeValue("type", gallery.Category);
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
        EnsureDiagramRelationship(smartArt, "cs", "rIdFreePColors");
        smartArt.Colors ??= new SmartArtColorMetadata();
        smartArt.Colors.UniqueId = gallery.UniqueId;
        smartArt.Colors.Title = gallery.Title;
        smartArt.Colors.Category = gallery.Category;
        smartArt.Colors.Palette.Clear();
        smartArt.Colors.Palette.AddRange(appliedColors.Select(color => color.ModelColor));

        return new SmartArtColorApplyResult(
            true,
            $"SmartArt colors changed to {gallery.Title}.",
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

    private static void EnsureDiagramRelationship(SmartArtShape smartArt, string key, string fallbackRelId)
    {
        if (!smartArt.DiagramRelIds.ContainsKey(key))
            smartArt.DiagramRelIds[key] = fallbackRelId;
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

    private static DiagramPart CreateNativeLayoutPart(SmartArtShape smartArt, string dataPartPath)
    {
        var directory = dataPartPath[..(dataPartPath.LastIndexOf('/') + 1)];
        var dataFileName = dataPartPath[(dataPartPath.LastIndexOf('/') + 1)..];
        var layoutFileName = dataFileName.StartsWith("data", StringComparison.OrdinalIgnoreCase)
            ? "layout" + dataFileName[4..]
            : "layout-freep.xml";
        var partPath = directory + layoutFileName;
        var suffix = 2;
        while (smartArt.Parts.ContainsKey(partPath))
            partPath = directory + $"layout-freep-{suffix++}.xml";

        var part = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            PartPath = partPath,
            Bytes = Serialize(CreateNativeLayoutDefinition()),
        };
        smartArt.Parts[part.PartPath] = part;
        return part;
    }

    private static XDocument CreateNativeLayoutDefinition() =>
        new(new XElement(
            Diagram + "layoutDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
            new XAttribute("uniqueId", "urn:freep:smartart:layout:pending"),
            new XElement(Diagram + "title", new XAttribute("val", "")),
            new XElement(Diagram + "desc", new XAttribute("val", "")),
            new XElement(Diagram + "catLst",
                new XElement(Diagram + "cat", new XAttribute("type", "list"), new XAttribute("pri", "1000"))),
            new XElement(Diagram + "sampData",
                new XElement(Diagram + "dataModel",
                    new XElement(Diagram + "ptLst"),
                    new XElement(Diagram + "bg"),
                    new XElement(Diagram + "whole"))),
            new XElement(Diagram + "styleData",
                new XElement(Diagram + "dataModel",
                    new XElement(Diagram + "ptLst"),
                    new XElement(Diagram + "bg"),
                    new XElement(Diagram + "whole"))),
            new XElement(Diagram + "clrData",
                new XElement(Diagram + "dataModel",
                    new XElement(Diagram + "ptLst"),
                    new XElement(Diagram + "bg"),
                    new XElement(Diagram + "whole"))),
            new XElement(Diagram + "layoutNode",
                new XAttribute("name", "root"),
                new XElement(Diagram + "alg", new XAttribute("type", "lin")),
                new XElement(Diagram + "shape", new XElement(Diagram + "adjLst")),
                new XElement(Diagram + "presOf"),
                new XElement(Diagram + "constrLst"),
                new XElement(Diagram + "ruleLst"))));

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

    private static SmartArtColorGalleryEntry ResolveGalleryEntry(SmartArtColorPreset preset) =>
        preset switch
        {
            SmartArtColorPreset.ThemeAccents => FindGallery(SmartArtColorPreset.ColorfulAccentColors),
            SmartArtColorPreset.SingleAccent => FindGallery(SmartArtColorPreset.ColoredFillAccent1),
            SmartArtColorPreset.MonochromaticAccent2 => FindGallery(SmartArtColorPreset.ColoredFillAccent2),
            SmartArtColorPreset.MonochromaticAccent3 => FindGallery(SmartArtColorPreset.ColoredFillAccent3),
            SmartArtColorPreset.MonochromaticAccent4 => FindGallery(SmartArtColorPreset.ColoredFillAccent4),
            SmartArtColorPreset.MonochromaticAccent5 => FindGallery(SmartArtColorPreset.ColoredFillAccent5),
            SmartArtColorPreset.MonochromaticAccent6 => FindGallery(SmartArtColorPreset.ColoredFillAccent6),
            SmartArtColorPreset.Grayscale => FindGallery(SmartArtColorPreset.Dark1Outline),
            _ => FindGallery(preset),
        };

    private static SmartArtColorGalleryEntry FindGallery(SmartArtColorPreset preset) =>
        ColorGallery.First(entry => entry.Preset == preset);

    private static IReadOnlyList<PaletteColor> BuildColors(
        SmartArtColorGalleryEntry gallery,
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

        if (gallery.Category == "mainScheme")
        {
            var grays = gallery.Preset == SmartArtColorPreset.Dark2Fill
                ? new[] { 0x262626, 0x404040, 0x595959, 0x737373, 0x8C8C8C, 0xA6A6A6 }
                : new[] { 0x404040, 0x666666, 0x808080, 0x999999, 0xB3B3B3, 0xD9D9D9 };
            return Enumerable.Range(0, count)
                .Select(index =>
                {
                    var resolved = SrgbColor.FromRgb(grays[index % grays.Length]);
                    return new PaletteColor(resolved, null, new ThemeAwareColor(resolved));
                })
                .ToArray();
        }

        var categorySlot = gallery.Category.StartsWith("accent", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(gallery.Category[6..], System.Globalization.CultureInfo.InvariantCulture) - 1
            : -1;
        if (categorySlot >= 0)
        {
            var style = gallery.UniqueId[^1];
            return Enumerable.Range(0, count)
                .Select(index =>
                {
                    var offset = style switch
                    {
                        '3' => index % 2,
                        '4' => (index * 2) % 3,
                        '5' => index % 3,
                        _ => 0,
                    };
                    return CreateSchemeColor(accents[(categorySlot + offset) % accents.Length], theme, effectiveClrMap);
                })
                .ToArray();
        }

        var colorfulOffset = gallery.UniqueId switch
        {
            var id when id.EndsWith("colorful2", StringComparison.OrdinalIgnoreCase) => 1,
            var id when id.EndsWith("colorful3", StringComparison.OrdinalIgnoreCase) => 2,
            var id when id.EndsWith("colorful4", StringComparison.OrdinalIgnoreCase) => 3,
            var id when id.EndsWith("colorful5", StringComparison.OrdinalIgnoreCase) => 4,
            _ => 0,
        };
        return Enumerable.Range(0, count)
            .Select(index => CreateSchemeColor(accents[(index + colorfulOffset) % accents.Length], theme, effectiveClrMap))
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
