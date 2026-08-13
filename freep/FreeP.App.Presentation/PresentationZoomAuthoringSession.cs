using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationZoomTargetKind
{
    Slide,
    Section,
}

public sealed record PresentationZoomTargetDialogRequest(
    PresentationZoomTargetKind Kind,
    uint? ShapeId,
    string Title,
    IReadOnlyList<(string Id, string DisplayName)> Options,
    string? SelectedTargetId = null);

public sealed record PresentationSummaryZoomDialogRequest(
    uint? ShapeId,
    string Title,
    IReadOnlyList<(string Id, string DisplayName)> Options,
    IReadOnlyList<string> SelectedTargetIds);

public sealed record PresentationZoomPropertiesRequest(
    uint ShapeId,
    ZoomObjectProperties Properties,
    IReadOnlyList<SummaryZoomTarget> SummaryTargets,
    IReadOnlyList<ZoomObjectProperties> SummaryTileProperties);

public sealed record PresentationZoomPropertiesApplyRequest(
    ZoomObjectProperties Properties,
    bool ApplySummaryPropertiesToAllTiles,
    ZoomObjectPropertiesPlanner.SummaryZoomTilePropertiesEdit? SummaryTileProperties,
    ZoomObjectPropertiesPlanner.SummaryZoomTileLayoutEdit? SummaryTileLayout);

public sealed record PresentationZoomCoverTargetRequest(
    uint ShapeId,
    IReadOnlyList<(string Id, string DisplayName)> SummaryTargetOptions)
{
    public bool RequiresSummaryTarget => SummaryTargetOptions.Count > 0;
}

public sealed record PresentationZoomAuthoringSessionCallbacks(
    Action MarkDirty,
    Action RefreshCanvas,
    Action UpdateHost,
    Func<Presentation, int, int, int, byte[]> RenderSlidePreview);

/// <summary>
/// Owns renderer-neutral Zoom insertion, retargeting, properties, cover images, and preview persistence.
/// Hosts retain native dialogs, file pickers, error presentation, and platform slide-image rendering.
/// </summary>
public sealed class PresentationZoomAuthoringSession
{
    private readonly Func<EditingSession> _getEditor;
    private readonly PresentationZoomAuthoringSessionCallbacks _callbacks;

    public PresentationZoomAuthoringSession(
        Func<EditingSession> getEditor,
        PresentationZoomAuthoringSessionCallbacks callbacks)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public PresentationZoomTargetDialogRequest? BuildSlideInsertionRequest()
    {
        var editor = _getEditor();
        var options = SlideZoomInsertionPlanner.BuildTargetOptions(
            editor.Presentation.Slides,
            editor.CurrentSlideIndex);
        return options.Count == 0
            ? null
            : new PresentationZoomTargetDialogRequest(
                PresentationZoomTargetKind.Slide,
                null,
                SlideZoomInsertionPlanner.DialogTitle,
                options);
    }

    public SlideShape? ApplySlideInsertion(string? targetSlideId)
    {
        if (string.IsNullOrWhiteSpace(targetSlideId))
            return null;

        var editor = _getEditor();
        var shape = editor.InsertSlideZoom(targetSlideId);
        var targetIndex = editor.Presentation.Slides.FindIndex(slide =>
            string.Equals(slide.Id, targetSlideId, StringComparison.OrdinalIgnoreCase));
        AttachPreview(editor.Presentation, shape, targetIndex);
        return shape;
    }

    public PresentationZoomTargetDialogRequest? BuildSectionInsertionRequest()
    {
        var editor = _getEditor();
        var options = SectionZoomInsertionPlanner.BuildTargetOptions(
            editor.Presentation,
            editor.CurrentSlideIndex);
        return options.Count == 0
            ? null
            : new PresentationZoomTargetDialogRequest(
                PresentationZoomTargetKind.Section,
                null,
                SectionZoomInsertionPlanner.DialogTitle,
                options);
    }

    public SlideShape? ApplySectionInsertion(string? targetSectionId)
    {
        if (string.IsNullOrWhiteSpace(targetSectionId))
            return null;

        var editor = _getEditor();
        var shape = editor.InsertSectionZoom(targetSectionId);
        if (SummaryZoomPreviewPlanner.TryResolveTargetSlideIndex(
                editor.Presentation,
                targetSectionId,
                out var targetIndex))
        {
            AttachPreview(editor.Presentation, shape, targetIndex);
        }
        return shape;
    }

    public PresentationSummaryZoomDialogRequest? BuildSummaryInsertionRequest()
    {
        var editor = _getEditor();
        var options = SummaryZoomInsertionPlanner.BuildTargetOptions(
            editor.Presentation,
            editor.CurrentSlideIndex);
        return options.Count < 2
            ? null
            : new PresentationSummaryZoomDialogRequest(
                null,
                SummaryZoomInsertionPlanner.DialogTitle,
                options,
                Array.Empty<string>());
    }

    public SlideShape? ApplySummaryInsertion(IReadOnlyList<string>? targetSectionIds)
    {
        if (targetSectionIds is null || targetSectionIds.Count < 2)
            return null;

        var editor = _getEditor();
        var shape = editor.InsertSummaryZoom(targetSectionIds);
        AttachSummaryPreviews(editor.Presentation, shape);
        return shape;
    }

    public PresentationZoomTargetDialogRequest? BuildSelectedTargetRequest()
    {
        var editor = _getEditor();
        if (!TryGetSelectedZoom(editor, out var shape, out var info)
            || info.SummaryZoomTargets.Count != 0)
        {
            return null;
        }

        if (info.ZoomTargetSlideNumericId is uint targetNumericId)
        {
            var options = SlideZoomInsertionPlanner.BuildTargetOptions(
                editor.Presentation.Slides,
                editor.CurrentSlideIndex);
            var currentId = editor.Presentation.Slides
                .FirstOrDefault(slide => slide.NumericId == targetNumericId)?.Id;
            return new PresentationZoomTargetDialogRequest(
                PresentationZoomTargetKind.Slide,
                shape.Id,
                ZoomTargetPlanner.DialogTitle,
                options,
                currentId);
        }

        if (string.IsNullOrWhiteSpace(info.ZoomTargetSectionId))
            return null;

        return new PresentationZoomTargetDialogRequest(
            PresentationZoomTargetKind.Section,
            shape.Id,
            ZoomTargetPlanner.DialogTitle,
            SectionZoomInsertionPlanner.BuildTargetOptions(
                editor.Presentation,
                editor.CurrentSlideIndex),
            info.ZoomTargetSectionId);
    }

    public bool ApplySelectedTarget(
        PresentationZoomTargetDialogRequest request,
        string? targetId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShapeId is not uint shapeId || string.IsNullOrWhiteSpace(targetId))
            return false;

        var editor = _getEditor();
        var shape = editor.CurrentSlide is { } slide
            ? SlideShapeTraversal.FindById(slide, shapeId)
            : null;
        if (shape?.Kind != SlideShapeKind.Zoom)
            return false;

        editor.Select(shapeId);
        int targetIndex;
        bool changed;
        if (request.Kind == PresentationZoomTargetKind.Slide)
        {
            changed = editor.SetSlideZoomTarget(shapeId, targetId);
            targetIndex = editor.Presentation.Slides.FindIndex(slide =>
                string.Equals(slide.Id, targetId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            changed = editor.SetSectionZoomTarget(shapeId, targetId);
            targetIndex = SummaryZoomPreviewPlanner.TryResolveTargetSlideIndex(
                editor.Presentation,
                targetId,
                out var resolvedIndex)
                ? resolvedIndex
                : -1;
        }

        if (changed)
            AttachPreview(editor.Presentation, shape, targetIndex);
        return changed;
    }

    public PresentationSummaryZoomDialogRequest? BuildSelectedSummaryTargetsRequest()
    {
        var editor = _getEditor();
        if (!TryGetSelectedZoom(editor, out var shape, out var info)
            || info.SummaryZoomTargets.Count < 2)
        {
            return null;
        }

        return new PresentationSummaryZoomDialogRequest(
            shape.Id,
            SummaryZoomTargetPlanner.DialogTitle,
            SummaryZoomInsertionPlanner.BuildTargetOptions(
                editor.Presentation,
                editor.CurrentSlideIndex),
            info.SummaryZoomTargets.Select(target => target.SectionId).ToArray());
    }

    public bool ApplySelectedSummaryTargets(
        PresentationSummaryZoomDialogRequest request,
        IReadOnlyList<string>? targetSectionIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ShapeId is not uint shapeId || targetSectionIds is null)
            return false;

        var editor = _getEditor();
        var shape = editor.CurrentSlide is { } slide
            ? SlideShapeTraversal.FindById(slide, shapeId)
            : null;
        if (shape?.Kind != SlideShapeKind.Zoom)
            return false;

        editor.Select(shapeId);
        if (!editor.SetSummaryZoomTargets(shapeId, targetSectionIds))
            return false;

        AttachSummaryPreviews(editor.Presentation, shape);
        return true;
    }

    public PresentationZoomPropertiesRequest? BuildSelectedPropertiesRequest()
    {
        var editor = _getEditor();
        if (!TryGetSelectedZoom(editor, out var shape, out var info)
            || editor.SelectedZoomObjectProperties is not { } properties)
        {
            return null;
        }

        var summaryTargets = info.SummaryZoomTargets.ToArray();
        var tileProperties = summaryTargets
            .Select(target => ZoomObjectPropertiesPlanner.EffectiveSummaryTile(info, target.SectionId))
            .ToArray();
        return new PresentationZoomPropertiesRequest(
            shape.Id,
            properties,
            summaryTargets,
            tileProperties);
    }

    public bool ApplySelectedProperties(
        PresentationZoomPropertiesRequest request,
        PresentationZoomPropertiesApplyRequest apply)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(apply);

        var editor = _getEditor();
        editor.Select(request.ShapeId);
        var changed = apply.ApplySummaryPropertiesToAllTiles
            ? editor.SetSelectedZoomObjectProperties(apply.Properties)
            : apply.SummaryTileProperties is { } tileProperties
                ? editor.SetSummaryZoomTileProperties(
                    request.ShapeId,
                    tileProperties.SectionId,
                    tileProperties.Properties)
                : editor.SetSelectedZoomObjectProperties(apply.Properties);

        if (apply.SummaryTileLayout is { } layout)
        {
            changed |= editor.SetSummaryZoomTileLayout(
                request.ShapeId,
                layout.SectionId,
                layout.OffsetFactorX,
                layout.OffsetFactorY,
                layout.ScaleFactorX,
                layout.ScaleFactorY);
        }

        return CompleteHostMutation(changed);
    }

    public PresentationZoomCoverTargetRequest? BuildSelectedCoverTargetRequest()
    {
        var editor = _getEditor();
        if (!TryGetSelectedZoom(editor, out var shape, out var info))
            return null;

        return new PresentationZoomCoverTargetRequest(
            shape.Id,
            info.SummaryZoomTargets
                .Select(target => (
                    target.SectionId,
                    string.IsNullOrWhiteSpace(target.Title) ? target.SectionId : target.Title))
                .ToArray());
    }

    public bool ApplySelectedCoverImage(
        PresentationZoomCoverTargetRequest request,
        string? summarySectionId,
        byte[] imageBytes,
        string contentType)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0 || !ZoomCoverImagePlanner.IsSupportedContentType(contentType))
            return false;

        var editor = _getEditor();
        editor.Select(request.ShapeId);
        var changed = summarySectionId is null
            ? editor.SetSelectedZoomCoverImage(imageBytes, contentType)
            : editor.SetSummaryZoomTileCoverImage(
                request.ShapeId,
                summarySectionId,
                imageBytes,
                contentType);
        return CompleteHostMutation(changed);
    }

    public bool RestoreSelectedPreview(
        PresentationZoomCoverTargetRequest request,
        string? summarySectionId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var editor = _getEditor();
        var shape = editor.CurrentSlide is { } slide
            ? SlideShapeTraversal.FindById(slide, request.ShapeId)
            : null;
        if (shape?.Kind != SlideShapeKind.Zoom || shape.PreservedObject is not { } info)
            return false;

        var targetIndex = summarySectionId is not null
            ? SummaryZoomPreviewPlanner.TryResolveTargetSlideIndex(
                editor.Presentation,
                summarySectionId,
                out var summaryIndex)
                ? summaryIndex
                : -1
            : ZoomNavigationService.TryGetTargetSlideIndex(
                editor.Presentation,
                info,
                out var singleIndex)
                ? singleIndex
                : -1;
        if (targetIndex < 0)
            return false;

        var preview = RenderPreview(editor.Presentation, targetIndex);
        editor.Select(request.ShapeId);
        var changed = summarySectionId is null
            ? editor.ResetSelectedZoomCoverImage(preview, "image/png")
            : editor.ResetSummaryZoomTileCoverImage(
                request.ShapeId,
                summarySectionId,
                preview,
                "image/png");
        return CompleteHostMutation(changed);
    }

    private bool CompleteHostMutation(bool changed)
    {
        if (!changed)
            return false;

        _callbacks.MarkDirty();
        _callbacks.RefreshCanvas();
        _callbacks.UpdateHost();
        return true;
    }

    private void AttachSummaryPreviews(Presentation presentation, SlideShape shape)
    {
        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(presentation, widthPx);
        SummaryZoomPreviewPlanner.AttachPreviewImages(
            presentation,
            shape,
            slideIndex => _callbacks.RenderSlidePreview(
                presentation,
                slideIndex,
                widthPx,
                heightPx));
    }

    private void AttachPreview(Presentation presentation, SlideShape shape, int targetSlideIndex)
    {
        if (targetSlideIndex < 0)
            return;

        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(presentation, widthPx);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation,
            shape,
            targetSlideIndex,
            slideIndex => _callbacks.RenderSlidePreview(
                presentation,
                slideIndex,
                widthPx,
                heightPx));
    }

    private byte[] RenderPreview(Presentation presentation, int slideIndex)
    {
        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(presentation, widthPx);
        return _callbacks.RenderSlidePreview(presentation, slideIndex, widthPx, heightPx);
    }

    private static bool TryGetSelectedZoom(
        EditingSession editor,
        out SlideShape shape,
        out PreservedObjectInfo info)
    {
        shape = null!;
        info = null!;
        if (editor.SelectedShapeIds.Count != 1 || editor.CurrentSlide is not { } slide)
            return false;

        shape = SlideShapeTraversal.FindById(slide, editor.SelectedShapeIds[0])!;
        if (shape?.Kind != SlideShapeKind.Zoom
            || shape.PreservedObject is not { ObjectKind: PreservedObjectKind.Zoom } preserved)
        {
            shape = null!;
            return false;
        }

        info = preserved;
        return true;
    }
}
