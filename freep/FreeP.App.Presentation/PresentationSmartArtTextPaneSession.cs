using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationSmartArtTextPaneSessionCallbacks(
    Action MarkDirty,
    Action RefreshCanvas,
    Action UpdateHost,
    Action<PresentationSmartArtTextPanePlan> RenderPane);

public sealed record PresentationSmartArtTextPanePlan(
    string Heading,
    string Message,
    IReadOnlyList<SmartArtNodeOutlineItem> Rows,
    string? SelectedModelId,
    bool CanApply,
    bool CanToggleAssistant,
    bool CanEditSelectedRow);

/// <summary>
/// Owns renderer-neutral SmartArt text-pane state, mutations, and native package commit policy.
/// Hosts retain native controls, key adaptation, file picking, focus, and rendering.
/// </summary>
public sealed class PresentationSmartArtTextPaneSession
{
    private const string NativeRefreshFailureMessage =
        "SmartArt native data or drawing cache refresh failed.";

    private readonly Func<EditingSession> _getEditor;
    private readonly PresentationSmartArtTextPaneSessionCallbacks _callbacks;

    public PresentationSmartArtTextPaneSession(
        Func<EditingSession> getEditor,
        PresentationSmartArtTextPaneSessionCallbacks callbacks)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public string? SelectedModelId { get; private set; }

    public SmartArtTextPaneApplyResult? LastTextPaneApplyResult { get; private set; }

    public SmartArtNodeEditResult? LastTextPaneEditResult { get; private set; }

    public SmartArtTextPaneKeyboardRoute? LastKeyboardRoute { get; private set; }

    public SmartArtColorApplyResult? LastColorApplyResult { get; private set; }

    public SmartArtDataPartRewriteResult? LastDataPartRewriteResult { get; private set; }

    public SmartArtDrawingCacheRegenerationResult? LastDrawingCacheRegenerationResult { get; private set; }

    public void SelectModel(string? modelId) => SelectedModelId = modelId;

    public PresentationSmartArtTextPanePlan Refresh()
    {
        var shape = GetSelectedSmartArtShape();
        var outline = SmartArtEditingPlanner.BuildOutline(shape?.SmartArt?.Data);
        if (SelectedModelId is null || outline.All(item =>
                !StringComparer.Ordinal.Equals(item.ModelId, SelectedModelId)))
        {
            SelectedModelId = outline.FirstOrDefault()?.ModelId;
        }

        var selectedItem = outline.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.ModelId, SelectedModelId));
        var plan = new PresentationSmartArtTextPanePlan(
            shape is null || string.IsNullOrWhiteSpace(shape.Name)
                ? "SmartArt Text Pane"
                : $"SmartArt Text Pane - {shape.Name}",
            shape is null
                ? "Select a SmartArt graphic to edit its text outline."
                : outline.Count == 0
                    ? "The selected SmartArt graphic has no editable shared outline rows."
                    : "Rows mirror the shared SmartArt outline.",
            outline,
            SelectedModelId,
            CanApply: shape is not null && outline.Count > 0,
            CanToggleAssistant:
                shape?.SmartArt?.Data?.Family == SmartArtFamily.Hierarchy && selectedItem is { Level: > 0 },
            CanEditSelectedRow: shape is not null && selectedItem is not null);
        _callbacks.RenderPane(plan);
        return plan;
    }

    public SmartArtTextPaneApplyResult ApplyOutline(
        IReadOnlyList<SmartArtTextPaneOutlineRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var editor = _getEditor();
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
        {
            LastTextPaneApplyResult = SmartArtEditingPlanner.ApplyTextPaneOutline(null, rows);
        }
        else
        {
            var previousData = smartArtShape.SmartArt is { } original
                ? SlideCloner.CloneSmartArt(original).Data
                : null;
            editor.EditSmartArt(smartArtShape.Id, smartArt =>
            {
                LastTextPaneApplyResult = SmartArtEditingPlanner.ApplyTextPaneOutline(
                    smartArt.Data,
                    rows);
                if (LastTextPaneApplyResult is not { Applied: true })
                    return false;

                if (CommitMutation(smartArt, smartArtShape, previousData))
                    return true;

                LastTextPaneApplyResult = LastTextPaneApplyResult with
                {
                    Applied = false,
                    Message = NativeRefreshFailureMessage
                };
                return false;
            });
        }

        CompleteMutation(LastTextPaneApplyResult is { Applied: true }, refreshPane: true);
        return LastTextPaneApplyResult!;
    }

    public SmartArtNodeEditResult ApplyPicture(byte[] imageBytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var smartArtShape = GetSelectedSmartArtShape();
        LastTextPaneEditResult = smartArtShape is null || string.IsNullOrWhiteSpace(SelectedModelId)
            ? SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.SetPicture,
                SelectedModelId,
                "Select a SmartArt row first.")
            : _getEditor().ReplaceSmartArtNodePicture(
                smartArtShape.Id,
                SelectedModelId,
                imageBytes,
                contentType);

        CompleteMutation(LastTextPaneEditResult.Applied, refreshPane: true);
        return LastTextPaneEditResult;
    }

    public SmartArtNodeEditResult ClearPicture()
    {
        var smartArtShape = GetSelectedSmartArtShape();
        LastTextPaneEditResult = smartArtShape is null || string.IsNullOrWhiteSpace(SelectedModelId)
            ? SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ClearPicture,
                SelectedModelId,
                "Select a SmartArt row first.")
            : _getEditor().ClearSmartArtNodePicture(smartArtShape.Id, SelectedModelId);

        CompleteMutation(LastTextPaneEditResult.Applied, refreshPane: true);
        return LastTextPaneEditResult;
    }

    public SmartArtNodeEditResult ToggleAssistant()
    {
        var smartArtShape = GetSelectedSmartArtShape();
        LastTextPaneEditResult = smartArtShape is null || string.IsNullOrWhiteSpace(SelectedModelId)
            ? SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ToggleAssistant,
                SelectedModelId,
                "Select a SmartArt hierarchy row first.")
            : _getEditor().ToggleSmartArtAssistant(smartArtShape.Id, SelectedModelId);

        CompleteMutation(LastTextPaneEditResult.Applied, refreshPane: true);
        return LastTextPaneEditResult;
    }

    public SmartArtNodeEditResult ApplyAction(SmartArtNodeEditKind kind)
    {
        if (string.IsNullOrWhiteSpace(SelectedModelId))
        {
            LastTextPaneEditResult = SmartArtNodeEditResult.NotApplied(
                kind,
                SelectedModelId,
                "Select a SmartArt row first.");
            Refresh();
            return LastTextPaneEditResult;
        }

        var intent = kind switch
        {
            SmartArtNodeEditKind.AddSiblingAfter => SmartArtNodeEditIntent.AddSiblingAfter(
                SelectedModelId,
                SmartArtEditingPlanner.DefaultNewNodeText),
            SmartArtNodeEditKind.AddChild => SmartArtNodeEditIntent.AddChild(
                SelectedModelId,
                SmartArtEditingPlanner.DefaultNewNodeText),
            SmartArtNodeEditKind.Remove => SmartArtNodeEditIntent.Remove(SelectedModelId),
            SmartArtNodeEditKind.MoveUp => SmartArtNodeEditIntent.MoveUp(SelectedModelId),
            SmartArtNodeEditKind.MoveDown => SmartArtNodeEditIntent.MoveDown(SelectedModelId),
            SmartArtNodeEditKind.Promote => SmartArtNodeEditIntent.Promote(SelectedModelId),
            SmartArtNodeEditKind.Demote => SmartArtNodeEditIntent.Demote(SelectedModelId),
            SmartArtNodeEditKind.AddAssistant => SmartArtNodeEditIntent.AddAssistant(
                SelectedModelId,
                "Assistant"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported SmartArt text-pane action.")
        };
        return ApplyEdit(intent);
    }

    public SmartArtNodeEditResult? ApplyKeyboardRoute(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers)
    {
        LastKeyboardRoute = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            key,
            modifiers,
            SelectedModelId);
        return LastKeyboardRoute is null
            ? null
            : ApplyEdit(LastKeyboardRoute.Intent);
    }

    public SmartArtLayoutApplyResult ApplyLayoutPreset(SmartArtLayoutPreset preset)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
            return SmartArtAuthoringPlanner.ApplyLayoutPreset(null, preset);

        SmartArtLayoutApplyResult? result = null;
        _getEditor().EditSmartArt(smartArtShape.Id, smartArt =>
        {
            result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset);
            if (result is not { Applied: true })
                return false;

            if (CommitMutation(smartArt, smartArtShape, allowCachedPackageEdit: true))
                return true;

            result = result with { Applied = false, Message = NativeRefreshFailureMessage };
            return false;
        });

        CompleteMutation(result is { Applied: true }, refreshPane: false);
        return result ?? new SmartArtLayoutApplyResult(
            false,
            "No SmartArt layout was changed.",
            null,
            null,
            SmartArtFamily.Unknown);
    }

    public SmartArtQuickStyleApplyResult ApplyQuickStylePreset(SmartArtQuickStylePreset preset)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
            return SmartArtAuthoringPlanner.ApplyQuickStylePreset(null, preset);

        SmartArtQuickStyleApplyResult? result = null;
        _getEditor().EditSmartArt(smartArtShape.Id, smartArt =>
        {
            result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset);
            if (result is not { Applied: true })
                return false;

            if (CommitMutation(smartArt, smartArtShape, allowCachedPackageEdit: true))
                return true;

            result = result with { Applied = false, Message = NativeRefreshFailureMessage };
            return false;
        });

        CompleteMutation(result is { Applied: true }, refreshPane: false);
        return result ?? new SmartArtQuickStyleApplyResult(
            false,
            "No SmartArt Quick Style was changed.",
            null,
            null);
    }

    public SmartArtColorApplyResult ApplyColorPreset(SmartArtColorPreset preset)
    {
        var editor = _getEditor();
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
        {
            LastColorApplyResult = SmartArtAuthoringPlanner.ApplyColorPreset(
                null,
                preset,
                ResolveCurrentSlideTheme());
            return LastColorApplyResult;
        }

        editor.EditSmartArt(smartArtShape.Id, smartArt =>
        {
            LastColorApplyResult = SmartArtAuthoringPlanner.ApplyColorPreset(
                smartArt,
                preset,
                ResolveCurrentSlideTheme(),
                editor.CurrentSlide?.ColorMapOverride);
            if (LastColorApplyResult is not { Applied: true })
                return false;

            if (CommitMutation(smartArt, smartArtShape, allowCachedPackageEdit: true))
                return true;

            LastColorApplyResult = LastColorApplyResult with
            {
                Applied = false,
                Message = NativeRefreshFailureMessage
            };
            return false;
        });

        CompleteMutation(LastColorApplyResult is { Applied: true }, refreshPane: false);
        return LastColorApplyResult!;
    }

    public bool ConvertSelectedToShapes()
    {
        var editor = _getEditor();
        if (editor.SelectedShapeIds.Count != 1 ||
            !editor.ConvertSmartArtToShapes(editor.SelectedShapeIds[0]))
        {
            return false;
        }

        CompleteMutation(applied: true, refreshPane: false);
        return true;
    }

    private SmartArtNodeEditResult ApplyEdit(SmartArtNodeEditIntent intent)
    {
        var editor = _getEditor();
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
        {
            LastTextPaneEditResult = SmartArtEditingPlanner.Apply(null, intent);
        }
        else
        {
            var previousData = smartArtShape.SmartArt is { } original
                ? SlideCloner.CloneSmartArt(original).Data
                : null;
            editor.EditSmartArt(smartArtShape.Id, smartArt =>
            {
                LastTextPaneEditResult = SmartArtEditingPlanner.Apply(smartArt.Data, intent);
                if (LastTextPaneEditResult is not { Applied: true })
                    return false;

                SelectedModelId = LastTextPaneEditResult.SelectedModelId;
                if (CommitMutation(smartArt, smartArtShape, previousData))
                    return true;

                LastTextPaneEditResult = LastTextPaneEditResult with
                {
                    Applied = false,
                    Message = NativeRefreshFailureMessage
                };
                return false;
            });
        }

        CompleteMutation(LastTextPaneEditResult is { Applied: true }, refreshPane: true);
        return LastTextPaneEditResult!;
    }

    private bool CommitMutation(
        SmartArtShape smartArt,
        SlideShape smartArtShape,
        SmartArtData? previousData = null,
        bool allowCachedPackageEdit = false)
    {
        if (smartArt.Data is null)
            return allowCachedPackageEdit;

        LastDataPartRewriteResult = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        if (LastDataPartRewriteResult is not { Applied: true })
            return false;

        var editor = _getEditor();
        LastDrawingCacheRegenerationResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            ResolveCurrentSlideTheme(),
            editor.CurrentSlide?.ColorMapOverride);
        if (LastDrawingCacheRegenerationResult is { Applied: true })
            return true;

        LastDrawingCacheRegenerationResult =
            SmartArtEditingPlanner.SynchronizePreservedDrawingText(smartArt, previousData);
        return LastDrawingCacheRegenerationResult is { Applied: true };
    }

    private SlideShape? GetSelectedSmartArtShape()
    {
        var editor = _getEditor();
        if (editor.SelectedShapeIds.Count != 1 || editor.CurrentSlide is not { } slide)
            return null;

        var shape = SlideShapeTraversal.FindById(slide, editor.SelectedShapeIds[0]);
        return shape?.Kind == SlideShapeKind.SmartArt && shape.SmartArt is not null
            ? shape
            : null;
    }

    private PresentationTheme ResolveCurrentSlideTheme()
    {
        var editor = _getEditor();
        var presentation = editor.Presentation;
        var slide = editor.CurrentSlide;
        var layout = slide is null
            ? null
            : presentation.Layouts.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, slide.LayoutId));
        var master = layout is null
            ? null
            : presentation.Masters.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, layout.MasterId));
        return master?.Theme ?? presentation.Theme;
    }

    private void CompleteMutation(bool applied, bool refreshPane)
    {
        if (applied)
        {
            _callbacks.MarkDirty();
            _callbacks.RefreshCanvas();
            _callbacks.UpdateHost();
        }

        if (refreshPane)
            Refresh();
    }
}
