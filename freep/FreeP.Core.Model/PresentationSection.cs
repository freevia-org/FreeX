namespace FreeP.Core.Model;

/// <summary>
/// A named section that groups a contiguous run of slides in the presentation.
/// Sections are stored in ppt/presentation.xml inside a p14:sectionLst extension.
///
/// Each section has a stable GUID <see cref="Id"/> (the p14:section id= attribute) and a
/// display <see cref="Name"/>.  <see cref="SlideIds"/> holds the presentation-level sldId
/// integer values (matching p:sldId id= in the slide-id list) in order — these are the
/// canonical keys used to map sections back to loaded <see cref="Slide"/> instances.
/// </summary>
public sealed class PresentationSection
{
    /// <summary>
    /// GUID string for the section (p14:section id="{…}").
    /// When creating a new section, generate via <c>Guid.NewGuid().ToString("B").ToUpperInvariant()</c>.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("B").ToUpperInvariant();

    /// <summary>Display name shown in the slide panel and presentation outlines.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of sldId integer values (as strings) that belong to this section.
    /// These map directly to p:sldId id= attributes in the presentation.xml slide list.
    /// </summary>
    public List<string> SlideIds { get; } = new();
}
