namespace FreeP.Core.Model;

/// <summary>
/// Captures section definitions and their ordered slide memberships for undoable slide edits.
/// Also centralizes membership changes that must follow slide insertion, removal, and reordering.
/// </summary>
internal sealed class PresentationSectionMembershipSnapshot
{
    private readonly IReadOnlyList<SectionState> _sections;

    private PresentationSectionMembershipSnapshot(IEnumerable<PresentationSection> sections)
    {
        _sections = sections
            .Select(section => new SectionState(
                section.Id,
                section.Name,
                section.SlideIds.ToArray()))
            .ToArray();
    }

    public static PresentationSectionMembershipSnapshot Capture(Presentation presentation) =>
        new(presentation.Sections);

    public void Restore(Presentation presentation)
    {
        presentation.Sections.Clear();
        foreach (var state in _sections)
        {
            var section = new PresentationSection
            {
                Id = state.Id,
                Name = state.Name,
            };
            section.SlideIds.AddRange(state.SlideIds);
            presentation.Sections.Add(section);
        }
    }

    public static void AddInsertedSlide(
        Presentation presentation,
        int insertedIndex,
        string insertedSlideId)
    {
        if (presentation.Slides.Count <= 1)
            return;

        var insertAfterNeighbor = insertedIndex > 0;
        var neighborSlideId = insertAfterNeighbor
            ? presentation.Slides[insertedIndex - 1].Id
            : presentation.Slides[1].Id;

        foreach (var section in presentation.Sections)
        {
            var neighborIndex = section.SlideIds.FindIndex(id =>
                string.Equals(id, neighborSlideId, StringComparison.Ordinal));
            if (neighborIndex < 0)
                continue;

            section.SlideIds.Insert(
                insertAfterNeighbor ? neighborIndex + 1 : neighborIndex,
                insertedSlideId);
            return;
        }
    }

    public static void RemoveSlide(Presentation presentation, string slideId)
    {
        foreach (var section in presentation.Sections)
        {
            section.SlideIds.RemoveAll(id =>
                string.Equals(id, slideId, StringComparison.Ordinal));
        }
    }

    public static void SynchronizeOrder(Presentation presentation)
    {
        foreach (var section in presentation.Sections)
        {
            var remaining = section.SlideIds.ToList();
            var ordered = new List<string>(remaining.Count);

            foreach (var slide in presentation.Slides)
            {
                var index = remaining.FindIndex(id =>
                    string.Equals(id, slide.Id, StringComparison.Ordinal));
                if (index < 0)
                    continue;

                ordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            ordered.AddRange(remaining);
            section.SlideIds.Clear();
            section.SlideIds.AddRange(ordered);
        }
    }

    private sealed record SectionState(string Id, string Name, IReadOnlyList<string> SlideIds);
}
