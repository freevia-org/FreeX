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
    int PlaybackSlideCount,
    int SourceSlideOccurrenceIndex,
    string SourceSlideId,
    string? CustomShowName,
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
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";

    public const string GeneratedInkContentType = "application/inkml+xml";

    private static readonly XNamespace P =
        "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace P14 =
        "http://schemas.microsoft.com/office/powerpoint/2010/main";

    private static readonly XNamespace InkMl = "http://www.w3.org/2003/InkML";

    private static readonly XNamespace FreePInk = "https://freex.local/freep/ink/2026";

    public static SlideShowInkPersistenceResult ApplyRetentionOnExit(
        Presentation presentation,
        SlideShowInkExecutionState state,
        SlideShowPlaybackRoute playbackRoute)
    {
        ArgumentNullException.ThrowIfNull(playbackRoute);

        return ApplyRetentionOnExit(
            presentation,
            state,
            playbackRoute.GetSourceSlideIndex,
            playbackRoute.CustomShowName,
            playbackRoute.SourceSlideIndices);
    }

    public static SlideShowInkPersistenceResult ApplyRetentionOnExit(
        Presentation presentation,
        SlideShowInkExecutionState state,
        Func<int, int>? mapRouteSlideToPresentationSlide = null,
        string? customShowName = null,
        IReadOnlyList<int>? routeSourceSlideIndices = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(state);

        var retainedState = SlideShowInkExecutionPlanner.ApplyRetentionOnExit(state).State with
        {
            LaserOverlayPoint = null,
        };
        var plan = BuildPlan(
            presentation,
            retainedState,
            mapRouteSlideToPresentationSlide,
            customShowName,
            routeSourceSlideIndices);
        ApplyPlan(presentation, plan);

        return new SlideShowInkPersistenceResult(retainedState, plan);
    }

    public static SlideShowInkPersistencePlan BuildPlan(
        Presentation presentation,
        SlideShowInkExecutionState retainedState,
        SlideShowPlaybackRoute playbackRoute)
    {
        ArgumentNullException.ThrowIfNull(playbackRoute);

        return BuildPlan(
            presentation,
            retainedState,
            playbackRoute.GetSourceSlideIndex,
            playbackRoute.CustomShowName,
            playbackRoute.SourceSlideIndices);
    }

    public static SlideShowInkPersistencePlan BuildPlan(
        Presentation presentation,
        SlideShowInkExecutionState retainedState,
        Func<int, int>? mapRouteSlideToPresentationSlide = null,
        string? customShowName = null,
        IReadOnlyList<int>? routeSourceSlideIndices = null)
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
        var playbackSlideCount = routeSourceSlideIndices?.Count ?? presentation.Slides.Count;
        var routeGroups = retainedState.CommittedStrokes
            .Where(IsPersistableStroke)
            .GroupBy(stroke => stroke.SlideIndex)
            .OrderBy(group => group.Key)
            .ToArray();

        var slidePlans = new List<SlideShowInkPersistenceSlidePlan>();
        var nextShapeIds = new Dictionary<int, uint>();

        foreach (var routeGroup in routeGroups)
        {
            // A negative key encodes a slide displayed via slideshow's hidden-slide reveal (see
            // SlideShowInkExecutionPlanner.EncodeHiddenSlideInkIndex): it is not a playback-route
            // index at all, since hidden slides are excluded from the route, so it must be
            // decoded directly rather than passed through mapRouteSlideToPresentationSlide.
            var presentationSlideIndex = SlideShowInkExecutionPlanner.TryDecodeHiddenSlideInkIndex(
                routeGroup.Key,
                out var revealedHiddenSlideIndex)
                ? revealedHiddenSlideIndex
                : mapRouteSlideToPresentationSlide(routeGroup.Key);
            if (presentationSlideIndex < 0 || presentationSlideIndex >= presentation.Slides.Count)
            {
                continue;
            }

            var slide = presentation.Slides[presentationSlideIndex];
            if (!nextShapeIds.TryGetValue(presentationSlideIndex, out var shapeId))
            {
                shapeId = NextShapeId(slide);
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
                presentation.SlideSizeCyEmu,
                strokes);
            var sourceSlideId = string.IsNullOrWhiteSpace(slide.Id)
                ? string.Empty
                : slide.Id.Trim();
            var normalizedCustomShowName = NormalizeOptional(customShowName);
            var sourceSlideOccurrenceIndex = SourceSlideOccurrenceIndex(
                routeGroup.Key,
                presentationSlideIndex,
                routeSourceSlideIndices);
            var inkXml = BuildInkXml(
                routeGroup.Key,
                presentationSlideIndex,
                playbackSlideCount,
                sourceSlideOccurrenceIndex,
                sourceSlideId,
                normalizedCustomShowName,
                shapeId,
                strokes);

            slidePlans.Add(new SlideShowInkPersistenceSlidePlan(
                routeGroup.Key,
                presentationSlideIndex,
                playbackSlideCount,
                sourceSlideOccurrenceIndex,
                sourceSlideId,
                normalizedCustomShowName,
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
        long slideCyEmu,
        IReadOnlyList<SlideShowInkPersistenceStrokePlan> strokes)
    {
        const double emuPerDip = 914400d / 96d;
        const double paddingDip = 8;
        var points = strokes.SelectMany(stroke => stroke.Points).ToArray();
        var minX = points.Length == 0 ? 0 : points.Min(point => point.X) - paddingDip;
        var minY = points.Length == 0 ? 0 : points.Min(point => point.Y) - paddingDip;
        var maxX = points.Length == 0 ? slideCxEmu / emuPerDip : points.Max(point => point.X) + paddingDip;
        var maxY = points.Length == 0 ? slideCyEmu / emuPerDip : points.Max(point => point.Y) + paddingDip;
        var offX = Math.Clamp((long)Math.Round(minX * emuPerDip), 0, Math.Max(0, slideCxEmu - 1));
        var offY = Math.Clamp((long)Math.Round(minY * emuPerDip), 0, Math.Max(0, slideCyEmu - 1));
        var right = Math.Clamp((long)Math.Round(maxX * emuPerDip), offX + 1, Math.Max(offX + 1, slideCxEmu));
        var bottom = Math.Clamp((long)Math.Round(maxY * emuPerDip), offY + 1, Math.Max(offY + 1, slideCyEmu));
        var extentCx = Math.Max(1, right - offX);
        var extentCy = Math.Max(1, bottom - offY);

        var el = new XElement(P + "contentPart",
            new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
            new XAttribute(P14 + "bwMode", "auto"),
            new XAttribute(R + "id", relationshipId),
            new XElement(P14 + "nvContentPartPr",
                new XElement(P14 + "cNvPr",
                    new XAttribute("id", shapeId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", shapeName)),
                new XElement(P14 + "cNvContentPartPr"),
                new XElement(P14 + "nvPr")),
            new XElement(P14 + "xfrm",
                new XElement(A + "off",
                    new XAttribute("x", offX.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("y", offY.ToString(CultureInfo.InvariantCulture))),
                new XElement(A + "ext",
                    new XAttribute("cx", extentCx.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("cy", extentCy.ToString(CultureInfo.InvariantCulture)))));

        return el.ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildInkXml(
        int routeSlideIndex,
        int presentationSlideIndex,
        int playbackSlideCount,
        int sourceSlideOccurrenceIndex,
        string sourceSlideId,
        string? customShowName,
        uint shapeId,
        IReadOnlyList<SlideShowInkPersistenceStrokePlan> strokes)
    {
        var attributes = new List<XAttribute>
        {
            new(XNamespace.Xmlns + "inkml", InkMl.NamespaceName),
            new(XNamespace.Xmlns + "freep", FreePInk.NamespaceName),
            new(FreePInk + "format", "freep-slideshow-ink"),
            new(FreePInk + "shapeId", shapeId.ToString(CultureInfo.InvariantCulture)),
            new(FreePInk + "routeSlideIndex", routeSlideIndex.ToString(CultureInfo.InvariantCulture)),
            new(FreePInk + "presentationSlideIndex", presentationSlideIndex.ToString(CultureInfo.InvariantCulture)),
            new(FreePInk + "playbackSlideCount", playbackSlideCount.ToString(CultureInfo.InvariantCulture)),
            new(FreePInk + "sourceSlideOccurrenceIndex", sourceSlideOccurrenceIndex.ToString(CultureInfo.InvariantCulture))
        };
        if (!string.IsNullOrWhiteSpace(sourceSlideId))
        {
            attributes.Add(new XAttribute(FreePInk + "sourceSlideId", sourceSlideId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(customShowName))
        {
            attributes.Add(new XAttribute(FreePInk + "customShowName", customShowName.Trim()));
        }

        var traceFormat = new XElement(InkMl + "traceFormat",
            new XElement(InkMl + "channel",
                new XAttribute("name", "X"),
                new XAttribute("type", "decimal"),
                new XAttribute("units", "cm")),
            new XElement(InkMl + "channel",
                new XAttribute("name", "Y"),
                new XAttribute("type", "decimal"),
                new XAttribute("units", "cm")));
        var inkSource = new XElement(InkMl + "inkSource",
            new XAttribute(XNamespace.Xml + "id", "inkSrc0"),
            traceFormat);
        var context = new XElement(InkMl + "context",
            new XAttribute(XNamespace.Xml + "id", "ctx0"),
            inkSource);
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(InkMl + "ink",
                attributes,
                new XElement(InkMl + "definitions",
                    context,
                    strokes.Select((stroke, index) => BuildBrush(stroke, index))),
                strokes.Select((stroke, index) =>
                    new XElement(InkMl + "trace",
                        new XAttribute("id", StableStrokeId(stroke.StrokeId, index)),
                        new XAttribute("contextRef", "#ctx0"),
                        new XAttribute("brushRef", $"#br{index}"),
                        new XAttribute(FreePInk + "pointerMode", stroke.PointerMode.ToString()),
                        new XAttribute(FreePInk + "color", stroke.ColorHex),
                        new XAttribute(FreePInk + "thicknessDip", FormatDouble(stroke.ThicknessDip)),
                        new XAttribute(FreePInk + "opacity", FormatDouble(stroke.Opacity)),
                        new XAttribute(FreePInk + "points", string.Join(" ", stroke.Points.Select(FormatPoint))),
                        FormatInkTrace(stroke.Points)))));

        return doc.ToString(SaveOptions.None);
    }

    private static string StableStrokeId(string strokeId, int index) =>
        string.IsNullOrWhiteSpace(strokeId) ? $"stroke{index + 1}" : strokeId;

    private static string FormatPoint(SlideShowInkPoint point) =>
        $"{FormatDouble(point.X)},{FormatDouble(point.Y)}";

    private static string FormatInkTrace(IReadOnlyList<SlideShowInkPoint> points) =>
        string.Join(", ", points.Select(point =>
            $"{FormatDouble(DipToCm(point.X))} {FormatDouble(DipToCm(point.Y))}"));

    private static XElement BuildBrush(
        SlideShowInkPersistenceStrokePlan stroke,
        int index) =>
        new(InkMl + "brush",
            new XAttribute(XNamespace.Xml + "id", $"br{index}"),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "width"),
                new XAttribute("value", FormatDouble(ThicknessDipToCm(stroke))),
                new XAttribute("units", "cm")),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "height"),
                new XAttribute("value", FormatDouble(ThicknessDipToCm(stroke))),
                new XAttribute("units", "cm")),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "color"),
                new XAttribute("value", stroke.ColorHex)),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "transparency"),
                new XAttribute("value", TransparencyByte(stroke.Opacity))),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "antiAliased"),
                new XAttribute("value", "1")),
            new XElement(InkMl + "brushProperty",
                new XAttribute("name", "fitToCurve"),
                new XAttribute("value", "0")));

    private static double DipToCm(double dip) => dip * 2.54 / 96;

    private static double ThicknessDipToCm(SlideShowInkPersistenceStrokePlan stroke) =>
        Math.Max(0.001, stroke.ThicknessDip * 2.54 / 96);

    private static int TransparencyByte(double opacity) =>
        (int)Math.Clamp(Math.Round((1 - Math.Clamp(opacity, 0, 1)) * 255), 0, 255);

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static int SourceSlideOccurrenceIndex(
        int routeSlideIndex,
        int presentationSlideIndex,
        IReadOnlyList<int>? routeSourceSlideIndices)
    {
        if (routeSourceSlideIndices is null ||
            routeSlideIndex < 0 ||
            routeSlideIndex >= routeSourceSlideIndices.Count)
        {
            return 0;
        }

        var occurrenceIndex = 0;
        for (var index = 0; index < routeSlideIndex; index++)
        {
            if (routeSourceSlideIndices[index] == presentationSlideIndex)
            {
                occurrenceIndex++;
            }
        }

        return occurrenceIndex;
    }
}
