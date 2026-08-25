using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed class FreePRibbonHostActionEndpoints
{
    public Action? Copy { get; init; }
    public Action? Cut { get; init; }
    public Action? Paste { get; init; }
    public Action? InsertPicture { get; init; }
    public Action? InsertVideo { get; init; }
    public Action? InsertAudio { get; init; }
    public Action? OpenTablePicker { get; init; }
    public Func<PresentationDomainContextActionKind, bool>? ExecuteTableStructureAction { get; init; }
    public Action? MergeTableCells { get; init; }
    public Action? SplitTableCell { get; init; }
    public Action? PickPictureBullet { get; init; }
    public Action? InsertSlideZoom { get; init; }
    public Action? InsertSectionZoom { get; init; }
    public Action? InsertSummaryZoom { get; init; }
    public Action? EditZoomTarget { get; init; }
    public Action? EditSummaryZoomTargets { get; init; }
    public Action? FormatZoom { get; init; }
    public Action? SetZoomCoverImage { get; init; }
    public Action? ResetZoomCoverImage { get; init; }
    public Action<HeaderFooterCommandFocus>? OpenHeaderFooter { get; init; }
    public Action<SmartArtColorPreset>? ApplySmartArtColor { get; init; }
    public Action<SmartArtLayoutPreset>? ApplySmartArtLayout { get; init; }
    public Action<SmartArtQuickStylePreset>? ApplySmartArtQuickStyle { get; init; }
    public Action? ConvertSmartArtToShapes { get; init; }
    public Action? OpenSmartArtTextPane { get; init; }
    public Action? OpenChartData { get; init; }
    public Action? OpenChartDisplayOptions { get; init; }
    public Action? OpenChartAxisOptions { get; init; }
    public Action? OpenChartSeriesOptions { get; init; }
    public Action? OpenChartPointOptions { get; init; }
    public Action? OpenChartLayoutOptions { get; init; }
    public Action? OpenChartExSeriesLayout { get; init; }
    public Action? OpenChartDataTableOptions { get; init; }
    public Action? OpenChartBubbleOptions { get; init; }
    public Action? OpenChartPieOptions { get; init; }
    public Action? OpenChartPlotStyleOptions { get; init; }
    public Action? OpenChart3DViewOptions { get; init; }
    public Action? OpenChartTextOptions { get; init; }
    public Action? OpenChartAreaOptions { get; init; }
    public Action? OpenChartProtectionOptions { get; init; }
    public Action? OpenHyperlink { get; init; }
    public Action? OpenRotationOptions { get; init; }
    public Action<bool>? SetEditPointsEnabled { get; init; }
    public Action? OpenFind { get; init; }
    public Action? OpenReplace { get; init; }
    public Action? ShowCommentsPane { get; init; }
    public Action? ShowAccessibilityPane { get; init; }
    public Action? ShowAltTextPane { get; init; }
    public Action? ShowReadingOrderPane { get; init; }
    public Action? ShowSelectionPane { get; init; }
    public Action? ShowProofingPane { get; init; }
    public Action? AddComment { get; init; }
    public Action? EditComment { get; init; }
    public Action? ReplyComment { get; init; }
    public Action? DeleteComment { get; init; }
    public Action? PreviousComment { get; init; }
    public Action? NextComment { get; init; }
    public Action? ResolveComment { get; init; }
    public Action? ReopenComment { get; init; }
    public Action<PresentationViewShowState>? ApplyViewShowState { get; init; }
    public Action<PresentationViewZoomState>? ApplyViewZoomState { get; init; }
    public Action<PresentationViewModeState>? ApplyViewModeState { get; init; }
    public Action? StartReadingView { get; init; }
    public Action? NewPresentationWindow { get; init; }
    public Action? ArrangeAllPresentationWindows { get; init; }
    public Action? CascadePresentationWindows { get; init; }
    public Action? SwitchPresentationWindow { get; init; }
    public Action? PickTransitionSound { get; init; }
    public Action<PresentationAnimationCommandPlan>? ToggleAnimationPane { get; init; }
    public Action? StartSlideShowFromBeginning { get; init; }
    public Action? StartSlideShowFromCurrent { get; init; }
    public Action? RehearseTimings { get; init; }
    public Action? RecordTimings { get; init; }
    public Action? OpenCustomShows { get; init; }
    public Action? OpenSlideShowSettings { get; init; }
}

/// <summary>Native surfaces selected by portable presentation design command plans.</summary>
public sealed class FreePRibbonDesignCommandEndpoints
{
    public Action<PresentationDesignCommandPlan>? OpenCustomSlideSize { get; init; }
    public Action<PresentationDesignCommandPlan>? OpenLayoutPicker { get; init; }
}

/// <summary>
/// Applies portable host policy before falling through to native action dispatch. This keeps
/// renderer-specific callbacks thin while preserving Presentation's built-in fallbacks.
/// </summary>
public static class FreePRibbonHostActionRouter
{
    public static bool Dispatch(
        EditingSession editor,
        FreePRibbonHostAction action,
        FreePRibbonHostActionEndpoints endpoints,
        FreePRibbonDesignCommandEndpoints designEndpoints)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(designEndpoints);

        return action.Kind switch
        {
            FreePRibbonHostActionKind.OpenTablePicker => OpenTablePicker(editor, endpoints),
            FreePRibbonHostActionKind.OpenHeaderFooter =>
                OpenHeaderFooter(editor, action.Argument, endpoints),
            FreePRibbonHostActionKind.DesignRequest =>
                RouteDesignRequest(action.Argument, designEndpoints),
            _ => FreePRibbonHostActionDispatcher.Dispatch(action, endpoints),
        };
    }

    private static bool OpenTablePicker(
        EditingSession editor,
        FreePRibbonHostActionEndpoints endpoints)
    {
        if (endpoints.OpenTablePicker is not null)
            return FreePRibbonHostActionDispatcher.Dispatch(
                new FreePRibbonHostAction(FreePRibbonHostActionKind.OpenTablePicker),
                endpoints);

        var plan = SlideObjectInsertionPlanner.BuiltInPlans.Single(
            item => item.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId);
        SlideObjectInsertionPlanner.Apply(editor, plan);
        return true;
    }

    private static bool OpenHeaderFooter(
        EditingSession editor,
        object? argument,
        FreePRibbonHostActionEndpoints endpoints)
    {
        if (argument is not HeaderFooterCommandFocus focus)
            return false;

        if (endpoints.OpenHeaderFooter is not null)
            return FreePRibbonHostActionDispatcher.Dispatch(
                new FreePRibbonHostAction(FreePRibbonHostActionKind.OpenHeaderFooter, focus),
                endpoints);

        var state = HeaderFooterCommandPlanner.BuildState(editor);
        HeaderFooterCommandPlanner.TryApply(
            editor,
            HeaderFooterCommandPlanner.BuildDefaultOptions(state, focus),
            out _);
        return true;
    }

    private static bool RouteDesignRequest(
        object? argument,
        FreePRibbonDesignCommandEndpoints endpoints)
    {
        if (argument is not PresentationDesignCommandPlan plan)
            return false;

        var endpoint = plan.Intent switch
        {
            PresentationDesignCommandIntentKind.RequestCustomSlideSize => endpoints.OpenCustomSlideSize,
            PresentationDesignCommandIntentKind.RequestLayoutPicker => endpoints.OpenLayoutPicker,
            _ => null,
        };
        if (endpoint is null)
            return false;

        endpoint(plan);
        return true;
    }
}

/// <summary>
/// Exhaustive, renderer-neutral dispatch for FreeP ribbon host actions. Renderers provide only
/// native endpoints; action classification and typed payload validation remain shared.
/// </summary>
public static class FreePRibbonHostActionDispatcher
{
    public static bool Dispatch(
        FreePRibbonHostAction action,
        FreePRibbonHostActionEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(endpoints);

        return action.Kind switch
        {
            FreePRibbonHostActionKind.Copy => Invoke(endpoints.Copy),
            FreePRibbonHostActionKind.Cut => Invoke(endpoints.Cut),
            FreePRibbonHostActionKind.Paste => Invoke(endpoints.Paste),
            FreePRibbonHostActionKind.InsertPicture => Invoke(endpoints.InsertPicture),
            FreePRibbonHostActionKind.InsertVideo => Invoke(endpoints.InsertVideo),
            FreePRibbonHostActionKind.InsertAudio => Invoke(endpoints.InsertAudio),
            FreePRibbonHostActionKind.OpenTablePicker => Invoke(endpoints.OpenTablePicker),
            FreePRibbonHostActionKind.ExecuteTableStructureAction =>
                Invoke(action.Argument, endpoints.ExecuteTableStructureAction),
            FreePRibbonHostActionKind.MergeTableCells => Invoke(endpoints.MergeTableCells),
            FreePRibbonHostActionKind.SplitTableCell => Invoke(endpoints.SplitTableCell),
            FreePRibbonHostActionKind.PickPictureBullet => Invoke(endpoints.PickPictureBullet),
            FreePRibbonHostActionKind.InsertSlideZoom => Invoke(endpoints.InsertSlideZoom),
            FreePRibbonHostActionKind.InsertSectionZoom => Invoke(endpoints.InsertSectionZoom),
            FreePRibbonHostActionKind.InsertSummaryZoom => Invoke(endpoints.InsertSummaryZoom),
            FreePRibbonHostActionKind.EditZoomTarget => Invoke(endpoints.EditZoomTarget),
            FreePRibbonHostActionKind.EditSummaryZoomTargets => Invoke(endpoints.EditSummaryZoomTargets),
            FreePRibbonHostActionKind.FormatZoom => Invoke(endpoints.FormatZoom),
            FreePRibbonHostActionKind.SetZoomCoverImage => Invoke(endpoints.SetZoomCoverImage),
            FreePRibbonHostActionKind.ResetZoomCoverImage => Invoke(endpoints.ResetZoomCoverImage),
            FreePRibbonHostActionKind.OpenHeaderFooter => Invoke(action.Argument, endpoints.OpenHeaderFooter),
            FreePRibbonHostActionKind.ApplySmartArtColor => Invoke(action.Argument, endpoints.ApplySmartArtColor),
            FreePRibbonHostActionKind.ApplySmartArtLayout => Invoke(action.Argument, endpoints.ApplySmartArtLayout),
            FreePRibbonHostActionKind.ApplySmartArtQuickStyle => Invoke(action.Argument, endpoints.ApplySmartArtQuickStyle),
            FreePRibbonHostActionKind.ConvertSmartArtToShapes => Invoke(endpoints.ConvertSmartArtToShapes),
            FreePRibbonHostActionKind.OpenSmartArtTextPane => Invoke(endpoints.OpenSmartArtTextPane),
            FreePRibbonHostActionKind.OpenChartData => Invoke(endpoints.OpenChartData),
            FreePRibbonHostActionKind.OpenChartDisplayOptions => Invoke(endpoints.OpenChartDisplayOptions),
            FreePRibbonHostActionKind.OpenChartAxisOptions => Invoke(endpoints.OpenChartAxisOptions),
            FreePRibbonHostActionKind.OpenChartSeriesOptions => Invoke(endpoints.OpenChartSeriesOptions),
            FreePRibbonHostActionKind.OpenChartPointOptions => Invoke(endpoints.OpenChartPointOptions),
            FreePRibbonHostActionKind.OpenChartLayoutOptions => Invoke(endpoints.OpenChartLayoutOptions),
            FreePRibbonHostActionKind.OpenChartExSeriesLayout => Invoke(endpoints.OpenChartExSeriesLayout),
            FreePRibbonHostActionKind.OpenChartDataTableOptions => Invoke(endpoints.OpenChartDataTableOptions),
            FreePRibbonHostActionKind.OpenChartBubbleOptions => Invoke(endpoints.OpenChartBubbleOptions),
            FreePRibbonHostActionKind.OpenChartPieOptions => Invoke(endpoints.OpenChartPieOptions),
            FreePRibbonHostActionKind.OpenChartPlotStyleOptions => Invoke(endpoints.OpenChartPlotStyleOptions),
            FreePRibbonHostActionKind.OpenChart3DViewOptions => Invoke(endpoints.OpenChart3DViewOptions),
            FreePRibbonHostActionKind.OpenChartTextOptions => Invoke(endpoints.OpenChartTextOptions),
            FreePRibbonHostActionKind.OpenChartAreaOptions => Invoke(endpoints.OpenChartAreaOptions),
            FreePRibbonHostActionKind.OpenChartProtectionOptions => Invoke(endpoints.OpenChartProtectionOptions),
            FreePRibbonHostActionKind.OpenHyperlink => Invoke(endpoints.OpenHyperlink),
            FreePRibbonHostActionKind.OpenRotationOptions => Invoke(endpoints.OpenRotationOptions),
            FreePRibbonHostActionKind.SetEditPointsEnabled => Invoke(action.Argument, endpoints.SetEditPointsEnabled),
            FreePRibbonHostActionKind.OpenFind => Invoke(endpoints.OpenFind),
            FreePRibbonHostActionKind.OpenReplace => Invoke(endpoints.OpenReplace),
            FreePRibbonHostActionKind.ShowCommentsPane => Invoke(endpoints.ShowCommentsPane),
            FreePRibbonHostActionKind.ShowAccessibilityPane => Invoke(endpoints.ShowAccessibilityPane),
            FreePRibbonHostActionKind.ShowAltTextPane => Invoke(endpoints.ShowAltTextPane),
            FreePRibbonHostActionKind.ShowReadingOrderPane => Invoke(endpoints.ShowReadingOrderPane),
            FreePRibbonHostActionKind.ShowSelectionPane => Invoke(endpoints.ShowSelectionPane),
            FreePRibbonHostActionKind.ShowProofingPane => Invoke(endpoints.ShowProofingPane),
            FreePRibbonHostActionKind.AddComment => Invoke(endpoints.AddComment),
            FreePRibbonHostActionKind.EditComment => Invoke(endpoints.EditComment),
            FreePRibbonHostActionKind.ReplyComment => Invoke(endpoints.ReplyComment),
            FreePRibbonHostActionKind.DeleteComment => Invoke(endpoints.DeleteComment),
            FreePRibbonHostActionKind.PreviousComment => Invoke(endpoints.PreviousComment),
            FreePRibbonHostActionKind.NextComment => Invoke(endpoints.NextComment),
            FreePRibbonHostActionKind.ResolveComment => Invoke(endpoints.ResolveComment),
            FreePRibbonHostActionKind.ReopenComment => Invoke(endpoints.ReopenComment),
            FreePRibbonHostActionKind.ApplyViewShowState => Invoke(action.Argument, endpoints.ApplyViewShowState),
            FreePRibbonHostActionKind.ApplyViewZoomState => Invoke(action.Argument, endpoints.ApplyViewZoomState),
            FreePRibbonHostActionKind.ApplyViewModeState => Invoke(action.Argument, endpoints.ApplyViewModeState),
            FreePRibbonHostActionKind.StartReadingView => Invoke(endpoints.StartReadingView),
            FreePRibbonHostActionKind.NewPresentationWindow => Invoke(endpoints.NewPresentationWindow),
            FreePRibbonHostActionKind.ArrangeAllPresentationWindows => Invoke(endpoints.ArrangeAllPresentationWindows),
            FreePRibbonHostActionKind.CascadePresentationWindows => Invoke(endpoints.CascadePresentationWindows),
            FreePRibbonHostActionKind.SwitchPresentationWindow => Invoke(endpoints.SwitchPresentationWindow),
            FreePRibbonHostActionKind.PickTransitionSound => Invoke(endpoints.PickTransitionSound),
            FreePRibbonHostActionKind.ToggleAnimationPane => Invoke(action.Argument, endpoints.ToggleAnimationPane),
            FreePRibbonHostActionKind.StartSlideShowFromBeginning => Invoke(endpoints.StartSlideShowFromBeginning),
            FreePRibbonHostActionKind.StartSlideShowFromCurrent => Invoke(endpoints.StartSlideShowFromCurrent),
            FreePRibbonHostActionKind.RehearseTimings => Invoke(endpoints.RehearseTimings),
            FreePRibbonHostActionKind.RecordTimings => Invoke(endpoints.RecordTimings),
            FreePRibbonHostActionKind.OpenCustomShows => Invoke(endpoints.OpenCustomShows),
            FreePRibbonHostActionKind.OpenSlideShowSettings => Invoke(endpoints.OpenSlideShowSettings),
            _ => false
        };
    }

    private static bool Invoke(Action? endpoint)
    {
        if (endpoint is null)
            return false;
        endpoint();
        return true;
    }

    private static bool Invoke<T>(object? argument, Action<T>? endpoint)
    {
        if (endpoint is null || argument is not T typedArgument)
            return false;
        endpoint(typedArgument);
        return true;
    }

    private static bool Invoke<T>(object? argument, Func<T, bool>? endpoint) =>
        endpoint is not null && argument is T typedArgument && endpoint(typedArgument);
}
