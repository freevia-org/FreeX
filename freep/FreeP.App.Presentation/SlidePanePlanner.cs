using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlidePaneEntryKind
{
    SectionHeader,
    Slide
}

public sealed record SlidePaneEntry(
    SlidePaneEntryKind Kind,
    int SlideIndex,
    string Text,
    int SectionSlideCount = 0);

public static class SlidePanePlanner
{
    public const string NewSlideButtonText = "+ New Slide";
    public const string NewSlideMenuText = "New Slide";
    public const string DuplicateSlideMenuText = "Duplicate Slide";
    public const string DeleteSlideMenuText = "Delete Slide";
    public const double DefaultSectionHeaderHeight = 30.0;

    public static IReadOnlyList<SlidePaneEntry> BuildEntries(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        var sectionHeaders = BuildSectionHeaders(slides, sections);
        var entries = new List<SlidePaneEntry>(slides.Count + sectionHeaders.Count);

        for (var i = 0; i < slides.Count; i++)
        {
            if (sectionHeaders.TryGetValue(i, out var header))
                entries.Add(header);

            entries.Add(new SlidePaneEntry(
                SlidePaneEntryKind.Slide,
                SlideIndex: i,
                Text: FormatSlideNumber(i)));
        }

        return entries;
    }

    public static string FormatSlideNumber(int slideIndex) =>
        (slideIndex + 1).ToString(CultureInfo.InvariantCulture);

    public static string FormatSectionHeader(string name, int slideCount) =>
        slideCount > 0 ? $"{name}  ({slideCount})" : name;

    public static int HitTestInsertionPoint(
        IReadOnlyList<bool> paneItemIsSlide,
        double y,
        double slideItemHeight,
        double nonSlideItemHeight = DefaultSectionHeaderHeight)
    {
        ArgumentNullException.ThrowIfNull(paneItemIsSlide);

        var slideIndex = 0;
        var runningY = 0.0;
        foreach (var isSlide in paneItemIsSlide)
        {
            if (isSlide)
            {
                var midY = runningY + slideItemHeight * 0.5;
                if (y < midY)
                    return slideIndex;

                runningY += slideItemHeight;
                slideIndex++;
            }
            else
            {
                runningY += nonSlideItemHeight;
            }
        }

        return slideIndex;
    }

    public static double ComputeInsertionIndicatorOffset(
        IReadOnlyList<bool> paneItemIsSlide,
        int targetSlideIndex,
        double slideItemHeight,
        double nonSlideItemHeight = DefaultSectionHeaderHeight)
    {
        ArgumentNullException.ThrowIfNull(paneItemIsSlide);

        var slideIndex = 0;
        var offset = 0.0;
        foreach (var isSlide in paneItemIsSlide)
        {
            if (slideIndex >= targetSlideIndex)
                break;

            if (isSlide)
            {
                offset += slideItemHeight;
                slideIndex++;
            }
            else
            {
                offset += nonSlideItemHeight;
            }
        }

        return offset;
    }

    private static Dictionary<int, SlidePaneEntry> BuildSectionHeaders(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections)
    {
        var headers = new Dictionary<int, SlidePaneEntry>();
        if (sections.Count == 0)
            return headers;

        var slideIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < slides.Count; i++)
            slideIndexById[slides[i].Id] = i;

        foreach (var section in sections)
        {
            var firstIndex = FindFirstSectionSlideIndex(section, slideIndexById);
            if (firstIndex < 0 || headers.ContainsKey(firstIndex))
                continue;

            var count = CountKnownSectionSlides(section, slideIndexById);
            headers[firstIndex] = new SlidePaneEntry(
                SlidePaneEntryKind.SectionHeader,
                SlideIndex: firstIndex,
                Text: FormatSectionHeader(section.Name, count),
                SectionSlideCount: count);
        }

        return headers;
    }

    private static int FindFirstSectionSlideIndex(
        PresentationSection section,
        IReadOnlyDictionary<string, int> slideIndexById)
    {
        var firstIndex = -1;
        foreach (var slideId in section.SlideIds)
        {
            if (slideIndexById.TryGetValue(slideId, out var index) &&
                (firstIndex < 0 || index < firstIndex))
            {
                firstIndex = index;
            }
        }

        return firstIndex;
    }

    private static int CountKnownSectionSlides(
        PresentationSection section,
        IReadOnlyDictionary<string, int> slideIndexById)
    {
        var count = 0;
        foreach (var slideId in section.SlideIds)
        {
            if (slideIndexById.ContainsKey(slideId))
                count++;
        }

        return count;
    }
}
