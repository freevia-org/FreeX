using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Builds a native multi-target PowerPoint Summary Zoom.</summary>
public sealed record SummaryZoomInsertionPlan(
    IReadOnlyList<SummaryZoomTarget> Targets,
    string TargetDisplayName,
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu);

public static class SummaryZoomInsertionPlanner
{
    public const string CommandId = "freep.insert-summary-zoom";
    public const string DialogTitle = "Insert Summary Zoom";
    public const string ShapeName = "Summary Zoom";

    private const string SummaryZoomUri =
        "http://schemas.microsoft.com/office/powerpoint/2016/summaryzoom";
    private const long DefaultOffsetXEmu = 457200;
    private const long DefaultOffsetYEmu = 274638;
    private const long DefaultWidthEmu = 5486400;
    private const long DefaultHeightEmu = 3657600;

    public static IReadOnlyList<(string Id, string DisplayName)> BuildTargetOptions(
        Presentation presentation,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return presentation.Sections
            .Where(section => section.SlideIds.Any(slideId => presentation.Slides.Any(slide =>
                string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase))))
            .Select(section => (
                section.Id,
                DisplayName: $"{(string.IsNullOrWhiteSpace(section.Name) ? "Untitled section" : section.Name.Trim())} ({ValidSlideCount(presentation, section)} slides)"))
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .ToArray();
    }

    public static bool TryBuildPlan(
        Presentation presentation,
        IEnumerable<string>? targetSectionIds,
        out SummaryZoomInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        plan = null!;

        var requested = targetSectionIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        if (requested.Length < 2)
            return false;

        var sections = requested
            .Select(id => presentation.Sections.FirstOrDefault(section =>
                string.Equals(section.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (sections.Any(section => section is null || ValidSlideCount(presentation, section) == 0))
            return false;

        var columns = (int)Math.Ceiling(Math.Sqrt(sections.Length));
        var rows = (int)Math.Ceiling(sections.Length / (double)columns);
        var scaleX = 100000 / columns;
        var scaleY = 100000 / rows;
        var targets = sections.Select((section, index) => new SummaryZoomTarget(
            section!.Id,
            string.IsNullOrWhiteSpace(section.Name) ? "Untitled section" : section.Name.Trim(),
            string.Empty,
            (index % columns) * scaleX,
            (index / columns) * scaleY,
            scaleX,
            scaleY)).ToArray();

        plan = new SummaryZoomInsertionPlan(
            targets,
            $"{targets.Length} sections",
            DefaultOffsetXEmu,
            DefaultOffsetYEmu,
            DefaultWidthEmu,
            DefaultHeightEmu);
        return true;
    }

    public static SlideShape CreateShape(Presentation presentation, IEnumerable<string> targetSectionIds)
    {
        if (!TryBuildPlan(presentation, targetSectionIds, out var plan))
            throw new InvalidOperationException("Choose at least two sections containing slides as Summary Zoom targets.");

        var shapeId = NextShapeId(presentation);
        var preserved = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            RawXml = BuildRawXml(shapeId, plan.Targets),
            AlternateContentFallbackXml = BuildFallbackXml(shapeId),
            WasAlternateContent = true,
            McRequiresToken = "p14",
            McRequiresNsUri = "http://schemas.microsoft.com/office/powerpoint/2010/main",
        };
        preserved.McRequiresNsUris["p14"] = preserved.McRequiresNsUri;
        preserved.SummaryZoomTargets.AddRange(plan.Targets);

        return new SlideShape
        {
            Id = shapeId,
            Name = ShapeName,
            Kind = SlideShapeKind.Zoom,
            AlternativeTextTitle = ShapeName,
            AlternativeText = $"Zoom to {plan.TargetDisplayName}",
            OffsetXEmu = plan.OffsetXEmu,
            OffsetYEmu = plan.OffsetYEmu,
            ExtentCxEmu = plan.ExtentCxEmu,
            ExtentCyEmu = plan.ExtentCyEmu,
            PreservedObject = preserved,
        };
    }

    private static int ValidSlideCount(Presentation presentation, PresentationSection section) =>
        section.SlideIds.Count(slideId => presentation.Slides.Any(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase)));

    private static string BuildRawXml(uint shapeId, IReadOnlyList<SummaryZoomTarget> targets)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace p166 = "http://schemas.microsoft.com/office/powerpoint/2016/6/main";
        XNamespace psuz = SummaryZoomUri;

        var summaryObjects = targets.Select(target =>
            new XElement(psuz + "summaryZmObj",
                new XAttribute("sectionId", target.SectionId),
                new XAttribute("title", target.Title),
                new XAttribute("descr", target.Description),
                new XAttribute("offsetFactorX", target.OffsetFactorX),
                new XAttribute("offsetFactorY", target.OffsetFactorY),
                new XAttribute("scaleFactorX", target.ScaleFactorX),
                new XAttribute("scaleFactorY", target.ScaleFactorY),
                ZoomObjectPropertiesXml.Build(p166, a)));

        return new XElement(p + "graphicFrame",
            new XAttribute(XNamespace.Xmlns + "p", p.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", a.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p166", p166.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "psuz", psuz.NamespaceName),
            new XElement(p + "nvGraphicFramePr",
                new XElement(p + "cNvPr", new XAttribute("id", shapeId), new XAttribute("name", ShapeName)),
                new XElement(p + "cNvGraphicFramePr"),
                new XElement(p + "nvPr")),
            new XElement(p + "xfrm",
                new XElement(a + "off", new XAttribute("x", DefaultOffsetXEmu), new XAttribute("y", DefaultOffsetYEmu)),
                new XElement(a + "ext", new XAttribute("cx", DefaultWidthEmu), new XAttribute("cy", DefaultHeightEmu))),
            new XElement(a + "graphic",
                new XElement(a + "graphicData",
                    new XAttribute("uri", SummaryZoomUri),
                    new XElement(psuz + "summaryZm",
                        summaryObjects,
                        new XElement(psuz + "fixedLayout")))))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildFallbackXml(uint shapeId)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XElement(p + "sp",
            new XAttribute(XNamespace.Xmlns + "p", p.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", a.NamespaceName),
            new XElement(p + "nvSpPr",
                new XElement(p + "cNvPr", new XAttribute("id", shapeId), new XAttribute("name", ShapeName)),
                new XElement(p + "cNvSpPr"),
                new XElement(p + "nvPr")),
            new XElement(p + "spPr",
                new XElement(a + "xfrm",
                    new XElement(a + "off", new XAttribute("x", DefaultOffsetXEmu), new XAttribute("y", DefaultOffsetYEmu)),
                    new XElement(a + "ext", new XAttribute("cx", DefaultWidthEmu), new XAttribute("cy", DefaultHeightEmu))),
                new XElement(a + "prstGeom", new XAttribute("prst", "roundRect"), new XElement(a + "avLst")),
                new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "4472C4"))),
                new XElement(a + "ln", new XAttribute("w", 12700),
                    new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "2F5597"))))),
            new XElement(p + "txBody",
                new XElement(a + "bodyPr", new XAttribute("wrap", "square")),
                new XElement(a + "lstStyle"),
                new XElement(a + "p",
                    new XElement(a + "pPr", new XAttribute("algn", "ctr")),
                    new XElement(a + "r",
                        new XElement(a + "rPr", new XAttribute("lang", "en-US"), new XAttribute("sz", 1800), new XAttribute("b", 1)),
                        new XElement(a + "solidFill", new XElement(a + "srgbClr", new XAttribute("val", "FFFFFF"))),
                        new XElement(a + "t", "Summary Zoom")),
                    new XElement(a + "endParaRPr", new XAttribute("lang", "en-US")))))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static uint NextShapeId(Presentation presentation) =>
        presentation.Slides
            .SelectMany(slide => Enumerate(slide.Shapes))
            .Select(shape => shape.Id)
            .DefaultIfEmpty(0u)
            .Max() + 1;

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
