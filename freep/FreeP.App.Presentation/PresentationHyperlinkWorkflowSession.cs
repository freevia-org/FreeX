using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationHyperlinkApplyTarget
{
    None,
    SelectedTextRun,
    SelectedShape,
}

public sealed record PresentationHyperlinkWorkflowRequest(
    HyperlinkDialogRequest DialogRequest,
    bool EditsSelectedTextRun);

public sealed record PresentationHyperlinkWorkflowResult(
    HyperlinkDialogApplyPlan ApplyPlan,
    PresentationHyperlinkApplyTarget Target)
{
    public bool Applied => Target != PresentationHyperlinkApplyTarget.None;
}

/// <summary>
/// Owns renderer-neutral hyperlink request selection and selected-run/shape mutation fallback.
/// Hosts retain native dialogs and their in-canvas text-editor adapter.
/// </summary>
public sealed class PresentationHyperlinkWorkflowSession
{
    private readonly Func<EditingSession> _getEditor;

    public PresentationHyperlinkWorkflowSession(Func<EditingSession> getEditor)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
    }

    public PresentationHyperlinkWorkflowRequest BuildRequest(
        bool editsSelectedTextRun,
        Hyperlink? selectedTextRunHyperlink)
    {
        var editor = _getEditor();
        return new PresentationHyperlinkWorkflowRequest(
            HyperlinkDialogPlanner.BuildDialogRequest(
                editor.Presentation.Slides,
                editsSelectedTextRun
                    ? selectedTextRunHyperlink
                    : editor.SelectedShapeHyperlink),
            editsSelectedTextRun);
    }

    public PresentationHyperlinkWorkflowResult Apply(
        PresentationHyperlinkWorkflowRequest request,
        Hyperlink? result,
        Func<Hyperlink, bool>? tryApplySelectedTextRun = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var applyPlan = HyperlinkDialogPlanner.BuildApplyPlan(result);
        if (!applyPlan.ShouldApply)
            return new PresentationHyperlinkWorkflowResult(
                applyPlan,
                PresentationHyperlinkApplyTarget.None);

        var hyperlink = new Hyperlink
        {
            Url = applyPlan.Url,
            TargetSlideId = applyPlan.TargetSlideId,
            Tooltip = applyPlan.Tooltip,
        };
        if (request.EditsSelectedTextRun
            && tryApplySelectedTextRun?.Invoke(hyperlink) == true)
        {
            return new PresentationHyperlinkWorkflowResult(
                applyPlan,
                PresentationHyperlinkApplyTarget.SelectedTextRun);
        }

        _getEditor().SetShapeHyperlink(
            applyPlan.Url,
            applyPlan.TargetSlideId,
            applyPlan.Tooltip);
        return new PresentationHyperlinkWorkflowResult(
            applyPlan,
            PresentationHyperlinkApplyTarget.SelectedShape);
    }
}
