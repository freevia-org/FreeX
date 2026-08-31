using FreeP.Core.Model;

namespace FreeP.App.Compositor;

internal sealed class PresentationSectionSlideCatalog
{
    private readonly HashSet<string> _slideIds;
    private readonly Dictionary<string, PresentationSection> _sectionsById;

    private PresentationSectionSlideCatalog(Presentation presentation)
    {
        _slideIds = presentation.Slides
            .Select(slide => slide.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _sectionsById = new Dictionary<string, PresentationSection>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in presentation.Sections)
        {
            if (section.Id is not null)
                _sectionsById.TryAdd(section.Id, section);
        }
    }

    public static PresentationSectionSlideCatalog Create(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        return new PresentationSectionSlideCatalog(presentation);
    }

    public PresentationSection? FindSection(string? sectionId) =>
        sectionId is not null && _sectionsById.TryGetValue(sectionId, out var section)
            ? section
            : null;

    public int CountValidSlides(PresentationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        int count = 0;
        foreach (var slideId in section.SlideIds)
        {
            if (_slideIds.Contains(slideId))
                count++;
        }

        return count;
    }
}
