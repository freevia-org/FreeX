using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Which ends of a line-type gallery shape carry an arrowhead.</summary>
public enum PresetShapeLineEnds
{
    None,
    Tail,
    Both,
}

/// <summary>One Insert &gt; Shapes gallery entry: a DrawingML preset plus its localized name.</summary>
public sealed record PresetShapeCatalogItem(
    string Id,
    string Preset,
    PresetShapeLineEnds LineEnds,
    ResourceTextDescriptor Name)
{
    public bool IsLine => PresetShapeGeometry.IsLinePreset(Preset);
}

/// <summary>A Word-style gallery group (Lines, Rectangles, Basic Shapes, ...).</summary>
public sealed record PresetShapeCatalogGroup(
    string Id,
    ResourceTextDescriptor Title,
    IReadOnlyList<PresetShapeCatalogItem> Items);

/// <summary>
/// The Insert &gt; Shapes gallery, grouped the way Word groups it. Shared by both shells: the hosts only
/// draw the thumbnails (from <see cref="BuildThumbnail"/>) and hand the chosen item id back.
/// </summary>
public static class PresetShapeCatalog
{
    public const double ThumbnailSizePt = 24;

    private static readonly ResourceTextDescriptor DialogTitleText = new("ShapeGallery_Dialog_Title", "Shapes");

    public static IReadOnlyList<PresetShapeCatalogGroup> Groups { get; } =
    [
        new("Lines", new("ShapeGallery_Group_Lines", "Lines"),
        [
            new("Line", "line", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Line", "Line")),
            new("LineArrow", "straightConnector1", PresetShapeLineEnds.Tail, new("ShapeGallery_Shape_LineArrow", "Line Arrow")),
            new("LineDoubleArrow", "straightConnector1", PresetShapeLineEnds.Both, new("ShapeGallery_Shape_LineDoubleArrow", "Line Arrow: Double")),
            new("ElbowConnector", "bentConnector3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_ElbowConnector", "Connector: Elbow")),
            new("ElbowArrow", "bentConnector3", PresetShapeLineEnds.Tail, new("ShapeGallery_Shape_ElbowArrow", "Connector: Elbow Arrow")),
            new("ElbowDoubleArrow", "bentConnector3", PresetShapeLineEnds.Both, new("ShapeGallery_Shape_ElbowDoubleArrow", "Connector: Elbow Double-Arrow")),
            new("CurvedConnector", "curvedConnector3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CurvedConnector", "Connector: Curved")),
            new("CurvedArrow", "curvedConnector3", PresetShapeLineEnds.Tail, new("ShapeGallery_Shape_CurvedArrow", "Connector: Curved Arrow")),
            new("CurvedDoubleArrow", "curvedConnector3", PresetShapeLineEnds.Both, new("ShapeGallery_Shape_CurvedDoubleArrow", "Connector: Curved Double-Arrow")),
        ]),
        new("Rectangles", new("ShapeGallery_Group_Rectangles", "Rectangles"),
        [
            new("Rectangle", "rect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Rectangle", "Rectangle")),
            new("RoundedRectangle", "roundRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RoundedRectangle", "Rectangle: Rounded Corners")),
            new("Snip1Rect", "snip1Rect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Snip1Rect", "Rectangle: Single Corner Snipped")),
            new("Snip2SameRect", "snip2SameRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Snip2SameRect", "Rectangle: Top Corners Snipped")),
            new("Snip2DiagRect", "snip2DiagRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Snip2DiagRect", "Rectangle: Diagonal Corners Snipped")),
            new("SnipRoundRect", "snipRoundRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_SnipRoundRect", "Rectangle: Top Corners One Rounded and One Snipped")),
            new("Round1Rect", "round1Rect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Round1Rect", "Rectangle: Single Corner Rounded")),
            new("Round2SameRect", "round2SameRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Round2SameRect", "Rectangle: Top Corners Rounded")),
            new("Round2DiagRect", "round2DiagRect", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Round2DiagRect", "Rectangle: Diagonal Corners Rounded")),
        ]),
        new("BasicShapes", new("ShapeGallery_Group_BasicShapes", "Basic Shapes"),
        [
            new("Oval", "ellipse", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Oval", "Oval")),
            new("Triangle", "triangle", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Triangle", "Isosceles Triangle")),
            new("RightTriangle", "rtTriangle", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RightTriangle", "Right Triangle")),
            new("Parallelogram", "parallelogram", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Parallelogram", "Parallelogram")),
            new("Trapezoid", "trapezoid", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Trapezoid", "Trapezoid")),
            new("Diamond", "diamond", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Diamond", "Diamond")),
            new("Pentagon", "pentagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Pentagon", "Pentagon")),
            new("Hexagon", "hexagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Hexagon", "Hexagon")),
            new("Heptagon", "heptagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Heptagon", "Heptagon")),
            new("Octagon", "octagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Octagon", "Octagon")),
            new("Decagon", "decagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Decagon", "Decagon")),
            new("Dodecagon", "dodecagon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Dodecagon", "Dodecagon")),
            new("Pie", "pie", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Pie", "Partial Circle")),
            new("Chord", "chord", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Chord", "Chord")),
            new("Teardrop", "teardrop", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Teardrop", "Teardrop")),
            new("Frame", "frame", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Frame", "Frame")),
            new("HalfFrame", "halfFrame", PresetShapeLineEnds.None, new("ShapeGallery_Shape_HalfFrame", "Half Frame")),
            new("LShape", "corner", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LShape", "L-Shape")),
            new("DiagonalStripe", "diagStripe", PresetShapeLineEnds.None, new("ShapeGallery_Shape_DiagonalStripe", "Diagonal Stripe")),
            new("Cross", "plus", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Cross", "Cross")),
            new("Plaque", "plaque", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Plaque", "Plaque")),
            new("Cylinder", "can", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Cylinder", "Cylinder")),
            new("Cube", "cube", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Cube", "Cube")),
            new("Bevel", "bevel", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Bevel", "Rectangle: Beveled")),
            new("Donut", "donut", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Donut", "Circle: Hollow")),
            new("BlockArc", "blockArc", PresetShapeLineEnds.None, new("ShapeGallery_Shape_BlockArc", "Block Arc")),
            new("FoldedCorner", "foldedCorner", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FoldedCorner", "Rectangle: Folded Corner")),
            new("SmileyFace", "smileyFace", PresetShapeLineEnds.None, new("ShapeGallery_Shape_SmileyFace", "Smiley Face")),
            new("Heart", "heart", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Heart", "Heart")),
            new("LightningBolt", "lightningBolt", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LightningBolt", "Lightning Bolt")),
            new("Sun", "sun", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Sun", "Sun")),
            new("Moon", "moon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Moon", "Moon")),
            new("Cloud", "cloud", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Cloud", "Cloud")),
            new("Arc", "arc", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Arc", "Arc")),
            new("DoubleBracket", "bracketPair", PresetShapeLineEnds.None, new("ShapeGallery_Shape_DoubleBracket", "Double Bracket")),
            new("DoubleBrace", "bracePair", PresetShapeLineEnds.None, new("ShapeGallery_Shape_DoubleBrace", "Double Brace")),
            new("LeftBracket", "leftBracket", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftBracket", "Left Bracket")),
            new("RightBracket", "rightBracket", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RightBracket", "Right Bracket")),
            new("LeftBrace", "leftBrace", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftBrace", "Left Brace")),
            new("RightBrace", "rightBrace", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RightBrace", "Right Brace")),
        ]),
        new("BlockArrows", new("ShapeGallery_Group_BlockArrows", "Block Arrows"),
        [
            new("RightArrow", "rightArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RightArrow", "Arrow: Right")),
            new("LeftArrow", "leftArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftArrow", "Arrow: Left")),
            new("UpArrow", "upArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_UpArrow", "Arrow: Up")),
            new("DownArrow", "downArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_DownArrow", "Arrow: Down")),
            new("LeftRightArrow", "leftRightArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftRightArrow", "Arrow: Left-Right")),
            new("UpDownArrow", "upDownArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_UpDownArrow", "Arrow: Up-Down")),
            new("QuadArrow", "quadArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_QuadArrow", "Arrow: Quad")),
            new("LeftRightUpArrow", "leftRightUpArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftRightUpArrow", "Arrow: Left-Right-Up")),
            new("BentArrow", "bentArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_BentArrow", "Arrow: Bent")),
            new("UTurnArrow", "uturnArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_UTurnArrow", "Arrow: U-Turn")),
            new("LeftUpArrow", "leftUpArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftUpArrow", "Arrow: Left-Up")),
            new("BentUpArrow", "bentUpArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_BentUpArrow", "Arrow: Bent-Up")),
            new("StripedRightArrow", "stripedRightArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_StripedRightArrow", "Arrow: Striped Right")),
            new("NotchedRightArrow", "notchedRightArrow", PresetShapeLineEnds.None, new("ShapeGallery_Shape_NotchedRightArrow", "Arrow: Notched Right")),
            new("PentagonArrow", "homePlate", PresetShapeLineEnds.None, new("ShapeGallery_Shape_PentagonArrow", "Arrow: Pentagon")),
            new("Chevron", "chevron", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Chevron", "Arrow: Chevron")),
            new("RightArrowCallout", "rightArrowCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RightArrowCallout", "Callout: Right Arrow")),
            new("DownArrowCallout", "downArrowCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_DownArrowCallout", "Callout: Down Arrow")),
            new("LeftArrowCallout", "leftArrowCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_LeftArrowCallout", "Callout: Left Arrow")),
            new("UpArrowCallout", "upArrowCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_UpArrowCallout", "Callout: Up Arrow")),
        ]),
        new("EquationShapes", new("ShapeGallery_Group_EquationShapes", "Equation Shapes"),
        [
            new("MathPlus", "mathPlus", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathPlus", "Plus Sign")),
            new("MathMinus", "mathMinus", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathMinus", "Minus Sign")),
            new("MathMultiply", "mathMultiply", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathMultiply", "Multiplication Sign")),
            new("MathDivide", "mathDivide", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathDivide", "Division Sign")),
            new("MathEqual", "mathEqual", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathEqual", "Equals")),
            new("MathNotEqual", "mathNotEqual", PresetShapeLineEnds.None, new("ShapeGallery_Shape_MathNotEqual", "Not Equal")),
        ]),
        new("Flowchart", new("ShapeGallery_Group_Flowchart", "Flowchart"),
        [
            new("FlowProcess", "flowChartProcess", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowProcess", "Flowchart: Process")),
            new("FlowAlternateProcess", "flowChartAlternateProcess", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowAlternateProcess", "Flowchart: Alternate Process")),
            new("FlowDecision", "flowChartDecision", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowDecision", "Flowchart: Decision")),
            new("FlowData", "flowChartInputOutput", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowData", "Flowchart: Data")),
            new("FlowPredefinedProcess", "flowChartPredefinedProcess", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowPredefinedProcess", "Flowchart: Predefined Process")),
            new("FlowInternalStorage", "flowChartInternalStorage", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowInternalStorage", "Flowchart: Internal Storage")),
            new("FlowDocument", "flowChartDocument", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowDocument", "Flowchart: Document")),
            new("FlowMultidocument", "flowChartMultidocument", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowMultidocument", "Flowchart: Multidocument")),
            new("FlowTerminator", "flowChartTerminator", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowTerminator", "Flowchart: Terminator")),
            new("FlowPreparation", "flowChartPreparation", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowPreparation", "Flowchart: Preparation")),
            new("FlowManualInput", "flowChartManualInput", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowManualInput", "Flowchart: Manual Input")),
            new("FlowManualOperation", "flowChartManualOperation", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowManualOperation", "Flowchart: Manual Operation")),
            new("FlowConnector", "flowChartConnector", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowConnector", "Flowchart: Connector")),
            new("FlowOffpageConnector", "flowChartOffpageConnector", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowOffpageConnector", "Flowchart: Off-page Connector")),
            new("FlowCard", "flowChartPunchedCard", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowCard", "Flowchart: Card")),
            new("FlowPunchedTape", "flowChartPunchedTape", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowPunchedTape", "Flowchart: Punched Tape")),
            new("FlowSummingJunction", "flowChartSummingJunction", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowSummingJunction", "Flowchart: Summing Junction")),
            new("FlowOr", "flowChartOr", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowOr", "Flowchart: Or")),
            new("FlowCollate", "flowChartCollate", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowCollate", "Flowchart: Collate")),
            new("FlowSort", "flowChartSort", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowSort", "Flowchart: Sort")),
            new("FlowExtract", "flowChartExtract", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowExtract", "Flowchart: Extract")),
            new("FlowMerge", "flowChartMerge", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowMerge", "Flowchart: Merge")),
            new("FlowStoredData", "flowChartOnlineStorage", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowStoredData", "Flowchart: Stored Data")),
            new("FlowDelay", "flowChartDelay", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowDelay", "Flowchart: Delay")),
            new("FlowSequentialAccess", "flowChartMagneticTape", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowSequentialAccess", "Flowchart: Sequential Access Storage")),
            new("FlowMagneticDisk", "flowChartMagneticDisk", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowMagneticDisk", "Flowchart: Magnetic Disk")),
            new("FlowDirectAccess", "flowChartMagneticDrum", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowDirectAccess", "Flowchart: Direct Access Storage")),
            new("FlowDisplay", "flowChartDisplay", PresetShapeLineEnds.None, new("ShapeGallery_Shape_FlowDisplay", "Flowchart: Display")),
        ]),
        new("StarsAndBanners", new("ShapeGallery_Group_StarsAndBanners", "Stars and Banners"),
        [
            new("Explosion8", "irregularSeal1", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Explosion8", "Explosion: 8 Points")),
            new("Explosion14", "irregularSeal2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Explosion14", "Explosion: 14 Points")),
            new("Star4", "star4", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star4", "Star: 4 Points")),
            new("Star5", "star5", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star5", "Star: 5 Points")),
            new("Star6", "star6", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star6", "Star: 6 Points")),
            new("Star7", "star7", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star7", "Star: 7 Points")),
            new("Star8", "star8", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star8", "Star: 8 Points")),
            new("Star10", "star10", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star10", "Star: 10 Points")),
            new("Star12", "star12", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star12", "Star: 12 Points")),
            new("Star16", "star16", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star16", "Star: 16 Points")),
            new("Star24", "star24", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star24", "Star: 24 Points")),
            new("Star32", "star32", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Star32", "Star: 32 Points")),
            new("RibbonUp", "ribbon2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RibbonUp", "Ribbon: Tilted Up")),
            new("RibbonDown", "ribbon", PresetShapeLineEnds.None, new("ShapeGallery_Shape_RibbonDown", "Ribbon: Tilted Down")),
            new("Wave", "wave", PresetShapeLineEnds.None, new("ShapeGallery_Shape_Wave", "Wave")),
        ]),
        new("Callouts", new("ShapeGallery_Group_Callouts", "Callouts"),
        [
            new("SpeechRectangle", "wedgeRectCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_SpeechRectangle", "Speech Bubble: Rectangle")),
            new("SpeechRoundedRectangle", "wedgeRoundRectCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_SpeechRoundedRectangle", "Speech Bubble: Rectangle with Corners Rounded")),
            new("SpeechOval", "wedgeEllipseCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_SpeechOval", "Speech Bubble: Oval")),
            new("ThoughtCloud", "cloudCallout", PresetShapeLineEnds.None, new("ShapeGallery_Shape_ThoughtCloud", "Thought Bubble: Cloud")),
            new("CalloutLine", "borderCallout1", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutLine", "Callout: Line")),
            new("CalloutBentLine", "borderCallout2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutBentLine", "Callout: Bent Line")),
            new("CalloutDoubleBentLine", "borderCallout3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutDoubleBentLine", "Callout: Double Bent Line")),
            new("CalloutAccentLine", "accentCallout1", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutAccentLine", "Callout: Line with Accent Bar")),
            new("CalloutAccentBentLine", "accentCallout2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutAccentBentLine", "Callout: Bent Line with Accent Bar")),
            new("CalloutAccentDoubleBentLine", "accentCallout3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutAccentDoubleBentLine", "Callout: Double Bent Line with Accent Bar")),
            new("CalloutNoBorderLine", "callout1", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutNoBorderLine", "Callout: Line with No Border")),
            new("CalloutNoBorderBentLine", "callout2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutNoBorderBentLine", "Callout: Bent Line with No Border")),
            new("CalloutNoBorderDoubleBentLine", "callout3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutNoBorderDoubleBentLine", "Callout: Double Bent Line with No Border")),
            new("CalloutBorderAccentLine", "accentBorderCallout1", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutBorderAccentLine", "Callout: Line with Border and Accent Bar")),
            new("CalloutBorderAccentBentLine", "accentBorderCallout2", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutBorderAccentBentLine", "Callout: Bent Line with Border and Accent Bar")),
            new("CalloutBorderAccentDoubleBentLine", "accentBorderCallout3", PresetShapeLineEnds.None, new("ShapeGallery_Shape_CalloutBorderAccentDoubleBentLine", "Callout: Double Bent Line with Border and Accent Bar")),
        ]),
    ];

    private static readonly Dictionary<string, PresetShapeCatalogItem> ItemsById =
        Groups.SelectMany(group => group.Items).ToDictionary(item => item.Id, StringComparer.Ordinal);

    public static IEnumerable<PresetShapeCatalogItem> Items => Groups.SelectMany(group => group.Items);

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
        Groups.Select(group => group.Title.ResourceKey)
            .Concat(Groups.SelectMany(group => group.Items).Select(item => item.Name.ResourceKey))
            .Append(DialogTitleText.ResourceKey)
            .ToArray();

    public static string DialogTitle(Func<string, string?>? getText = null) => DialogTitleText.Resolve(getText);

    public static bool TryGetItem(string? id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PresetShapeCatalogItem? item)
    {
        item = null;
        return id is not null && ItemsById.TryGetValue(id, out item);
    }

    /// <summary>
    /// Creates the shape a gallery click inserts: Word-like default size, the app's default shape fill
    /// for closed shapes, and a visible stroke (with arrowheads where the entry has them) for lines.
    /// </summary>
    public static Shape CreateShape(PresetShapeCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsLine)
        {
            var line = new Shape(ShapeKind.Rectangle, widthPt: 120, heightPt: 60)
            {
                ExtendedFill = ShapeFill.NoFill(),
                OutlineColorHex = "#4472C4",
                OutlineWidthPt = 1,
                TailEnd = item.LineEnds is PresetShapeLineEnds.Tail or PresetShapeLineEnds.Both ? new ShapeLineEnd("triangle") : null,
                HeadEnd = item.LineEnds is PresetShapeLineEnds.Both ? new ShapeLineEnd("triangle") : null,
            };
            PresetShapeFactory.ApplyPreset(line, item.Preset);
            return line;
        }

        var (width, height) = DefaultSize(item.Preset);
        var shape = new Shape(ShapeKind.Rectangle, width, height, fillColorHex: "#DCE6F1")
        {
            OutlineColorHex = "#4472C4",
            OutlineWidthPt = 1,
        };
        PresetShapeFactory.ApplyPreset(shape, item.Preset);
        return shape;
    }

    /// <summary>The gallery thumbnail outline for an entry, in a square box of <see cref="ThumbnailSizePt"/>.</summary>
    public static CustomGeometry? BuildThumbnail(PresetShapeCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var (width, height) = item.IsLine ? (ThumbnailSizePt, ThumbnailSizePt * 0.7) : ThumbnailBox(item.Preset);
        return item.Preset switch
        {
            "rect" => CustomGeometry.RectanglePoly(),
            "roundRect" => PresetShapeGeometry.Build("flowChartAlternateProcess", width, height),
            "ellipse" => PresetShapeGeometry.Build("flowChartConnector", width, height),
            _ => PresetShapeGeometry.Build(
                item.Preset,
                width,
                height,
                headEnd: item.LineEnds is PresetShapeLineEnds.Both ? new ShapeLineEnd("triangle") : null,
                tailEnd: item.LineEnds is PresetShapeLineEnds.None ? null : new ShapeLineEnd("triangle"),
                outlineWidthPt: 1),
        };
    }

    /// <summary>Width/height (points) of the box a thumbnail is drawn in; wide shapes get a 4:3 box.</summary>
    public static (double Width, double Height) ThumbnailBox(string preset) =>
        IsSquare(preset) ? (ThumbnailSizePt, ThumbnailSizePt) : (ThumbnailSizePt, ThumbnailSizePt * 0.75);

    private static (double Width, double Height) DefaultSize(string preset) =>
        IsSquare(preset) ? (90, 90) : (120, 80);

    private static bool IsSquare(string preset) =>
        preset.StartsWith("star", StringComparison.Ordinal)
        || preset.StartsWith("math", StringComparison.Ordinal)
        || preset is "ellipse" or "donut" or "pie" or "chord" or "teardrop" or "arc" or "blockArc" or "smileyFace"
            or "heart" or "sun" or "moon" or "plus" or "cube" or "octagon" or "decagon" or "dodecagon" or "heptagon"
            or "pentagon" or "diamond" or "quadArrow" or "irregularSeal1" or "irregularSeal2" or "lightningBolt"
            or "flowChartConnector" or "flowChartOr" or "flowChartSummingJunction" or "uturnArrow" or "bentArrow"
            or "leftUpArrow" or "bentUpArrow" or "leftRightUpArrow" or "frame" or "halfFrame" or "corner"
            or "diagStripe" or "rtTriangle" or "noSmoking";
}