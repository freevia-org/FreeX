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
    int SectionSlideCount = 0,
    int SectionIndex = -1,
    string SectionId = "",
    bool IsSectionCollapsed = false);

public sealed record SlidePaneThumbnailVisualPlan(
    int SlideIndex,
    string LabelText,
    string TitleText,
    int ShapeCount,
    bool IsSelected,
    double ThumbnailWidth,
    double ThumbnailHeight,
    double LabelHeight,
    double ItemPadding,
    double ItemHeight,
    double ItemCornerRadius,
    double NormalBorderThickness,
    double SelectedBorderThickness,
    string PaneBackgroundHex,
    string ItemNormalBackgroundHex,
    string ItemSelectedBackgroundHex,
    string ItemHoverBackgroundHex,
    string ItemNormalBorderHex,
    string ItemSelectedBorderHex,
    string ThumbnailBorderHex,
    string LabelForegroundHex,
    string AccessibleName,
    string ToolTipText);

public sealed record SlidePaneSectionHeaderVisualPlan(
    int SlideIndex,
    int SectionIndex,
    string SectionId,
    string LabelText,
    int SlideCount,
    bool IsCollapsed,
    double HeaderHeight,
    double DisclosureWidth,
    double FontSize,
    double HorizontalPadding,
    double VerticalPadding,
    double TopMargin,
    double BottomMargin,
    double CornerRadius,
    string DisclosureText,
    string BackgroundHex,
    string HoverBackgroundHex,
    string ForegroundHex,
    string AccessibleName,
    string ToolTipText);

public sealed record SlidePaneDropVisualPlan(
    int SourceSlideIndex,
    int TargetSlideIndex,
    bool IsTargetValid,
    bool IsMoveEnabled,
    bool IsVisible,
    double IndicatorOffset,
    double IndicatorTopMargin,
    double IndicatorThickness,
    double HorizontalInset,
    string AccentColorHex,
    string AutomationDescription);

public enum SlidePaneActionKind
{
    InsertAfterSlide,
    DuplicateSlide,
    DeleteSlide,
    MoveSlide,
}

public enum SlidePaneKeyboardIntentKind
{
    None,
    InsertAfterCurrentSlide,
    DuplicateCurrentSlide,
    DeleteCurrentSlide,
    MoveCurrentSlideEarlier,
    MoveCurrentSlideLater,
}

public sealed record SlidePaneActionPlan(
    SlidePaneActionKind Kind,
    string Text,
    int SourceSlideIndex,
    int TargetSlideIndex,
    bool IsEnabled);

public static class SlidePanePlanner
{
    public const string NewSlideButtonText = "+ New Slide";
    public const string NewSlideMenuText = "New Slide";
    public const string DuplicateSlideMenuText = "Duplicate Slide";
    public const string DeleteSlideMenuText = "Delete Slide";
    public const double DefaultThumbnailWidth = 150.0;
    public const double DefaultThumbnailHeight = DefaultThumbnailWidth * 9.0 / 16.0;
    public const double DefaultItemPadding = 8.0;
    public const double DefaultLabelHeight = 16.0;
    public const double DefaultSlideItemHeight = 4 + DefaultItemPadding + DefaultLabelHeight + 4 + DefaultThumbnailHeight + DefaultItemPadding + 4;
    public const double DefaultSectionHeaderHeight = 30.0;
    public const double DefaultDragStartThreshold = 5.0;
    public const double DefaultItemCornerRadius = 3.0;
    public const double DefaultNormalBorderThickness = 1.0;
    public const double DefaultSelectedBorderThickness = 2.0;
    public const string DefaultPaneBackgroundHex = "#E0E0E0";
    public const string DefaultItemNormalBackgroundHex = "#F5F5F5";
    public const string DefaultItemSelectedBackgroundHex = "#FFE0D6";
    public const string DefaultItemHoverBackgroundHex = "#EBEBEB";
    public const string DefaultItemNormalBorderHex = "#CCCCCC";
    public const string DefaultItemSelectedBorderHex = "#B7472A";
    public const string DefaultThumbnailBorderHex = "#CCCCCC";
    public const string DefaultLabelForegroundHex = "#444444";
    public const double DefaultSectionHeaderDisclosureWidth = 14.0;
    public const double DefaultSectionHeaderFontSize = 11.0;
    public const double DefaultSectionHeaderHorizontalPadding = 10.0;
    public const double DefaultSectionHeaderVerticalPadding = 4.0;
    public const double DefaultSectionHeaderTopMargin = 6.0;
    public const double DefaultSectionHeaderBottomMargin = 2.0;
    public const double DefaultSectionHeaderCornerRadius = 2.0;
    public const string DefaultSectionHeaderBackgroundHex = "#C8C8C8";
    public const string DefaultSectionHeaderHoverBackgroundHex = "#D6D6D6";
    public const string DefaultSectionHeaderForegroundHex = "#333333";
    public const string DefaultSectionHeaderExpandedDisclosureText = "v";
    public const string DefaultSectionHeaderCollapsedDisclosureText = ">";
    public const double DefaultDropIndicatorThickness = 2.0;
    public const double DefaultDropIndicatorHorizontalInset = 0.0;
    public const string DefaultDropIndicatorAccentHex = "#B7472A";

    public static IReadOnlyList<SlidePaneEntry> BuildEntries(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        IReadOnlySet<string>? collapsedSectionIds = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        var sectionHeaders = BuildSectionHeaders(slides, sections, collapsedSectionIds);
        var collapsedSlideIds = BuildCollapsedSlideIds(sections, collapsedSectionIds);
        var entries = new List<SlidePaneEntry>(slides.Count + sectionHeaders.Count);

        for (var i = 0; i < slides.Count; i++)
        {
            if (sectionHeaders.TryGetValue(i, out var header))
                entries.Add(header);

            if (collapsedSlideIds.Contains(slides[i].Id))
                continue;

            entries.Add(new SlidePaneEntry(
                SlidePaneEntryKind.Slide,
                SlideIndex: i,
                Text: FormatSlideNumber(i)));
        }

        return entries;
    }

    public static string FormatSlideNumber(int slideIndex) =>
        (slideIndex + 1).ToString(CultureInfo.InvariantCulture);

    public static SlidePaneThumbnailVisualPlan BuildThumbnailVisualPlan(
        SlidePaneEntry entry,
        Slide slide,
        int currentSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(slide);

        if (entry.Kind != SlidePaneEntryKind.Slide)
            throw new ArgumentException("Only slide entries can be projected as thumbnail visual plans.", nameof(entry));

        var title = FormatSlideTitle(slide);
        var objectText = FormatShapeCount(slide.Shapes.Count);
        var accessibleName = $"Slide {entry.Text}: {title}, {objectText}";

        return new SlidePaneThumbnailVisualPlan(
            entry.SlideIndex,
            entry.Text,
            title,
            slide.Shapes.Count,
            entry.SlideIndex == currentSlideIndex,
            DefaultThumbnailWidth,
            DefaultThumbnailHeight,
            DefaultLabelHeight,
            DefaultItemPadding,
            DefaultSlideItemHeight,
            DefaultItemCornerRadius,
            DefaultNormalBorderThickness,
            DefaultSelectedBorderThickness,
            DefaultPaneBackgroundHex,
            DefaultItemNormalBackgroundHex,
            DefaultItemSelectedBackgroundHex,
            DefaultItemHoverBackgroundHex,
            DefaultItemNormalBorderHex,
            DefaultItemSelectedBorderHex,
            DefaultThumbnailBorderHex,
            DefaultLabelForegroundHex,
            accessibleName,
            accessibleName);
    }

    public static SlidePaneSectionHeaderVisualPlan BuildSectionHeaderVisualPlan(SlidePaneEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Kind != SlidePaneEntryKind.SectionHeader)
            throw new ArgumentException("Only section-header entries can be projected as section-header visual plans.", nameof(entry));

        var state = entry.IsSectionCollapsed ? "collapsed" : "expanded";
        var action = entry.IsSectionCollapsed ? "Expand section" : "Collapse section";
        var accessibleName = $"Section {entry.Text}, {state}";

        return new SlidePaneSectionHeaderVisualPlan(
            entry.SlideIndex,
            entry.SectionIndex,
            entry.SectionId,
            entry.Text,
            entry.SectionSlideCount,
            entry.IsSectionCollapsed,
            DefaultSectionHeaderHeight,
            DefaultSectionHeaderDisclosureWidth,
            DefaultSectionHeaderFontSize,
            DefaultSectionHeaderHorizontalPadding,
            DefaultSectionHeaderVerticalPadding,
            DefaultSectionHeaderTopMargin,
            DefaultSectionHeaderBottomMargin,
            DefaultSectionHeaderCornerRadius,
            entry.IsSectionCollapsed
                ? DefaultSectionHeaderCollapsedDisclosureText
                : DefaultSectionHeaderExpandedDisclosureText,
            DefaultSectionHeaderBackgroundHex,
            DefaultSectionHeaderHoverBackgroundHex,
            DefaultSectionHeaderForegroundHex,
            accessibleName,
            action);
    }

    public static IReadOnlyList<SlidePaneActionPlan> BuildContextActions(
        int slideCount,
        int slideIndex)
    {
        var hasTargetSlide = IsValidSlideIndex(slideCount, slideIndex);
        return
        [
            new SlidePaneActionPlan(
                SlidePaneActionKind.InsertAfterSlide,
                NewSlideMenuText,
                slideIndex,
                slideIndex + 1,
                hasTargetSlide),
            new SlidePaneActionPlan(
                SlidePaneActionKind.DuplicateSlide,
                DuplicateSlideMenuText,
                slideIndex,
                slideIndex + 1,
                hasTargetSlide),
            new SlidePaneActionPlan(
                SlidePaneActionKind.DeleteSlide,
                DeleteSlideMenuText,
                slideIndex,
                slideIndex,
                hasTargetSlide && slideCount > 1),
        ];
    }

    public static SlidePaneActionPlan PlanMoveAction(
        int slideCount,
        int sourceSlideIndex,
        int targetInsertionIndex)
    {
        var canMove = IsValidSlideIndex(slideCount, sourceSlideIndex)
            && targetInsertionIndex >= 0
            && targetInsertionIndex <= slideCount
            && targetInsertionIndex != sourceSlideIndex
            && targetInsertionIndex != sourceSlideIndex + 1;

        return new SlidePaneActionPlan(
            SlidePaneActionKind.MoveSlide,
            "Move Slide",
            sourceSlideIndex,
            targetInsertionIndex,
            canMove);
    }

    public static SlidePaneActionPlan BuildKeyboardAction(
        int slideCount,
        int currentSlideIndex,
        SlidePaneKeyboardIntentKind intent)
    {
        var contextActions = BuildContextActions(slideCount, currentSlideIndex);
        return intent switch
        {
            SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide => contextActions
                .First(action => action.Kind == SlidePaneActionKind.InsertAfterSlide),
            SlidePaneKeyboardIntentKind.DuplicateCurrentSlide => contextActions
                .First(action => action.Kind == SlidePaneActionKind.DuplicateSlide),
            SlidePaneKeyboardIntentKind.DeleteCurrentSlide => contextActions
                .First(action => action.Kind == SlidePaneActionKind.DeleteSlide),
            SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier => PlanMoveAction(
                slideCount,
                currentSlideIndex,
                currentSlideIndex - 1),
            SlidePaneKeyboardIntentKind.MoveCurrentSlideLater => PlanMoveAction(
                slideCount,
                currentSlideIndex,
                currentSlideIndex + 2),
            _ => new SlidePaneActionPlan(
                SlidePaneActionKind.MoveSlide,
                "Move Slide",
                currentSlideIndex,
                currentSlideIndex,
                false),
        };
    }

    public static bool TryApplyAction(EditingSession editor, SlidePaneActionPlan action)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(action);

        if (!action.IsEnabled)
            return false;

        switch (action.Kind)
        {
            case SlidePaneActionKind.InsertAfterSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                editor.InsertSlide();
                return true;

            case SlidePaneActionKind.DuplicateSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                editor.DuplicateCurrentSlide();
                return true;

            case SlidePaneActionKind.DeleteSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                editor.DeleteCurrentSlide();
                return true;

            case SlidePaneActionKind.MoveSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                editor.MoveSlide(action.SourceSlideIndex, action.TargetSlideIndex);
                return true;

            default:
                return false;
        }
    }

    public static string FormatSectionHeader(string name, int slideCount) =>
        slideCount > 0 ? $"{name}  ({slideCount})" : name;

    public static string GetSectionIdentity(PresentationSection section, int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (!string.IsNullOrWhiteSpace(section.Id))
            return section.Id.Trim();

        if (!string.IsNullOrWhiteSpace(section.Name))
            return section.Name.Trim();

        return sectionIndex.ToString(CultureInfo.InvariantCulture);
    }

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

    public static SlidePaneDropVisualPlan BuildDropVisualPlan(
        IReadOnlyList<bool> paneItemIsSlide,
        int sourceSlideIndex,
        int targetSlideIndex,
        double slideItemHeight,
        double nonSlideItemHeight = DefaultSectionHeaderHeight)
    {
        ArgumentNullException.ThrowIfNull(paneItemIsSlide);

        var slideCount = paneItemIsSlide.Count(isSlide => isSlide);
        var isSourceValid = IsValidSlideIndex(slideCount, sourceSlideIndex);
        var isTargetValid = targetSlideIndex >= 0 && targetSlideIndex <= slideCount;
        var offset = isTargetValid
            ? ComputeInsertionIndicatorOffset(
                paneItemIsSlide,
                targetSlideIndex,
                slideItemHeight,
                nonSlideItemHeight)
            : 0.0;
        var moveAction = PlanMoveAction(slideCount, sourceSlideIndex, targetSlideIndex);
        var description = isTargetValid
            ? $"Move slide {sourceSlideIndex + 1} to position {targetSlideIndex + 1}"
            : "No slide drop target";

        return new SlidePaneDropVisualPlan(
            sourceSlideIndex,
            targetSlideIndex,
            isTargetValid,
            moveAction.IsEnabled,
            isSourceValid && isTargetValid,
            offset,
            offset - DefaultDropIndicatorThickness * 0.5,
            DefaultDropIndicatorThickness,
            DefaultDropIndicatorHorizontalInset,
            DefaultDropIndicatorAccentHex,
            description);
    }

    private static Dictionary<int, SlidePaneEntry> BuildSectionHeaders(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        IReadOnlySet<string>? collapsedSectionIds)
    {
        var headers = new Dictionary<int, SlidePaneEntry>();
        if (sections.Count == 0)
            return headers;

        var slideIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < slides.Count; i++)
            slideIndexById[slides[i].Id] = i;

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var sectionId = GetSectionIdentity(section, sectionIndex);
            var firstIndex = FindFirstSectionSlideIndex(section, slideIndexById);
            if (firstIndex < 0 || headers.ContainsKey(firstIndex))
                continue;

            var count = CountKnownSectionSlides(section, slideIndexById);
            var isCollapsed = IsSectionCollapsed(sectionId, collapsedSectionIds);
            headers[firstIndex] = new SlidePaneEntry(
                SlidePaneEntryKind.SectionHeader,
                SlideIndex: firstIndex,
                Text: FormatSectionHeader(section.Name, count),
                SectionSlideCount: count,
                SectionIndex: sectionIndex,
                SectionId: sectionId,
                IsSectionCollapsed: isCollapsed);
        }

        return headers;
    }

    private static HashSet<string> BuildCollapsedSlideIds(
        IReadOnlyList<PresentationSection> sections,
        IReadOnlySet<string>? collapsedSectionIds)
    {
        var slideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (collapsedSectionIds is null || collapsedSectionIds.Count == 0)
            return slideIds;

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (!IsSectionCollapsed(GetSectionIdentity(section, sectionIndex), collapsedSectionIds))
                continue;

            foreach (var slideId in section.SlideIds)
                slideIds.Add(slideId);
        }

        return slideIds;
    }

    private static bool IsSectionCollapsed(
        string sectionId,
        IReadOnlySet<string>? collapsedSectionIds) =>
        collapsedSectionIds is not null &&
        collapsedSectionIds.Any(id => string.Equals(id, sectionId, StringComparison.OrdinalIgnoreCase));

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

    private static bool IsValidSlideIndex(int slideCount, int slideIndex) =>
        slideCount > 0 && slideIndex >= 0 && slideIndex < slideCount;

    private static string FormatSlideTitle(Slide slide)
    {
        var title = slide.Title.Trim();
        return title.Length == 0 ? "Untitled slide" : title;
    }

    private static string FormatShapeCount(int shapeCount) =>
        shapeCount == 1
            ? "1 object"
            : shapeCount.ToString(CultureInfo.InvariantCulture) + " objects";
}
