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
    {
        slideIndex = -1;
        if (presentation is null || zoom?.ObjectKind != PreservedObjectKind.Zoom)
            return false;

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
