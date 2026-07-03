using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCustomSlideSequence(
    string Name,
    IReadOnlyList<string> SlideIds);

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
}
