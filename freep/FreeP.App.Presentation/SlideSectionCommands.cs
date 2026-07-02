using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed class ReplaceSlideSectionsCommand : IPresentationCommand
{
    private readonly List<PresentationSection> _nextSections;
    private List<PresentationSection>? _previousSections;

    public ReplaceSlideSectionsCommand(IEnumerable<PresentationSection> nextSections)
    {
        ArgumentNullException.ThrowIfNull(nextSections);
        _nextSections = SlideSectionPlanner.CloneSections(nextSections);
    }

    public string Label => "Edit Sections";

    public bool HasEffect(Presentation presentation) =>
        !SlideSectionPlanner.SectionListsEqual(presentation.Sections, _nextSections);

    public void Apply(Presentation presentation)
    {
        _previousSections = SlideSectionPlanner.CloneSections(presentation.Sections);
        ReplaceSections(presentation, _nextSections);
    }

    public void Revert(Presentation presentation)
    {
        if (_previousSections is null)
            return;

        ReplaceSections(presentation, _previousSections);
    }

    private static void ReplaceSections(Presentation presentation, IEnumerable<PresentationSection> sections)
    {
        presentation.Sections.Clear();
        presentation.Sections.AddRange(SlideSectionPlanner.CloneSections(sections));
    }
}
