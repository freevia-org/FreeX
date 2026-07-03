using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowInkPersistenceStrokePlan(
    string StrokeId,
    SlideShowPresenterPointerMode PointerMode,
    string ColorHex,
    double ThicknessDip,
    double Opacity,
    IReadOnlyList<SlideShowInkPoint> Points);

public sealed record SlideShowInkPersistenceSlidePlan(
    int RouteSlideIndex,
    int PresentationSlideIndex,
    uint ShapeId,
    string ShapeName,
    string RelationshipId,
    string InkPartPath,
    string ContentPartXml,
    string InkXml,
    IReadOnlyList<SlideShowInkPersistenceStrokePlan> Strokes);

public sealed record SlideShowInkPersistencePlan(
    SlideShowInkRetentionDecision RetentionDecision,
    IReadOnlyList<SlideShowInkPersistenceSlidePlan> Slides)
{
    public bool HasGeneratedInk => Slides.Count > 0;
}

public sealed record SlideShowInkPersistenceResult(
    SlideShowInkExecutionState State,
    SlideShowInkPersistencePlan Plan);

public static class SlideShowInkPersistencePlanner
{
    public const string GeneratedInkRelationshipType =
        "http://schemas.microsoft.com/office/2016/05/19/relationships/ink";

    public const string GeneratedInkContentType = "application/xml";

    private static readonly XNamespace P =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace InkMl = "http://www.w3.org/2003/InkML";

    private static readonly XNamespace FreePInk = "https://freex.local/freep/ink/2026";

    public static SlideShowInkPersistenceResult ApplyRetentionOnExit(
        Presentation presentation,
        SlideShowInkExecutionState state,
        Func<int, int>? mapRouteSlideToPresentationSlide = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(state);

        var retainedState = SlideShowInkExecutionPlanner.ApplyRetentionOnExit(state).State with
        {
            LaserOverlayPoint = null,
        };
        var plan = BuildPlan(presentation, retainedState, mapRouteSlideToPresentationSlide);
        ApplyPlan(presentation, plan);

        return new SlideShowInkPersistenceResult(retainedState, plan);
    }

    public static SlideShowInkPersistencePlan BuildPlan(
        Presentation presentation,
        SlideShowInkExecutionState retainedState,
        Func<int, int>? mapRouteSlideToPresentationSlide = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(retainedState);

        if (retainedState.InkRetentionDecision == SlideShowInkRetentionDecision.ClearInk)
        {
            return new SlideShowInkPersistencePlan(
                retainedState.InkRetentionDecision,
                Array.Empty<SlideShowInkPersistenceSlidePlan>());
        }

        mapRouteSlideToPresentationSlide ??= static slideIndex => slideIndex;
        var routeGroups = retainedState.CommittedStrokes
            .Where(IsPersistableStroke)
            .GroupBy(stroke => stroke.SlideIndex)
            .OrderBy(group => group.Key)
            .ToArray();

        var slidePlans = new List<SlideShowInkPersistenceSlidePlan>();
        var nextShapeIds = new Dictionary<int, uint>();

        foreach (var routeGroup in routeGroups)
        {
            var presentationSlideIndex = mapRouteSlideToPresentationSlide(routeGroup.Key);
            if (presentationSlideIndex < 0 || presentationSlideIndex >= presentation.Slides.Count)
            {
                continue;
            }

            if (!nextShapeIds.TryGetValue(presentationSlideIndex, out var shapeId))
            {
                shapeId = NextShapeId(presentation.Slides[presentationSlideIndex]);
            }

            var strokes = routeGroup
                .Select(stroke => new SlideShowInkPersistenceStrokePlan(
                    stroke.StrokeId,
                    stroke.PointerMode,
                    NormalizeColor(stroke.InkState.ColorHex),
                    stroke.InkState.ThicknessDip,
                    stroke.InkState.Opacity,
                    stroke.Points.ToArray()))
                .ToArray();

            var shapeName = $"FreeP Ink {shapeId}";
            var relationshipId = $"rIdFreePInk{shapeId}";
            var inkPartPath = $"ppt/ink/freepInk_s{presentationSlideIndex + 1}_{shapeId}.xml";
            var contentPartXml = BuildContentPartXml(
                shapeId,
                shapeName,
                relationshipId,
                presentation.SlideSizeCxEmu,
                presentation.SlideSizeCyEmu);
            var inkXml = BuildInkXml(routeGroup.Key, presentationSlideIndex, shapeId, strokes);

            slidePlans.Add(new SlideShowInkPersistenceSlidePlan(
                routeGroup.Key,
                presentationSlideIndex,
                shapeId,
                shapeName,
                relationshipId,
                inkPartPath,
                contentPartXml,
                inkXml,
                strokes));

            nextShapeIds[presentationSlideIndex] = shapeId + 1;
        }

        return new SlideShowInkPersistencePlan(retainedState.InkRetentionDecision, slidePlans);
    }

    public static void ApplyPlan(Presentation presentation, SlideShowInkPersistencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var slidePlan in plan.Slides)
        {
            if (slidePlan.PresentationSlideIndex < 0 ||
                slidePlan.PresentationSlideIndex >= presentation.Slides.Count)
            {
                continue;
            }

            var preserved = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Ink,
                RawXml = slidePlan.ContentPartXml,
            };
            preserved.Parts[slidePlan.InkPartPath] = Encoding.UTF8.GetBytes(slidePlan.InkXml);
            preserved.PartContentTypes[slidePlan.InkPartPath] = GeneratedInkContentType;
            preserved.SlideRels[slidePlan.RelationshipId] =
                (GeneratedInkRelationshipType, slidePlan.InkPartPath);

            presentation.Slides[slidePlan.PresentationSlideIndex].Shapes.Add(new SlideShape
            {
                Id = slidePlan.ShapeId,
                Name = slidePlan.ShapeName,
                Kind = SlideShapeKind.Ink,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = Math.Max(1, presentation.SlideSizeCxEmu),
                ExtentCyEmu = Math.Max(1, presentation.SlideSizeCyEmu),
                PreservedObject = preserved,
            });
        }
    }

    private static bool IsPersistableStroke(SlideShowInkStroke stroke) =>
        stroke.Points.Count > 0 &&
        stroke.PointerMode is SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter;

    private static uint NextShapeId(Slide slide)
    {
        var max = MaxShapeId(slide.Shapes);
        return max >= uint.MaxValue ? uint.MaxValue : max + 1;
    }

    private static uint MaxShapeId(IEnumerable<SlideShape> shapes)
    {
        uint max = 0;
        foreach (var shape in shapes)
        {
            if (shape.Id > max)
            {
                max = shape.Id;
            }

            if (shape.Kind == SlideShapeKind.Group && shape.Children.Count > 0)
            {
                var childMax = MaxShapeId(shape.Children);
                if (childMax > max)
                {
                    max = childMax;
                }
            }
        }

        return max;
    }

    private static string BuildContentPartXml(
        uint shapeId,
        string shapeName,
        string relationshipId,
        long slideCxEmu,
        long slideCyEmu)
    {
        var el = new XElement(P + "contentPart",
            new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XAttribute(R + "id", relationshipId),
            new XElement(P + "nvContentPartPr",
                new XElement(P + "cNvPr",
                    new XAttribute("id", shapeId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", shapeName)),
                new XElement(P + "cNvContentPartPr"),
                new XElement(P + "nvPr")),
            new XElement(P + "xfrm",
                new XElement(A + "off",
                    new XAttribute("x", "0"),
                    new XAttribute("y", "0")),
                new XElement(A + "ext",
                    new XAttribute("cx", Math.Max(1, slideCxEmu).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("cy", Math.Max(1, slideCyEmu).ToString(CultureInfo.InvariantCulture)))));

        return el.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildInkXml(
        int routeSlideIndex,
        int presentationSlideIndex,
        uint shapeId,
        IReadOnlyList<SlideShowInkPersistenceStrokePlan> strokes)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(InkMl + "ink",
                new XAttribute(XNamespace.Xmlns + "inkml", InkMl.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "freep", FreePInk.NamespaceName),
                new XAttribute(FreePInk + "format", "freep-slideshow-ink"),
                new XAttribute(FreePInk + "shapeId", shapeId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(FreePInk + "routeSlideIndex", routeSlideIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(FreePInk + "presentationSlideIndex", presentationSlideIndex.ToString(CultureInfo.InvariantCulture)),
                strokes.Select((stroke, index) =>
                    new XElement(InkMl + "trace",
                        new XAttribute("id", StableStrokeId(stroke.StrokeId, index)),
                        new XAttribute(FreePInk + "pointerMode", stroke.PointerMode.ToString()),
                        new XAttribute(FreePInk + "color", stroke.ColorHex),
                        new XAttribute(FreePInk + "thicknessDip", FormatDouble(stroke.ThicknessDip)),
                        new XAttribute(FreePInk + "opacity", FormatDouble(stroke.Opacity)),
                        string.Join(" ", stroke.Points.Select(FormatPoint))))));

        return doc.ToString(SaveOptions.None);
    }

    private static string StableStrokeId(string strokeId, int index) =>
        string.IsNullOrWhiteSpace(strokeId) ? $"stroke{index + 1}" : strokeId;

    private static string FormatPoint(SlideShowInkPoint point) =>
        $"{FormatDouble(point.X)},{FormatDouble(point.Y)}";

    private static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string NormalizeColor(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return "#000000";
        }

        var trimmed = colorHex.Trim();
        return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : ("#" + trimmed).ToUpperInvariant();
    }
}
