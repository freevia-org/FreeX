using System.Linq;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Builds a native PowerPoint Section Zoom object for an existing section.</summary>
public sealed record SectionZoomInsertionPlan(
    string TargetSectionId,
    string TargetDisplayName,
    int TargetSlideCount,
    long OffsetXEmu,
    long OffsetYEmu,
    long ExtentCxEmu,
    long ExtentCyEmu);

public static class SectionZoomInsertionPlanner
{
    public const string CommandId = "freep.insert-section-zoom";
    public const string DialogTitle = "Insert Section Zoom";
    public const string ShapeName = "Section Zoom";

    private const string SectionZoomUri =
        "http://schemas.microsoft.com/office/powerpoint/2016/sectionzoom";
    private const long DefaultOffsetXEmu = 457200;
    private const long DefaultOffsetYEmu = 274638;
    private const long DefaultWidthEmu = 2743200;
    private const long DefaultHeightEmu = 1828800;

    public static IReadOnlyList<(string Id, string DisplayName)> BuildTargetOptions(
        Presentation presentation,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return presentation.Sections
            .Where(section => section.SlideIds.Count > 0
                && section.SlideIds.Any(slideId => presentation.Slides.Any(slide =>
                    string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase))))
            .Select(section => (
                section.Id,
                DisplayName: $"{section.Name.Trim()} ({section.SlideIds.Count} slides)"))
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .ToArray();
    }

    public static bool TryBuildPlan(
        Presentation presentation,
        string? targetSectionId,
        out SectionZoomInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        plan = null!;

        var section = presentation.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, targetSectionId?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (section is null || section.SlideIds.Count == 0)
            return false;

        var validSlideCount = section.SlideIds.Count(slideId => presentation.Slides.Any(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase)));
        if (validSlideCount == 0)
            return false;

        plan = new SectionZoomInsertionPlan(
            section.Id,
            string.IsNullOrWhiteSpace(section.Name) ? "Untitled section" : section.Name.Trim(),
            validSlideCount,
            DefaultOffsetXEmu,
            DefaultOffsetYEmu,
            DefaultWidthEmu,
            DefaultHeightEmu);
        return true;
    }

    public static SlideShape CreateShape(Presentation presentation, string targetSectionId)
    {
        if (!TryBuildPlan(presentation, targetSectionId, out var plan))
            throw new InvalidOperationException("Choose a section containing slides as the Section Zoom target.");

        var shapeId = NextShapeId(presentation);
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
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Zoom,
                ZoomTargetSectionId = plan.TargetSectionId,
                RawXml = BuildRawXml(shapeId, plan.TargetSectionId),
            },
        };
    }

    private static string BuildRawXml(uint shapeId, string targetSectionId)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace psez = SectionZoomUri;

        return new XElement(p + "graphicFrame",
            new XAttribute(XNamespace.Xmlns + "p", p.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", a.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "psez", psez.NamespaceName),
            new XElement(p + "nvGraphicFramePr",
                new XElement(p + "cNvPr", new XAttribute("id", shapeId), new XAttribute("name", ShapeName)),
                new XElement(p + "cNvGraphicFramePr"),
                new XElement(p + "nvPr")),
            new XElement(p + "xfrm",
                new XElement(a + "off", new XAttribute("x", DefaultOffsetXEmu), new XAttribute("y", DefaultOffsetYEmu)),
                new XElement(a + "ext", new XAttribute("cx", DefaultWidthEmu), new XAttribute("cy", DefaultHeightEmu))),
            new XElement(a + "graphic",
                new XElement(a + "graphicData",
                    new XAttribute("uri", SectionZoomUri),
                    new XElement(psez + "sectionZm",
                        new XElement(psez + "sectionZmObj", new XAttribute("sectionId", targetSectionId))))))
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
