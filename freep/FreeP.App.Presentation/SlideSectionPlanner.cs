using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideSectionActionKind
{
    AddSection,
    RenameSection,
    RemoveSection,
    RemoveAllSections
}

public sealed record SlideSectionActionPlan(
    SlideSectionActionKind Kind,
    string Text,
    int SlideIndex,
    int SectionIndex,
    bool IsEnabled,
    string SuggestedName = "");

public sealed record SlideSectionActionExecutionPlan(
    SlideSectionActionKind Kind,
    int SlideIndex,
    int SectionIndex,
    bool IsEnabled,
    bool RequiresNamePrompt,
    string PromptTitle = "",
    string PromptLabel = "",
    string PromptAcceptText = "",
    string PromptCancelText = "",
    string SuggestedName = "");

public static class SlideSectionPlanner
{
    public const string AddSectionMenuText = "Add Section";
    public const string RenameSectionMenuText = "Rename Section";
    public const string RemoveSectionMenuText = "Remove Section";
    public const string RemoveAllSectionsMenuText = "Remove All Sections";
    public const string DefaultSectionName = "Untitled Section";

    public static IReadOnlyList<SlideSectionActionPlan> BuildSlideContextActions(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        var isValidSlide = IsValidSlideIndex(slides, slideIndex);
        return
        [
            new SlideSectionActionPlan(
                SlideSectionActionKind.AddSection,
                AddSectionMenuText,
                slideIndex,
                SectionIndex: -1,
                isValidSlide,
                BuildDefaultSectionName(sections)),
        ];
    }

    public static IReadOnlyList<SlideSectionActionPlan> BuildSectionHeaderActions(
        IReadOnlyList<PresentationSection> sections,
        int sectionIndex,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var isValidSection = sectionIndex >= 0 && sectionIndex < sections.Count;
        var currentName = isValidSection ? NormalizeSectionName(sections[sectionIndex].Name) : DefaultSectionName;
        return
        [
            new SlideSectionActionPlan(
                SlideSectionActionKind.RenameSection,
                RenameSectionMenuText,
                slideIndex,
                sectionIndex,
                isValidSection,
                currentName),
            new SlideSectionActionPlan(
                SlideSectionActionKind.RemoveSection,
                RemoveSectionMenuText,
                slideIndex,
                sectionIndex,
                isValidSection),
            new SlideSectionActionPlan(
                SlideSectionActionKind.RemoveAllSections,
                RemoveAllSectionsMenuText,
                slideIndex,
                sectionIndex,
                sections.Count > 0),
        ];
    }

    public static SlideSectionActionExecutionPlan BuildExecutionPlan(SlideSectionActionPlan action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var prompt = PresentationPaneTextResources.BuildSlideSectionNamePrompt(action.Kind);

        return action.Kind switch
        {
            SlideSectionActionKind.AddSection => new SlideSectionActionExecutionPlan(
                action.Kind,
                action.SlideIndex,
                action.SectionIndex,
                action.IsEnabled,
                RequiresNamePrompt: action.IsEnabled,
                PromptTitle: prompt.Title,
                PromptLabel: prompt.Label,
                PromptAcceptText: prompt.AcceptText,
                PromptCancelText: prompt.CancelText,
                SuggestedName: action.SuggestedName),

            SlideSectionActionKind.RenameSection => new SlideSectionActionExecutionPlan(
                action.Kind,
                action.SlideIndex,
                action.SectionIndex,
                action.IsEnabled,
                RequiresNamePrompt: action.IsEnabled,
                PromptTitle: prompt.Title,
                PromptLabel: prompt.Label,
                PromptAcceptText: prompt.AcceptText,
                PromptCancelText: prompt.CancelText,
                SuggestedName: action.SuggestedName),

            SlideSectionActionKind.RemoveSection => new SlideSectionActionExecutionPlan(
                action.Kind,
                action.SlideIndex,
                action.SectionIndex,
                action.IsEnabled,
                RequiresNamePrompt: false),

            SlideSectionActionKind.RemoveAllSections => new SlideSectionActionExecutionPlan(
                action.Kind,
                action.SlideIndex,
                action.SectionIndex,
                action.IsEnabled,
                RequiresNamePrompt: false),

            _ => new SlideSectionActionExecutionPlan(
                action.Kind,
                action.SlideIndex,
                action.SectionIndex,
                IsEnabled: false,
                RequiresNamePrompt: false),
        };
    }

    public static bool TryApplyAction(
        EditingSession editor,
        SlideSectionActionExecutionPlan execution,
        string? promptedName = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(execution);

        if (!execution.IsEnabled)
            return false;

        if (execution.RequiresNamePrompt && promptedName is null)
            return false;

        return execution.Kind switch
        {
            SlideSectionActionKind.AddSection =>
                editor.AddSectionAtSlide(execution.SlideIndex, promptedName),
            SlideSectionActionKind.RenameSection =>
                editor.RenameSection(execution.SectionIndex, promptedName),
            SlideSectionActionKind.RemoveSection =>
                editor.RemoveSection(execution.SectionIndex),
            SlideSectionActionKind.RemoveAllSections =>
                editor.RemoveAllSections(),
            _ => false,
        };
    }

    public static string NormalizeSectionName(string? name, string fallback = DefaultSectionName)
    {
        var normalized = string.Join(
            " ",
            (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 0 ? fallback : normalized;
    }

    public static string BuildDefaultSectionName(IReadOnlyList<PresentationSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        if (!sections.Any(section => StringComparer.OrdinalIgnoreCase.Equals(
                NormalizeSectionName(section.Name),
                DefaultSectionName)))
        {
            return DefaultSectionName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{DefaultSectionName} {i}";
            if (!sections.Any(section => StringComparer.OrdinalIgnoreCase.Equals(
                    NormalizeSectionName(section.Name),
                    candidate)))
            {
                return candidate;
            }
        }
    }

    public static IReadOnlyList<PresentationSection>? PlanAddSection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int slideIndex,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        if (!IsValidSlideIndex(slides, slideIndex))
            return null;

        var planned = CloneAndPruneSections(slides, sections);
        var slideIdsInOrder = slides.Select(slide => slide.Id).ToArray();
        var slideIndexById = BuildSlideIndexById(slides);
        var nextSectionStart = planned
            .Select(section => FirstKnownSlideIndex(section, slideIndexById))
            .Where(index => index > slideIndex)
            .DefaultIfEmpty(slides.Count)
            .Min();

        var newMemberIds = slideIdsInOrder
            .Skip(slideIndex)
            .Take(nextSectionStart - slideIndex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var section in planned)
            section.SlideIds.RemoveAll(newMemberIds.Contains);

        planned.RemoveAll(section => section.SlideIds.Count == 0);

        var newSection = new PresentationSection
        {
            Name = NormalizeSectionName(name, BuildDefaultSectionName(planned)),
        };
        foreach (var slideId in slideIdsInOrder.Where(newMemberIds.Contains))
            newSection.SlideIds.Add(slideId);

        planned.Add(newSection);
        SortSectionsByFirstSlide(planned, slideIndexById);
        return planned;
    }

    public static IReadOnlyList<PresentationSection>? PlanRenameSection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int sectionIndex,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        if (sectionIndex < 0 || sectionIndex >= sections.Count)
            return null;

        var originalSection = sections[sectionIndex];
        var planned = CloneAndPruneSections(slides, sections);
        var plannedIndex = FindPlannedSectionIndex(planned, originalSection);
        if (plannedIndex < 0)
            return null;

        planned[plannedIndex].Name = NormalizeSectionName(name);
        return planned;
    }

    public static IReadOnlyList<PresentationSection>? PlanRemoveSection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        if (sectionIndex < 0 || sectionIndex >= sections.Count)
            return null;

        var originalSection = sections[sectionIndex];
        var planned = CloneAndPruneSections(slides, sections);
        var plannedIndex = FindPlannedSectionIndex(planned, originalSection);
        if (plannedIndex < 0)
            return null;

        var removed = planned[plannedIndex];
        planned.RemoveAt(plannedIndex);

        if (plannedIndex > 0 && plannedIndex - 1 < planned.Count)
        {
            var previous = planned[plannedIndex - 1];
            var mergedIds = previous.SlideIds
                .Concat(removed.SlideIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            previous.SlideIds.Clear();
            foreach (var slide in slides)
            {
                if (mergedIds.Contains(slide.Id))
                    previous.SlideIds.Add(slide.Id);
            }
        }

        return planned;
    }

    public static IReadOnlyList<PresentationSection> PlanRemoveAllSections() =>
        Array.Empty<PresentationSection>();

    internal static List<PresentationSection> CloneSections(IEnumerable<PresentationSection> sections) =>
        sections.Select(CloneSection).ToList();

    internal static bool SectionListsEqual(
        IReadOnlyList<PresentationSection> left,
        IReadOnlyList<PresentationSection> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(left[i].Id, right[i].Id) ||
                !StringComparer.Ordinal.Equals(left[i].Name, right[i].Name) ||
                !left[i].SlideIds.SequenceEqual(right[i].SlideIds, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidSlideIndex(IReadOnlyList<Slide> slides, int slideIndex) =>
        slides.Count > 0 && slideIndex >= 0 && slideIndex < slides.Count;

    private static List<PresentationSection> CloneAndPruneSections(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections)
    {
        var slideIds = slides.Select(slide => slide.Id).ToArray();
        var slideIdSet = slideIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pruned = new List<PresentationSection>();

        foreach (var section in sections)
        {
            var clone = new PresentationSection
            {
                Id = string.IsNullOrWhiteSpace(section.Id)
                    ? Guid.NewGuid().ToString("B").ToUpperInvariant()
                    : section.Id,
                Name = NormalizeSectionName(section.Name),
            };

            var memberSet = section.SlideIds
                .Where(slideIdSet.Contains)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var slideId in slideIds.Where(memberSet.Contains))
                clone.SlideIds.Add(slideId);

            if (clone.SlideIds.Count > 0)
                pruned.Add(clone);
        }

        var slideIndexById = BuildSlideIndexById(slides);
        SortSectionsByFirstSlide(pruned, slideIndexById);
        return pruned;
    }

    private static PresentationSection CloneSection(PresentationSection source)
    {
        var clone = new PresentationSection
        {
            Id = source.Id,
            Name = source.Name,
        };
        clone.SlideIds.AddRange(source.SlideIds);
        return clone;
    }

    private static int FindPlannedSectionIndex(
        IReadOnlyList<PresentationSection> planned,
        PresentationSection originalSection)
    {
        for (var i = 0; i < planned.Count; i++)
        {
            if (SectionIdentityMatches(planned[i], originalSection))
                return i;
        }

        return -1;
    }

    private static bool SectionIdentityMatches(
        PresentationSection planned,
        PresentationSection original)
    {
        if (!string.IsNullOrWhiteSpace(original.Id)
            && StringComparer.Ordinal.Equals(planned.Id, original.Id))
        {
            return true;
        }

        return planned.SlideIds.SequenceEqual(
            original.SlideIds.Where(id => planned.SlideIds.Contains(id, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> BuildSlideIndexById(IReadOnlyList<Slide> slides)
    {
        var slideIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < slides.Count; i++)
            slideIndexById[slides[i].Id] = i;
        return slideIndexById;
    }

    private static void SortSectionsByFirstSlide(
        List<PresentationSection> sections,
        IReadOnlyDictionary<string, int> slideIndexById) =>
        sections.Sort((left, right) =>
            FirstKnownSlideIndex(left, slideIndexById).CompareTo(FirstKnownSlideIndex(right, slideIndexById)));

    private static int FirstKnownSlideIndex(
        PresentationSection section,
        IReadOnlyDictionary<string, int> slideIndexById)
    {
        var first = int.MaxValue;
        foreach (var slideId in section.SlideIds)
        {
            if (slideIndexById.TryGetValue(slideId, out var index) && index < first)
                first = index;
        }

        return first;
    }
}
