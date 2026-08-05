using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCustomShowSessionState(
    int SelectedCustomShowIndex = 0,
    int SelectedSlideIndex = -1);

public sealed record SlideShowCustomShowSessionShowItemPlan(
    int Index,
    string Name,
    int SlideCount,
    string DisplayText);

public sealed record SlideShowCustomShowSessionSlideItemPlan(
    int Index,
    string SlideId,
    string DisplayText);

public sealed record SlideShowCustomShowSessionPlan(
    IReadOnlyList<SlideShowCustomShowSessionShowItemPlan> CustomShows,
    IReadOnlyList<SlideShowCustomShowSlideOption> AvailableSlides,
    SlideShowCustomShowSummary? SelectedShow,
    IReadOnlyList<string> SelectedSlideIds,
    IReadOnlyList<SlideShowCustomShowSessionSlideItemPlan> SelectedSlides,
    int SelectedSlideIndex,
    bool CanRename,
    bool CanUpdateSlides,
    bool CanDelete,
    bool CanStart,
    bool CanMoveUp,
    bool CanMoveDown,
    bool CanRemove);

public static class SlideShowCustomShowSessionPlanner
{
    public static SlideShowCustomShowSessionPlan BuildPlan(
        SlideShowCustomShowAuthoringPlan authoringPlan,
        SlideShowCustomShowSessionState state)
    {
        ArgumentNullException.ThrowIfNull(authoringPlan);
        ArgumentNullException.ThrowIfNull(state);

        var customShows = authoringPlan.CustomShows
            .Select(show => new SlideShowCustomShowSessionShowItemPlan(
                show.Index,
                show.Name,
                show.SlideIds.Count,
                FormatShowListText(show)))
            .ToArray();

        var selectedShow = ResolveSelectedShow(authoringPlan.CustomShows, state.SelectedCustomShowIndex);
        var selectedSlideIds = selectedShow?.SlideIds.ToArray() ?? Array.Empty<string>();
        var selectedSlideIndex = selectedSlideIds.Length == 0
            ? -1
            : state.SelectedSlideIndex < 0
                ? 0
                : Math.Clamp(state.SelectedSlideIndex, 0, selectedSlideIds.Length - 1);
        var titleBySlideId = authoringPlan.AvailableSlides.ToDictionary(
            slide => slide.SlideId,
            slide => $"Slide {slide.Index + 1}: {slide.Title}",
            StringComparer.Ordinal);
        var selectedSlides = selectedSlideIds
            .Select((slideId, index) => new SlideShowCustomShowSessionSlideItemPlan(
                index,
                slideId,
                titleBySlideId.TryGetValue(slideId, out var title)
                    ? title
                    : $"Missing slide: {slideId}"))
            .ToArray();

        var hasSelection = selectedShow is not null;
        return new SlideShowCustomShowSessionPlan(
            customShows,
            authoringPlan.AvailableSlides,
            selectedShow,
            selectedSlideIds,
            selectedSlides,
            selectedSlideIndex,
            hasSelection,
            hasSelection,
            hasSelection,
            selectedShow?.SlideIds.Count > 0,
            selectedSlideIndex > 0,
            selectedSlideIndex >= 0 && selectedSlideIndex < selectedSlides.Length - 1,
            selectedSlideIndex >= 0);
    }

    public static SlideShowCustomShowSessionState SelectShow(int customShowIndex) =>
        new(customShowIndex, -1);

    public static SlideShowCustomShowDragReorderPlan BuildDragReorderPlan(
        SlideShowCustomShowSessionPlan session,
        int sourceSlideIndex,
        int targetDropIndex)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.SelectedShow is null)
        {
            return new SlideShowCustomShowDragReorderPlan(
                IsValid: false,
                ShouldApplyMutation: false,
                SourceSlideIndex: sourceSlideIndex,
                SourceSlideId: string.Empty,
                TargetDropIndex: targetDropIndex,
                TargetSlideIndex: -1,
                SelectedSlideIndex: -1,
                SlideIds: Array.Empty<string>(),
                ErrorMessage: SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        var sourceSlideId = sourceSlideIndex >= 0 && sourceSlideIndex < session.SelectedSlides.Count
            ? session.SelectedSlides[sourceSlideIndex].SlideId
            : string.Empty;
        return SlideShowCustomShowPlanner.BuildCustomShowSlideDragReorderPlan(
            session.SelectedSlides.Select(slide => slide.SlideId).ToArray(),
            sourceSlideIndex,
            sourceSlideId,
            targetDropIndex);
    }

    private static SlideShowCustomShowSummary? ResolveSelectedShow(
        IReadOnlyList<SlideShowCustomShowSummary> customShows,
        int selectedCustomShowIndex)
    {
        if (customShows.Count == 0)
        {
            return null;
        }

        var normalizedIndex = selectedCustomShowIndex >= 0 && selectedCustomShowIndex < customShows.Count
            ? selectedCustomShowIndex
            : 0;
        return customShows[normalizedIndex];
    }

    private static string FormatShowListText(SlideShowCustomShowSummary show)
    {
        var name = string.IsNullOrWhiteSpace(show.Name)
            ? $"Custom Show {show.Index + 1}"
            : show.Name;
        var slideLabel = show.SlideIds.Count == 1 ? "slide" : "slides";
        return $"{name} ({show.SlideIds.Count} {slideLabel})";
    }
}
