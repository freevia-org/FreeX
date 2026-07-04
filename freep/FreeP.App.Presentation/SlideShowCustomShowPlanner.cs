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

public sealed class SlideShowPlaybackRoute
{
    public SlideShowPlaybackRoute(
        string? customShowName,
        IReadOnlyList<Slide> slides,
        IReadOnlyList<int> sourceSlideIndices,
        int startIndex)
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
    }

    public string? CustomShowName { get; }

    public IReadOnlyList<Slide> Slides { get; }

    public IReadOnlyList<int> SourceSlideIndices { get; }

    public int StartIndex { get; }

    public int SlideCount => Slides.Count;

    public int GetSourceSlideIndex(int playbackSlideIndex) =>
        playbackSlideIndex >= 0 && playbackSlideIndex < SourceSlideIndices.Count
            ? SourceSlideIndices[playbackSlideIndex]
            : -1;
}

public static class SlideShowCustomShowPlanner
{
    public const string FullPresentationChoiceId = "full-presentation";
    public const string FromCurrentSlideChoiceId = "from-current-slide";
    public const string CustomShowChoicePrefix = "custom-show:";
    public const string NoSlidesMessage = "No slides are available for slide show playback.";
    public const string EmptyCustomShowMessage = "Custom show has no available slides.";

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

        return new SlideShowPlaybackRoute(
            customShowName: null,
            presentation.Slides.ToArray(),
            Enumerable.Range(0, presentation.Slides.Count).ToArray(),
            startIndex);
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
