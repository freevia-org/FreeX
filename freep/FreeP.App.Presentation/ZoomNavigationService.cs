using System.Xml.Linq;
using System.Xml;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves a preserved Slide Zoom or Section Zoom to the editor slide it targets.
/// </summary>
public static class ZoomNavigationService
{
    /// <summary>
    /// Finds the zero-based presentation index targeted by a Slide Zoom or Section Zoom.
    /// </summary>
    public static bool TryGetTargetSlideIndex(
        Presentation presentation,
        PreservedObjectInfo? zoom,
        out int slideIndex)
        => TryGetTargetSlideIndex(
            presentation,
            zoom,
            null,
            null,
            out slideIndex,
            out _);

    /// <summary>
    /// Resolves a Summary Zoom target using coordinates normalized to the containing shape's
    /// width and height. Slide and Section Zoom callers can use the simpler overload above.
    /// </summary>
    public static bool TryGetTargetSlideIndex(
        Presentation presentation,
        PreservedObjectInfo? zoom,
        double? relativeX,
        double? relativeY,
        out int slideIndex)
        => TryGetTargetSlideIndex(
            presentation,
            zoom,
            relativeX,
            relativeY,
            out slideIndex,
            out _);

    /// <summary>
    /// Resolves a Zoom target and returns the authored Return to Parent behavior.
    /// An omitted PowerPoint returnToParent attribute uses the application default (true).
    /// </summary>
    public static bool TryGetTargetSlideIndex(
        Presentation presentation,
        PreservedObjectInfo? zoom,
        double? relativeX,
        double? relativeY,
        out int slideIndex,
        out bool returnToParent)
    {
        slideIndex = -1;
        returnToParent = false;
        if (presentation is null || zoom?.ObjectKind != PreservedObjectKind.Zoom)
            return false;

        returnToParent = zoom.ZoomProperties?.ReturnToParent ?? true;

        if (zoom.SummaryZoomTargets.Count > 0)
        {
            var target = SelectSummaryTarget(zoom.SummaryZoomTargets, relativeX, relativeY);
            return target is not null && TryGetFirstSectionSlideIndex(presentation, target.SectionId, out slideIndex);
        }

        var numericId = zoom.ZoomTargetSlideNumericId ?? ReadTargetSlideNumericId(zoom.RawXml);
        if (numericId.HasValue)
        {
            slideIndex = presentation.Slides.FindIndex(slide => slide.NumericId == numericId.Value);
            return slideIndex >= 0;
        }

        var sectionId = zoom.ZoomTargetSectionId ?? ReadTargetSectionId(zoom.RawXml);
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            var section = presentation.Sections.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, sectionId, StringComparison.OrdinalIgnoreCase));
            if (section is null)
                return false;

            foreach (var memberId in section.SlideIds)
            {
                slideIndex = presentation.Slides.FindIndex(slide =>
                    string.Equals(slide.Id, memberId, StringComparison.OrdinalIgnoreCase));
                if (slideIndex >= 0)
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetFirstSectionSlideIndex(
        Presentation presentation,
        string sectionId,
        out int slideIndex)
    {
        slideIndex = -1;
        var section = presentation.Sections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (section is null)
            return false;

        foreach (var memberId in section.SlideIds)
        {
            slideIndex = presentation.Slides.FindIndex(slide =>
                string.Equals(slide.Id, memberId, StringComparison.OrdinalIgnoreCase));
            if (slideIndex >= 0)
                return true;
        }

        slideIndex = -1;
        return false;
    }

    private static SummaryZoomTarget? SelectSummaryTarget(
        IReadOnlyList<SummaryZoomTarget> targets,
        double? relativeX,
        double? relativeY)
    {
        if (targets.Count == 0)
            return null;
        if (!relativeX.HasValue || !relativeY.HasValue)
            return targets[0];

        var x = Math.Clamp(relativeX.Value, 0, 1);
        var y = Math.Clamp(relativeY.Value, 0, 1);
        var containing = targets.FirstOrDefault(target =>
        {
            var left = target.OffsetFactorX / 100000d;
            var top = target.OffsetFactorY / 100000d;
            var right = left + target.ScaleFactorX / 100000d;
            var bottom = top + target.ScaleFactorY / 100000d;
            return x >= left && x <= right && y >= top && y <= bottom;
        });
        if (containing is not null)
            return containing;

        return targets
            .OrderBy(target => DistanceSquared(
                x, y,
                target.OffsetFactorX / 100000d + target.ScaleFactorX / 200000d,
                target.OffsetFactorY / 100000d + target.ScaleFactorY / 200000d))
            .First();
    }

    private static double DistanceSquared(double x, double y, double targetX, double targetY) =>
        Math.Pow(x - targetX, 2) + Math.Pow(y - targetY, 2);

    private static uint? ReadTargetSlideNumericId(string? rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return null;

        try
        {
            var root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace);
            var value = root.Descendants()
                .FirstOrDefault(element => string.Equals(
                    element.Name.LocalName, "sldZmObj", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("sldId")?.Value;
            return uint.TryParse(value, out var numericId) ? numericId : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string? ReadTargetSectionId(string? rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return null;

        try
        {
            var root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace);
            return root.Descendants()
                .FirstOrDefault(element => string.Equals(
                    element.Name.LocalName, "sectionZmObj", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("sectionId")?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
