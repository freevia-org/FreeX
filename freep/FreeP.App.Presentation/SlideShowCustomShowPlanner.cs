using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCustomSlideSequence(
    string Name,
    IReadOnlyList<string> SlideIds);

public enum SlideShowLaunchChoiceKind
{
    FullPresentation,
    FromCurrentSlide,
    CustomShow
}

public sealed record SlideShowLaunchChoice(
    string ChoiceId,
    string Label,
    SlideShowLaunchChoiceKind Kind,
    int SlideCount,
    int StartIndex,
    string? CustomShowName,
    bool IsEnabled,
    string? DisabledReason);

public sealed record SlideShowLaunchPlan(
    int TotalSlideCount,
    int CurrentSlideIndex,
    IReadOnlyList<SlideShowLaunchChoice> Choices)
{
    public SlideShowLaunchChoice? DefaultChoice =>
        Choices.FirstOrDefault(choice => choice.IsEnabled);
}

public sealed record SlideShowCustomShowSummary(
    int Index,
    uint Id,
    string Name,
    IReadOnlyList<string> SlideIds);

public sealed record SlideShowCustomShowSlideOption(
    int Index,
    string SlideId,
    string Title)
{
    public string DisplayText => $"Slide {Index + 1}: {Title}";

    public override string ToString() => DisplayText;
}

public sealed record SlideShowCustomShowAuthoringPlan(
    IReadOnlyList<SlideShowCustomShowSummary> CustomShows,
    IReadOnlyList<SlideShowCustomShowSlideOption> AvailableSlides);

public sealed record SlideShowCustomShowMutationResult(
    bool Succeeded,
    string? ErrorMessage,
    int CustomShowIndex,
    PresentationCustomShow? CustomShow,
    int SelectedSlideIndex = -1)
{
    public static SlideShowCustomShowMutationResult Success(
        int customShowIndex,
        PresentationCustomShow? customShow,
        int selectedSlideIndex = -1) =>
        new(true, null, customShowIndex, customShow, selectedSlideIndex);

    public static SlideShowCustomShowMutationResult Failure(string errorMessage) =>
        new(false, errorMessage, -1, null);
}

public sealed record SlideShowCustomShowDragReorderPlan(
    bool IsValid,
    bool ShouldApplyMutation,
    int SourceSlideIndex,
    string SourceSlideId,
    int TargetDropIndex,
    int TargetSlideIndex,
    int SelectedSlideIndex,
    IReadOnlyList<string> SlideIds,
    string? ErrorMessage);

public sealed class SlideShowPlaybackRoute
{
    public SlideShowPlaybackRoute(
        string? customShowName,
        IReadOnlyList<Slide> slides,
        IReadOnlyList<int> sourceSlideIndices,
        int startIndex,
        int animationStartIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sourceSlideIndices);

        if (slides.Count != sourceSlideIndices.Count)
        {
            throw new ArgumentException(
                "The playback slide list and source slide index list must have the same count.",
                nameof(sourceSlideIndices));
        }

        CustomShowName = string.IsNullOrWhiteSpace(customShowName)
            ? null
            : customShowName.Trim();
        Slides = slides;
        SourceSlideIndices = sourceSlideIndices;
        StartIndex = slides.Count == 0
            ? 0
            : Math.Clamp(startIndex, 0, slides.Count - 1);
        AnimationStartIndex = animationStartIndex;
    }

    public string? CustomShowName { get; }

    public IReadOnlyList<Slide> Slides { get; }

    public IReadOnlyList<int> SourceSlideIndices { get; }

    public int StartIndex { get; }

    /// <summary>
    /// Optional zero-based animation index for Animation Pane playback. A negative
    /// value keeps the normal first-animation state for the route's first slide.
    /// </summary>
    public int AnimationStartIndex { get; }

    public int SlideCount => Slides.Count;

    public int GetSourceSlideIndex(int playbackSlideIndex) =>
        playbackSlideIndex >= 0 && playbackSlideIndex < SourceSlideIndices.Count
            ? SourceSlideIndices[playbackSlideIndex]
            : -1;

    public SlideShowPlaybackRoute WithAnimationStartIndex(int animationStartIndex) =>
        new(
            CustomShowName,
            Slides,
            SourceSlideIndices,
            StartIndex,
            animationStartIndex);
}

public static class SlideShowCustomShowPlanner
{
    public const string FullPresentationChoiceId = "full-presentation";
    public const string FromCurrentSlideChoiceId = "from-current-slide";
    public const string CustomShowChoicePrefix = "custom-show:";
    public const string NoSlidesMessage = "No slides are available for slide show playback.";
    public const string EmptyCustomShowMessage = "Custom show has no available slides.";
    public const string EmptyCustomShowNameMessage = "Custom show name is required.";
    public const string DuplicateCustomShowNameMessage = "Custom show name must be unique.";
    public const string MissingCustomShowMessage = "Custom show was not found.";
    public const string MissingCustomShowSlideMessage = "Custom show slide was not found.";

    public static SlideShowCustomShowAuthoringPlan BuildAuthoringPlan(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var customShows = presentation.CustomShows
            .Select((show, index) => new SlideShowCustomShowSummary(
                index,
                show.Id,
                NormalizeDisplayName(show.Name),
                NormalizeSlideIds(presentation, show.SlideIds)))
            .ToArray();

        var availableSlides = presentation.Slides
            .Select((slide, index) => new SlideShowCustomShowSlideOption(
                index,
                slide.Id,
                string.IsNullOrWhiteSpace(slide.Title)
                    ? $"Slide {index + 1}"
                    : slide.Title.Trim()))
            .ToArray();

        return new SlideShowCustomShowAuthoringPlan(customShows, availableSlides);
    }

    public static SlideShowCustomShowMutationResult CreateCustomShow(
        Presentation presentation,
        string? name,
        IEnumerable<string?> slideIds)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slideIds);

        if (!TryNormalizeCustomShowName(presentation, name, excludedCustomShowIndex: null, out var normalizedName, out var errorMessage))
        {
            return SlideShowCustomShowMutationResult.Failure(errorMessage);
        }

        var customShow = new PresentationCustomShow
        {
            Id = AllocateNextCustomShowId(presentation),
            Name = normalizedName
        };
        customShow.SlideIds.AddRange(NormalizeSlideIds(presentation, slideIds));
        presentation.CustomShows.Add(customShow);

        return SlideShowCustomShowMutationResult.Success(presentation.CustomShows.Count - 1, customShow);
    }

    public static SlideShowCustomShowMutationResult RenameCustomShow(
        Presentation presentation,
        int customShowIndex,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (!TryGetCustomShow(presentation, customShowIndex, out var customShow))
        {
            return SlideShowCustomShowMutationResult.Failure(MissingCustomShowMessage);
        }

        if (!TryNormalizeCustomShowName(presentation, name, customShowIndex, out var normalizedName, out var errorMessage))
        {
            return SlideShowCustomShowMutationResult.Failure(errorMessage);
        }

        customShow.Name = normalizedName;
        NormalizeCustomShowSlides(presentation, customShow);
        return SlideShowCustomShowMutationResult.Success(customShowIndex, customShow);
    }

    public static SlideShowCustomShowMutationResult DeleteCustomShow(
        Presentation presentation,
        int customShowIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (!TryGetCustomShow(presentation, customShowIndex, out var customShow))
        {
            return SlideShowCustomShowMutationResult.Failure(MissingCustomShowMessage);
        }

        presentation.CustomShows.RemoveAt(customShowIndex);
        return SlideShowCustomShowMutationResult.Success(customShowIndex, customShow);
    }

    public static SlideShowCustomShowMutationResult UpdateCustomShowSlides(
        Presentation presentation,
        int customShowIndex,
        IEnumerable<string?> slideIds)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slideIds);

        if (!TryGetCustomShow(presentation, customShowIndex, out var customShow))
        {
            return SlideShowCustomShowMutationResult.Failure(MissingCustomShowMessage);
        }

        customShow.SlideIds.Clear();
        customShow.SlideIds.AddRange(NormalizeSlideIds(presentation, slideIds));
        return SlideShowCustomShowMutationResult.Success(customShowIndex, customShow);
    }

    public static SlideShowCustomShowMutationResult MoveCustomShowSlide(
        Presentation presentation,
        int customShowIndex,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        if (!TryGetCustomShow(presentation, customShowIndex, out var customShow))
        {
            return SlideShowCustomShowMutationResult.Failure(MissingCustomShowMessage);
        }

        var normalizedSourceSlideId = string.IsNullOrWhiteSpace(sourceSlideId)
            ? string.Empty
            : sourceSlideId.Trim();
        if (string.IsNullOrEmpty(normalizedSourceSlideId) ||
            sourceSlideIndex < 0 ||
            sourceSlideIndex >= customShow.SlideIds.Count ||
            !string.Equals(customShow.SlideIds[sourceSlideIndex], normalizedSourceSlideId, StringComparison.Ordinal))
        {
            return SlideShowCustomShowMutationResult.Failure(MissingCustomShowSlideMessage);
        }

        var clampedTargetIndex = Math.Clamp(targetSlideIndex, 0, customShow.SlideIds.Count - 1);
        if (sourceSlideIndex == clampedTargetIndex)
        {
            return SlideShowCustomShowMutationResult.Success(
                customShowIndex,
                customShow,
                selectedSlideIndex: sourceSlideIndex);
        }

        customShow.SlideIds.RemoveAt(sourceSlideIndex);
        customShow.SlideIds.Insert(clampedTargetIndex, normalizedSourceSlideId);

        return SlideShowCustomShowMutationResult.Success(
            customShowIndex,
            customShow,
            selectedSlideIndex: clampedTargetIndex);
    }

    public static SlideShowCustomShowDragReorderPlan BuildCustomShowSlideDragReorderPlan(
        IReadOnlyList<string> slideIds,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetDropIndex)
    {
        ArgumentNullException.ThrowIfNull(slideIds);

        var normalizedSourceSlideId = string.IsNullOrWhiteSpace(sourceSlideId)
            ? string.Empty
            : sourceSlideId.Trim();
        if (string.IsNullOrEmpty(normalizedSourceSlideId) ||
            sourceSlideIndex < 0 ||
            sourceSlideIndex >= slideIds.Count ||
            !string.Equals(slideIds[sourceSlideIndex], normalizedSourceSlideId, StringComparison.Ordinal))
        {
            return new SlideShowCustomShowDragReorderPlan(
                IsValid: false,
                ShouldApplyMutation: false,
                SourceSlideIndex: sourceSlideIndex,
                SourceSlideId: normalizedSourceSlideId,
                TargetDropIndex: Math.Clamp(targetDropIndex, 0, slideIds.Count),
                TargetSlideIndex: -1,
                SelectedSlideIndex: NormalizeSelectedSlideIndex(sourceSlideIndex, slideIds.Count),
                SlideIds: slideIds.ToArray(),
                ErrorMessage: MissingCustomShowSlideMessage);
        }

        var clampedDropIndex = Math.Clamp(targetDropIndex, 0, slideIds.Count);
        var targetSlideIndex = clampedDropIndex > sourceSlideIndex
            ? clampedDropIndex - 1
            : clampedDropIndex;
        targetSlideIndex = Math.Clamp(targetSlideIndex, 0, slideIds.Count - 1);

        if (targetSlideIndex == sourceSlideIndex)
        {
            return new SlideShowCustomShowDragReorderPlan(
                IsValid: true,
                ShouldApplyMutation: false,
                SourceSlideIndex: sourceSlideIndex,
                SourceSlideId: normalizedSourceSlideId,
                TargetDropIndex: clampedDropIndex,
                TargetSlideIndex: targetSlideIndex,
                SelectedSlideIndex: sourceSlideIndex,
                SlideIds: slideIds.ToArray(),
                ErrorMessage: null);
        }

        var reorderedSlideIds = slideIds.ToList();
        reorderedSlideIds.RemoveAt(sourceSlideIndex);
        reorderedSlideIds.Insert(targetSlideIndex, normalizedSourceSlideId);

        return new SlideShowCustomShowDragReorderPlan(
            IsValid: true,
            ShouldApplyMutation: true,
            SourceSlideIndex: sourceSlideIndex,
            SourceSlideId: normalizedSourceSlideId,
            TargetDropIndex: clampedDropIndex,
            TargetSlideIndex: targetSlideIndex,
            SelectedSlideIndex: targetSlideIndex,
            SlideIds: reorderedSlideIds,
            ErrorMessage: null);
    }

    public static SlideShowLaunchPlan BuildLaunchPlan(
        Presentation presentation,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var hasSlides = presentation.Slides.Count > 0;
        var startIndex = hasSlides
            ? Math.Clamp(currentSlideIndex, 0, presentation.Slides.Count - 1)
            : 0;

        var choices = new List<SlideShowLaunchChoice>
        {
            new(
                FullPresentationChoiceId,
                "From Beginning",
                SlideShowLaunchChoiceKind.FullPresentation,
                presentation.Slides.Count,
                0,
                CustomShowName: null,
                IsEnabled: hasSlides,
                DisabledReason: hasSlides ? null : NoSlidesMessage),
            new(
                FromCurrentSlideChoiceId,
                "From Current Slide",
                SlideShowLaunchChoiceKind.FromCurrentSlide,
                presentation.Slides.Count,
                startIndex,
                CustomShowName: null,
                IsEnabled: hasSlides,
                DisabledReason: hasSlides ? null : NoSlidesMessage)
        };

        for (var index = 0; index < presentation.CustomShows.Count; index++)
        {
            var customShow = presentation.CustomShows[index];
            var route = BuildCustomShowRoute(presentation, customShow);
            choices.Add(new SlideShowLaunchChoice(
                CustomShowChoicePrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(customShow.Name)
                    ? $"Custom Show {index + 1}"
                    : customShow.Name.Trim(),
                SlideShowLaunchChoiceKind.CustomShow,
                route.SlideCount,
                route.StartIndex,
                route.CustomShowName,
                IsEnabled: route.SlideCount > 0,
                DisabledReason: route.SlideCount > 0 ? null : EmptyCustomShowMessage));
        }

        return new SlideShowLaunchPlan(
            presentation.Slides.Count,
            startIndex,
            choices);
    }

    public static bool TryBuildRouteForLaunchChoice(
        Presentation presentation,
        string? choiceId,
        int currentSlideIndex,
        out SlideShowPlaybackRoute route)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        route = BuildFullPresentationRoute(presentation);
        if (string.IsNullOrWhiteSpace(choiceId))
        {
            return false;
        }

        var normalizedChoiceId = choiceId.Trim();
        var plan = BuildLaunchPlan(presentation, currentSlideIndex);
        var choice = plan.Choices.FirstOrDefault(candidate =>
            string.Equals(candidate.ChoiceId, normalizedChoiceId, StringComparison.Ordinal));
        if (choice is null || !choice.IsEnabled)
        {
            return false;
        }

        route = choice.Kind switch
        {
            SlideShowLaunchChoiceKind.FullPresentation =>
                BuildFullPresentationRoute(presentation, 0),
            SlideShowLaunchChoiceKind.FromCurrentSlide =>
                BuildFullPresentationRoute(presentation, plan.CurrentSlideIndex),
            SlideShowLaunchChoiceKind.CustomShow when TryParseCustomShowIndex(choice.ChoiceId, out var customShowIndex) &&
                customShowIndex >= 0 &&
                customShowIndex < presentation.CustomShows.Count =>
                BuildCustomShowRoute(presentation, presentation.CustomShows[customShowIndex]),
            _ => route
        };

        return route.SlideCount > 0;
    }

    public static SlideShowPlaybackRoute BuildFullPresentationRoute(
        Presentation presentation,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var visibleSlides = presentation.Slides
            .Select((slide, sourceIndex) => (slide, sourceIndex))
            .Where(entry => !entry.slide.IsHidden)
            .ToArray();
        var visibleStartIndex = visibleSlides.Length == 0
            ? 0
            : Array.FindIndex(
                visibleSlides,
                entry => entry.sourceIndex >= Math.Clamp(startIndex, 0, presentation.Slides.Count - 1));

        if (visibleStartIndex < 0 && visibleSlides.Length > 0)
        {
            visibleStartIndex = visibleSlides.Length - 1;
        }

        return new SlideShowPlaybackRoute(
            customShowName: null,
            visibleSlides.Select(entry => entry.slide).ToArray(),
            visibleSlides.Select(entry => entry.sourceIndex).ToArray(),
            visibleStartIndex);
    }

    public static SlideShowPlaybackRoute BuildCustomShowRoute(
        Presentation presentation,
        SlideShowCustomSlideSequence customShow,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(customShow);
        ArgumentNullException.ThrowIfNull(customShow.SlideIds);

        var slides = new List<Slide>();
        var sourceSlideIndices = new List<int>();

        foreach (var slideId in customShow.SlideIds)
        {
            if (string.IsNullOrWhiteSpace(slideId))
            {
                continue;
            }

            var sourceIndex = FindSlideIndex(presentation.Slides, slideId);
            if (sourceIndex < 0)
            {
                continue;
            }

            slides.Add(presentation.Slides[sourceIndex]);
            sourceSlideIndices.Add(sourceIndex);
        }

        return new SlideShowPlaybackRoute(
            customShow.Name,
            slides,
            sourceSlideIndices,
            startIndex);
    }

    public static SlideShowPlaybackRoute BuildCustomShowRoute(
        Presentation presentation,
        PresentationCustomShow customShow,
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(customShow);

        return BuildCustomShowRoute(
            presentation,
            new SlideShowCustomSlideSequence(customShow.Name, customShow.SlideIds),
            startIndex);
    }

    public static bool TryBuildNamedCustomShowRoute(
        Presentation presentation,
        IEnumerable<SlideShowCustomSlideSequence> customShows,
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(customShows);

        route = BuildFullPresentationRoute(presentation, startIndex);
        if (string.IsNullOrWhiteSpace(customShowName))
        {
            return false;
        }

        var customShow = customShows.FirstOrDefault(show =>
            string.Equals(show.Name, customShowName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (customShow is null)
        {
            return false;
        }

        route = BuildCustomShowRoute(presentation, customShow, startIndex);
        return true;
    }

    public static bool TryBuildNamedCustomShowRoute(
        Presentation presentation,
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        route = BuildFullPresentationRoute(presentation, startIndex);
        if (string.IsNullOrWhiteSpace(customShowName))
        {
            return false;
        }

        var customShow = presentation.CustomShows.FirstOrDefault(show =>
            string.Equals(show.Name, customShowName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (customShow is null)
        {
            return false;
        }

        route = BuildCustomShowRoute(presentation, customShow, startIndex);
        return true;
    }

    private static int FindSlideIndex(IReadOnlyList<Slide> slides, string slideId)
    {
        for (var i = 0; i < slides.Count; i++)
        {
            if (string.Equals(slides[i].Id, slideId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetCustomShow(
        Presentation presentation,
        int customShowIndex,
        out PresentationCustomShow customShow)
    {
        if (customShowIndex >= 0 && customShowIndex < presentation.CustomShows.Count)
        {
            customShow = presentation.CustomShows[customShowIndex];
            return true;
        }

        customShow = null!;
        return false;
    }

    private static bool TryNormalizeCustomShowName(
        Presentation presentation,
        string? name,
        int? excludedCustomShowIndex,
        out string normalizedName,
        out string errorMessage)
    {
        normalizedName = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = EmptyCustomShowNameMessage;
            return false;
        }

        normalizedName = name.Trim();
        for (var index = 0; index < presentation.CustomShows.Count; index++)
        {
            if (excludedCustomShowIndex == index)
            {
                continue;
            }

            if (string.Equals(presentation.CustomShows[index].Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = DuplicateCustomShowNameMessage;
                return false;
            }
        }

        return true;
    }

    private static uint AllocateNextCustomShowId(Presentation presentation)
    {
        var usedIds = presentation.CustomShows
            .Select(show => show.Id)
            .Where(id => id > 0)
            .ToHashSet();

        for (uint candidate = 1; candidate < uint.MaxValue; candidate++)
        {
            if (!usedIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return uint.MaxValue;
    }

    private static void NormalizeCustomShowSlides(
        Presentation presentation,
        PresentationCustomShow customShow)
    {
        var normalizedSlideIds = NormalizeSlideIds(presentation, customShow.SlideIds);
        customShow.SlideIds.Clear();
        customShow.SlideIds.AddRange(normalizedSlideIds);
    }

    private static IReadOnlyList<string> NormalizeSlideIds(
        Presentation presentation,
        IEnumerable<string?> slideIds)
    {
        var validSlideIds = presentation.Slides
            .Select(slide => slide.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        return slideIds
            .Where(slideId => !string.IsNullOrWhiteSpace(slideId))
            .Select(slideId => slideId!.Trim())
            .Where(validSlideIds.Contains)
            .ToArray();
    }

    private static string NormalizeDisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Trim();

    private static int NormalizeSelectedSlideIndex(int selectedSlideIndex, int slideCount) =>
        slideCount == 0
            ? -1
            : Math.Clamp(selectedSlideIndex, 0, slideCount - 1);

    private static bool TryParseCustomShowIndex(string choiceId, out int index)
    {
        index = -1;
        if (!choiceId.StartsWith(CustomShowChoicePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            choiceId.AsSpan(CustomShowChoicePrefix.Length),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out index);
    }
}
