using System.Linq;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Builds a native PowerPoint Slide Zoom object for an existing target slide.</summary>
public sealed record SlideZoomInsertionPlan(
    string TargetSlideId,
    uint TargetSlideNumericId,
    string TargetDisplayName,
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu);

public static class SlideZoomInsertionPlanner
{
    public const string CommandId = "freep.insert-slide-zoom";
    public const string DialogTitle = "Insert Slide Zoom";
    public const string ShapeName = "Slide Zoom";

    private const string SlideZoomUri =
        "http://schemas.microsoft.com/office/powerpoint/2016/slidezoom";
    private const long DefaultOffsetXEmu = 457200;   // 0.5 inch
    private const long DefaultOffsetYEmu = 274638;   // 0.3 inch
    private const long DefaultWidthEmu = 2743200;    // 3 inches
    private const long DefaultHeightEmu = 1828800;   // 2 inches

    public static IReadOnlyList<(string Id, string DisplayName)> BuildTargetOptions(
        IReadOnlyList<Slide> slides,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);
        return slides
            .Select((slide, index) => (slide, index))
            .Where(item => item.index != currentSlideIndex)
            .Select(item => (
                item.slide.Id,
                DisplayName: $"{item.index + 1}. {(string.IsNullOrWhiteSpace(item.slide.Title)
                    ? "Untitled slide"
                    : item.slide.Title)}"))
            .ToArray();
    }

    public static bool TryBuildPlan(
        Presentation presentation,
        int currentSlideIndex,
        string? targetSlideId,
        out SlideZoomInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        plan = null!;

        var target = presentation.Slides.FirstOrDefault(slide =>
            string.Equals(slide.Id, targetSlideId?.Trim(), StringComparison.Ordinal));
        if (target is null || presentation.Slides.IndexOf(target) == currentSlideIndex)
            return false;

        var index = presentation.Slides.IndexOf(target);
        var targetNumericId = EffectiveNumericId(presentation, target);
        plan = new SlideZoomInsertionPlan(
            target.Id,
            targetNumericId,
            $"{index + 1}. {(string.IsNullOrWhiteSpace(target.Title) ? "Untitled slide" : target.Title)}",
            DefaultOffsetXEmu,
            DefaultOffsetYEmu,
            DefaultWidthEmu,
            DefaultHeightEmu);
        return true;
    }

    public static SlideShape CreateShape(
        Presentation presentation,
        int currentSlideIndex,
        string targetSlideId)
    {
        if (!TryBuildPlan(presentation, currentSlideIndex, targetSlideId, out var plan))
            throw new InvalidOperationException("Choose a different slide as the Slide Zoom target.");

        var shapeId = NextShapeId(presentation);
        return new SlideShape
        {
            Id = shapeId,
            Name = ShapeName,
            Kind = SlideShapeKind.Zoom,
            AlternativeTextTitle = "Slide Zoom",
            AlternativeText = $"Zoom to {plan.TargetDisplayName}",
            OffsetXEmu = plan.OffsetXEmu,
            OffsetYEmu = plan.OffsetYEmu,
            ExtentCxEmu = plan.ExtentCxEmu,
            ExtentCyEmu = plan.ExtentCyEmu,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Zoom,
                ZoomTargetSlideNumericId = plan.TargetSlideNumericId,
                ZoomProperties = new ZoomObjectProperties(true, "preview", null, true),
                RawXml = BuildRawXml(shapeId, plan.TargetSlideNumericId),
            },
        };
    }

    private static string BuildRawXml(uint shapeId, uint targetNumericId)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace p166 = "http://schemas.microsoft.com/office/powerpoint/2016/6/main";
        XNamespace pslz = SlideZoomUri;

        var zoomObject = new XElement(pslz + "sldZmObj",
            new XAttribute("sldId", targetNumericId),
            ZoomObjectPropertiesXml.Build(p166, a));

        var frame = new XElement(p + "graphicFrame",
            new XAttribute(XNamespace.Xmlns + "p", p.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", a.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p166", p166.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pslz", pslz.NamespaceName),
            new XElement(p + "nvGraphicFramePr",
                new XElement(p + "cNvPr", new XAttribute("id", shapeId), new XAttribute("name", ShapeName)),
                new XElement(p + "cNvGraphicFramePr"),
                new XElement(p + "nvPr")),
            new XElement(p + "xfrm",
                new XElement(a + "off", new XAttribute("x", DefaultOffsetXEmu), new XAttribute("y", DefaultOffsetYEmu)),
                new XElement(a + "ext", new XAttribute("cx", DefaultWidthEmu), new XAttribute("cy", DefaultHeightEmu))),
            new XElement(a + "graphic",
                new XElement(a + "graphicData",
                    new XAttribute("uri", SlideZoomUri),
                    new XElement(pslz + "sldZm",
                        zoomObject))));

        return frame.ToString(SaveOptions.DisableFormatting);
    }

    private static uint NextShapeId(Presentation presentation) =>
        presentation.Slides
            .SelectMany(slide => Enumerate(slide.Shapes))
            .Select(shape => shape.Id)
            .DefaultIfEmpty(0u)
            .Max() + 1;

    // PptxPackageWriter uses the same 256-upward allocation for new/duplicate slide IDs.
    // Resolve that value here so an authored zoom can carry a valid target before the first save.
    private static uint EffectiveNumericId(Presentation presentation, Slide target)
    {
        uint next = 256;
        var used = new HashSet<uint>();
        foreach (var slide in presentation.Slides)
        {
            var numericId = slide.NumericId.GetValueOrDefault();
            if (numericId == 0 || !used.Add(numericId))
            {
                do { numericId = next++; } while (!used.Add(numericId));
            }
            else if (numericId >= next)
            {
                next = numericId + 1;
            }

            if (ReferenceEquals(slide, target))
                return numericId;
        }

        throw new InvalidOperationException("The target slide is not part of the presentation.");
    }

    private static IEnumerable<SlideShape> Enumerate(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in Enumerate(shape.Children))
                yield return child;
        }
    }
}
