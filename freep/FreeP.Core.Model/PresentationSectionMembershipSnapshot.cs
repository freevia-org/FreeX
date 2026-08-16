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

    /// <summary>
    /// Reassigns <paramref name="movedSlideId"/>'s section membership to follow its new
    /// position (<paramref name="newIndex"/>) in <paramref name="presentation"/>.Slides.
    /// Called after a slide has moved in the slide list so a drag across a section boundary
    /// reassigns membership instead of just leaving the slide listed under its old section.
    /// </summary>
    /// <remarks>
    /// Generalizes <see cref="AddInsertedSlide"/>'s neighbor convention (join whichever
    /// section the adjacent slide belongs to — the slide before the drop point, or, at
    /// index 0, the slide after it) to also resolve a drop that lands squarely between two
    /// different sections' contiguous ranges: when the slide immediately before and the
    /// slide immediately after the drop point belong to two DIFFERENT, non-adjacent sections
    /// (in section order), any section(s) sitting between them in that order must currently
    /// be empty — sections are contiguous, so a populated section between them would itself
    /// have shown up as a neighbor. The moved slide claims the first such empty section.
    /// </remarks>
    public static void ReassignMovedSlide(
        Presentation presentation,
        int newIndex,
        string movedSlideId)
    {
        // Strip old membership first — the slide's correct section (if any) is
        // recomputed below purely from the new slide order.
        RemoveSlide(presentation, movedSlideId);

        if (presentation.Sections.Count == 0)
            return;
        if (presentation.Slides.Count <= 1)
            return;
        if (newIndex < 0 || newIndex >= presentation.Slides.Count)
            return;

        var sections = presentation.Sections;
        var prevSlideId = newIndex > 0 ? presentation.Slides[newIndex - 1].Id : null;
        var nextSlideId = newIndex < presentation.Slides.Count - 1
            ? presentation.Slides[newIndex + 1].Id
            : null;

        var prevSectionIndex = FindSectionIndexContaining(sections, prevSlideId);
        var nextSectionIndex = FindSectionIndexContaining(sections, nextSlideId);

        int targetSectionIndex;
        if (prevSectionIndex >= 0 && nextSectionIndex >= 0)
        {
            if (prevSectionIndex == nextSectionIndex || nextSectionIndex == prevSectionIndex + 1)
            {
                // Same section, or two sections that already directly abut — join whichever
                // section precedes the drop point (mirrors AddInsertedSlide's convention).
                targetSectionIndex = prevSectionIndex;
            }
            else
            {
                // One or more empty sections sit between the two neighbors' sections —
                // claim the first one.
                targetSectionIndex = prevSectionIndex + 1;
            }
        }
        else if (prevSectionIndex >= 0)
        {
            targetSectionIndex = prevSectionIndex;
        }
        else if (nextSectionIndex >= 0)
        {
            targetSectionIndex = nextSectionIndex;
        }
        else
        {
            // Neither neighbor belongs to a section — stays unsectioned.
            return;
        }

        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < presentation.Slides.Count; i++)
            indexById[presentation.Slides[i].Id] = i;

        var targetIds = sections[targetSectionIndex].SlideIds;
        var insertAt = 0;
        while (insertAt < targetIds.Count && indexById[targetIds[insertAt]] < newIndex)
            insertAt++;
        targetIds.Insert(insertAt, movedSlideId);
    }

    private static int FindSectionIndexContaining(
        IReadOnlyList<PresentationSection> sections,
        string? slideId)
    {
        if (slideId is null)
            return -1;

        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].SlideIds.Any(id => string.Equals(id, slideId, StringComparison.Ordinal)))
                return i;
        }

        return -1;
    }

    private sealed record SectionState(string Id, string Name, IReadOnlyList<string> SlideIds);
}
