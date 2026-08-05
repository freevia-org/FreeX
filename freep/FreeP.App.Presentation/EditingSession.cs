using Free.Shared.Drawing;
using FreeP.Core.Model;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace FreeP.App.Compositor;

// Wave 12B: TextSearchMatch, TextSearchOptions, PresentationTextSearch, ReplaceOneCommand,
// ReplaceAllCommand are all in FreeP.Core.Model (no extra using needed — same namespace chain).

// Paste offset: ~0.2 inches in EMU.
file static class PasteOffset
{
    internal const long Emu = DrawingMlCoordinateUnits.EmuPerInch / 5;
}

/// <summary>
/// Framework-free view-model for an active editing session.
///
/// Owns the <see cref="Presentation"/> and <see cref="PresentationCommandBus"/> and exposes a
/// clean API that the host (3A), slide pane (3B), and canvas (3C) bind to.  All mutations go
/// through the bus so they are fully undoable.
///
/// Design contract for downstream agents:
/// <list type="bullet">
///   <item><description>3B (thumbnail pane): bind to <see cref="CurrentSlideChanged"/>, read <see cref="CurrentSlideIndex"/>, call <see cref="SelectSlide"/>.</description></item>
///   <item><description>3C (canvas): bind to <see cref="SelectionChanged"/>, read <see cref="SelectedShapeIds"/>/<see cref="CurrentSlide"/>; call Move/Resize/Rotate/SetFill/SetOutline/Delete; use the run-format toggles for in-canvas text editing.</description></item>
/// </list>
/// </summary>
public sealed class EditingSession
{
    private const long Standard43WidthEmu = DrawingMlCoordinateUnits.EmuPerInch * 10;
    private const long StandardSlideHeightEmu = DrawingMlCoordinateUnits.EmuPerInch * 15 / 2;
    private const long Widescreen169WidthEmu = DrawingMlCoordinateUnits.EmuPerInch * 40 / 3;

    // ── Core state ────────────────────────────────────────────────────────────────

    private int _currentSlideIndex;
    private readonly List<uint> _selectedShapeIds = new();

    // ── Clipboard state ───────────────────────────────────────────────────────────

    /// <summary>
    /// Internal shape clipboard.  Each entry is a deep-clone captured at copy/cut time.
    /// Non-null + non-empty → <see cref="CanPaste"/> is true and <see cref="Paste"/> will paste shapes.
    /// </summary>
    private List<SlideShape>? _shapeClipboard;

    /// <summary>
    /// Internal slide clipboard.  Non-null → <see cref="Paste"/> will paste a slide when
    /// <see cref="_shapeClipboard"/> is empty.
    /// </summary>
    private Slide? _slideClipboard;

    // ── Format-painter clipboard ──────────────────────────────────────────────────

    private ShapeFill?          _fmtFill;
    private ShapeOutline?       _fmtOutline;
    private RunFormatSnapshot?  _fmtRun;
    private bool                 _formatPainterActive;

    // ── Construction ──────────────────────────────────────────────────────────────

    public EditingSession(Presentation presentation, PresentationCommandBus bus)
    {
        Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        Bus          = bus          ?? throw new ArgumentNullException(nameof(bus));
        // Initialize index to -1 so the clamp sets it correctly.
        _currentSlideIndex = -1;
        ClampCurrentSlide();
    }

    // ── Public model access ───────────────────────────────────────────────────────

    /// <summary>The presentation model. Treat as read-only outside of commands.</summary>
    public Presentation Presentation { get; }

    /// <summary>The undo/redo bus. Raise <see cref="Changed"/> on mutations routed here.</summary>
    public PresentationCommandBus Bus { get; }

    // ── Slide navigation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Zero-based index of the currently displayed slide.
    /// Clamped to [0, Count-1]. -1 only when the presentation has no slides.
    /// </summary>
    public int CurrentSlideIndex
    {
        get => _currentSlideIndex;
        private set
        {
            var clamped = Presentation.Slides.Count == 0
                ? -1
                : Math.Clamp(value, 0, Presentation.Slides.Count - 1);
            if (clamped == _currentSlideIndex) return;
            _currentSlideIndex = clamped;
            CurrentSlideChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The slide currently on stage, or null if the presentation is empty.</summary>
    public Slide? CurrentSlide =>
        _currentSlideIndex >= 0 && _currentSlideIndex < Presentation.Slides.Count
            ? Presentation.Slides[_currentSlideIndex]
            : null;

    /// <summary>The preserved OLE payload when exactly one embedded object is selected.</summary>
    public OleObjectInfo? SelectedOleObject =>
        CurrentSlide is { } slide && _selectedShapeIds.Count == 1
            ? FindShape(slide.Shapes, _selectedShapeIds[0])?.OleObject
            : null;

    /// <summary>
    /// Activates an inline embedded object from the live shape model. Resolving the payload here
    /// keeps external edits attached to the model even when a host text overlay commits as it
    /// loses focus to the external OLE application.
    /// </summary>
    public bool TryActivateInlineOleObject(
        uint shapeId,
        int logicalPosition,
        Action<byte[]>? onPayloadUpdated = null)
    {
        var shape = CurrentSlide is { } slide
            ? FindShape(slide.Shapes, shapeId)
            : null;
        if (shape?.TextBody is null)
            return false;

        return InCanvasRichTextEditBuffer.FindInlineOleObjectAt(
            shape.TextBody,
            logicalPosition,
            out var inlineObject)
            && OleActivationService.TryActivate(inlineObject, onPayloadUpdated);
    }

    /// <summary>
    /// Prepares and commits one SmartArt edit through the shared undo bus. The callback receives
    /// an isolated payload, so callers can run planner mutations and regenerate its package/cache
    /// state without exposing a partially edited model to the canvas.
    /// </summary>
    public bool EditSmartArt(uint shapeId, Func<SmartArtShape, bool> edit)
    {
        if (CurrentSlide is null || edit is null)
            return false;

        var shape = FindShape(CurrentSlide.Shapes, shapeId);
        if (shape is null ||
            shape.Kind != SlideShapeKind.SmartArt ||
            shape.SmartArt is null)
            return false;

        var before = SlideCloner.CloneSmartArt(shape.SmartArt);
        var after = SlideCloner.CloneSmartArt(shape.SmartArt);
        if (!edit(after))
            return false;

        Bus.Execute(new ReplaceSmartArtCommand(_currentSlideIndex, shapeId, before, after));
        return true;
    }

    /// <summary>
    /// Applies one supported SmartArt layout through the shared undoable edit path and refreshes
    /// the native data/cache payload before the replacement command is committed.
    /// </summary>
    public bool ApplySmartArtLayout(uint shapeId, SmartArtLayoutPreset preset) =>
        EditSmartArtWithPackageRefresh(
            shapeId,
            smartArt => SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset).Applied,
            allowCachedPackageEdit: true);

    /// <summary>
    /// Applies one supported SmartArt Quick Style through the shared undoable edit path and
    /// refreshes the native drawing cache so a saved package does not retain stale visuals.
    /// </summary>
    public bool ApplySmartArtQuickStyle(uint shapeId, SmartArtQuickStylePreset preset) =>
        EditSmartArtWithPackageRefresh(
            shapeId,
            smartArt => SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset).Applied,
            allowCachedPackageEdit: true);

    /// <summary>
    /// Applies one supported SmartArt Change Colors preset through the same shared, undoable
    /// package-refresh path used by layout and Quick Style edits.
    /// </summary>
    public bool ApplySmartArtColor(uint shapeId, SmartArtColorPreset preset)
    {
        return EditSmartArtWithPackageRefresh(
            shapeId,
            smartArt => SmartArtAuthoringPlanner.ApplyColorPreset(
                smartArt,
                preset,
                Presentation.Theme,
                CurrentSlide?.ColorMapOverride).Applied,
            allowCachedPackageEdit: true);
    }

    /// <summary>
    /// Replaces the image assigned to one node in a picture-backed SmartArt layout. The
    /// operation updates the shared node model, the diagram data/cache parts, and the cached
    /// media payload in one undoable command.
    /// </summary>
    public SmartArtNodeEditResult ReplaceSmartArtNodePicture(
        uint shapeId,
        string targetModelId,
        byte[] imageBytes,
        string contentType)
    {
        SmartArtNodeEditResult? result = null;
        string? failureMessage = null;
        var shape = CurrentSlide is { } slide
            ? FindShape(slide.Shapes, shapeId)
            : null;
        var applied = shape?.SmartArt is not null && EditSmartArt(shapeId, smartArt =>
        {
            result = SmartArtEditingPlanner.Apply(
                smartArt.Data,
                SmartArtNodeEditIntent.SetPicture(
                    targetModelId,
                    new ImagePart
                    {
                        Bytes = imageBytes.ToArray(),
                        ContentType = contentType,
                    }));
            if (!result.Applied)
            {
                failureMessage = result.Message;
                return false;
            }

            var dataRewrite = SmartArtEditingPlanner.RewriteDataPart(smartArt);
            if (!dataRewrite.Applied)
            {
                failureMessage = dataRewrite.Message;
                return false;
            }

            var cacheRefresh = SmartArtEditingPlanner.RegenerateDrawingCache(
                smartArt,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                Presentation.Theme,
                CurrentSlide?.ColorMapOverride);
            if (!cacheRefresh.Applied)
                failureMessage = cacheRefresh.Message;
            return cacheRefresh.Applied;
        });

        return applied && result is not null
            ? result
            : SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.SetPicture,
                targetModelId,
                failureMessage ?? "The selected SmartArt picture node could not be updated.");
    }

    /// <summary>
    /// Removes the image assigned to one SmartArt node, restoring its authored placeholder in
    /// the live model and refreshing the native data/cache package in one undoable command.
    /// </summary>
    public SmartArtNodeEditResult ClearSmartArtNodePicture(uint shapeId, string targetModelId)
    {
        SmartArtNodeEditResult? result = null;
        string? failureMessage = null;
        var shape = CurrentSlide is { } slide
            ? FindShape(slide.Shapes, shapeId)
            : null;
        var applied = shape?.SmartArt is not null && EditSmartArt(shapeId, smartArt =>
        {
            result = SmartArtEditingPlanner.Apply(
                smartArt.Data,
                SmartArtNodeEditIntent.ClearPicture(targetModelId));
            if (!result.Applied)
            {
                failureMessage = result.Message;
                return false;
            }

            var dataRewrite = SmartArtEditingPlanner.RewriteDataPart(smartArt);
            if (!dataRewrite.Applied)
            {
                failureMessage = dataRewrite.Message;
                return false;
            }

            var cacheRefresh = SmartArtEditingPlanner.RegenerateDrawingCache(
                smartArt,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                Presentation.Theme,
                CurrentSlide?.ColorMapOverride);
            if (!cacheRefresh.Applied)
                failureMessage = cacheRefresh.Message;
            return cacheRefresh.Applied;
        });

        return applied && result is not null
            ? result
            : SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ClearPicture,
                targetModelId,
                failureMessage ?? "The selected SmartArt picture node could not be cleared.");
    }

    /// <summary>
    /// Converts one SmartArt graphic to the ordinary shapes produced by its live layout (or its
    /// cached fallback when no live layout is available). The replacement is one undoable edit and
    /// retains the graphic's original z-order slot.
    /// </summary>
    public bool ConvertSmartArtToShapes(uint shapeId)
    {
        var slide = CurrentSlide;
        var smartArtShape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (slide is null || smartArtShape?.SmartArt is null)
            return false;

        var smartArt = smartArtShape.SmartArt;
        var converted = smartArt.Data is null
            ? null
            : SmartArtLayoutEngine.Layout(
                smartArt.Data,
                smartArtShape.OffsetXEmu,
                smartArtShape.OffsetYEmu,
                smartArtShape.ExtentCxEmu,
                smartArtShape.ExtentCyEmu,
                Presentation.Theme,
                slide.ColorMapOverride,
                smartArt.QuickStyle,
                smartArt.Colors)?.Select(SlideCloner.CloneShape).ToList();

        if (converted is not { Count: > 0 })
            converted = smartArt.FallbackShapes.Select(SlideCloner.CloneShape).ToList();
        if (converted.Count == 0)
            return false;

        RemapConvertedShapeIds(slide, converted);
        Bus.Execute(new ConvertSmartArtToShapesCommand(
            _currentSlideIndex,
            shapeId,
            smartArtShape,
            converted));

        ClearSelection();
        foreach (var shape in converted)
            Select(shape.Id, addToSelection: true);
        return true;
    }

    private static void RemapConvertedShapeIds(Slide slide, IReadOnlyList<SlideShape> shapes)
    {
        var used = new HashSet<uint>();
        foreach (var shape in slide.Shapes)
            CollectShapeIds(shape, used);

        var remap = new Dictionary<uint, uint>();
        uint next = 1;
        while (used.Contains(next))
            next++;

        foreach (var shape in shapes)
            AssignShapeIds(shape, used, remap, ref next);

        foreach (var shape in shapes)
            RewriteConnectorTargets(shape, remap);
    }

    private static void CollectShapeIds(SlideShape shape, HashSet<uint> used)
    {
        used.Add(shape.Id);
        foreach (var child in shape.Children)
            CollectShapeIds(child, used);
    }

    private static void AssignShapeIds(
        SlideShape shape,
        HashSet<uint> used,
        Dictionary<uint, uint> remap,
        ref uint next)
    {
        var oldId = shape.Id;
        while (used.Contains(next))
            next++;
        var newId = next++;
        used.Add(newId);
        if (!remap.ContainsKey(oldId))
            remap.Add(oldId, newId);
        shape.Id = newId;

        foreach (var child in shape.Children)
            AssignShapeIds(child, used, remap, ref next);
    }

    private static void RewriteConnectorTargets(SlideShape shape, IReadOnlyDictionary<uint, uint> remap)
    {
        if (shape.ConnectionStart is { } start && remap.TryGetValue(start.ShapeId, out var startId))
            start.ShapeId = startId;
        if (shape.ConnectionEnd is { } end && remap.TryGetValue(end.ShapeId, out var endId))
            end.ShapeId = endId;
        foreach (var child in shape.Children)
            RewriteConnectorTargets(child, remap);
    }

    /// <summary>
    /// Toggles the selected hierarchy node's assistant designation through the shared undoable
    /// package-refresh path.  PowerPoint stores this semantic distinction as dgm:pt type="asst".
    /// </summary>
    public SmartArtNodeEditResult ToggleSmartArtAssistant(uint shapeId, string targetModelId)
    {
        SmartArtNodeEditResult? result = null;
        EditSmartArtWithPackageRefresh(shapeId, smartArt =>
        {
            result = SmartArtEditingPlanner.Apply(
                smartArt.Data,
                SmartArtNodeEditIntent.ToggleAssistant(targetModelId));
            return result.Applied;
        });

        return result ?? SmartArtNodeEditResult.NotApplied(
            SmartArtNodeEditKind.ToggleAssistant,
            targetModelId,
            "The selected SmartArt graphic is not available.");
    }

    /// <summary>
    /// Adds a hierarchy assistant below the selected node through the shared undoable
    /// package-refresh path. PowerPoint stores the new node as dgm:pt type="asst".
    /// </summary>
    public SmartArtNodeEditResult AddSmartArtAssistant(
        uint shapeId,
        string targetModelId,
        string? text = null)
    {
        SmartArtNodeEditResult? result = null;
        EditSmartArtWithPackageRefresh(shapeId, smartArt =>
        {
            result = SmartArtEditingPlanner.Apply(
                smartArt.Data,
                SmartArtNodeEditIntent.AddAssistant(targetModelId, text));
            return result.Applied;
        });

        return result ?? SmartArtNodeEditResult.NotApplied(
            SmartArtNodeEditKind.AddAssistant,
            targetModelId,
            "The selected SmartArt graphic is not available.");
    }

    private bool EditSmartArtWithPackageRefresh(
        uint shapeId,
        Func<SmartArtShape, bool> edit,
        bool allowCachedPackageEdit = false)
    {
        var shape = CurrentSlide is { } slide
            ? FindShape(slide.Shapes, shapeId)
            : null;
        if (shape is null ||
            shape.Kind != SlideShapeKind.SmartArt ||
            shape.SmartArt is null)
            return false;

        return EditSmartArt(shapeId, smartArt =>
        {
            if (!edit(smartArt))
                return false;

            // Quick Style and Change Colors are native diagram-part edits.  A legacy or
            // preview-backed graphic may have those parts while its live data model is
            // unavailable; commit the package edit and keep the imported fallback drawing.
            // Layout and node edits leave this flag false because they need fresh cache output.
            if (smartArt.Data is null)
                return allowCachedPackageEdit;

            var dataRewrite = SmartArtEditingPlanner.RewriteDataPart(smartArt);
            if (!dataRewrite.Applied)
                return false;

            var cacheRefresh = SmartArtEditingPlanner.RegenerateDrawingCache(
                smartArt,
                shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.ExtentCxEmu,
                shape.ExtentCyEmu,
                Presentation.Theme,
                CurrentSlide?.ColorMapOverride);
            return cacheRefresh.Applied;
        });
    }

    // ── Selection ─────────────────────────────────────────────────────────────────

    /// <summary>The set of selected shape ids on the current slide.</summary>
    public IReadOnlyList<uint> SelectedShapeIds => _selectedShapeIds;

    /// <summary>
    /// Selects a shape. If <paramref name="addToSelection"/> is false (default), replaces
    /// the current selection.
    /// </summary>
    public void Select(uint shapeId, bool addToSelection = false)
    {
        var selectedShape = CurrentSlide is { } slide
            ? FindShape(slide.Shapes, shapeId)
            : null;
        if (selectedShape?.Chart?.ChartSelectionProtected == true)
            return;

        if (!addToSelection)
            _selectedShapeIds.Clear();
        if (!_selectedShapeIds.Contains(shapeId))
            _selectedShapeIds.Add(shapeId);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        if (_selectedShapeIds.Count == 0) return;
        _selectedShapeIds.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects all shapes on the current slide.</summary>
    public void SelectAll()
    {
        var slide = CurrentSlide;
        if (slide is null) return;
        _selectedShapeIds.Clear();
        foreach (var s in slide.Shapes)
        {
            if (s.Chart?.ChartSelectionProtected != true)
                _selectedShapeIds.Add(s.Id);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Events ────────────────────────────────────────────────────────────────────

    /// <summary>Fired when <see cref="CurrentSlideIndex"/> changes (navigation or insert/delete).</summary>
    public event EventHandler? CurrentSlideChanged;

    /// <summary>Fired when the selection set changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Surfaces the bus <see cref="PresentationCommandBus.Changed"/> event.</summary>
    public event Action? Changed
    {
        add    => Bus.Changed += value;
        remove => Bus.Changed -= value;
    }

    // ── Undo/redo ─────────────────────────────────────────────────────────────────

    public bool CanUndo => Bus.CanUndo;
    public bool CanRedo => Bus.CanRedo;

    public void Undo()
    {
        Bus.Undo();
        ClampCurrentSlide();
    }

    public void Redo()
    {
        Bus.Redo();
        ClampCurrentSlide();
    }

    // ── Slide operations ──────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a new slide after the current slide (or appends if empty).
    /// The new slide gets a default title based on its position.
    /// </summary>
    public void InsertSlide(string? layoutId = null)
    {
        var insertAt = Presentation.Slides.Count == 0 ? 0 : _currentSlideIndex + 1;
        var slide = new Slide
        {
            LayoutId = layoutId ?? Presentation.Slides.FirstOrDefault()?.LayoutId
        };
        slide.Title = $"Slide {insertAt + 1}";
        Bus.Execute(new InsertSlideCommand(insertAt, slide));
        CurrentSlideIndex = insertAt;
    }

    public bool SetCurrentSlideLayout(string layoutId)
    {
        if (CurrentSlide is null || string.IsNullOrWhiteSpace(layoutId))
        {
            return false;
        }

        if (!Presentation.Layouts.Any(layout => StringComparer.Ordinal.Equals(layout.Id, layoutId)))
        {
            return false;
        }

        Bus.Execute(new SetSlideLayoutCommand(_currentSlideIndex, layoutId));
        return StringComparer.Ordinal.Equals(CurrentSlide.LayoutId, layoutId);
    }

    public bool SetSlideTitle(int slideIndex, string title)
    {
        if (slideIndex < 0 || slideIndex >= Presentation.Slides.Count || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        Bus.Execute(new SetSlideTitleCommand(slideIndex, title));
        return StringComparer.Ordinal.Equals(Presentation.Slides[slideIndex].Title, title);
    }

    /// <summary>Sets one chart title through the shared undoable command bus.</summary>
    public bool SetChartTitle(int slideIndex, uint shapeId, string title)
    {
        if (slideIndex < 0 || slideIndex >= Presentation.Slides.Count || string.IsNullOrWhiteSpace(title))
            return false;

        var command = new SetChartTitleCommand(slideIndex, shapeId, title);
        Bus.Execute(command);
        return string.Equals(
            FindShape(Presentation.Slides[slideIndex].Shapes, shapeId)?.Chart?.Title,
            title.Trim(),
            StringComparison.Ordinal);
    }

    /// <summary>Deletes the current slide. Adjusts CurrentSlideIndex after deletion.</summary>
    public void DeleteCurrentSlide()
    {
        if (Presentation.Slides.Count == 0) return;
        var idx = _currentSlideIndex;
        Bus.Execute(new DeleteSlideCommand(idx));
        ClampCurrentSlide();
    }

    /// <summary>
    /// Duplicates the current slide (inserts an independent deep-clone immediately after).
    /// Moves current slide to the duplicate.
    /// </summary>
    public void DuplicateCurrentSlide()
    {
        if (Presentation.Slides.Count == 0) return;
        var idx = _currentSlideIndex;
        Bus.Execute(new DuplicateSlideCommand(idx));
        CurrentSlideIndex = idx + 1;
    }

    /// <summary>Moves the slide at <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void MoveSlide(int from, int to)
    {
        Bus.Execute(new MoveSlideCommand(from, to));
        // Track the current slide if it was the one that moved.
        if (_currentSlideIndex == from)
            CurrentSlideIndex = to < from ? to : to - 1; // list shrinks by 1 during remove
        ClampCurrentSlide();
    }

    /// <summary>Sets whether a slide is skipped by slide-show playback.</summary>
    public bool SetSlideHidden(int slideIndex, bool isHidden)
    {
        if (slideIndex < 0 || slideIndex >= Presentation.Slides.Count)
            return false;

        Bus.Execute(new SetSlideHiddenCommand(slideIndex, isHidden));
        return Presentation.Slides[slideIndex].IsHidden == isHidden;
    }

    /// <summary>Sets the presentation-wide slideshow media-control visibility through the undo bus.</summary>
    public bool SetShowMediaControls(bool show)
    {
        Bus.Execute(new SetShowMediaControlsCommand(Presentation.ShowMediaControls, show));
        return Presentation.ShowMediaControls == show;
    }

    /// <summary>Sets the presentation-wide slideshow playback settings through the undo bus.</summary>
    public bool SetSlideShowSettings(
        bool useSlideTimings,
        bool showWithAnimation,
        bool loopUntilStopped,
        PresentationShowType showType = PresentationShowType.PresentedBySpeaker,
        bool showBrowseScrollbar = true,
        uint? kioskRestartAfterMilliseconds = null,
        bool showWithNarration = true,
        bool showMediaControls = true,
        bool showMasterShapes = true)
    {
        Bus.Execute(new SetSlideShowSettingsCommand(
            Presentation.UseSlideTimings,
            Presentation.ShowWithAnimation,
            Presentation.LoopUntilStopped,
            Presentation.ShowType,
            Presentation.ShowBrowseScrollbar,
            Presentation.KioskRestartAfterMilliseconds,
            Presentation.ShowWithNarration,
            useSlideTimings,
            showWithAnimation,
            loopUntilStopped,
            showType,
            showBrowseScrollbar,
            kioskRestartAfterMilliseconds,
            showWithNarration,
            Presentation.ShowMediaControls,
            showMediaControls,
            Presentation.ShowMasterShapes,
            showMasterShapes));

        return Presentation.UseSlideTimings == useSlideTimings &&
            Presentation.ShowWithAnimation == showWithAnimation &&
            Presentation.ShowWithNarration == showWithNarration &&
            Presentation.LoopUntilStopped == loopUntilStopped &&
            Presentation.ShowType == showType &&
            Presentation.ShowBrowseScrollbar == showBrowseScrollbar &&
            Presentation.KioskRestartAfterMilliseconds == kioskRestartAfterMilliseconds &&
            Presentation.ShowMediaControls == showMediaControls &&
            Presentation.ShowMasterShapes == showMasterShapes;
    }

    /// <summary>Toggles the current slide's hidden/show state.</summary>
    public bool ToggleCurrentSlideHidden()
    {
        var slide = CurrentSlide;
        return slide is not null && SetSlideHidden(_currentSlideIndex, !slide.IsHidden);
    }

    /// <summary>Sets object visibility through the shared undo bus, including grouped children.</summary>
    public bool SetShapeHidden(uint shapeId, bool isHidden)
    {
        var slide = CurrentSlide;
        if (slide is null || FindShape(slide.Shapes, shapeId) is null)
            return false;

        Bus.Execute(new SetShapeHiddenCommand(_currentSlideIndex, shapeId, isHidden));
        return FindShape(slide.Shapes, shapeId)?.IsHidden == isHidden;
    }

    /// <summary>Toggles visibility for one selected object, including a grouped child.</summary>
    public bool ToggleShapeHidden(uint shapeId)
    {
        var shape = CurrentSlide is { } slide ? FindShape(slide.Shapes, shapeId) : null;
        return shape is not null && SetShapeHidden(shapeId, !shape.IsHidden);
    }

    /// <summary>Applies native PowerPoint Zoom properties through the shared undo bus.</summary>
    public bool SetZoomObjectProperties(uint shapeId, ZoomObjectProperties properties)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom)
            return false;

        Bus.Execute(new SetZoomObjectPropertiesCommand(_currentSlideIndex, shapeId, properties));
        var normalized = properties with
        {
            ImageType = properties.ImageType?.Trim().ToLowerInvariant(),
            TransitionDuration = properties.TransitionDuration?.Trim(),
        };
        return Equals(shape.PreservedObject.ZoomProperties, normalized);
    }

    /// <summary>Returns the supported Zoom properties when exactly one Zoom is selected.</summary>
    public ZoomObjectProperties? SelectedZoomObjectProperties =>
        _selectedShapeIds.Count == 1
            && CurrentSlide is { } slide
            && FindShape(slide.Shapes, _selectedShapeIds[0]) is
                { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom } shape
            ? ZoomObjectPropertiesPlanner.Effective(shape.PreservedObject)
            : null;

    /// <summary>Applies Zoom properties to the single selected Zoom through the undo bus.</summary>
    public bool SetSelectedZoomObjectProperties(ZoomObjectProperties properties) =>
        _selectedShapeIds.Count == 1
        && SetZoomObjectProperties(_selectedShapeIds[0], properties);

    /// <summary>Applies supported Zoom properties to one Summary Zoom tile only.</summary>
    public bool SetSummaryZoomTileProperties(
        uint shapeId,
        string sectionId,
        ZoomObjectProperties properties)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || string.IsNullOrWhiteSpace(sectionId)
            || shape.PreservedObject.SummaryZoomTargets.All(target =>
                !string.Equals(target.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        var before = shape.PreservedObject.RawXml;
        Bus.Execute(new SetSummaryZoomTilePropertiesCommand(
            _currentSlideIndex,
            shapeId,
            sectionId,
            properties));
        return !string.Equals(before, shape.PreservedObject.RawXml, StringComparison.Ordinal);
    }

    /// <summary>Applies supported properties to one selected Summary Zoom tile.</summary>
    public bool SetSelectedSummaryZoomTileProperties(
        string sectionId,
        ZoomObjectProperties properties) =>
        _selectedShapeIds.Count == 1
        && SetSummaryZoomTileProperties(_selectedShapeIds[0], sectionId, properties);

    /// <summary>Sets a user-authored cover image on one Slide or Section Zoom.</summary>
    public bool SetZoomCoverImage(uint shapeId, byte[] imageBytes, string contentType)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || shape.PreservedObject.SummaryZoomTargets.Count != 0)
            return false;

        Bus.Execute(new SetZoomCoverImageCommand(
            _currentSlideIndex,
            shapeId,
            imageBytes,
            contentType));
        return shape.PreservedObject.ZoomProperties?.ImageType == "cover";
    }

    /// <summary>Sets a cover image on the single selected Slide or Section Zoom.</summary>
    public bool SetSelectedZoomCoverImage(byte[] imageBytes, string contentType) =>
        _selectedShapeIds.Count == 1
        && SetZoomCoverImage(_selectedShapeIds[0], imageBytes, contentType);

    /// <summary>Restores the rendered target preview on one Slide or Section Zoom.</summary>
    public bool ResetZoomCoverImage(uint shapeId, byte[] previewBytes, string contentType)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || shape.PreservedObject.SummaryZoomTargets.Count != 0)
            return false;

        Bus.Execute(new SetZoomCoverImageCommand(
            _currentSlideIndex,
            shapeId,
            previewBytes,
            contentType,
            useCoverImage: false));
        return string.Equals(
            shape.PreservedObject.ZoomProperties?.ImageType,
            "preview",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Restores the rendered target preview on the single selected Zoom.</summary>
    public bool ResetSelectedZoomCoverImage(byte[] previewBytes, string contentType) =>
        _selectedShapeIds.Count == 1
        && ResetZoomCoverImage(_selectedShapeIds[0], previewBytes, contentType);

    /// <summary>Sets a cover image on one Summary Zoom tile identified by section id.</summary>
    public bool SetSummaryZoomTileCoverImage(
        uint shapeId,
        string sectionId,
        byte[] imageBytes,
        string contentType)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || string.IsNullOrWhiteSpace(sectionId)
            || shape.PreservedObject.SummaryZoomTargets.All(target =>
                !string.Equals(target.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        Bus.Execute(new SetZoomCoverImageCommand(
            _currentSlideIndex,
            shapeId,
            imageBytes,
            contentType,
            sectionId));
        return HasSummaryTileCover(shape.PreservedObject, sectionId);
    }

    /// <summary>Sets a cover image on one selected Summary Zoom tile.</summary>
    public bool SetSelectedSummaryZoomTileCoverImage(
        string sectionId,
        byte[] imageBytes,
        string contentType) =>
        _selectedShapeIds.Count == 1
        && SetSummaryZoomTileCoverImage(_selectedShapeIds[0], sectionId, imageBytes, contentType);

    /// <summary>Restores the rendered preview on one Summary Zoom tile.</summary>
    public bool ResetSummaryZoomTileCoverImage(
        uint shapeId,
        string sectionId,
        byte[] previewBytes,
        string contentType)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || string.IsNullOrWhiteSpace(sectionId)
            || shape.PreservedObject.SummaryZoomTargets.All(target =>
                !string.Equals(target.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        Bus.Execute(new SetZoomCoverImageCommand(
            _currentSlideIndex,
            shapeId,
            previewBytes,
            contentType,
            sectionId,
            useCoverImage: false));
        return HasSummaryTileImageType(shape.PreservedObject, sectionId, "preview");
    }

    /// <summary>Restores the rendered preview on one selected Summary Zoom tile.</summary>
    public bool ResetSelectedSummaryZoomTileCoverImage(
        string sectionId,
        byte[] previewBytes,
        string contentType) =>
        _selectedShapeIds.Count == 1
        && ResetSummaryZoomTileCoverImage(
            _selectedShapeIds[0], sectionId, previewBytes, contentType);

    /// <summary>Sets one Summary Zoom tile's native offset and scale factors through undo.</summary>
    public bool SetSummaryZoomTileLayout(
        uint shapeId,
        string sectionId,
        int offsetFactorX,
        int offsetFactorY,
        int scaleFactorX,
        int scaleFactorY)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || string.IsNullOrWhiteSpace(sectionId)
            || shape.PreservedObject.SummaryZoomTargets.All(target =>
                !string.Equals(target.SectionId, sectionId, StringComparison.OrdinalIgnoreCase)))
            return false;

        Bus.Execute(new SetSummaryZoomTileLayoutCommand(
            _currentSlideIndex,
            shapeId,
            sectionId,
            offsetFactorX,
            offsetFactorY,
            scaleFactorX,
            scaleFactorY));
        var target = shape.PreservedObject.SummaryZoomTargets.First(candidate =>
            string.Equals(candidate.SectionId, sectionId, StringComparison.OrdinalIgnoreCase));
        return target.OffsetFactorX == offsetFactorX
            && target.OffsetFactorY == offsetFactorY
            && target.ScaleFactorX == scaleFactorX
            && target.ScaleFactorY == scaleFactorY;
    }

    /// <summary>Sets one selected Summary Zoom tile's native offset and scale factors.</summary>
    public bool SetSelectedSummaryZoomTileLayout(
        string sectionId,
        int offsetFactorX,
        int offsetFactorY,
        int scaleFactorX,
        int scaleFactorY) =>
        _selectedShapeIds.Count == 1
        && SetSummaryZoomTileLayout(
            _selectedShapeIds[0],
            sectionId,
            offsetFactorX,
            offsetFactorY,
            scaleFactorX,
            scaleFactorY);

    /// <summary>Replaces the ordered section membership of a Summary Zoom. Undoable.</summary>
    public bool SetSummaryZoomTargets(uint shapeId, IEnumerable<string> targetSectionIds)
    {
        var slide = CurrentSlide;
        var shape = slide is null ? null : FindShape(slide.Shapes, shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom }
            || shape.PreservedObject?.ObjectKind != PreservedObjectKind.Zoom
            || shape.PreservedObject.SummaryZoomTargets.Count < 2
            || !SummaryZoomTargetPlanner.TryBuildPlan(
                Presentation,
                shape.PreservedObject,
                targetSectionIds,
                out var plan))
            return false;

        Bus.Execute(new SetSummaryZoomTargetsCommand(
            _currentSlideIndex,
            shapeId,
            plan.Targets,
            plan.RawXml));
        return string.Equals(shape.PreservedObject.RawXml, plan.RawXml, StringComparison.Ordinal);
    }

    public bool SetSelectedSummaryZoomTargets(IEnumerable<string> targetSectionIds) =>
        _selectedShapeIds.Count == 1
        && SetSummaryZoomTargets(_selectedShapeIds[0], targetSectionIds);

    private static bool HasSummaryTileCover(PreservedObjectInfo info, string sectionId)
    {
        try
        {
            var root = XElement.Parse(info.RawXml);
            return root.Descendants().Any(element =>
                element.Name.LocalName == "summaryZmObj"
                && string.Equals(element.Attribute("sectionId")?.Value, sectionId,
                    StringComparison.OrdinalIgnoreCase)
                && element.Descendants().Any(child =>
                    child.Name.LocalName == "zmPr"
                    && string.Equals(child.Attribute("imageType")?.Value, "cover",
                        StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSummaryTileImageType(
        PreservedObjectInfo info,
        string sectionId,
        string imageType)
    {
        try
        {
            var root = XElement.Parse(info.RawXml);
            return root.Descendants().Any(element =>
                element.Name.LocalName == "summaryZmObj"
                && string.Equals(element.Attribute("sectionId")?.Value, sectionId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    element.Descendants().FirstOrDefault(child => child.Name.LocalName == "zmPr")
                        ?.Attribute("imageType")?.Value,
                    imageType,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Renames an object through the shared undo bus, including grouped children.</summary>
    public bool SetShapeName(uint shapeId, string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        var slide = CurrentSlide;
        if (slide is null || normalized.Length == 0 || FindShape(slide.Shapes, shapeId) is null)
            return false;

        Bus.Execute(new SetShapeNameCommand(_currentSlideIndex, shapeId, normalized));
        return string.Equals(FindShape(slide.Shapes, shapeId)?.Name, normalized, StringComparison.Ordinal);
    }

    /// <summary>Navigates to the slide at <paramref name="index"/> and clears selection.</summary>
    public void SelectSlide(int index)
    {
        _formatPainterActive = false;
        ClearSelection();
        CurrentSlideIndex = index;
    }

    public bool AddSectionAtCurrentSlide(string? name = null) =>
        AddSectionAtSlide(CurrentSlideIndex, name);

    public bool AddSectionAtSlide(int slideIndex, string? name = null)
    {
        var nextSections = SlideSectionPlanner.PlanAddSection(
            Presentation.Slides,
            Presentation.Sections,
            slideIndex,
            name);
        if (nextSections is null)
            return false;

        Bus.Execute(new ReplaceSlideSectionsCommand(nextSections));
        return true;
    }

    public bool RenameSection(int sectionIndex, string? name)
    {
        var nextSections = SlideSectionPlanner.PlanRenameSection(
            Presentation.Slides,
            Presentation.Sections,
            sectionIndex,
            name);
        if (nextSections is null)
            return false;

        Bus.Execute(new ReplaceSlideSectionsCommand(nextSections));
        return true;
    }

    public bool RemoveSection(int sectionIndex)
    {
        var nextSections = SlideSectionPlanner.PlanRemoveSection(
            Presentation.Slides,
            Presentation.Sections,
            sectionIndex);
        if (nextSections is null)
            return false;

        Bus.Execute(new ReplaceSlideSectionsCommand(nextSections));
        return true;
    }

    public bool RemoveAllSections()
    {
        if (Presentation.Sections.Count == 0)
            return false;

        Bus.Execute(new ReplaceSlideSectionsCommand(SlideSectionPlanner.PlanRemoveAllSections()));
        return true;
    }

    // ── Shape operations (operate on current slide) ───────────────────────────────

    /// <summary>Adds a shape to the current slide.</summary>
    public void AddShape(SlideShape shape)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new AddShapeCommand(_currentSlideIndex, shape));
    }

    /// <summary>
    /// Inserts a native PowerPoint Slide Zoom targeting <paramref name="targetSlideId"/>.
    /// The target must be a different slide in this presentation; the operation is undoable.
    /// </summary>
    public SlideShape InsertSlideZoom(string targetSlideId)
    {
        if (CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to insert a Slide Zoom.");

        var shape = SlideZoomInsertionPlanner.CreateShape(
            Presentation,
            _currentSlideIndex,
            targetSlideId);
        AddShape(shape);
        return shape;
    }

    /// <summary>Inserts a native PowerPoint Section Zoom targeting an existing section.</summary>
    public SlideShape InsertSectionZoom(string targetSectionId)
    {
        if (CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to insert a Section Zoom.");

        var shape = SectionZoomInsertionPlanner.CreateShape(Presentation, targetSectionId);
        AddShape(shape);
        return shape;
    }

    /// <summary>Inserts a native multi-target PowerPoint Summary Zoom.</summary>
    public SlideShape InsertSummaryZoom(IEnumerable<string> targetSectionIds)
    {
        if (CurrentSlide is null)
            throw new InvalidOperationException("A current slide is required to insert a Summary Zoom.");

        var shape = SummaryZoomInsertionPlanner.CreateShape(Presentation, targetSectionIds);
        AddShape(shape);
        return shape;
    }

    /// <summary>Retargets an existing Slide Zoom to another slide. Undoable.</summary>
    public bool SetSlideZoomTarget(uint shapeId, string targetSlideId)
    {
        if (CurrentSlide is null
            || !SlideZoomInsertionPlanner.TryBuildPlan(
                Presentation,
                _currentSlideIndex,
                targetSlideId,
                out var plan)
            || !IsSingleTargetZoom(shapeId))
            return false;

        Bus.Execute(new SetZoomTargetCommand(
            _currentSlideIndex,
            shapeId,
            ZoomTargetKind.Slide,
            plan.TargetSlideNumericId,
            sectionId: null,
            $"Zoom to {plan.TargetDisplayName}"));
        return CurrentSlide is { } slide
            && FindShape(slide.Shapes, shapeId)?.PreservedObject?.ZoomTargetSlideNumericId
                == plan.TargetSlideNumericId;
    }

    /// <summary>Retargets an existing Section Zoom to another section. Undoable.</summary>
    public bool SetSectionZoomTarget(uint shapeId, string targetSectionId)
    {
        if (CurrentSlide is null
            || !SectionZoomInsertionPlanner.TryBuildPlan(Presentation, targetSectionId, out var plan)
            || !IsSingleTargetZoom(shapeId))
            return false;

        Bus.Execute(new SetZoomTargetCommand(
            _currentSlideIndex,
            shapeId,
            ZoomTargetKind.Section,
            slideNumericId: null,
            plan.TargetSectionId,
            $"Zoom to {plan.TargetDisplayName}"));
        return CurrentSlide is { } slide
            && string.Equals(
                FindShape(slide.Shapes, shapeId)?.PreservedObject?.ZoomTargetSectionId,
                plan.TargetSectionId,
                StringComparison.Ordinal);
    }

    public bool SetSelectedSlideZoomTarget(string targetSlideId) =>
        _selectedShapeIds.Count == 1
        && SetSlideZoomTarget(_selectedShapeIds[0], targetSlideId);

    public bool SetSelectedSectionZoomTarget(string targetSectionId) =>
        _selectedShapeIds.Count == 1
        && SetSectionZoomTarget(_selectedShapeIds[0], targetSectionId);

    private bool IsSingleTargetZoom(uint shapeId) =>
        CurrentSlide is { } slide
        && FindShape(slide.Shapes, shapeId) is
            { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom } shape
        && shape.PreservedObject.SummaryZoomTargets.Count == 0;

    /// <summary>Deletes all currently selected shapes.</summary>
    public void DeleteSelected()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var toDelete = _selectedShapeIds.ToList();
        ClearSelection();

        var commands = toDelete
            .Select(id => (IPresentationCommand)new DeleteShapeCommand(_currentSlideIndex, id))
            .ToArray();
        if (commands.Length == 1)
            Bus.Execute(commands[0]);
        else
            Bus.Execute(new BatchCommand("Delete Shapes", commands));
    }

    /// <summary>Translates all selected shapes by (dx, dy) in EMU.</summary>
    public void MoveSelected(long dxEmu, long dyEmu)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new MoveShapeCommand(_currentSlideIndex, id, dxEmu, dyEmu));
    }

    /// <summary>Sets absolute position and size for a single shape.</summary>
    public void ResizeShape(uint shapeId, long newOffsetX, long newOffsetY, long newCx, long newCy)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new ResizeShapeCommand(_currentSlideIndex, shapeId, newOffsetX, newOffsetY, newCx, newCy));
    }

    /// <summary>
    /// Applies a canvas transform plan to the current selection as one undoable operation.
    /// Plans are filtered against the live selection so a stale pointer-up cannot transform a
    /// shape that was removed from the selection while the pointer was captured.
    /// </summary>
    public bool ApplySelectedTransforms(IEnumerable<CanvasShapeTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return false;

        var selected = _selectedShapeIds.ToHashSet();
        var commands = new List<IPresentationCommand>();
        foreach (var transform in transforms)
        {
            if (!selected.Contains(transform.ShapeId))
                continue;

            var shape = FindShape(CurrentSlide.Shapes, transform.ShapeId);
            if (shape is null || shape.Chart?.ChartSelectionProtected == true)
                continue;

            if (shape.OffsetXEmu != transform.XEmu ||
                shape.OffsetYEmu != transform.YEmu ||
                shape.ExtentCxEmu != transform.CxEmu ||
                shape.ExtentCyEmu != transform.CyEmu)
            {
                commands.Add(new ResizeShapeCommand(
                    _currentSlideIndex,
                    transform.ShapeId,
                    transform.XEmu,
                    transform.YEmu,
                    transform.CxEmu,
                    transform.CyEmu));
            }

            var normalizedRotation = RotationOptionsPlanner.Normalize(transform.RotationDeg);
            if (Math.Abs(shape.RotationDeg - normalizedRotation) > 0.0001)
            {
                commands.Add(new RotateShapeCommand(
                    _currentSlideIndex,
                    transform.ShapeId,
                    normalizedRotation));
            }
        }

        if (commands.Count == 0)
            return false;

        Bus.Execute(new BatchCommand("Transform Shapes", commands));
        return true;
    }

    /// <summary>Sets or removes one authored DrawingML preset-geometry adjustment.</summary>
    public void SetShapeGeometryAdjustment(uint shapeId, string name, double? value)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new SetShapeGeometryAdjustmentCommand(_currentSlideIndex, shapeId, name, value));
    }

    /// <summary>Moves one vertex or curve control point in an imported custom geometry path.</summary>
    public void SetCustomGeometryPoint(
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double x,
        double y,
        CustomGeometryPointSlot slot = CustomGeometryPointSlot.Endpoint)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new SetCustomGeometryPointCommand(
            _currentSlideIndex, shapeId, pathIndex, segmentIndex, x, y, slot));
    }

    /// <summary>Sets one authored ArcTo angle or radius in an imported custom geometry path.</summary>
    public void SetCustomGeometryArcPoint(
        uint shapeId,
        int pathIndex,
        int segmentIndex,
        double value,
        CustomGeometryArcPointSlot slot)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new SetCustomGeometryArcPointCommand(
            _currentSlideIndex, shapeId, pathIndex, segmentIndex, value, slot));
    }

    /// <summary>Adds a straight custom-geometry vertex after the selected endpoint.</summary>
    public bool TryInsertCustomGeometryPoint(uint shapeId, string handleName)
    {
        if (CurrentSlide is null)
            return false;

        var shape = FindShape(CurrentSlide.Shapes, shapeId);
        if (shape is null || !ShapeGeometryAdjustmentPlanner.TryBuildCustomVertexInsertion(
                shape, handleName, out var pathIndex, out var segmentIndex, out var x, out var y))
            return false;

        Bus.Execute(new InsertCustomGeometryPointCommand(
            _currentSlideIndex, shapeId, pathIndex, segmentIndex, x, y));
        return true;
    }

    /// <summary>Deletes the selected straight custom-geometry vertex when path structure permits it.</summary>
    public bool TryDeleteCustomGeometryPoint(uint shapeId, string handleName)
    {
        if (CurrentSlide is null)
            return false;

        var shape = FindShape(CurrentSlide.Shapes, shapeId);
        if (shape is null || !ShapeGeometryAdjustmentPlanner.CanDeleteCustomVertex(shape, handleName) ||
            !ShapeGeometryAdjustmentPlanner.TryGetCustomVertexTarget(
                shape, handleName, out var pathIndex, out var segmentIndex))
            return false;

        Bus.Execute(new DeleteCustomGeometryPointCommand(
            _currentSlideIndex, shapeId, pathIndex, segmentIndex));
        return true;
    }

    /// <summary>Sets the rotation (degrees, clockwise) of a single shape.</summary>
    public void RotateShape(uint shapeId, double newRotationDeg)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new RotateShapeCommand(_currentSlideIndex, shapeId, newRotationDeg));
    }

    /// <summary>Sets the rotation of every selected editable shape as one undoable operation.</summary>
    public bool SetSelectedRotation(double newRotationDeg)
    {
        if (CurrentSlide is null || !double.IsFinite(newRotationDeg))
            return false;

        var normalized = RotationOptionsPlanner.Normalize(newRotationDeg);
        var commands = _selectedShapeIds
            .Select(id => FindShape(CurrentSlide.Shapes, id))
            .Where(shape => shape is not null)
            .Where(shape => Math.Abs(shape!.RotationDeg - normalized) > 0.0001)
            .Select(shape => (IPresentationCommand)new RotateShapeCommand(
                _currentSlideIndex, shape!.Id, normalized))
            .ToList();

        if (commands.Count == 0)
            return false;

        Bus.Execute(new BatchCommand("Set Rotation", commands));
        return true;
    }

    /// <summary>Toggles a single shape's horizontal or vertical mirror state.</summary>
    public void FlipShape(uint shapeId, bool horizontal)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new FlipShapeCommand(_currentSlideIndex, shapeId, horizontal));
    }

    /// <summary>Flips all selected shapes horizontally in one undoable operation.</summary>
    public void FlipSelectedHorizontal() => FlipSelected(horizontal: true);

    /// <summary>Flips all selected shapes vertically in one undoable operation.</summary>
    public void FlipSelectedVertical() => FlipSelected(horizontal: false);

    private void FlipSelected(bool horizontal)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var commands = _selectedShapeIds
            .Select(id => (IPresentationCommand)new FlipShapeCommand(_currentSlideIndex, id, horizontal));
        Bus.Execute(new BatchCommand(horizontal ? "Flip Horizontal" : "Flip Vertical", commands));
    }

    /// <summary>Rotates all selected shapes 90 degrees counter-clockwise in one undoable operation.</summary>
    public void RotateSelectedLeft90() => RotateSelectedBy(-90);

    /// <summary>Rotates all selected shapes 90 degrees clockwise in one undoable operation.</summary>
    public void RotateSelectedRight90() => RotateSelectedBy(90);

    private void RotateSelectedBy(double deltaDegrees)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;

        var commands = _selectedShapeIds.Select(id =>
        {
            var shape = FindShape(CurrentSlide.Shapes, id);
            var rotation = shape?.RotationDeg ?? 0;
            return (IPresentationCommand)new RotateShapeCommand(
                _currentSlideIndex, id, rotation + deltaDegrees);
        });
        Bus.Execute(new BatchCommand(
            deltaDegrees < 0 ? "Rotate Left 90" : "Rotate Right 90", commands));
    }

    /// <summary>Sets the source crop fractions on a picture and records one undoable edit.</summary>
    public bool SetPictureCrop(uint shapeId, PictureCropValues values)
    {
        if (CurrentSlide is null || !PictureCropAuthoringPlanner.TryPlan(
                values.Left, values.Top, values.Right, values.Bottom, out var plan))
            return false;

        var shape = FindShape(CurrentSlide.Shapes, shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        Bus.Execute(new SetPictureCropCommand(
            _currentSlideIndex,
            shapeId,
            plan.Left,
            plan.Top,
            plan.Right,
            plan.Bottom));
        return true;
    }

    /// <summary>Applies one crop edit to every selected picture.</summary>
    public int SetSelectedPictureCrop(PictureCropValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            if (SetPictureCrop(id, values))
                count++;
        }
        return count;
    }

    /// <summary>Sets color effects on one picture and records one undoable edit.</summary>
    public bool SetPictureColorEffects(uint shapeId, PictureColorEffectValues values)
    {
        if (CurrentSlide is null)
            return false;

        var shape = FindShape(CurrentSlide.Shapes, shapeId);
        if (shape?.Kind != SlideShapeKind.Picture)
            return false;

        Bus.Execute(new SetPictureColorEffectsCommand(_currentSlideIndex, shapeId, values));
        return true;
    }

    /// <summary>Applies one picture color-effect edit to every selected picture.</summary>
    public int SetSelectedPictureColorEffects(PictureColorEffectValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            if (SetPictureColorEffects(id, values))
                count++;
        }
        return count;
    }

    /// <summary>Applies one outer-shadow preset to every selected shape.</summary>
    public int SetSelectedShapeShadow(ShapeShadowValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShapeShadowCommand(_currentSlideIndex, id, values));
            count++;
        }

        return count;
    }

    /// <summary>Applies one glow preset to every selected shape.</summary>
    public int SetSelectedShapeGlow(ShapeGlowValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShapeGlowCommand(_currentSlideIndex, id, values));
            count++;
        }

        return count;
    }

    /// <summary>Applies one soft-edge preset to every selected shape.</summary>
    public int SetSelectedShapeSoftEdge(ShapeSoftEdgeValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShapeSoftEdgeCommand(_currentSlideIndex, id, values));
            count++;
        }

        return count;
    }

    /// <summary>Applies one bevel preset to every selected shape.</summary>
    public int SetSelectedShapeBevel(ShapeBevelValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShapeBevelCommand(_currentSlideIndex, id, values));
            count++;
        }

        return count;
    }

    /// <summary>Applies one 3-D styling preset to every selected shape.</summary>
    public int SetSelectedShape3d(Shape3dValues values)
    {
        var count = 0;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShape3dCommand(_currentSlideIndex, id, values));
            count++;
        }

        return count;
    }

    /// <summary>Sets fill on all selected shapes.</summary>
    public void SetSelectedFill(ShapeFill? fill)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new SetShapeFillCommand(_currentSlideIndex, id, fill));
    }

    /// <summary>Sets fill transparency on all selected shapes while preserving fill kind, theme, and stops.</summary>
    public void SetSelectedFillTransparency(double transparencyPercent)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var shape = FindShape(CurrentSlide.Shapes, id);
            var fill = ShapeTransparencyPlanner.ApplyFill(shape?.Fill, transparencyPercent);
            if (shape?.Fill is not null && !ReferenceEquals(fill, shape.Fill))
                Bus.Execute(new SetShapeFillCommand(_currentSlideIndex, id, fill));
        }
    }

    /// <summary>Sets outline on all selected shapes.</summary>
    public void SetSelectedOutline(ShapeOutline? outline)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new SetShapeOutlineCommand(_currentSlideIndex, id, outline));
    }

    /// <summary>Sets outline transparency on all selected shapes while preserving stroke geometry.</summary>
    public void SetSelectedOutlineTransparency(double transparencyPercent)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var shape = FindShape(CurrentSlide.Shapes, id);
            var outline = ShapeTransparencyPlanner.ApplyOutline(shape?.Outline, transparencyPercent);
            if (shape?.Outline is not null && !ReferenceEquals(outline, shape.Outline))
                Bus.Execute(new SetShapeOutlineCommand(_currentSlideIndex, id, outline));
        }
    }

    /// <summary>
    /// Brings the first selected shape one step forward in z-order (swap with next shape).
    /// </summary>
    public void BringForward()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var id = _selectedShapeIds[0];
        var shapes = FindContainingShapeList(CurrentSlide.Shapes, id);
        if (shapes is null) return;
        var idx    = shapes.FindIndex(s => s.Id == id);
        if (idx < 0 || idx >= shapes.Count - 1) return;
        Bus.Execute(new ReorderShapeCommand(_currentSlideIndex, id, idx + 1));
    }

    /// <summary>
    /// Sends the first selected shape one step backward in z-order (swap with previous shape).
    /// </summary>
    public void SendBackward()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var id = _selectedShapeIds[0];
        var shapes = FindContainingShapeList(CurrentSlide.Shapes, id);
        if (shapes is null) return;
        var idx    = shapes.FindIndex(s => s.Id == id);
        if (idx <= 0) return;
        Bus.Execute(new ReorderShapeCommand(_currentSlideIndex, id, idx - 1));
    }

    /// <summary>
    /// Moves one selected shape within its containing reading-order sibling list.
    /// Group children remain inside their parent group while moving among siblings.
    /// </summary>
    public bool MoveSelectedShapeInReadingOrder(int offset)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count != 1) return false;
        var step = Math.Sign(offset);
        if (step == 0) return false;

        var id = _selectedShapeIds[0];
        var shapes = FindContainingShapeList(CurrentSlide.Shapes, id);
        if (shapes is null) return false;
        var idx = shapes.FindIndex(s => s.Id == id);
        var targetIndex = idx + step;
        if (idx < 0 || targetIndex < 0 || targetIndex >= shapes.Count) return false;

        Bus.Execute(new ReorderShapeCommand(_currentSlideIndex, id, targetIndex));
        return true;
    }

    private static List<SlideShape>? FindContainingShapeList(
        List<SlideShape> shapes,
        uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
            {
                return shapes;
            }

            if (shape.Children.Count > 0 &&
                FindContainingShapeList(shape.Children, shapeId) is { } childList)
            {
                return childList;
            }
        }

        return null;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId) return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    // ── Transition operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Sets (or clears) the slide transition on the current slide. Undoable.
    /// Pass null to remove the transition.
    /// </summary>
    public void SetTransition(SlideTransition? transition)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new SetSlideTransitionCommand(_currentSlideIndex, transition));
    }

    /// <summary>Sets or clears the current slide's transition sound as one undoable edit.</summary>
    public void SetCurrentSlideTransitionSound(TransitionSound? sound)
    {
        if (CurrentSlide is null || (CurrentSlideTransition is null && sound is null)) return;

        var transition = PresentationTransitionCommandPlanner.CloneTransition(CurrentSlideTransition)
            ?? new SlideTransition
            {
                Kind = TransitionKind.Fade,
                DurationMs = PresentationTransitionCommandPlanner.DefaultDurationMs,
            };
        transition.Sound = sound;
        SetTransition(transition);
    }

    /// <summary>Gets the transition for the current slide, or null if none.</summary>
    public SlideTransition? CurrentSlideTransition => CurrentSlide?.Transition;

    /// <summary>Replaces the current slide's paragraph-build XML as one undoable edit.</summary>
    public void SetCurrentSlideAnimationBuildList(string? buildListXml)
    {
        if (CurrentSlide is null)
            return;

        if (string.Equals(CurrentSlide.AnimationBuildListXml, buildListXml, StringComparison.Ordinal))
            return;

        Bus.Execute(new SetSlideAnimationBuildListCommand(_currentSlideIndex, buildListXml));
    }

    // ── Animation operations ──────────────────────────────────────────────────────

    /// <summary>Read-only ordered animation list for the current slide.</summary>
    public IReadOnlyList<ShapeAnimation> CurrentSlideAnimations =>
        (IReadOnlyList<ShapeAnimation>?)CurrentSlide?.Animations ?? Array.Empty<ShapeAnimation>();

    /// <summary>
    /// Appends an animation to the current slide's build sequence. Undoable.
    /// If <paramref name="shapeId"/> is 0 and a shape is selected, uses the first selected shape.
    /// </summary>
    public void AddAnimation(uint shapeId, ShapeAnimation animation)
    {
        if (CurrentSlide is null) return;
        var id = shapeId != 0 ? shapeId
                 : _selectedShapeIds.Count > 0 ? _selectedShapeIds[0] : 0u;
        if (id == 0) return;
        animation.ShapeId = id;
        Bus.Execute(new AddShapeAnimationCommand(_currentSlideIndex, animation));
    }

    /// <summary>Removes the animation at <paramref name="index"/> from the current slide. Undoable.</summary>
    public void RemoveAnimation(int index)
    {
        if (CurrentSlide is null) return;
        if (index < 0 || index >= CurrentSlide.Animations.Count) return;
        Bus.Execute(new RemoveShapeAnimationCommand(_currentSlideIndex, index));
    }

    /// <summary>
    /// Moves the animation at <paramref name="fromIndex"/> to <paramref name="toIndex"/>. Undoable.
    /// </summary>
    public void MoveAnimation(int fromIndex, int toIndex)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new ReorderShapeAnimationCommand(_currentSlideIndex, fromIndex, toIndex));
    }

    /// <summary>
    /// Replaces the animation at <paramref name="index"/> with <paramref name="animation"/>. Undoable.
    /// </summary>
    public void SetAnimation(int index, ShapeAnimation animation)
    {
        if (CurrentSlide is null) return;
        if (index < 0 || index >= CurrentSlide.Animations.Count) return;
        Bus.Execute(new SetShapeAnimationCommand(_currentSlideIndex, index, animation));
    }

    // ── Text / run-format operations ──────────────────────────────────────────────

    /// <summary>
    /// Toggles bold on every run in the first selected shape's text body.
    /// The toggle direction is based on the majority: if all runs are bold, turns them off;
    /// otherwise turns them all on.
    /// </summary>
    public void ToggleBoldOnSelection()      => TogglePropOnSelection(RunToggleKind.Bold);
    public void ToggleItalicOnSelection()    => TogglePropOnSelection(RunToggleKind.Italic);
    public void ToggleUnderlineOnSelection() => TogglePropOnSelection(RunToggleKind.Underline);
    public void ToggleSuperscriptOnSelection() => TogglePropOnSelection(RunToggleKind.Superscript);
    public void ToggleSubscriptOnSelection()   => TogglePropOnSelection(RunToggleKind.Subscript);

    /// <summary>
    /// Sets font family on every run in all selected shapes.
    /// </summary>
    public void SetFontOnSelection(string? fontFamily)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var s = FindShape(CurrentSlide.Shapes, id);
            if (s?.TextBody is null) continue;
            for (int pi = 0; pi < s.TextBody.Paragraphs.Count; pi++)
            for (int ri = 0; ri < s.TextBody.Paragraphs[pi].Runs.Count; ri++)
                Bus.Execute(new SetRunFontCommand(_currentSlideIndex, id, pi, ri, fontFamily));
        }
    }

    /// <summary>Sets font size (pt) on every run in all selected shapes.</summary>
    public void SetFontSizeOnSelection(double? sizePt)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var s = FindShape(CurrentSlide.Shapes, id);
            if (s?.TextBody is null) continue;
            for (int pi = 0; pi < s.TextBody.Paragraphs.Count; pi++)
            for (int ri = 0; ri < s.TextBody.Paragraphs[pi].Runs.Count; ri++)
                Bus.Execute(new SetRunFontSizeCommand(_currentSlideIndex, id, pi, ri, sizePt));
        }
    }

    /// <summary>Sets text color on every run in all selected shapes.</summary>
    public void SetColorOnSelection(ThemeAwareColor? color)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var s = FindShape(CurrentSlide.Shapes, id);
            if (s?.TextBody is null) continue;
            for (int pi = 0; pi < s.TextBody.Paragraphs.Count; pi++)
            for (int ri = 0; ri < s.TextBody.Paragraphs[pi].Runs.Count; ri++)
                Bus.Execute(new SetRunColorCommand(_currentSlideIndex, id, pi, ri, color));
        }
    }

    /// <summary>Sets the DrawingML text-frame autofit mode on all selected text shapes as one undo step.</summary>
    public int SetTextAutoFitOnSelection(TextAutoFitKind kind)
    {
        if (CurrentSlide is null)
            return 0;

        var commands = _selectedShapeIds
            .Where(id => FindShape(CurrentSlide.Shapes, id)?.TextBody is not null)
            .Select(id => (IPresentationCommand)new SetShapeTextAutoFitCommand(_currentSlideIndex, id, kind))
            .ToArray();

        if (commands.Length == 0)
            return 0;

        Bus.Execute(new BatchCommand("Set Text Autofit", commands));
        return commands.Length;
    }

    /// <summary>Sets the DrawingML text direction on all selected text shapes as one undo step.</summary>
    public int SetTextVerticalTypeOnSelection(TextVerticalType verticalType)
    {
        if (CurrentSlide is null)
            return 0;

        var commands = _selectedShapeIds
            .Where(id => FindShape(CurrentSlide.Shapes, id)?.TextBody is not null)
            .Select(id => (IPresentationCommand)new SetShapeTextVerticalTypeCommand(
                _currentSlideIndex,
                id,
                verticalType))
            .ToArray();

        if (commands.Length == 0)
            return 0;

        Bus.Execute(new BatchCommand("Set Text Direction", commands));
        return commands.Length;
    }

    /// <summary>Sets the number of text columns on all selected text shapes as one undo step.</summary>
    public int SetTextColumnCountOnSelection(int columnCount)
    {
        if (CurrentSlide is null || columnCount < 1)
            return 0;

        var commands = _selectedShapeIds
            .Where(id => FindShape(CurrentSlide.Shapes, id)?.TextBody is not null)
            .Select(id => (IPresentationCommand)new SetShapeTextColumnCountCommand(
                _currentSlideIndex,
                id,
                columnCount))
            .ToArray();

        if (commands.Length == 0)
            return 0;

        Bus.Execute(new BatchCommand("Set Text Columns", commands));
        return commands.Length;
    }

    /// <summary>Sets text-column spacing on all selected text shapes as one undo step.</summary>
    public int SetTextColumnSpacingOnSelection(long spacingEmu)
    {
        if (CurrentSlide is null || spacingEmu < 0)
            return 0;

        var commands = _selectedShapeIds
            .Where(id => FindShape(CurrentSlide.Shapes, id)?.TextBody is not null)
            .Select(id => (IPresentationCommand)new SetShapeTextColumnSpacingCommand(
                _currentSlideIndex,
                id,
                spacingEmu))
            .ToArray();

        if (commands.Length == 0)
            return 0;

        Bus.Execute(new BatchCommand("Set Text Column Spacing", commands));
        return commands.Length;
    }

    // ── Notes operations ─────────────────────────────────────────────────────────

    /// <summary>
    /// The speaker-notes text body for the current slide, or null if no notes have been set.
    /// </summary>
    public TextBody? CurrentSlideNotes => CurrentSlide?.Notes;

    /// <summary>
    /// Replaces the speaker notes on the current slide with plain text. Each explicit line break
    /// becomes a separate paragraph so the notes pane does not flatten authored structure on save.
    /// Pass null or empty to clear notes. This operation is undoable.
    /// </summary>
    public void SetCurrentSlideNotesText(string? text)
    {
        SetSlideNotesText(_currentSlideIndex, text);
    }

    /// <summary>
    /// Replaces speaker notes on an arbitrary slide with plain text. This is the shared
    /// mutation entry point for workflows that can navigate independently of the editor's
    /// selected slide, such as Presenter View. The operation remains undoable.
    /// </summary>
    public void SetSlideNotesText(int slideIndex, string? text)
    {
        if (slideIndex < 0 || slideIndex >= Presentation.Slides.Count)
            return;

        Bus.Execute(new SetSlideNotesCommand(slideIndex, BuildNotesTextBody(text)));
    }

    /// <summary>
    /// Replaces the speaker notes on the current slide with a structured <see cref="TextBody"/>.
    /// Pass null to clear notes. This operation is undoable.
    /// </summary>
    public void SetCurrentSlideNotes(TextBody? notes)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new SetSlideNotesCommand(_currentSlideIndex, notes));
    }

    private static TextBody? BuildNotesTextBody(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var body = new TextBody();
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
        {
            var para = new Paragraph();
            if (line.Length > 0)
                para.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(para);
        }

        return body;
    }

    // ── Default shape factories (used by ribbon insert commands) ──────────────────

    /// <summary>
    /// Slide center in EMU, width=~3 inches, height=~2 inches. Gives 3C a reasonable target.
    /// </summary>
    private (long x, long y, long cx, long cy) DefaultShapeBounds()
    {
        const long cx = DrawingMlCoordinateUnits.EmuPerInch * 3;
        const long cy = DrawingMlCoordinateUnits.EmuPerInch * 2;
        var x = (Presentation.SlideSizeCxEmu - cx) / 2;
        var y = (Presentation.SlideSizeCyEmu - cy) / 2;
        return (x, y, cx, cy);
    }

    private uint NextShapeId()
    {
        var slide = CurrentSlide;
        if (slide is null) return 1u;
        return EnumerateAllShapes(slide.Shapes).Select(shape => shape.Id).DefaultIfEmpty().Max() + 1u;
    }

    private static IEnumerable<SlideShape> EnumerateAllShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateAllShapes(shape.Children))
                yield return child;
        }
    }

    private int NextSmartArtPartIndex()
    {
        var max = 0;
        foreach (var slide in Presentation.Slides)
        {
            foreach (var shape in EnumerateShapes(slide.Shapes))
            {
                if (shape.SmartArt is not { } smartArt)
                    continue;

                foreach (var path in smartArt.Parts.Keys)
                {
                    var fileName = path[(path.LastIndexOf('/') + 1)..];
                    if (!fileName.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
                        !fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                        !int.TryParse(fileName[4..^4], out var index))
                        continue;

                    max = Math.Max(max, index);
                }
            }
        }

        return max + 1;

        static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
        {
            foreach (var shape in shapes)
            {
                yield return shape;
                foreach (var child in EnumerateShapes(shape.Children))
                    yield return child;
            }
        }
    }

    /// <summary>Creates and inserts a default text-box shape onto the current slide.</summary>
    public SlideShape InsertDefaultTextBox()
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id           = NextShapeId(),
            Name         = "TextBox",
            Kind         = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu   = x,
            OffsetYEmu   = y,
            ExtentCxEmu  = cx,
            ExtentCyEmu  = cy,
            Fill         = ShapeFill.None.Instance,
            TextBody     = new TextBody { Wrap = true }
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = string.Empty });
        shape.TextBody.Paragraphs.Add(para);
        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Creates and inserts a text-box shape already carrying <paramref name="text"/> as its
    /// content, as a single undoable <see cref="AddShapeCommand"/>.
    ///
    /// Y8 fix: the text is baked into the shape BEFORE the command is executed so it is
    /// captured atomically by the undo bus — no out-of-band mutation after the fact.
    ///
    /// Y9 fix: <paramref name="text"/> is split on line-breaks into separate
    /// <see cref="Paragraph"/>s so multi-line clipboard content preserves its structure.
    /// Each paragraph is guaranteed to have at least one <see cref="Run"/> (the empty
    /// paragraph fallback ensures the shape always has a valid text body).
    /// </summary>
    public SlideShape InsertTextBox(string text)
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        var body = new TextBody { Wrap = true };

        // Split on common line-break sequences; keep empty lines so spacing is preserved.
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(para);
        }

        // Guard: ensure at least one paragraph exists (handles empty string gracefully).
        if (body.Paragraphs.Count == 0)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = string.Empty });
            body.Paragraphs.Add(para);
        }

        return InsertTextBox(body, x, y, cx, cy);
    }

    /// <summary>
    /// Creates and inserts a text-box shape from a shared rich text body. The body is cloned
    /// before it enters the undo command so clipboard payload ownership never leaks into the
    /// presentation model.
    /// </summary>
    public SlideShape InsertTextBox(TextBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (x, y, cx, cy) = DefaultShapeBounds();
        return InsertTextBox(body, x, y, cx, cy);
    }

    private SlideShape InsertTextBox(TextBody body, long x, long y, long cx, long cy)
    {
        var copiedBody = TextBodyModelCloner.CloneTextBody(body) ?? new TextBody { Wrap = true };
        copiedBody.Wrap = true;
        if (copiedBody.Paragraphs.Count == 0)
            copiedBody.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = string.Empty } } });

        var shape = new SlideShape
        {
            Id            = NextShapeId(),
            Name          = "TextBox",
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
            Fill          = ShapeFill.None.Instance,
            TextBody      = copiedBody
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>Creates and inserts a default rectangle autoshape onto the current slide.</summary>
    public SlideShape InsertDefaultRectangle()
        => InsertDefaultAutoShape(DrawingShapeKind.Rectangle);

    /// <summary>Creates and inserts a default ellipse autoshape onto the current slide.</summary>
    public SlideShape InsertDefaultEllipse()
        => InsertDefaultAutoShape(DrawingShapeKind.Ellipse);

    /// <summary>Creates and inserts a default renderable AutoShape preset onto the current slide.</summary>
    public SlideShape InsertDefaultAutoShape(DrawingShapeKind shapeKind)
    {
        if (!DrawingShapeKindSupport.IsRenderable(shapeKind) || DrawingShapeKindSupport.IsLineLike(shapeKind))
            throw new ArgumentOutOfRangeException(nameof(shapeKind), shapeKind, "The shape kind is not a fillable AutoShape preset.");

        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id            = NextShapeId(),
            Name          = shapeKind.ToString(),
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = shapeKind,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Creates and inserts a straight connector. When exactly two shapes are selected, the
    /// connector attaches to the nearest cardinal connection sites so subsequent shape moves
    /// can reroute it. With no two-shape selection, a free centered connector is inserted.
    /// </summary>
    public SlideShape InsertDefaultConnector(DrawingShapeKind connectorKind = DrawingShapeKind.Line)
    {
        if (!DrawingShapeKindSupport.IsLineLike(connectorKind))
            throw new ArgumentOutOfRangeException(nameof(connectorKind), connectorKind, "The connector kind must be line-like.");

        var slide = CurrentSlide ?? throw new InvalidOperationException("A current slide is required to insert a connector.");
        ConnectorAttachment? start = null;
        ConnectorAttachment? end = null;
        long x;
        long y;
        long cx;
        long cy;

        var selected = _selectedShapeIds
            .Select(id => FindShape(slide.Shapes, id))
            .Where(shape => shape is not null && shape.Kind != SlideShapeKind.Connector)
            .Cast<SlideShape>()
            .Take(2)
            .ToArray();

        if (selected.Length == 2)
        {
            var first = selected[0];
            var second = selected[1];
            var firstSite = SelectConnectionSite(first, second);
            var secondSite = OppositeConnectionSite(firstSite);
            start = new ConnectorAttachment { ShapeId = first.Id, SiteIndex = firstSite };
            end = new ConnectorAttachment { ShapeId = second.Id, SiteIndex = secondSite };

            var startPoint = ConnectionSiteHelper.Resolve(start, slide);
            var endPoint = ConnectionSiteHelper.Resolve(end, slide);
            x = Math.Min(startPoint.X, endPoint.X);
            y = Math.Min(startPoint.Y, endPoint.Y);
            cx = Math.Max(Math.Abs(endPoint.X - startPoint.X), 1L);
            cy = Math.Max(Math.Abs(endPoint.Y - startPoint.Y), 1L);
        }
        else
        {
            var bounds = DefaultShapeBounds();
            x = bounds.x;
            y = bounds.y + bounds.cy / 2;
            cx = bounds.cx;
            cy = 1;
        }

        var shape = new SlideShape
        {
            Id = NextShapeId(),
            Name = "Connector",
            Kind = SlideShapeKind.Connector,
            AutoShapeKind = connectorKind,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            ConnectionStart = start,
            ConnectionEnd = end,
        };
        AddShape(shape);
        return shape;
    }

    private static int SelectConnectionSite(SlideShape from, SlideShape to)
    {
        var fromCenterX = from.OffsetXEmu + from.ExtentCxEmu / 2;
        var fromCenterY = from.OffsetYEmu + from.ExtentCyEmu / 2;
        var toCenterX = to.OffsetXEmu + to.ExtentCxEmu / 2;
        var toCenterY = to.OffsetYEmu + to.ExtentCyEmu / 2;
        var dx = toCenterX - fromCenterX;
        var dy = toCenterY - fromCenterY;

        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? 2 : 0;

        return dy >= 0 ? 3 : 1;
    }

    private static int OppositeConnectionSite(int siteIndex) => siteIndex switch
    {
        0 => 2,
        1 => 3,
        2 => 0,
        3 => 1,
        _ => 0,
    };

    /// <summary>
    /// Creates and inserts a picture shape from raw image bytes onto the current slide.
    /// </summary>
    public SlideShape InsertPicture(
        byte[] imageBytes,
        string contentType = "image/png",
        long? widthEmu = null,
        long? heightEmu = null)
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        if (widthEmu is > 0 && heightEmu is > 0)
        {
            cx = Math.Clamp(widthEmu.Value, 9_525L, 63_500_000_000L);
            cy = Math.Clamp(heightEmu.Value, 9_525L, 63_500_000_000L);
        }
        var shape = new SlideShape
        {
            Id          = NextShapeId(),
            Name        = "Picture",
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = x,
            OffsetYEmu  = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Picture     = new ImagePart { Bytes = imageBytes, ContentType = contentType }
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Creates and inserts an embedded audio or video shape onto the current slide.
    /// The media bytes are retained by the model and written by the native PPTX package
    /// writer; playback remains a host concern.
    /// </summary>
    public SlideShape InsertMedia(
        byte[] mediaBytes,
        bool isVideo,
        string contentType)
    {
        ArgumentNullException.ThrowIfNull(mediaBytes);
        if (mediaBytes.Length == 0)
            throw new ArgumentException("Media payload cannot be empty.", nameof(mediaBytes));

        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id = NextShapeId(),
            Name = isVideo ? "Video" : "Audio",
            Kind = SlideShapeKind.Media,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Media = new MediaInfo
            {
                IsVideo = isVideo,
                Bytes = mediaBytes.ToArray(),
                ContentType = contentType,
            },
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Applies a prepared caption mutation through the shared command bus so caption authoring
    /// participates in the same undo/redo contract as the other slide edits.
    /// </summary>
    public PresentationMediaCaptionTrackMutationResult ApplyMediaCaptionAuthoring(
        PresentationMediaCaptionAuthoringMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            CurrentSlide,
            SelectedShapeIds);
        var media = mediaShape?.Media;
        if (mediaShape is null || media is null)
            return PresentationMediaTranscriptPlanner.ApplyCaptionAuthoringMutation(media, plan);

        var before = CloneCaptionTracks(media.CaptionTracks);
        var staged = new MediaInfo
        {
            IsVideo = media.IsVideo,
            VolumePercent = media.VolumePercent,
            PlaybackStartMode = media.PlaybackStartMode,
            Loop = media.Loop,
            TrimStartMilliseconds = media.TrimStartMilliseconds,
            TrimEndMilliseconds = media.TrimEndMilliseconds,
            FadeInMilliseconds = media.FadeInMilliseconds,
            FadeOutMilliseconds = media.FadeOutMilliseconds,
            Bytes = media.Bytes.ToArray(),
            ContentType = media.ContentType,
            SourcePackagePath = media.SourcePackagePath,
            LinkUrl = media.LinkUrl
        };
        staged.CaptionTracks.AddRange(CloneCaptionTracks(before));
        staged.Bookmarks.AddRange(CloneMediaBookmarks(media.Bookmarks));

        var result = PresentationMediaTranscriptPlanner.ApplyCaptionAuthoringMutation(staged, plan);
        if (!result.Succeeded)
            return result;

        Bus.Execute(new SetMediaCaptionTracksCommand(
            CurrentSlideIndex,
            mediaShape.Id,
            before,
            staged.CaptionTracks));

        if (plan.Intent == PresentationMediaCaptionAuthoringIntentKind.Delete)
            return result;

        if (result.TrackIndex >= 0 && result.TrackIndex < media.CaptionTracks.Count)
            return PresentationMediaCaptionTrackMutationResult.Success(
                result.TrackIndex,
                media.CaptionTracks[result.TrackIndex]);

        return result;
    }

    /// <summary>Sets the selected media's authored volume through the shared undo bus.</summary>
    public bool SetSelectedMediaVolume(int volumePercent)
    {
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            CurrentSlide,
            SelectedShapeIds);
        var media = mediaShape?.Media;
        if (mediaShape is null || media is null)
            return false;

        Bus.Execute(new SetMediaVolumeCommand(
            CurrentSlideIndex,
            mediaShape.Id,
            media.VolumePercent,
            Math.Clamp(volumePercent, 0, 100)));
        return true;
    }

    /// <summary>Sets the selected media's authored start mode and loop policy through the shared undo bus.</summary>
    public bool SetSelectedMediaPlaybackOptions(MediaPlaybackStartMode startMode, bool loop)
    {
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            CurrentSlide,
            SelectedShapeIds);
        var media = mediaShape?.Media;
        if (mediaShape is null || media is null)
            return false;

        Bus.Execute(new SetMediaPlaybackOptionsCommand(
            CurrentSlideIndex,
            mediaShape.Id,
            media.PlaybackStartMode,
            media.Loop,
            startMode,
            loop));
        return true;
    }

    /// <summary>Sets selected media trim and fade timing through the shared undo bus.</summary>
    public bool SetSelectedMediaTiming(
        double trimStartMilliseconds,
        double trimEndMilliseconds,
        double fadeInMilliseconds,
        double fadeOutMilliseconds)
    {
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            CurrentSlide,
            SelectedShapeIds);
        var media = mediaShape?.Media;
        if (mediaShape is null || media is null)
            return false;

        Bus.Execute(new SetMediaTimingCommand(
            CurrentSlideIndex,
            mediaShape.Id,
            media.TrimStartMilliseconds,
            media.TrimEndMilliseconds,
            media.FadeInMilliseconds,
            media.FadeOutMilliseconds,
            trimStartMilliseconds,
            trimEndMilliseconds,
            fadeInMilliseconds,
            fadeOutMilliseconds));
        return true;
    }

    /// <summary>Replaces selected media bookmarks through one shared undoable command.</summary>
    public bool SetSelectedMediaBookmarks(IReadOnlyList<MediaBookmarkInfo> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            CurrentSlide,
            SelectedShapeIds);
        var media = mediaShape?.Media;
        if (mediaShape is null || media is null)
            return false;

        Bus.Execute(new SetMediaBookmarksCommand(
            CurrentSlideIndex,
            mediaShape.Id,
            media.Bookmarks,
            bookmarks));
        return true;
    }

    /// <summary>
    /// Runs a custom-show authoring mutation against a staged snapshot and commits the resulting
    /// collection through the shared undo bus. The planner remains responsible for validation and
    /// normalization; the command bus owns the reversible presentation state transition.
    /// </summary>
    public T ApplyCustomShowMutation<T>(Func<Presentation, T> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        var before = CloneCustomShows(Presentation.CustomShows);
        T result;
        try
        {
            result = mutation(Presentation);
        }
        catch
        {
            RestoreCustomShows(Presentation, before);
            throw;
        }

        var after = CloneCustomShows(Presentation.CustomShows);
        RestoreCustomShows(Presentation, before);
        if (!CustomShowsEqual(before, after))
        {
            Bus.Execute(new ReplaceCustomShowsCommand(before, after));
        }

        return result;
    }

    private static List<MediaCaptionTrackInfo> CloneCaptionTracks(
        IEnumerable<MediaCaptionTrackInfo> tracks) =>
        tracks.Select(track => new MediaCaptionTrackInfo
        {
            RelationshipId = track.RelationshipId,
            Source = track.Source,
            Bytes = track.Bytes.ToArray(),
            ContentType = track.ContentType,
            Language = track.Language,
            Label = track.Label,
            IsExternal = track.IsExternal
        }).ToList();

    private static List<MediaBookmarkInfo> CloneMediaBookmarks(
        IEnumerable<MediaBookmarkInfo> bookmarks) =>
        bookmarks.Select(bookmark => new MediaBookmarkInfo
        {
            Name = bookmark.Name,
            TimeMilliseconds = bookmark.TimeMilliseconds
        }).ToList();

    private static List<PresentationCustomShow> CloneCustomShows(
        IEnumerable<PresentationCustomShow> shows) =>
        shows.Select(show =>
        {
            var clone = new PresentationCustomShow { Id = show.Id, Name = show.Name };
            clone.SlideIds.AddRange(show.SlideIds);
            return clone;
        }).ToList();

    private static void RestoreCustomShows(
        Presentation presentation,
        IReadOnlyList<PresentationCustomShow> shows)
    {
        presentation.CustomShows.Clear();
        foreach (var show in CloneCustomShows(shows))
            presentation.CustomShows.Add(show);
    }

    private static bool CustomShowsEqual(
        IReadOnlyList<PresentationCustomShow> left,
        IReadOnlyList<PresentationCustomShow> right)
    {
        if (left.Count != right.Count)
            return false;

        return left.Zip(right).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name
            && pair.First.SlideIds.SequenceEqual(pair.Second.SlideIds));
    }

    /// <summary>
    /// Creates and inserts an embedded OLE package from raw file bytes. The package payload
    /// remains editable/activatable after save and the insertion is one undoable shape add.
    /// </summary>
    public SlideShape InsertEmbeddedObject(
        byte[] embeddedBytes,
        string fileName,
        string? sourceProgId = null)
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id = NextShapeId(),
            Name = string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName))
                ? "Embedded Object"
                : Path.GetFileNameWithoutExtension(fileName),
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            OleObject = OleInsertionPlanner.CreatePayload(
                embeddedBytes,
                fileName,
                sourceProgId),
        };
        AddShape(shape);
        return shape;
    }

    // ── Clipboard — shapes ────────────────────────────────────────────────────────

    /// <summary>
    /// True when there is something on the internal clipboard that can be pasted.
    /// </summary>
    public bool CanPaste =>
        (_shapeClipboard is { Count: > 0 }) || _slideClipboard is not null;

    /// <summary>
    /// Copies the currently selected shapes to the internal shape clipboard (deep-clones).
    /// Does nothing if there is no current slide or no selection.
    /// </summary>
    public void CopySelectedShapes()
    {
        var slide = CurrentSlide;
        if (slide is null || _selectedShapeIds.Count == 0) return;

        _shapeClipboard = _selectedShapeIds
            .Select(id => FindShape(slide.Shapes, id))
            .Where(s => s is not null)
            .Select(s => SlideCloner.CloneShape(s!))
            .ToList();

        // Shape clipboard wins — clear any stale slide clipboard.
        _slideClipboard = null;
    }

    /// <summary>
    /// Copies the selected shapes to the clipboard, then deletes them from the slide (cut).
    /// </summary>
    public void CutSelectedShapes()
    {
        CopySelectedShapes();
        DeleteSelected();
    }

    /// <summary>
    /// Pastes the shape clipboard onto the current slide.  Each pasted shape gets:
    /// <list type="bullet">
    ///   <item>A fresh unique Id (max existing Id + 1, assigned in order).</item>
    ///   <item>An offset of +0.2" in both X and Y relative to its original position.</item>
    /// </list>
    /// The pasted shapes become the new selection.  Undoable via a single <see cref="PasteShapesCommand"/>.
    /// Does nothing if <see cref="_shapeClipboard"/> is null or empty.
    /// </summary>
    public void PasteShapes()
    {
        if (_shapeClipboard is not { Count: > 0 }) return;
        PasteShapeCopies(_shapeClipboard);
    }

    /// <summary>
    /// Pastes shapes decoded from the native FreeP system-clipboard format.
    /// The source objects are cloned, assigned fresh IDs, offset, and inserted as one
    /// undoable command using the same behavior as an in-process paste.
    /// </summary>
    public void PasteExternalShapes(IEnumerable<SlideShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        PasteShapeCopies(shapes);
    }

    private void PasteShapeCopies(IEnumerable<SlideShape> shapes)
    {
        if (CurrentSlide is null) return;

        var clones = shapes.Select(SlideCloner.CloneShape).ToList();
        if (clones.Count == 0) return;

        uint nextId = CurrentSlide.Shapes.Count == 0
            ? 1u
            : CurrentSlide.Shapes.Max(s => s.Id) + 1u;

        foreach (var clone in clones)
        {
            clone.Id = nextId++;
            clone.OffsetXEmu += PasteOffset.Emu;
            clone.OffsetYEmu += PasteOffset.Emu;
        }

        Bus.Execute(new PasteShapesCommand(_currentSlideIndex, clones));

        _selectedShapeIds.Clear();
        foreach (var clone in clones)
            _selectedShapeIds.Add(clone.Id);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Clipboard — slides ────────────────────────────────────────────────────────

    /// <summary>Copies the current slide to the internal slide clipboard (deep-clone).</summary>
    public void CopyCurrentSlide()
    {
        if (CurrentSlide is null) return;
        _slideClipboard = SlideCloner.CloneSlide(CurrentSlide);
        _shapeClipboard = null; // slide clipboard takes precedence
    }

    /// <summary>Copies then deletes the current slide (cut slide).</summary>
    public void CutCurrentSlide()
    {
        CopyCurrentSlide();
        DeleteCurrentSlide();
    }

    /// <summary>
    /// Pastes the slide clipboard as a new slide inserted immediately after the current slide.
    /// Undoable.  Does nothing if <see cref="_slideClipboard"/> is null.
    /// </summary>
    public void PasteSlide()
    {
        if (_slideClipboard is null) return;
        // Clone again for independent copy.
        var clone    = SlideCloner.CloneSlide(_slideClipboard);
        var insertAt = _currentSlideIndex < 0 ? 0 : _currentSlideIndex + 1;
        Bus.Execute(new PasteSlideCommand(insertAt, clone));
        CurrentSlideIndex = insertAt;
    }

    // ── Clipboard — unified paste ──────────────────────────────────────────────────

    /// <summary>
    /// Unified paste: if the shape clipboard is non-empty, calls <see cref="PasteShapes"/>;
    /// otherwise calls <see cref="PasteSlide"/>.
    /// </summary>
    public void Paste()
    {
        if (_shapeClipboard is { Count: > 0 })
            PasteShapes();
        else if (_slideClipboard is not null)
            PasteSlide();
    }

    // ── Theme ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the presentation theme with <paramref name="theme"/>. Undoable.
    /// Shapes that reference scheme-color slots will re-resolve automatically because
    /// the renderer reads the live <see cref="Presentation.Theme"/> via
    /// <see cref="ThemeColorResolver"/>.
    /// </summary>
    public void SetTheme(PresentationTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Bus.Execute(new SetThemeCommand(theme));
    }

    /// <summary>
    /// Looks up a theme by its built-in id and applies it.
    /// Throws <see cref="ArgumentException"/> if <paramref name="themeId"/> is not recognised.
    /// </summary>
    public void SetTheme(string themeId)
    {
        var theme = BuiltInThemes.GetById(themeId)
            ?? throw new ArgumentException($"Unknown built-in theme id '{themeId}'.", nameof(themeId));
        SetTheme(theme);
    }

    // ── Slide size ────────────────────────────────────────────────────────────────

    /// <summary>Sets the slide size to an arbitrary custom EMU extent. Undoable.</summary>
    public void SetSlideSizeCustom(long cxEmu, long cyEmu)
        => Bus.Execute(new SetSlideSizeCommand(cxEmu, cyEmu));

    /// <summary>Sets the slide size to 16:9 widescreen. Undoable.</summary>
    public void SetSlideSize16x9()
        => SetSlideSizeCustom(Widescreen169WidthEmu, StandardSlideHeightEmu);

    /// <summary>Sets the slide size to 4:3 standard. Undoable.</summary>
    public void SetSlideSize4x3()
        => SetSlideSizeCustom(Standard43WidthEmu, StandardSlideHeightEmu);

    /// <summary>
    /// Overload alias — same as <see cref="SetSlideSizeCustom"/> but named per the spec contract.
    /// </summary>
    public void SetSlideSize(long cxEmu, long cyEmu)
        => SetSlideSizeCustom(cxEmu, cyEmu);

    /// <summary>Sets or clears the current slide's explicit background fill. Undoable.</summary>
    public bool SetCurrentSlideBackground(ShapeFill? fill)
    {
        if (CurrentSlide is null)
            return false;

        Bus.Execute(new SetSlideBackgroundCommand(_currentSlideIndex, fill));
        return true;
    }

    // ── Insert table ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and inserts a default <see cref="TableShape"/> with <paramref name="rows"/> rows
    /// and <paramref name="cols"/> columns onto the current slide, approximately half the slide
    /// width, centered.  Undoable.
    /// </summary>
    public SlideShape InsertTable(int rows, int cols)
    {
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 1) throw new ArgumentOutOfRangeException(nameof(cols));

        // Size: ~60 % of slide width, ~30 % height, centered.
        const double widthFrac  = 0.60;
        const double heightFrac = 0.30;
        long cx = (long)(Presentation.SlideSizeCxEmu * widthFrac);
        long cy = (long)(Presentation.SlideSizeCyEmu * heightFrac);
        long x  = (Presentation.SlideSizeCxEmu - cx) / 2;
        long y  = (Presentation.SlideSizeCyEmu - cy) / 2;

        long colWidth = cols > 0 ? cx / cols : cx;
        long rowHeight = rows > 0 ? cy / rows : cy;

        var table = new TableShape
        {
            TableStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}", // Office default style GUID
            Flags        = new TableStyleFlags { FirstRow = true, BandRow = true }
        };

        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(colWidth);

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = rowHeight };
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        var shape = new SlideShape
        {
            Id          = NextShapeId(),
            Name        = $"Table {rows}×{cols}",
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = x,
            OffsetYEmu  = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Table       = table
        };

        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Inserts a native editable table when a standalone external clipboard payload is a
    /// tab-delimited table. Returns null for mixed prose or unsupported one-column projections,
    /// allowing the caller to retain the existing textbox fallback.
    /// </summary>
    public SlideShape? InsertTableFromClipboard(
        TextBody body,
        IReadOnlyList<long>? columnWidthsEmu = null,
        IReadOnlyList<InCanvasRichClipboardTableCellStyle>? cellStyles = null)
    {
        if (CurrentSlide is null
            || !ClipboardTablePlanner.TryBuildStandaloneTable(
                body,
                columnWidthsEmu,
                cellStyles,
                out var table))
            return null;

        long cx = table.ColumnWidthsEmu.Sum();
        long cy = table.Rows.Sum(row => row.HeightEmu);
        var shape = new SlideShape
        {
            Id = NextShapeId(),
            Name = "Pasted Table",
            Kind = SlideShapeKind.Table,
            OffsetXEmu = (Presentation.SlideSizeCxEmu - cx) / 2,
            OffsetYEmu = (Presentation.SlideSizeCyEmu - cy) / 2,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Table = table,
        };
        AddShape(shape);
        return shape;
    }

    // ── Insert chart ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and inserts a default <see cref="ChartShape"/> of the given
    /// <paramref name="chartType"/> with three sample categories and two series so it
    /// renders immediately.  Undoable.
    /// </summary>
    public SlideShape InsertChart(ChartType chartType = ChartType.ColumnClustered) =>
        InsertChartCore(chartType, isCombo: false);

    /// <summary>
    /// Creates and inserts a default column-plus-line combination chart as one undoable
    /// object. The second sample series is authored on the secondary axis with a line
    /// override, matching the OOXML combo-chart plot-group model.
    /// </summary>
    public SlideShape InsertComboChart() => InsertChartCore(ChartType.ColumnClustered, isCombo: true);

    private SlideShape InsertChartCore(ChartType chartType, bool isCombo)
    {
        var (x, y, cx, cy) = DefaultShapeBounds();

        var chart = new ChartShape
        {
            ChartType = chartType,
            Title     = "Chart Title",
            Legend    = LegendPosition.Bottom,
        };
        if (isCombo)
            chart.SecondaryValueAxis = new ChartAxis();

        if (chartType == ChartType.Stock)
        {
            chart.Categories.AddRange(["Day 1", "Day 2", "Day 3"]);
            foreach (var (name, values) in new[]
            {
                ("Open",  new double?[] { 10, 12, 11 }),
                ("High",  new double?[] { 14, 16, 15 }),
                ("Low",   new double?[] { 8, 9, 10 }),
                ("Close", new double?[] { 13, 11, 14 }),
            })
            {
                var series = new ChartSeries { Name = name };
                series.Values.AddRange(values);
                chart.Series.Add(series);
            }
        }
        else if (chartType == ChartType.Funnel)
        {
            chart.Categories.AddRange(["Awareness", "Interest", "Consideration", "Conversion"]);
            var series = new ChartSeries { Name = "Value" };
            series.Values.AddRange([100, 68, 42, 18]);
            chart.Series.Add(series);
        }
        else if (chartType == ChartType.Waterfall)
        {
            chart.Categories.AddRange(["Starting value", "Reduction", "Growth", "Ending value"]);
            chart.WaterfallTotalPointIndices = [3];
            var series = new ChartSeries { Name = "Value" };
            series.Values.AddRange([100, -30, 20, 90]);
            chart.Series.Add(series);
        }
        else
        {
            // Default sample data — 3 categories, 2 series.
            chart.Categories.AddRange(["Q1", "Q2", "Q3"]);

            var s1 = new ChartSeries { Name = "Series 1" };
            s1.Values.AddRange([4.3, 2.5, 3.5]);
            chart.Series.Add(s1);

            var s2 = new ChartSeries { Name = "Series 2" };
            s2.Values.AddRange([2.4, 4.4, 1.8]);
            if (isCombo)
            {
                s2.OverrideChartType = ChartType.LineMarkers;
                s2.OnSecondaryAxis = true;
            }
            chart.Series.Add(s2);
        }

        var shape = new SlideShape
        {
            Id          = NextShapeId(),
            Name        = "Chart",
            Kind        = SlideShapeKind.Chart,
            OffsetXEmu  = x,
            OffsetYEmu  = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            Chart       = chart
        };

        AddShape(shape);
        return shape;
    }

    // ── Hyperlinks ────────────────────────────────────────────────────────────────

    public SlideShape InsertSmartArt(
        SmartArtLayoutPreset layout = SmartArtLayoutPreset.BasicProcess,
        IReadOnlyList<SlideObjectPicturePayload>? picturePayloads = null)
    {
        if (!SlideObjectInsertionPlanner.InsertableSmartArtLayouts.Contains(layout))
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "The requested SmartArt layout is not available for insertion.");

        var (x, y, cx, cy) = DefaultShapeBounds();
        var smartArt = SmartArtInsertionFactory.Create(
            layout,
            NextSmartArtPartIndex(),
            ["Step 1", "Step 2", "Step 3"],
            picturePayloads);
        var shape = new SlideShape
        {
            Id = NextShapeId(),
            Name = "Basic Process",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = x,
            OffsetYEmu = y,
            ExtentCxEmu = cx,
            ExtentCyEmu = cy,
            SmartArt = smartArt,
        };

        // Seed the native drawing cache immediately so placeholder-only picture layouts
        // are visible to package consumers before the first interactive edit.
        var cacheRefresh = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            Presentation.Theme,
            CurrentSlide?.ColorMapOverride);
        if (!cacheRefresh.Applied)
            throw new InvalidOperationException(cacheRefresh.Message);

        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Sets a shape-level hyperlink on every selected shape.  Undoable.
    /// Pass <paramref name="url"/> for an external link or <paramref name="targetSlideId"/> for
    /// an internal slide jump; exactly one should be non-null.
    /// </summary>
    public void SetShapeHyperlink(string? url = null, string? targetSlideId = null, string? tooltip = null)
    {
        if (CurrentSlide is null) return;
        var link = (url is not null || targetSlideId is not null)
            ? new Hyperlink { Url = url, TargetSlideId = targetSlideId, Tooltip = tooltip }
            : null;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new SetShapeHyperlinkCommand(_currentSlideIndex, id, link));
    }

    /// <summary>Removes the shape-level hyperlink from every selected shape.  Undoable.</summary>
    public void RemoveShapeHyperlink()
        => SetShapeHyperlink(); // null link = remove

    /// <summary>Sets persistent alternative text metadata on every selected shape. Undoable.</summary>
    public void SetSelectedShapeAlternativeText(
        string? alternativeText,
        string? title = null,
        bool? isDecorative = null)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            Bus.Execute(new SetShapeAlternativeTextCommand(
                _currentSlideIndex,
                id,
                alternativeText,
                title,
                isDecorative));
        }
    }

    /// <summary>
    /// Returns the shape-level hyperlink of the first selected shape, if any.
    /// Used to pre-fill the HyperlinkDialog when editing an existing link.
    /// </summary>
    public Hyperlink? SelectedShapeHyperlink
    {
        get
        {
            if (CurrentSlide is null || _selectedShapeIds.Count == 0) return null;
            var firstId = _selectedShapeIds[0];
            var shape   = FindShape(CurrentSlide.Shapes, firstId);
            return shape?.Hyperlink;
        }
    }

    // ── Font family (named overload matching 5B contract) ─────────────────────────

    /// <summary>
    /// Sets the font family on every run in all selected shapes. Undoable.
    /// (Delegates to the existing <see cref="SetFontOnSelection"/> which already does this;
    /// exposed here under the canonical 5B-facing name for clarity.)
    /// </summary>
    public void SetFontFamilyOnSelection(string? family)
        => SetFontOnSelection(family);

    // ── Format painter ────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the fill, outline, and run-format defaults from the first selected shape
    /// into the format clipboard.  Does nothing if there is no selection.
    /// </summary>
    public void CopyFormatting()
    {
        var slide = CurrentSlide;
        if (slide is null || _selectedShapeIds.Count == 0) return;

        var source = FindShape(slide.Shapes, _selectedShapeIds[0]);
        if (source is null) return;

        _fmtFill    = source.Fill;
        _fmtOutline = source.Outline;

        // Capture first run defaults.
        var firstRun = source.TextBody?.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault();
        _fmtRun = firstRun is null ? null : new RunFormatSnapshot
        {
            FontFamily = firstRun.FontFamily,
            FontSizePt = firstRun.FontSizePt,
            Color      = firstRun.Color,
            Bold       = firstRun.Bold,
            Italic     = firstRun.Italic,
        };
    }

    /// <summary>
    /// True when format-painter clipboard has been populated via <see cref="CopyFormatting"/>.
    /// </summary>
    public bool HasFormatClipboard => _fmtFill is not null || _fmtOutline is not null || _fmtRun is not null;

    /// <summary>
    /// True after the single-click Format Painter workflow captures a source shape and is
    /// waiting for the next canvas shape to receive that formatting.
    /// </summary>
    public bool IsFormatPainterActive => _formatPainterActive;

    /// <summary>
    /// Captures the single selected source shape and enters the source-then-target Format
    /// Painter workflow used by the canvas gesture handlers.
    /// </summary>
    public bool BeginFormatPainter()
    {
        if (_selectedShapeIds.Count != 1)
            return false;

        CopyFormatting();
        _formatPainterActive = HasFormatClipboard;
        return _formatPainterActive;
    }

    /// <summary>Leaves the source-then-target Format Painter workflow without changing the model.</summary>
    public void CancelFormatPainter() => _formatPainterActive = false;

    /// <summary>
    /// Applies the captured source formatting to one hit-tested target shape. The target becomes
    /// selected, matching PowerPoint's single-click painter workflow, and the operation remains
    /// one undoable command.
    /// </summary>
    public bool TryApplyFormatPainterToShape(uint targetShapeId)
    {
        var slide = CurrentSlide;
        if (!_formatPainterActive || !HasFormatClipboard || slide is null ||
            FindShape(slide.Shapes, targetShapeId) is null)
        {
            return false;
        }

        Bus.Execute(new ApplyFormatPainterCommand(
            _currentSlideIndex,
            new[] { targetShapeId },
            _fmtFill,
            _fmtOutline,
            _fmtRun));
        _formatPainterActive = false;
        Select(targetShapeId);
        return true;
    }

    /// <summary>
    /// Applies the captured fill/outline/run-format to all currently selected shapes.
    /// A single undoable <see cref="ApplyFormatPainterCommand"/> is issued.
    /// Does nothing if the format clipboard is empty or there is no selection.
    /// </summary>
    public void ApplyFormattingToSelection()
    {
        var slide = CurrentSlide;
        if (slide is null || _selectedShapeIds.Count == 0) return;
        if (!HasFormatClipboard) return;

        Bus.Execute(new ApplyFormatPainterCommand(
            _currentSlideIndex,
            _selectedShapeIds.ToList(),
            _fmtFill,
            _fmtOutline,
            _fmtRun));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // TABLE EDITING API  (Wave 9A)
    // ════════════════════════════════════════════════════════════════════════════════
    //
    // All table ops work on the currently-selected shape (must be Kind==Table).
    // ActiveTableCell tracks the focused cell within that table.
    //
    // 9B should NOT touch this region.
    // ════════════════════════════════════════════════════════════════════════════════

    // ── Active cell selection ─────────────────────────────────────────────────────

    /// <summary>
    /// The currently focused (row, col) within the selected table shape.
    /// Null when no table is selected or no cell is explicitly focused.
    /// </summary>
    public (int Row, int Col)? ActiveTableCell { get; private set; }

    /// <summary>Fired when <see cref="ActiveTableCell"/> changes.</summary>
    public event EventHandler? ActiveTableCellChanged;

    /// <summary>
    /// Sets the active cell to (<paramref name="row"/>, <paramref name="col"/>).
    /// Clamps indices to the table's actual bounds.
    /// Does nothing if the currently selected shape is not a table.
    /// </summary>
    public void SetActiveTableCell(int row, int col)
    {
        var table = GetSelectedTable();
        if (table is null) return;
        int r = Math.Clamp(row, 0, table.Rows.Count - 1);
        int c = Math.Clamp(col, 0, table.ColumnWidthsEmu.Count - 1);
        ActiveTableCell = (r, c);
        ActiveTableCellChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the active cell selection.</summary>
    public void ClearActiveTableCell()
    {
        if (ActiveTableCell is null) return;
        ActiveTableCell = null;
        ActiveTableCellChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Cell text ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the text of the cell at (<paramref name="row"/>, <paramref name="col"/>)
    /// in the selected table with a plain-text string. Undoable.
    /// </summary>
    public void SetTableCellText(int row, int col, string text)
    {
        var (shapeId, _) = RequireSelectedTable();
        if (shapeId == 0) return;

        TextBody? body = null;
        if (!string.IsNullOrEmpty(text))
        {
            body = new TextBody { Wrap = true };
            foreach (var line in text.Split('\n'))
            {
                var para = new Paragraph();
                para.Runs.Add(new Run { Text = line });
                body.Paragraphs.Add(para);
            }
        }

        Bus.Execute(new SetTableCellTextCommand(_currentSlideIndex, shapeId, row, col, body));
    }

    /// <summary>
    /// Replaces the <see cref="TextBody"/> of the specified cell in the selected table. Undoable.
    /// </summary>
    public void SetTableCellText(int row, int col, TextBody? newBody)
        => ExecuteTableCommand((si, id) => new SetTableCellTextCommand(si, id, row, col, newBody));

    /// <summary>Sets or clears the explicit fill of the active table cell. Undoable.</summary>
    public bool TryApplyActiveTableCellFill(ThemeAwareColor? color)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        var row = table.Rows[active.Row];
        if (active.Col < 0 || active.Col >= row.Cells.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableCellFillCommand(
            si,
            id,
            active.Row,
            active.Col,
            color is null ? null : new ShapeFill.Solid(color)));
        return true;
    }

    /// <summary>Sets or clears the explicit vertical alignment of the active table cell. Undoable.</summary>
    public bool TryApplyActiveTableCellAnchor(TableCellAnchor? anchor)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        var row = table.Rows[active.Row];
        if (active.Col < 0 || active.Col >= row.Cells.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableCellAnchorCommand(
            si, id, active.Row, active.Col, anchor));
        return true;
    }

    /// <summary>Sets the DrawingML text direction on the active table cell. Undoable.</summary>
    public bool TryApplyActiveTableCellTextVerticalType(TextVerticalType verticalType)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        var row = table.Rows[active.Row];
        if (active.Col < 0 || active.Col >= row.Cells.Count || row.Cells[active.Col].TextBody is null)
            return false;

        ExecuteTableCommand((si, id) => new SetTableCellTextVerticalTypeCommand(
            si,
            id,
            active.Row,
            active.Col,
            verticalType));
        return true;
    }

    /// <summary>Sets or clears one explicit inset side of the active table cell. Undoable.</summary>
    public bool TryApplyActiveTableCellInset(TableCellInsetSide side, double? insetPt)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        var row = table.Rows[active.Row];
        if (active.Col < 0 || active.Col >= row.Cells.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableCellInsetCommand(
            si, id, active.Row, active.Col, side, insetPt));
        return true;
    }

    /// <summary>Sets the height of the active table row, or restores automatic height with zero.</summary>
    public bool TryApplyActiveTableRowHeight(long heightEmu)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableRowHeightCommand(
            si, id, active.Row, Math.Max(0, heightEmu)));
        return true;
    }

    /// <summary>Sets the width of the active table column in EMU. Undoable.</summary>
    public bool TryApplyActiveTableColumnWidth(long widthEmu)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null ||
            active.Col < 0 || active.Col >= table.ColumnWidthsEmu.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableColumnWidthCommand(
            si, id, active.Col, widthEmu));
        return true;
    }

    /// <summary>Sets or clears one explicit border side of the active table cell. Undoable.</summary>
    public bool TryApplyActiveTableCellBorder(
        TableCellBorderSide side,
        ShapeOutline? outline)
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        var row = table.Rows[active.Row];
        if (active.Col < 0 || active.Col >= row.Cells.Count)
            return false;

        ExecuteTableCommand((si, id) => new SetTableCellBorderCommand(
            si, id, active.Row, active.Col, side, outline));
        return true;
    }

    public TableCellTextFormatPlan PlanActiveTableCellTextFormat(
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanTextFormat(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            kind,
            selection);

    public bool TryApplyActiveTableCellTextFormat(
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellTextFormat(kind, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public TableCellTextValueFormatPlan PlanActiveTableCellFontFamily(
        string? fontFamily,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanFontFamily(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            fontFamily,
            selection);

    public TableCellTextValueFormatPlan PlanActiveTableCellFontSize(
        double? sizePt,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanFontSize(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            sizePt,
            selection);

    public TableCellTextValueFormatPlan PlanActiveTableCellColor(
        ThemeAwareColor? color,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanColor(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            color,
            selection);

    public bool TryApplyActiveTableCellFontFamily(
        string? fontFamily,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellFontFamily(fontFamily, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellFontSize(
        double? sizePt,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellFontSize(sizePt, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellColor(
        ThemeAwareColor? color,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellColor(color, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphAlignment(
        TextAlign alignment,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphAlignment(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            alignment,
            selection);

    public bool TryApplyActiveTableCellParagraphAlignment(
        TextAlign alignment,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphAlignment(alignment, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphBulletToggle(
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphBulletToggle(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            selection);

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphNumberingToggle(
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphNumberingToggle(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            selection);

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphListPreset(
        TableCellListPresetDescriptor preset,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphListPreset(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            preset,
            selection);

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphPictureBullet(
        PresentationPictureBulletPayload payload,
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphPictureBullet(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            payload,
            selection);

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphIndent(
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphIndent(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            selection);

    public TableCellParagraphFormatPlan PlanActiveTableCellParagraphOutdent(
        (int Start, int End)? selection = null) =>
        TableCellEditPlanner.PlanParagraphOutdent(
            _currentSlideIndex,
            CurrentSlide,
            _selectedShapeIds,
            ActiveTableCell,
            selection);

    public bool TryApplyActiveTableCellParagraphBulletToggle(
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphBulletToggle(selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellParagraphNumberingToggle(
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphNumberingToggle(selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellParagraphListPreset(
        TableCellListPresetDescriptor preset,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphListPreset(preset, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellParagraphListPreset(
        string? presetId,
        (int Start, int End)? selection = null)
    {
        if (!TableCellListPresetCatalog.TryGet(presetId, out var preset) || preset is null)
            return false;

        return TryApplyActiveTableCellParagraphListPreset(preset, selection);
    }

    public bool TryApplyActiveTableCellParagraphPictureBullet(
        PresentationPictureBulletPayload payload,
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphPictureBullet(payload, selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellParagraphIndent(
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphIndent(selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool TryApplyActiveTableCellParagraphOutdent(
        (int Start, int End)? selection = null)
    {
        var plan = PlanActiveTableCellParagraphOutdent(selection);
        if (plan.Command is null)
            return false;

        Bus.Execute(plan.Command);
        return true;
    }

    public bool ToggleBoldOnActiveTableCell() =>
        TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold);

    public bool ToggleItalicOnActiveTableCell() =>
        TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Italic);

    public bool ToggleUnderlineOnActiveTableCell() =>
        TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Underline);

    public bool ToggleSuperscriptOnActiveTableCell() =>
        TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Superscript);

    public bool ToggleSubscriptOnActiveTableCell() =>
        TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Subscript);

    public bool SetTableHeaderRow(int slideIndex, uint shapeId, bool isHeaderRow)
    {
        var command = new SetTableHeaderRowCommand(slideIndex, shapeId, isHeaderRow);
        if (!command.HasEffect(Presentation))
            return false;

        Bus.Execute(command);
        return true;
    }

    /// <summary>Toggles one PowerPoint table-design emphasis flag on the selected table.</summary>
    public bool ToggleSelectedTableStyleFlag(TableStyleFlagKind kind)
    {
        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null)
            return false;

        var command = new SetTableStyleFlagCommand(
            _currentSlideIndex,
            shapeId,
            kind,
            !GetTableStyleFlagValue(table.Flags, kind));
        if (!command.HasEffect(Presentation))
            return false;

        Bus.Execute(command);
        return true;
    }

    private static bool GetTableStyleFlagValue(TableStyleFlags flags, TableStyleFlagKind kind) => kind switch
    {
        TableStyleFlagKind.FirstRow => flags.FirstRow,
        TableStyleFlagKind.LastRow => flags.LastRow,
        TableStyleFlagKind.FirstCol => flags.FirstCol,
        TableStyleFlagKind.LastCol => flags.LastCol,
        TableStyleFlagKind.BandRow => flags.BandRow,
        TableStyleFlagKind.BandCol => flags.BandCol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // ── Row / column insert and delete ────────────────────────────────────────────

    /// <summary>Inserts a row above the active cell's row. Undoable.</summary>
    public void InsertRowAbove()
    {
        int row = ActiveTableCell?.Row ?? 0;
        ExecuteTableCommand((si, id) => new InsertTableRowCommand(si, id, row));
        // Active cell shifts down by one because a row was inserted above it.
        if (ActiveTableCell.HasValue)
            SetActiveTableCell(ActiveTableCell.Value.Row + 1, ActiveTableCell.Value.Col);
    }

    /// <summary>Inserts a row below the active cell's row. Undoable.</summary>
    public void InsertRowBelow()
    {
        int row = (ActiveTableCell?.Row ?? -1) + 1;
        ExecuteTableCommand((si, id) => new InsertTableRowCommand(si, id, row));
    }

    /// <summary>Deletes the active cell's row. Undoable.</summary>
    public void DeleteRow()
    {
        int row = ActiveTableCell?.Row ?? 0;
        ExecuteTableCommand((si, id) => new DeleteTableRowCommand(si, id, row));

        // Clamp active cell after deletion.
        var table = GetSelectedTable();
        if (table is not null && ActiveTableCell.HasValue)
        {
            int r = Math.Clamp(ActiveTableCell.Value.Row, 0, Math.Max(0, table.Rows.Count - 1));
            ActiveTableCell = (r, Math.Clamp(ActiveTableCell.Value.Col, 0, Math.Max(0, table.ColumnWidthsEmu.Count - 1)));
            ActiveTableCellChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Inserts a column to the left of the active cell's column. Undoable.</summary>
    public void InsertColumnLeft()
    {
        int col = ActiveTableCell?.Col ?? 0;
        ExecuteTableCommand((si, id) => new InsertTableColumnCommand(si, id, col));
        if (ActiveTableCell.HasValue)
            SetActiveTableCell(ActiveTableCell.Value.Row, ActiveTableCell.Value.Col + 1);
    }

    /// <summary>Inserts a column to the right of the active cell's column. Undoable.</summary>
    public void InsertColumnRight()
    {
        int col = (ActiveTableCell?.Col ?? -1) + 1;
        ExecuteTableCommand((si, id) => new InsertTableColumnCommand(si, id, col));
    }

    /// <summary>Deletes the active cell's column. Undoable.</summary>
    public void DeleteColumn()
    {
        int col = ActiveTableCell?.Col ?? 0;
        ExecuteTableCommand((si, id) => new DeleteTableColumnCommand(si, id, col));

        var table = GetSelectedTable();
        if (table is not null && ActiveTableCell.HasValue)
        {
            int c = Math.Clamp(ActiveTableCell.Value.Col, 0, Math.Max(0, table.ColumnWidthsEmu.Count - 1));
            ActiveTableCell = (Math.Clamp(ActiveTableCell.Value.Row, 0, Math.Max(0, table.Rows.Count - 1)), c);
            ActiveTableCellChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Merge / split ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Merges the rectangular region [r1,c1]..[r2,c2] in the selected table. Undoable.
    /// </summary>
    public void MergeTableCells(int r1, int c1, int r2, int c2)
        => ExecuteTableCommand((si, id) => new MergeTableCellsCommand(si, id, r1, c1, r2, c2));

    /// <summary>Merges a named selection range. Undoable.</summary>
    public void MergeSelectedCells(int r1, int c1, int r2, int c2)
        => MergeTableCells(r1, c1, r2, c2);

    /// <summary>Merge the active cell with its right neighbor, or the cell below at a row edge.</summary>
    public bool TryMergeActiveTableCell()
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, table) = RequireSelectedTable();
        if (shapeId == 0 || table is null || active.Row < 0 || active.Row >= table.Rows.Count)
            return false;

        int rightColumn = active.Col + 1 < table.ColumnWidthsEmu.Count ? active.Col + 1 : active.Col;
        int belowRow = rightColumn == active.Col ? active.Row + 1 : active.Row;
        if (rightColumn == active.Col && belowRow >= table.Rows.Count)
            return false;

        var command = new MergeTableCellsCommand(
            _currentSlideIndex,
            shapeId,
            active.Row,
            active.Col,
            belowRow,
            rightColumn);
        if (!command.HasEffect(Presentation))
            return false;

        Bus.Execute(command);
        return true;
    }

    /// <summary>Splits the merged cell at (<paramref name="row"/>, <paramref name="col"/>). Undoable.
    /// A no-op on an unmerged cell records no undo entry (the bus skips no-effect commands).</summary>
    public void SplitTableCell(int row, int col)
        => ExecuteTableCommand((si, id) => new SplitTableCellCommand(si, id, row, col));

    /// <summary>Splits the merged anchor at the active cell (if any). Undoable.</summary>
    public void SplitSelectedCell()
    {
        if (ActiveTableCell is null) return;
        SplitTableCell(ActiveTableCell.Value.Row, ActiveTableCell.Value.Col);
    }

    /// <summary>Split the active merged cell and report whether the command changed the table.</summary>
    public bool TrySplitActiveTableCell()
    {
        if (ActiveTableCell is not { } active)
            return false;

        var (shapeId, _) = RequireSelectedTable();
        if (shapeId == 0)
            return false;

        var command = new SplitTableCellCommand(
            _currentSlideIndex,
            shapeId,
            active.Row,
            active.Col);
        if (!command.HasEffect(Presentation))
            return false;

        Bus.Execute(command);
        return true;
    }

    // ── Table helpers ─────────────────────────────────────────────────────────────

    /// <summary>Returns the TableShape of the first selected shape, or null if it is not a table.</summary>
    public TableShape? GetSelectedTable()
    {
        var slide = CurrentSlide;
        if (slide is null || _selectedShapeIds.Count == 0) return null;
        var shape = FindShape(slide.Shapes, _selectedShapeIds[0]);
        return shape?.Kind == SlideShapeKind.Table ? shape.Table : null;
    }

    private (uint shapeId, TableShape? table) RequireSelectedTable()
    {
        var slide = CurrentSlide;
        if (slide is null || _selectedShapeIds.Count == 0) return (0, null);
        var shape = FindShape(slide.Shapes, _selectedShapeIds[0]);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null) return (0, null);
        return (shape.Id, shape.Table);
    }

    private void ExecuteTableCommand(Func<int, uint, IPresentationCommand> factory)
    {
        var (shapeId, _) = RequireSelectedTable();
        if (shapeId == 0) return;
        Bus.Execute(factory(_currentSlideIndex, shapeId));
    }

    // ── Chart data API (Wave 9B) ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the <see cref="ChartShape"/> for the currently selected chart shape, or null
    /// if the selection is empty or the first selected shape is not a chart.
    /// </summary>
    public ChartShape? SelectedChart
    {
        get
        {
            if (CurrentSlide is null || _selectedShapeIds.Count == 0) return null;
            var shape = FindShape(CurrentSlide.Shapes, _selectedShapeIds[0]);
            return shape?.Kind == SlideShapeKind.Chart ? shape.Chart : null;
        }
    }

    /// <summary>
    /// True when the selected chart can accept data edits under its imported PowerPoint
    /// protection policy. A missing protection token means the chart is editable.
    /// </summary>
    public bool CanEditSelectedChartData => SelectedChart is { } chart
        && chart.ChartObjectProtected != true
        && chart.ChartDataProtected != true;

    /// <summary>
    /// True when the selected chart can accept formatting edits under its imported
    /// PowerPoint protection policy. A missing protection token means the chart is editable.
    /// </summary>
    public bool CanEditSelectedChartFormatting => SelectedChart is { } chart
        && chart.ChartObjectProtected != true
        && chart.ChartFormattingProtected != true;

    /// <summary>
    /// Changes only the selected chart's type through the same coordinate-aware, undoable path
    /// used by the chart data dialog. Scatter and Bubble transitions receive valid coordinates;
    /// ordinary chart data and formatting remain intact.
    /// </summary>
    public bool ChangeSelectedChartType(ChartType chartType)
    {
        var selectedChart = SelectedChart;
        if (selectedChart is null || !CanEditSelectedChartData || chartType == ChartType.Unknown)
            return false;

        var planner = ChartDataDialogPlanner.FromChart(selectedChart);
        planner.SetChartType(chartType);
        var plan = planner.BuildCommitPlan();
        ReplaceChartData(
            plan.Categories,
            plan.SeriesNames,
            plan.ValuesForCommand(),
            plan.ChartType,
            plan.XValuesForCommand(),
            plan.BubbleSizesForCommand());
        return true;
    }

    /// <summary>
    /// Sets the numeric value at [<paramref name="seriesIndex"/>][<paramref name="categoryIndex"/>]
    /// in the selected chart.  Undoable.
    /// </summary>
    public void SetChartValue(int seriesIndex, int categoryIndex, double value)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new SetChartCellValueCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            seriesIndex, categoryIndex, value));
    }

    /// <summary>Renames the category at <paramref name="categoryIndex"/>. Undoable.</summary>
    public void SetChartCategory(int categoryIndex, string label)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new SetChartCategoryLabelCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            categoryIndex, label));
    }

    /// <summary>Renames the series at <paramref name="seriesIndex"/>. Undoable.</summary>
    public void SetChartSeriesName(int seriesIndex, string name)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new SetChartSeriesNameCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            seriesIndex, name));
    }

    /// <summary>
    /// Changes one native ChartEx series layout identifier while preserving the
    /// source ChartEx family payload. The operation is undoable and is a no-op for
    /// classic charts or protected chart formatting.
    /// </summary>
    public void SetChartExSeriesLayout(int seriesIndex, string layoutId)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0 ||
            !CanEditSelectedChartFormatting)
            return;

        Bus.Execute(new SetChartExSeriesLayoutCommand(
            _currentSlideIndex, _selectedShapeIds[0], seriesIndex, layoutId));
    }

    /// <summary>Appends a new series to the selected chart. Undoable.</summary>
    public void AddChartSeries(string name = "New Series")
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new AddChartSeriesCommand(
            _currentSlideIndex, _selectedShapeIds[0], name));
    }

    /// <summary>Removes the series at <paramref name="seriesIndex"/> from the selected chart. Undoable.</summary>
    public void RemoveChartSeries(int seriesIndex)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new RemoveChartSeriesCommand(
            _currentSlideIndex, _selectedShapeIds[0], seriesIndex));
    }

    public void MoveChartSeries(int sourceIndex, int targetIndex)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new MoveChartSeriesCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            sourceIndex,
            targetIndex));
    }

    /// <summary>Appends a new category to the selected chart. Undoable.</summary>
    public void AddChartCategory(string label = "New Category")
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new AddChartCategoryCommand(
            _currentSlideIndex, _selectedShapeIds[0], label));
    }

    /// <summary>Removes the category at <paramref name="categoryIndex"/> from the selected chart. Undoable.</summary>
    public void RemoveChartCategory(int categoryIndex)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new RemoveChartCategoryCommand(
            _currentSlideIndex, _selectedShapeIds[0], categoryIndex));
    }

    /// <summary>
    /// Replaces the entire data payload of the selected chart in one undoable batch command.
    /// Used by <c>ChartDataDialog</c> so all grid edits become a single undo step.
    /// Gap points should be passed as null; they are preserved verbatim in the model.
    /// </summary>
    public void ReplaceChartData(
        IEnumerable<string>               categories,
        IEnumerable<string>               seriesNames,
        IEnumerable<IEnumerable<double?>> values)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new ReplaceChartDataCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            categories, seriesNames, values));
    }

    /// <summary>
    /// Replaces chart data and changes its type in one undoable batch.
    /// </summary>
    public void ReplaceChartData(
        IEnumerable<string>               categories,
        IEnumerable<string>               seriesNames,
        IEnumerable<IEnumerable<double?>> values,
        ChartType                          chartType)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new ReplaceChartDataCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            categories, seriesNames, values, chartType));
    }

    /// <summary>
    /// Replaces chart data including Scatter X values or Bubble sizes in one undoable batch.
    /// Coordinate rows are optional for ordinary category charts.
    /// </summary>
    public void ReplaceChartData(
        IEnumerable<string>               categories,
        IEnumerable<string>               seriesNames,
        IEnumerable<IEnumerable<double?>> values,
        ChartType                          chartType,
        IEnumerable<IEnumerable<double?>>? xValues,
        IEnumerable<IEnumerable<double?>>? bubbleSizes)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new ReplaceChartDataCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            categories,
            seriesNames,
            values,
            chartType,
            xValues,
            bubbleSizes));
    }

    /// <summary>Applies common chart title, legend, label, and gridline options as one undo step.</summary>
    public void ApplyChartDisplayOptions(ChartDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartDisplayOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies chart data-table visibility and border options as one undo step.</summary>
    public void ApplyChartDataTableOptions(ChartDataTableOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartDataTableOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies one chart axis options edit as a single undo step.</summary>
    public void ApplyChartAxisOptions(ChartAxisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartAxisOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies one chart-series formatting edit as a single undo step.</summary>
    public void ApplyChartSeriesOptions(ChartSeriesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartSeriesOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies one chart-point formatting edit as a single undo step.</summary>
    public void ApplyChartPointOptions(ChartPointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartPointOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Sets or clears one waterfall point's PowerPoint total semantics.</summary>
    public void SetWaterfallPointTotal(int pointIndex, bool setAsTotal)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;
        Bus.Execute(new SetWaterfallTotalPointCommand(
            _currentSlideIndex, _selectedShapeIds[0], pointIndex, setAsTotal));
    }

    /// <summary>Applies one plot-area or legend layout edit as a single undo step.</summary>
    public void ApplyChartLayoutOptions(ChartLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartLayoutOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies chart camera and Surface3D wireframe options as one undo step.</summary>
    public void ApplyChart3DViewOptions(Chart3DViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChart3DViewOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies chart object/data/formatting/selection protection as one undo step.</summary>
    public void ApplyChartProtectionOptions(ChartProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartProtectionOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies bubble-chart sizing and negative-value display options as one undo step.</summary>
    public void ApplyChartBubbleOptions(ChartBubbleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartBubbleOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies pie/doughnut rotation and hole options as one undo step.</summary>
    public void ApplyChartPieOptions(ChartPieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartPieOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies Scatter/Radar plot style as one undo step.</summary>
    public void ApplyChartPlotStyleOptions(ChartPlotStyleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartPlotStyleOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>Applies chart-wide default text formatting as one undo step.</summary>
    public void ApplyChartTextOptions(ChartTextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;

        Bus.Execute(new SetChartTextOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    public void ApplyChartAreaOptions(ChartAreaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (CurrentSlide is null || _selectedShapeIds.Count == 0)
            return;
        Bus.Execute(new SetChartAreaOptionsCommand(
            _currentSlideIndex,
            _selectedShapeIds[0],
            options));
    }

    /// <summary>
    /// Non-nullable overload for callers that already work with <c>double</c> sequences (no gaps).
    /// </summary>
    public void ReplaceChartData(
        IEnumerable<string>              categories,
        IEnumerable<string>              seriesNames,
        IEnumerable<IEnumerable<double>> values)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new ReplaceChartDataCommand(
            _currentSlideIndex, _selectedShapeIds[0],
            categories, seriesNames, values));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // ARRANGE / GROUP / ALIGN / DISTRIBUTE  (Wave 12A)
    // ════════════════════════════════════════════════════════════════════════════════

    // ── Z-order — BringToFront / SendToBack (BringForward/SendBackward remain in the original region above) ──

    /// <summary>
    /// Brings ALL selected shapes to the very top of z-order, preserving their relative order.
    /// Processes shapes in ascending z-order so the highest-z-selected ends up on top.
    /// Wrapped in a single BatchCommand so undo restores all shapes in one step.
    /// </summary>
    public void BringToFront()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;

        if (_selectedShapeIds.Count == 1)
        {
            Bus.Execute(new BringToFrontCommand(_currentSlideIndex, _selectedShapeIds[0]));
            return;
        }

        // FF2: multi-select — bring all selected shapes to front preserving relative order.
        // Process in ascending z-order (lowest first) so the last-processed (originally topmost)
        // ends up at the very top, preserving their relative stacking.
        var shapes = FindContainingShapeList(CurrentSlide.Shapes, _selectedShapeIds[0]);
        if (shapes is null || _selectedShapeIds.Any(id => shapes.FindIndex(shape => shape.Id == id) < 0)) return;
        var orderedIds = _selectedShapeIds
            .Select(id => (id, zIdx: shapes.FindIndex(s => s.Id == id)))
            .Where(t => t.zIdx >= 0)
            .OrderBy(t => t.zIdx)
            .Select(t => t.id)
            .ToList();

        var cmds = orderedIds.Select(id => (IPresentationCommand)new BringToFrontCommand(_currentSlideIndex, id));
        Bus.Execute(new BatchCommand("Bring to Front", cmds));
    }

    /// <summary>
    /// Sends ALL selected shapes to the very bottom of z-order, preserving their relative order.
    /// Processes shapes in descending z-order so the lowest-z-selected ends up at the bottom.
    /// Wrapped in a single BatchCommand so undo restores all shapes in one step.
    /// </summary>
    public void SendToBack()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;

        if (_selectedShapeIds.Count == 1)
        {
            Bus.Execute(new SendToBackCommand(_currentSlideIndex, _selectedShapeIds[0]));
            return;
        }

        // FF2: multi-select — send all selected shapes to back preserving relative order.
        // Process in descending z-order (highest first) so the last-processed (originally bottommost)
        // ends up at index 0, preserving their relative stacking.
        var shapes = FindContainingShapeList(CurrentSlide.Shapes, _selectedShapeIds[0]);
        if (shapes is null || _selectedShapeIds.Any(id => shapes.FindIndex(shape => shape.Id == id) < 0)) return;
        var orderedIds = _selectedShapeIds
            .Select(id => (id, zIdx: shapes.FindIndex(s => s.Id == id)))
            .Where(t => t.zIdx >= 0)
            .OrderByDescending(t => t.zIdx)
            .Select(t => t.id)
            .ToList();

        var cmds = orderedIds.Select(id => (IPresentationCommand)new SendToBackCommand(_currentSlideIndex, id));
        Bus.Execute(new BatchCommand("Send to Back", cmds));
    }

    // ── Group / Ungroup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps the selected shapes (≥2) into a new Group shape and selects the group.
    /// Undoable in one step.
    /// </summary>
    public void GroupSelectedShapes()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count < 2) return;
        var cmd = new GroupShapesCommand(_currentSlideIndex, _selectedShapeIds);
        Bus.Execute(cmd);

        // Select the command's group wherever it was inserted, including inside a parent group.
        var group = cmd.GroupId is { } groupId
            ? FindShape(CurrentSlide.Shapes, groupId)
            : null;
        if (group is not null)
        {
            _selectedShapeIds.Clear();
            _selectedShapeIds.Add(group.Id);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Ungroupsthe first selected shape if it is a Group, selects the freed children.
    /// Undoable in one step.
    /// </summary>
    public void UngroupSelected()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var id     = _selectedShapeIds[0];
        var shape  = FindShape(CurrentSlide.Shapes, id);
        if (shape?.Kind != SlideShapeKind.Group) return;

        var childIds = shape.Children.Select(c => c.Id).ToList();
        Bus.Execute(new UngroupShapeCommand(_currentSlideIndex, id));

        // Select the freed children.
        _selectedShapeIds.Clear();
        foreach (var cid in childIds)
            _selectedShapeIds.Add(cid);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Changes the selected AutoShape's preset geometry while retaining its authored frame,
    /// text, formatting, and effects. The geometry transition is one undoable operation.
    /// </summary>
    public bool ChangeSelectedAutoShapeKind(DrawingShapeKind kind)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count != 1)
            return false;

        var shape = FindShape(CurrentSlide.Shapes, _selectedShapeIds[0]);
        if (shape is not { Kind: SlideShapeKind.AutoShape } || shape.AutoShapeKind == kind)
            return false;

        Bus.Execute(new ChangeAutoShapeKindCommand(_currentSlideIndex, shape.Id, kind));
        return shape.AutoShapeKind == kind;
    }

    // ── Align ─────────────────────────────────────────────────────────────────────

    /// <summary>Aligns selected shapes' left edges. One undo step.</summary>
    public void AlignLeft()         => ExecuteAlignCommand(AlignKind.Left);
    /// <summary>Centers selected shapes horizontally. One undo step.</summary>
    public void AlignCenterH()      => ExecuteAlignCommand(AlignKind.CenterH);
    /// <summary>Aligns selected shapes' right edges. One undo step.</summary>
    public void AlignRight()        => ExecuteAlignCommand(AlignKind.Right);
    /// <summary>Aligns selected shapes' top edges. One undo step.</summary>
    public void AlignTop()          => ExecuteAlignCommand(AlignKind.Top);
    /// <summary>Centers selected shapes vertically. One undo step.</summary>
    public void AlignMiddle()       => ExecuteAlignCommand(AlignKind.Middle);
    /// <summary>Aligns selected shapes' bottom edges. One undo step.</summary>
    public void AlignBottom()       => ExecuteAlignCommand(AlignKind.Bottom);

    /// <summary>Aligns selected shapes to the slide's left edge. One undo step.</summary>
    public void AlignLeftToSlide() => ExecuteAlignToSlideCommand(AlignKind.Left);
    /// <summary>Centers selected shapes on the slide horizontally. One undo step.</summary>
    public void AlignCenterHToSlide() => ExecuteAlignToSlideCommand(AlignKind.CenterH);
    /// <summary>Aligns selected shapes to the slide's right edge. One undo step.</summary>
    public void AlignRightToSlide() => ExecuteAlignToSlideCommand(AlignKind.Right);
    /// <summary>Aligns selected shapes to the slide's top edge. One undo step.</summary>
    public void AlignTopToSlide() => ExecuteAlignToSlideCommand(AlignKind.Top);
    /// <summary>Centers selected shapes on the slide vertically. One undo step.</summary>
    public void AlignMiddleToSlide() => ExecuteAlignToSlideCommand(AlignKind.Middle);
    /// <summary>Aligns selected shapes to the slide's bottom edge. One undo step.</summary>
    public void AlignBottomToSlide() => ExecuteAlignToSlideCommand(AlignKind.Bottom);

    private void ExecuteAlignCommand(AlignKind kind)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new AlignShapesCommand(_currentSlideIndex, _selectedShapeIds, kind));
    }

    private void ExecuteAlignToSlideCommand(AlignKind kind)
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        Bus.Execute(new AlignShapesToSlideCommand(_currentSlideIndex, _selectedShapeIds, kind));
    }

    // ── Distribute ────────────────────────────────────────────────────────────────

    /// <summary>Evenly spaces selected shapes horizontally (≥3 required). One undo step.</summary>
    public void DistributeHorizontally()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count < 3) return;
        Bus.Execute(new DistributeShapesCommand(_currentSlideIndex, _selectedShapeIds, DistributeKind.Horizontal));
    }

    /// <summary>Evenly spaces selected shapes vertically (≥3 required). One undo step.</summary>
    public void DistributeVertically()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count < 3) return;
        Bus.Execute(new DistributeShapesCommand(_currentSlideIndex, _selectedShapeIds, DistributeKind.Vertical));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // FIND & REPLACE  (Wave 12B)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all text matches for <paramref name="query"/> across the entire presentation
    /// (shape TextBody, table cells, slide Notes; not comments).
    /// Returns an empty list when query is null or empty.
    /// </summary>
    public List<TextSearchMatch> FindAll(string? query, TextSearchOptions? opts = null)
        => PresentationTextSearch.FindAll(Presentation, query, opts);

    /// <summary>
    /// Navigates to the slide and selects the shape that contains <paramref name="match"/>.
    /// Does nothing if the match refers to a virtual notes id or the slide/shape cannot be found.
    /// </summary>
    public void NavigateTo(TextSearchMatch match)
    {
        if (match.SlideIndex < 0 || match.SlideIndex >= Presentation.Slides.Count) return;

        // Go to the slide first.
        if (_currentSlideIndex != match.SlideIndex)
            SelectSlide(match.SlideIndex);

        // Select the shape (skip virtual notes shape ids).
        const uint NotesBase = 0xFFFF0000u;
        if (match.ShapeId < NotesBase)
            Select(match.ShapeId);
    }

    /// <summary>
    /// Replaces the text matched by <paramref name="match"/> with <paramref name="replacement"/>.
    /// Navigates to the match's slide and selects the shape. Undoable.
    /// </summary>
    public void ReplaceOne(TextSearchMatch match, string replacement)
    {
        NavigateTo(match);
        Bus.Execute(new ReplaceOneCommand(match, replacement));
    }

    /// <summary>
    /// Replaces ALL occurrences of <paramref name="query"/> in the presentation with
    /// <paramref name="replacement"/> in a single undoable step.
    /// Returns the number of replacements made (0 when nothing matched).
    /// </summary>
    public int ReplaceAll(string? query, string replacement, TextSearchOptions? opts = null)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        opts ??= new TextSearchOptions();

        var matches = PresentationTextSearch.FindAll(Presentation, query, opts);
        if (matches.Count == 0) return 0;

        Bus.Execute(new ReplaceAllCommand(query, replacement, opts));
        return matches.Count;
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    private enum RunToggleKind { Bold, Italic, Underline, Superscript, Subscript }

    private void ClampCurrentSlide()
    {
        if (Presentation.Slides.Count == 0)
        {
            if (_currentSlideIndex != -1)
            {
                _currentSlideIndex = -1;
                CurrentSlideChanged?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        CurrentSlideIndex = Math.Clamp(_currentSlideIndex, 0, Presentation.Slides.Count - 1);
    }

    private static bool GetRunProp(Run r, RunToggleKind k) => k switch
    {
        RunToggleKind.Bold      => r.Bold,
        RunToggleKind.Italic    => r.Italic,
        RunToggleKind.Underline => r.Underline,
        RunToggleKind.Superscript => r.BaselineOffset > 0,
        RunToggleKind.Subscript   => r.BaselineOffset < 0,
        _                       => false
    };

    private IPresentationCommand MakeToggleCommand(RunToggleKind k, int si, uint id, int pi, int ri) => k switch
    {
        RunToggleKind.Bold      => new ToggleRunBoldCommand(si, id, pi, ri),
        RunToggleKind.Italic    => new ToggleRunItalicCommand(si, id, pi, ri),
        RunToggleKind.Underline => new ToggleRunUnderlineCommand(si, id, pi, ri),
        RunToggleKind.Superscript => new ToggleRunSuperscriptCommand(si, id, pi, ri),
        RunToggleKind.Subscript   => new ToggleRunSubscriptCommand(si, id, pi, ri),
        _                       => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private void TogglePropOnSelection(RunToggleKind kind)
    {
        if (CurrentSlide is null) return;

        // Collect all runs from selected shapes.
        var allRuns = new List<(int si, uint shapeId, int pi, int ri, Run run)>();
        foreach (var id in _selectedShapeIds)
        {
            var s = FindShape(CurrentSlide.Shapes, id);
            if (s?.TextBody is null) continue;
            for (int pi = 0; pi < s.TextBody.Paragraphs.Count; pi++)
            for (int ri = 0; ri < s.TextBody.Paragraphs[pi].Runs.Count; ri++)
                allRuns.Add((_currentSlideIndex, id, pi, ri, s.TextBody.Paragraphs[pi].Runs[ri]));
        }

        if (allRuns.Count == 0) return;

        // Majority rule: if all already have the property → turn off; otherwise turn on.
        bool allSet     = allRuns.All(t => GetRunProp(t.run, kind));
        bool targetValue = !allSet;

        // Issue toggle commands only where the value needs to change.
        foreach (var (si, shapeId, pi, ri, run) in allRuns)
        {
            if (GetRunProp(run, kind) == targetValue) continue;
            Bus.Execute(MakeToggleCommand(kind, si, shapeId, pi, ri));
        }
    }
}
