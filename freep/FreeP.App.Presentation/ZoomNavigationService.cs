using System.Xml.Linq;
using System.Xml;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves a preserved Slide Zoom to the editor slide it targets.
/// </summary>
public static class ZoomNavigationService
{
    /// <summary>
    /// Finds the zero-based presentation index targeted by a Slide Zoom.
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
        if (!numericId.HasValue)
            return false;

        slideIndex = presentation.Slides.FindIndex(slide => slide.NumericId == numericId.Value);
        return slideIndex >= 0;
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
}
