using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Attaches rendered first-slide previews to the native Summary Zoom tile payload.
/// Rendering stays host-owned; this planner only resolves section targets and updates the
/// relationship-backed package model so WPF, Avalonia, and PowerPoint consume the same payload.
/// </summary>
public static class SummaryZoomPreviewPlanner
{
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string ImageContentType = "image/png";
    private const string MediaPathPrefix = "ppt/media/freep-summary-zoom";
    private const string SummaryObjectLocalName = "summaryZmObj";

    public const int DefaultPreviewWidthPx = 320;

    public static int ResolvePreviewHeightPx(Presentation presentation, int widthPx = DefaultPreviewWidthPx)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (widthPx < 1)
            throw new ArgumentOutOfRangeException(nameof(widthPx));

        var widthEmu = Math.Max(1, presentation.SlideSizeCxEmu);
        var heightEmu = Math.Max(1, presentation.SlideSizeCyEmu);
        return Math.Max(1, (int)Math.Round(widthPx * heightEmu / (double)widthEmu));
    }

    /// <summary>
    /// Renders and attaches one preview for each Summary Zoom target that resolves to a slide.
    /// A renderer failure leaves that tile without a preview while preserving the native target.
    /// </summary>
    /// <returns>The number of previews successfully attached.</returns>
    public static int AttachPreviewImages(
        Presentation presentation,
        SlideShape summaryZoomShape,
        Func<int, byte[]?> renderSlideToPng)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(summaryZoomShape);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);

        var info = summaryZoomShape.PreservedObject;
        if (summaryZoomShape.Kind != SlideShapeKind.Zoom
            || info?.ObjectKind != PreservedObjectKind.Zoom
            || info.SummaryZoomTargets.Count == 0
            || string.IsNullOrWhiteSpace(info.RawXml))
            return 0;

        XElement raw;
        try { raw = XElement.Parse(info.RawXml); }
        catch { return 0; }

        var objects = raw.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, SummaryObjectLocalName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var attached = 0;

        for (var index = 0; index < info.SummaryZoomTargets.Count && index < objects.Length; index++)
        {
            var target = info.SummaryZoomTargets[index];
            if (!TryResolveTargetSlideIndex(presentation, target.SectionId, out var slideIndex))
                continue;

            byte[]? preview;
            try { preview = renderSlideToPng(slideIndex); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                preview = null;
            }

            if (preview is not { Length: > 0 })
                continue;

            if (!CanAttachRelationship(objects[index]))
                continue;

            var relId = NextRelationshipId(info, index + 1);
            var mediaPath = $"{MediaPathPrefix}-{summaryZoomShape.Id}-{index + 1}.png";
            info.Parts[mediaPath] = preview;
            info.PartContentTypes[mediaPath] = ImageContentType;
            info.SlideRels[relId] = (ImageRelationshipType, mediaPath);

            var properties = objects[index].Descendants()
                .First(element => string.Equals(element.Name.LocalName, "zmPr",
                    StringComparison.OrdinalIgnoreCase));
            AttachRelationship(properties, relId);
            attached++;
        }

        if (attached > 0)
            info.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        return attached;
    }

    /// <summary>
    /// Renders and attaches the single preview used by a Slide or Section Zoom.
    /// The native target remains authoritative; this only adds the optional image
    /// relationship consumed by PowerPoint and by the host renderers.
    /// </summary>
    public static bool AttachPreviewImage(
        Presentation presentation,
        SlideShape zoomShape,
        int targetSlideIndex,
        Func<int, byte[]?> renderSlideToPng)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(zoomShape);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);

        if (targetSlideIndex < 0 || targetSlideIndex >= presentation.Slides.Count)
            return false;

        var info = zoomShape.PreservedObject;
        if (zoomShape.Kind != SlideShapeKind.Zoom
            || info?.ObjectKind != PreservedObjectKind.Zoom
            || string.IsNullOrWhiteSpace(info.RawXml))
            return false;

        XElement raw;
        try { raw = XElement.Parse(info.RawXml); }
        catch { return false; }

        var properties = raw.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "zmPr",
                StringComparison.OrdinalIgnoreCase));
        if (properties is null)
            return false;

        byte[]? preview;
        try { preview = renderSlideToPng(targetSlideIndex); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            preview = null;
        }

        if (preview is not { Length: > 0 })
            return false;

        var relId = NextRelationshipId(info, 1);
        var mediaPath = $"ppt/media/freep-zoom-preview-{zoomShape.Id}.png";
        info.Parts[mediaPath] = preview;
        info.PartContentTypes[mediaPath] = ImageContentType;
        info.SlideRels[relId] = (ImageRelationshipType, mediaPath);
        AttachRelationship(properties, relId);
        info.RawXml = raw.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    public static bool TryResolveTargetSlideIndex(
        Presentation presentation,
        string sectionId,
        out int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        slideIndex = -1;
        if (string.IsNullOrWhiteSpace(sectionId))
            return false;

        var section = presentation.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        var slideId = section?.SlideIds.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(slideId))
            return false;

        slideIndex = presentation.Slides.FindIndex(slide =>
            string.Equals(slide.Id, slideId, StringComparison.OrdinalIgnoreCase));
        return slideIndex >= 0;
    }

    private static bool CanAttachRelationship(XElement summaryObject)
    {
        return summaryObject.Descendants()
            .Any(element => string.Equals(element.Name.LocalName, "zmPr",
                StringComparison.OrdinalIgnoreCase));
    }

    private static void AttachRelationship(XElement properties, string relId)
    {
        XNamespace p166 = "http://schemas.microsoft.com/office/powerpoint/2016/6/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var blipFill = properties.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "blipFill",
                StringComparison.OrdinalIgnoreCase));
        if (blipFill is null)
        {
            blipFill = new XElement(p166 + "blipFill",
                new XElement(a + "stretch", new XElement(a + "fillRect")));
            properties.Add(blipFill);
        }

        var blip = blipFill.Element(a + "blip");
        if (blip is null)
        {
            blip = new XElement(a + "blip");
            blipFill.AddFirst(blip);
        }

        blip.SetAttributeValue(r + "embed", relId);
    }

    private static string NextRelationshipId(PreservedObjectInfo info, int ordinal)
    {
        var baseId = $"rIdFreePSummaryPreview{ordinal}";
        var id = baseId;
        var suffix = 2;
        while (info.SlideRels.ContainsKey(id))
            id = $"{baseId}_{suffix++}";
        return id;
    }
}
