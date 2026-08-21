using System.Globalization;
using Free.Shared.Theme;
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
    bool IsActive,
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
    double LabelFontSize,
    double LabelBottomMargin,
    double ThumbnailBorderThickness,
    double ItemMarginHorizontal,
    double ItemMarginVertical,
    bool CenterThumbnailContent,
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

public sealed record SlidePaneBottomAffordancePlan(
    string Text,
    string ToolTipText,
    string AccessibleName,
    bool IsVisible,
    SlidePaneActionPlan Action);

public sealed record SlidePaneDragSessionState(
    bool IsTracking,
    bool IsDragging,
    int SourceSlideIndex,
    int TargetSlideIndex,
    double StartPointerY)
{
    public static SlidePaneDragSessionState None { get; } = new(false, false, -1, -1, 0.0);
}

public sealed record SlidePaneDragUpdatePlan(
    SlidePaneDragSessionState State,
    SlidePaneDropVisualPlan DropVisualPlan,
    bool ShouldCapturePointer);

public sealed record SlidePaneDragCompletionPlan(
    SlidePaneDragSessionState State,
    SlidePaneActionPlan Action,
    bool ShouldReleaseCapture);

public sealed record SlidePaneSessionState(
    IReadOnlySet<string> CollapsedSectionIds,
    int SelectedSlideIndex,
    SlidePaneDragSessionState DragSession)
{
    public static SlidePaneSessionState Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        -1,
        SlidePaneDragSessionState.None);
}

public sealed record SlidePaneSessionProjection(
    SlidePaneSessionState State,
    IReadOnlyList<SlidePaneEntry> Entries,
    IReadOnlyList<bool> PaneItemIsSlide)
{
    public IReadOnlyList<SlidePaneEntry> PaneEntries => Entries;

    public int SelectedSlideIndex => State.SelectedSlideIndex;

    public SlidePaneDragSessionState DragSession => State.DragSession;
}

public enum SlidePaneActionKind
{
    InsertAfterSlide,
    DuplicateSlide,
    DeleteSlide,
    ToggleHiddenSlide,
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
    bool IsEnabled,
    bool IsChecked = false);

public sealed record SlidePaneContextCommandRoutePlan(
    FreePContextMenuCommand Command,
    SlidePaneActionPlan? SlideAction,
    SlideSectionActionExecutionPlan? SectionExecution)
{
    public bool IsEnabled => SlideAction?.IsEnabled == true || SectionExecution?.IsEnabled == true;
}

public static class SlidePanePlanner
{
    public const string NewSlideButtonText = "+ New Slide";
    public const string NewSlideMenuText = "New Slide";
    public const string DuplicateSlideMenuText = "Duplicate Slide";
    public const string DeleteSlideMenuText = "Delete Slide";
    public const string HideSlideMenuText = "Hide Slide";
    public const string ShowSlideMenuText = "Show Slide";
    public const double DefaultThumbnailWidth = 150.0;
    public const double DefaultThumbnailHeight = DefaultThumbnailWidth * 9.0 / 16.0;
    public const double DefaultItemPadding = 8.0;
    public const double DefaultLabelHeight = 16.0;
    public const double DefaultLabelFontSize = 11.0;
    public const double DefaultLabelBottomMargin = 4.0;
    public const double DefaultThumbnailBorderThickness = 1.0;
    public const double DefaultItemMarginHorizontal = 6.0;
    public const double DefaultItemMarginVertical = 4.0;
    public const bool DefaultCenterThumbnailContent = true;
    public const double DefaultSlideItemHeight = 4 + DefaultItemPadding + DefaultLabelHeight + 4 + DefaultThumbnailHeight + DefaultItemPadding + 4;
    public const double DefaultSectionHeaderHeight = 30.0;
    public const double DefaultDragStartThreshold = 5.0;
    public const double DefaultItemCornerRadius = 3.0;
    public const double DefaultNormalBorderThickness = 1.0;
    public const double DefaultSelectedBorderThickness = 2.0;
    public const string DefaultPaneBackgroundHex = "#E0E0E0";
    public const string DefaultItemNormalBackgroundHex = "#F5F5F5";
    public static readonly string DefaultItemSelectedBackgroundHex = BrandThemes.FreeP.Colors.AccentSoft.ToHex();
    public const string DefaultItemHoverBackgroundHex = "#EBEBEB";
    public const string DefaultItemNormalBorderHex = "#CCCCCC";
    public static readonly string DefaultItemSelectedBorderHex = BrandThemes.FreeP.Colors.Accent.ToHex();
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
    public static readonly string DefaultDropIndicatorAccentHex = BrandThemes.FreeP.Colors.Accent.ToHex();

    public static SlidePaneSessionProjection BuildSessionProjection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        SlidePaneSessionState? state = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        state ??= SlidePaneSessionState.Empty;
        var entries = BuildEntries(slides, sections, state.CollapsedSectionIds);
        var paneItemIsSlide = entries
            .Select(entry => entry.Kind == SlidePaneEntryKind.Slide)
            .ToArray();

        return new SlidePaneSessionProjection(state, entries, paneItemIsSlide);
    }

    public static SlidePaneSessionProjection BuildProjection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        SlidePaneSessionState? state = null) =>
        BuildSessionProjection(slides, sections, state);

    public static SlidePaneSessionState SetSelectedSlide(
        SlidePaneSessionState state,
        int selectedSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with { SelectedSlideIndex = selectedSlideIndex };
    }

    public static SlidePaneSessionState ToggleSection(
        SlidePaneSessionState state,
        string sectionId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(sectionId))
            return state;

        var collapsedSectionIds = new HashSet<string>(
            state.CollapsedSectionIds,
            StringComparer.OrdinalIgnoreCase);
        if (!collapsedSectionIds.Add(sectionId))
            collapsedSectionIds.Remove(sectionId);

        return state with { CollapsedSectionIds = collapsedSectionIds };
    }

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
            if (sectionHeaders.TryGetValue(i, out var headersAtIndex))
                entries.AddRange(headersAtIndex);

            if (collapsedSlideIds.Contains(slides[i].Id))
                continue;

            entries.Add(new SlidePaneEntry(
                SlidePaneEntryKind.Slide,
                SlideIndex: i,
                Text: FormatSlideNumber(i)));
        }

        // Sections left with no member slide (e.g. its last slide was just
        // dragged elsewhere) have nothing to anchor before -- render their
        // header after the last slide so it stays visible/renameable/removable
        // instead of vanishing from the pane (PowerPoint keeps it visible too).
        if (sectionHeaders.TryGetValue(slides.Count, out var trailingHeaders))
            entries.AddRange(trailingHeaders);

        return entries;
    }

    public static string FormatSlideNumber(int slideIndex) =>
        (slideIndex + 1).ToString(CultureInfo.InvariantCulture);

    public static SlidePaneThumbnailVisualPlan BuildThumbnailVisualPlan(
        SlidePaneEntry entry,
        Slide slide,
        int currentSlideIndex)
        => BuildThumbnailVisualPlan(
            entry,
            slide,
            currentSlideIndex,
            entry.SlideIndex == currentSlideIndex);

    public static SlidePaneThumbnailVisualPlan BuildThumbnailVisualPlan(
        SlidePaneEntry entry,
        Slide slide,
        int activeSlideIndex,
        bool isSelected)
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
            isSelected,
            entry.SlideIndex == activeSlideIndex,
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
            DefaultLabelFontSize,
            DefaultLabelBottomMargin,
            DefaultThumbnailBorderThickness,
            DefaultItemMarginHorizontal,
            DefaultItemMarginVertical,
            DefaultCenterThumbnailContent,
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

    public static SlidePaneActionPlan BuildHiddenSlideAction(
        IReadOnlyList<Slide> slides,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);

        var isValid = IsValidSlideIndex(slides.Count, slideIndex);
        var isHidden = isValid && slides[slideIndex].IsHidden;
        return new SlidePaneActionPlan(
            SlidePaneActionKind.ToggleHiddenSlide,
            isHidden ? ShowSlideMenuText : HideSlideMenuText,
            slideIndex,
            slideIndex,
            isValid,
            isHidden);
    }

    public static SlidePaneContextCommandRoutePlan BuildContextCommandRoute(
        FreePContextMenuCommand command,
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        int slideIndex,
        int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(slides);
        ArgumentNullException.ThrowIfNull(sections);

        SlidePaneActionPlan SlideAction(SlidePaneActionKind kind) =>
            kind == SlidePaneActionKind.ToggleHiddenSlide
                ? BuildHiddenSlideAction(slides, slideIndex)
                : BuildContextActions(slides.Count, slideIndex)
                    .Single(action => action.Kind == kind);

        SlideSectionActionExecutionPlan SectionAction(SlideSectionActionKind kind)
        {
            var action = kind == SlideSectionActionKind.AddSection
                ? SlideSectionPlanner.BuildSlideContextActions(slides, sections, slideIndex)
                    .Single(candidate => candidate.Kind == kind)
                : SlideSectionPlanner.BuildSectionHeaderActions(sections, sectionIndex, slideIndex)
                    .Single(candidate => candidate.Kind == kind);
            return SlideSectionPlanner.BuildExecutionPlan(action);
        }

        return command switch
        {
            FreePContextMenuCommand.NewSlide => SlideRoute(SlidePaneActionKind.InsertAfterSlide),
            FreePContextMenuCommand.DuplicateSlide => SlideRoute(SlidePaneActionKind.DuplicateSlide),
            FreePContextMenuCommand.DeleteSlide => SlideRoute(SlidePaneActionKind.DeleteSlide),
            FreePContextMenuCommand.ToggleHiddenSlide => SlideRoute(SlidePaneActionKind.ToggleHiddenSlide),
            FreePContextMenuCommand.AddSection => SectionRoute(SlideSectionActionKind.AddSection),
            FreePContextMenuCommand.RenameSection => SectionRoute(SlideSectionActionKind.RenameSection),
            FreePContextMenuCommand.RemoveSection => SectionRoute(SlideSectionActionKind.RemoveSection),
            FreePContextMenuCommand.RemoveAllSections => SectionRoute(SlideSectionActionKind.RemoveAllSections),
            _ => new SlidePaneContextCommandRoutePlan(command, null, null),
        };

        SlidePaneContextCommandRoutePlan SlideRoute(SlidePaneActionKind kind) =>
            new(command, SlideAction(kind), null);

        SlidePaneContextCommandRoutePlan SectionRoute(SlideSectionActionKind kind) =>
            new(command, null, SectionAction(kind));
    }

    public static SlidePaneBottomAffordancePlan BuildBottomNewSlideAffordance(
        int slideCount,
        int currentSlideIndex)
    {
        var hasCurrentSlide = IsValidSlideIndex(slideCount, currentSlideIndex);
        var action = new SlidePaneActionPlan(
            SlidePaneActionKind.InsertAfterSlide,
            NewSlideButtonText,
            currentSlideIndex,
            currentSlideIndex + 1,
            hasCurrentSlide);

        return new SlidePaneBottomAffordancePlan(
            NewSlideButtonText,
            "Insert a new slide after the current slide",
            "New Slide",
            IsVisible: true,
            action);
    }

    public static bool TryApplyBottomNewSlideAffordance(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var plan = BuildBottomNewSlideAffordance(
            editor.Presentation.Slides.Count,
            editor.CurrentSlideIndex);

        return TryApplyAction(editor, plan.Action);
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

    public static SlidePaneDragSessionState BeginDragSession(
        int sourceSlideIndex,
        double startPointerY)
    {
        if (sourceSlideIndex < 0)
            return SlidePaneDragSessionState.None;

        return new SlidePaneDragSessionState(
            true,
            false,
            sourceSlideIndex,
            sourceSlideIndex,
            startPointerY);
    }

    public static SlidePaneDragUpdatePlan UpdateDragSession(
        SlidePaneDragSessionState state,
        IReadOnlyList<bool> paneItemIsSlide,
        double pointerYWithinItem,
        double pointerYWithinPane,
        double slideItemHeight,
        double nonSlideItemHeight = DefaultSectionHeaderHeight)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(paneItemIsSlide);

        if (!state.IsTracking)
        {
            return new SlidePaneDragUpdatePlan(
                SlidePaneDragSessionState.None,
                BuildDropVisualPlan(
                    paneItemIsSlide,
                    sourceSlideIndex: -1,
                    targetSlideIndex: -1,
                    slideItemHeight: slideItemHeight,
                    nonSlideItemHeight: nonSlideItemHeight),
                false);
        }

        if (!state.IsDragging &&
            Math.Abs(pointerYWithinItem - state.StartPointerY) < DefaultDragStartThreshold)
        {
            return new SlidePaneDragUpdatePlan(
                state,
                BuildDropVisualPlan(
                    paneItemIsSlide,
                    state.SourceSlideIndex,
                    state.TargetSlideIndex,
                    slideItemHeight,
                    nonSlideItemHeight),
                false);
        }

        var targetSlideIndex = HitTestInsertionPoint(
            paneItemIsSlide,
            pointerYWithinPane,
            slideItemHeight,
            nonSlideItemHeight);
        var nextState = state with
        {
            IsDragging = true,
            TargetSlideIndex = targetSlideIndex,
        };

        return new SlidePaneDragUpdatePlan(
            nextState,
            BuildDropVisualPlan(
                paneItemIsSlide,
                nextState.SourceSlideIndex,
                nextState.TargetSlideIndex,
                slideItemHeight,
                nonSlideItemHeight),
            !state.IsDragging);
    }

    public static SlidePaneDragCompletionPlan CompleteDragSession(
        SlidePaneDragSessionState state,
        int slideCount)
    {
        ArgumentNullException.ThrowIfNull(state);

        var action = state.IsDragging
            ? PlanMoveAction(slideCount, state.SourceSlideIndex, state.TargetSlideIndex)
            : new SlidePaneActionPlan(
                SlidePaneActionKind.MoveSlide,
                "Move Slide",
                state.SourceSlideIndex,
                state.TargetSlideIndex,
                false);

        return new SlidePaneDragCompletionPlan(
            SlidePaneDragSessionState.None,
            action,
            state.IsDragging);
    }

    public static SlidePaneDragSessionState CancelDragSession(
        SlidePaneDragSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return SlidePaneDragSessionState.None;
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

            case SlidePaneActionKind.ToggleHiddenSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                return editor.ToggleCurrentSlideHidden();

            case SlidePaneActionKind.MoveSlide:
                editor.SelectSlide(action.SourceSlideIndex);
                // action.TargetSlideIndex is a pre-removal "insert before this original index"
                // position (see PlanMoveAction's own no-op check against sourceSlideIndex + 1,
                // and BuildKeyboardAction's +2/-1 deltas), but EditingSession.MoveSlide takes a
                // post-removal final index (see MoveSlideCommand.MoveInList, and the pinned
                // EditingSessionTests.MoveSlide_ReordersSlides / SlidePaneTests.
                // MoveSlide_Reorders_AndPaneReflectsNewOrder contracts for that API). The two
                // conventions coincide when the target is at or before the source, but a forward
                // target must be shifted back by one to land the slide where the pre-removal
                // index actually pointed.
                var moveToIndex = action.TargetSlideIndex > action.SourceSlideIndex
                    ? action.TargetSlideIndex - 1
                    : action.TargetSlideIndex;
                editor.MoveSlide(action.SourceSlideIndex, moveToIndex);
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

    private static Dictionary<int, List<SlidePaneEntry>> BuildSectionHeaders(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<PresentationSection> sections,
        IReadOnlySet<string>? collapsedSectionIds)
    {
        var headers = new Dictionary<int, List<SlidePaneEntry>>();
        if (sections.Count == 0)
            return headers;

        var slideIndexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < slides.Count; i++)
            slideIndexById[slides[i].Id] = i;

        var rawAnchors = new int[sections.Count];
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            rawAnchors[sectionIndex] = FindFirstSectionSlideIndex(sections[sectionIndex], slideIndexById);

        // A genuinely empty section (SlideIds.Count == 0 -- e.g. its last
        // slide was just dragged into another section) has no row of its own
        // to anchor before, but it's still a live, user-manageable section
        // (see MoveSlideCommand, which deliberately leaves it that way). Fall
        // forward to the next section that resolves to a real slide -- or to
        // the very end of the pane if every remaining section is also empty
        // -- so its header still renders instead of disappearing. A section
        // whose SlideIds are merely stale (non-empty but none of them match a
        // live slide, e.g. left over from a corrupted/foreign file) keeps the
        // prior behaviour of staying unrendered until it is pruned.
        var nextRealAnchorFrom = new int[sections.Count + 1];
        nextRealAnchorFrom[sections.Count] = slides.Count;
        for (var sectionIndex = sections.Count - 1; sectionIndex >= 0; sectionIndex--)
        {
            nextRealAnchorFrom[sectionIndex] = rawAnchors[sectionIndex] >= 0
                ? rawAnchors[sectionIndex]
                : nextRealAnchorFrom[sectionIndex + 1];
        }

        var usedRealAnchors = new HashSet<int>();
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var isEmptySection = section.SlideIds.Count == 0;
            if (rawAnchors[sectionIndex] < 0 && !isEmptySection)
                continue;

            var anchorIndex = rawAnchors[sectionIndex] >= 0
                ? rawAnchors[sectionIndex]
                : nextRealAnchorFrom[sectionIndex + 1];

            // Preserve the pre-existing dedup rule for sections whose member
            // slides genuinely resolve to the same first slide (corrupt/
            // overlapping membership) -- only ever show one real header
            // there. Empty sections cascading into this anchor are never
            // deduped: each one still deserves its own visible/removable
            // header.
            if (rawAnchors[sectionIndex] >= 0 && !usedRealAnchors.Add(anchorIndex))
                continue;

            var sectionId = GetSectionIdentity(section, sectionIndex);
            var count = CountKnownSectionSlides(section, slideIndexById);
            var isCollapsed = IsSectionCollapsed(sectionId, collapsedSectionIds);
            var entry = new SlidePaneEntry(
                SlidePaneEntryKind.SectionHeader,
                SlideIndex: anchorIndex,
                Text: FormatSectionHeader(section.Name, count),
                SectionSlideCount: count,
                SectionIndex: sectionIndex,
                SectionId: sectionId,
                IsSectionCollapsed: isCollapsed);

            if (!headers.TryGetValue(anchorIndex, out var list))
            {
                list = new List<SlidePaneEntry>();
                headers[anchorIndex] = list;
            }

            list.Add(entry);
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
