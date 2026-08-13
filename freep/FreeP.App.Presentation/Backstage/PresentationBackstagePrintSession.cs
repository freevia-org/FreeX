namespace FreeP.App.Compositor;

public sealed record PresentationBackstagePrintPreviewState(
    int? SelectedPageIndex,
    PresentationPrintPreviewPage? SelectedPage,
    int PageCount,
    bool CanGoToPreviousPage,
    bool CanGoToNextPage);

public sealed record PresentationBackstagePrintValidation(
    bool CanBuildPackage,
    bool CanHandoffToNativePrinter,
    bool CanPrint,
    string? FailureReason);

public sealed record PresentationBackstagePrintSessionState(
    PresentationPrintRequest Request,
    PresentationPrintBackstagePlan Plan,
    PresentationBackstagePrintSurface Surface,
    PresentationBackstagePrintPreviewState Preview,
    PresentationBackstagePrintValidation Validation);

/// <summary>
/// Owns renderer-neutral Backstage print interaction state. Hosts retain control creation,
/// preview rendering, native printer selection, and native print execution.
/// </summary>
public sealed class PresentationBackstagePrintSession
{
    private readonly Func<PresentationPrintRequest?, PresentationPrintBackstagePlan> _buildPlan;
    private readonly Action<PresentationPrintRequest> _executePrint;
    private PresentationPrintRequest? _request;
    private int _selectedPreviewPageIndex;

    public PresentationBackstagePrintSession(
        Func<PresentationPrintRequest?, PresentationPrintBackstagePlan> buildPlan,
        Action<PresentationPrintRequest> executePrint)
    {
        _buildPlan = buildPlan ?? throw new ArgumentNullException(nameof(buildPlan));
        _executePrint = executePrint ?? throw new ArgumentNullException(nameof(executePrint));
    }

    public PresentationBackstagePrintSessionState? Current { get; private set; }

    public PresentationBackstagePrintSessionState Refresh()
    {
        var plan = _buildPlan(_request);
        _request = PresentationBackstagePrintRequestPlanner.BuildRequest(plan);
        return SetCurrent(plan);
    }

    public PresentationBackstagePrintSessionState SetRequest(PresentationPrintRequest? request)
    {
        _request = request;
        _selectedPreviewPageIndex = 0;
        return Refresh();
    }

    public PresentationBackstagePrintSessionState ApplyCustomRange(string? rangeText)
    {
        var current = Current ?? Refresh();
        _request = PresentationBackstagePrintRequestPlanner.WithCustomRange(
            current.Request,
            rangeText);
        _selectedPreviewPageIndex = 0;
        return Refresh();
    }

    public PresentationBackstagePrintSessionState GoToPreviousPreviewPage() =>
        GoToPreviewPage(_selectedPreviewPageIndex - 1);

    public PresentationBackstagePrintSessionState GoToNextPreviewPage() =>
        GoToPreviewPage(_selectedPreviewPageIndex + 1);

    public PresentationBackstagePrintSessionState GoToPreviewPage(int pageIndex)
    {
        var current = Current ?? Refresh();
        _selectedPreviewPageIndex = NormalizePreviewPageIndex(
            pageIndex,
            current.Plan.PreviewPlan.Pages.Count);
        return SetCurrent(current.Plan);
    }

    public bool CanExecutePrint(string automationId) =>
        FindPrintAction(automationId) is { IsEnabled: true };

    public bool TryExecutePrint(string automationId)
    {
        var action = FindPrintAction(automationId);
        if (action is not { IsEnabled: true })
            return false;

        _executePrint(action.Request);
        return true;
    }

    private PresentationBackstagePrintSessionState SetCurrent(PresentationPrintBackstagePlan plan)
    {
        var pages = plan.PreviewPlan.Pages;
        _selectedPreviewPageIndex = NormalizePreviewPageIndex(
            _selectedPreviewPageIndex,
            pages.Count);
        var selectedPage = _selectedPreviewPageIndex >= 0
            ? pages[_selectedPreviewPageIndex]
            : null;
        var preview = new PresentationBackstagePrintPreviewState(
            _selectedPreviewPageIndex >= 0 ? _selectedPreviewPageIndex : null,
            selectedPage,
            pages.Count,
            _selectedPreviewPageIndex > 0,
            _selectedPreviewPageIndex >= 0 && _selectedPreviewPageIndex < pages.Count - 1);
        var validation = PresentationBackstagePrintRequestPlanner.Validate(plan);
        var surface = PresentationBackstagePrintSurfacePlanner.Build(
            plan,
            selectedPage?.PageNumber);

        Current = new PresentationBackstagePrintSessionState(
            _request ?? PresentationBackstagePrintRequestPlanner.BuildRequest(plan),
            plan,
            surface,
            preview,
            validation);
        return Current;
    }

    private PresentationBackstagePrintAction? FindPrintAction(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
            return null;

        var current = Current ?? Refresh();
        return current.Surface.PrintActions.FirstOrDefault(action =>
            string.Equals(action.AutomationId, automationId, StringComparison.Ordinal));
    }

    private static int NormalizePreviewPageIndex(int pageIndex, int pageCount) =>
        pageCount == 0 ? -1 : Math.Clamp(pageIndex, 0, pageCount - 1);
}

public static class PresentationBackstagePrintRequestPlanner
{
    public static PresentationPrintRequest BuildRequest(
        PresentationPrintBackstagePlan plan,
        PresentationPrintBackstageLayoutChoice? layoutChoice = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var choice = layoutChoice ?? plan.SelectedLayout;
        return new PresentationPrintRequest(
            choice.Layout.Layout,
            plan.SelectedRange.Request,
            choice.Layout.IsHandout ? choice.Layout.SlidesPerPage : null,
            plan.Options.PrintHiddenSlides,
            plan.Options.Copies,
            plan.Options.Collate,
            plan.Options.ColorMode,
            plan.Options.FrameSlides,
            plan.Options.IncludeCommentsAndInkMarkup);
    }

    public static PresentationPrintRequest WithCustomRange(
        PresentationPrintRequest request,
        string? rangeText)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalized = NormalizeCustomRangeText(rangeText);
        return request with
        {
            SlideRange = normalized is null
                ? null
                : new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: normalized),
        };
    }

    public static string? NormalizeCustomRangeText(string? rangeText)
    {
        var normalized = rangeText?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static PresentationBackstagePrintValidation Validate(
        PresentationPrintBackstagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var canBuildPackage = plan.CanBuildPackage &&
            plan.PackagePlan.CanBuildPackage &&
            plan.PageCount > 0 &&
            plan.SelectedRange.IsAvailable;
        var canHandoff = plan.NativePrintHandoff.CanOpenNativePrintDialog ||
            plan.NativePrintHandoff.CanSubmitToNativePrinter;
        var canPrint = canBuildPackage && canHandoff;
        var failureReason = canPrint
            ? null
            : plan.DisabledReason ??
              plan.PackagePlan.DisabledReason ??
              (!plan.SelectedRange.IsAvailable
                  ? "The selected slide range does not contain a printable slide."
                  : plan.NativePrintHandoff.Reason);

        return new PresentationBackstagePrintValidation(
            canBuildPackage,
            canHandoff,
            canPrint,
            failureReason);
    }
}
