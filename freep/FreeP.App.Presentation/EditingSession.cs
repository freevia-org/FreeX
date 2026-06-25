using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

// Paste offset: ~0.2 inches in EMU  (914400 EMU = 1 inch)
file static class PasteOffset
{
    internal const long Emu = 182880L; // 0.2 inch
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

    // ── Selection ─────────────────────────────────────────────────────────────────

    /// <summary>The set of selected shape ids on the current slide.</summary>
    public IReadOnlyList<uint> SelectedShapeIds => _selectedShapeIds;

    /// <summary>
    /// Selects a shape. If <paramref name="addToSelection"/> is false (default), replaces
    /// the current selection.
    /// </summary>
    public void Select(uint shapeId, bool addToSelection = false)
    {
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
            _selectedShapeIds.Add(s.Id);
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

    /// <summary>Navigates to the slide at <paramref name="index"/> and clears selection.</summary>
    public void SelectSlide(int index)
    {
        ClearSelection();
        CurrentSlideIndex = index;
    }

    // ── Shape operations (operate on current slide) ───────────────────────────────

    /// <summary>Adds a shape to the current slide.</summary>
    public void AddShape(SlideShape shape)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new AddShapeCommand(_currentSlideIndex, shape));
    }

    /// <summary>Deletes all currently selected shapes.</summary>
    public void DeleteSelected()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        // Delete in reverse z-order to keep indices stable.
        var toDelete = _selectedShapeIds.ToList();
        ClearSelection();
        foreach (var id in toDelete)
            Bus.Execute(new DeleteShapeCommand(_currentSlideIndex, id));
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

    /// <summary>Sets the rotation (degrees, clockwise) of a single shape.</summary>
    public void RotateShape(uint shapeId, double newRotationDeg)
    {
        if (CurrentSlide is null) return;
        Bus.Execute(new RotateShapeCommand(_currentSlideIndex, shapeId, newRotationDeg));
    }

    /// <summary>Sets fill on all selected shapes.</summary>
    public void SetSelectedFill(ShapeFill? fill)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new SetShapeFillCommand(_currentSlideIndex, id, fill));
    }

    /// <summary>Sets outline on all selected shapes.</summary>
    public void SetSelectedOutline(ShapeOutline? outline)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
            Bus.Execute(new SetShapeOutlineCommand(_currentSlideIndex, id, outline));
    }

    /// <summary>
    /// Brings the first selected shape one step forward in z-order (swap with next shape).
    /// </summary>
    public void BringForward()
    {
        if (CurrentSlide is null || _selectedShapeIds.Count == 0) return;
        var shapes = CurrentSlide.Shapes;
        var id     = _selectedShapeIds[0];
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
        var shapes = CurrentSlide.Shapes;
        var id     = _selectedShapeIds[0];
        var idx    = shapes.FindIndex(s => s.Id == id);
        if (idx <= 0) return;
        Bus.Execute(new ReorderShapeCommand(_currentSlideIndex, id, idx - 1));
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

    /// <summary>Gets the transition for the current slide, or null if none.</summary>
    public SlideTransition? CurrentSlideTransition => CurrentSlide?.Transition;

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

    /// <summary>
    /// Sets font family on every run in all selected shapes.
    /// </summary>
    public void SetFontOnSelection(string? fontFamily)
    {
        if (CurrentSlide is null) return;
        foreach (var id in _selectedShapeIds)
        {
            var s = CurrentSlide.Shapes.FirstOrDefault(sh => sh.Id == id);
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
            var s = CurrentSlide.Shapes.FirstOrDefault(sh => sh.Id == id);
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
            var s = CurrentSlide.Shapes.FirstOrDefault(sh => sh.Id == id);
            if (s?.TextBody is null) continue;
            for (int pi = 0; pi < s.TextBody.Paragraphs.Count; pi++)
            for (int ri = 0; ri < s.TextBody.Paragraphs[pi].Runs.Count; ri++)
                Bus.Execute(new SetRunColorCommand(_currentSlideIndex, id, pi, ri, color));
        }
    }

    // ── Default shape factories (used by ribbon insert commands) ──────────────────

    /// <summary>
    /// Slide center in EMU, width=~3 inches, height=~2 inches. Gives 3C a reasonable target.
    /// </summary>
    private (long x, long y, long cx, long cy) DefaultShapeBounds()
    {
        // 1 inch = 914400 EMU; default 3"×2" centered on a 10"×7.5" slide
        const long cx = 2743200L; // 3 inches
        const long cy = 1828800L; // 2 inches
        var x = (Presentation.SlideSizeCxEmu - cx) / 2;
        var y = (Presentation.SlideSizeCyEmu - cy) / 2;
        return (x, y, cx, cy);
    }

    private uint NextShapeId()
    {
        var slide = CurrentSlide;
        if (slide is null) return 1u;
        return slide.Shapes.Count == 0 ? 1u : slide.Shapes.Max(s => s.Id) + 1u;
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

    /// <summary>Creates and inserts a default rectangle autoshape onto the current slide.</summary>
    public SlideShape InsertDefaultRectangle()
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id            = NextShapeId(),
            Name          = "Rectangle",
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>Creates and inserts a default ellipse autoshape onto the current slide.</summary>
    public SlideShape InsertDefaultEllipse()
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
        var shape = new SlideShape
        {
            Id            = NextShapeId(),
            Name          = "Ellipse",
            Kind          = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Ellipse,
            OffsetXEmu    = x,
            OffsetYEmu    = y,
            ExtentCxEmu   = cx,
            ExtentCyEmu   = cy,
        };
        AddShape(shape);
        return shape;
    }

    /// <summary>
    /// Creates and inserts a picture shape from raw image bytes onto the current slide.
    /// </summary>
    public SlideShape InsertPicture(byte[] imageBytes, string contentType = "image/png")
    {
        var (x, y, cx, cy) = DefaultShapeBounds();
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
            .Select(id => slide.Shapes.FirstOrDefault(s => s.Id == id))
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
        if (CurrentSlide is null) return;
        if (_shapeClipboard is not { Count: > 0 }) return;

        // Deep-clone again so repeated Paste produces independent copies.
        var clones = _shapeClipboard.Select(s => SlideCloner.CloneShape(s)).ToList();

        // Assign fresh Ids and apply paste offset.
        uint nextId = CurrentSlide.Shapes.Count == 0
            ? 1u
            : CurrentSlide.Shapes.Max(s => s.Id) + 1u;

        foreach (var c in clones)
        {
            c.Id          = nextId++;
            c.OffsetXEmu += PasteOffset.Emu;
            c.OffsetYEmu += PasteOffset.Emu;
        }

        Bus.Execute(new PasteShapesCommand(_currentSlideIndex, clones));

        // Select the pasted shapes.
        _selectedShapeIds.Clear();
        foreach (var c in clones)
            _selectedShapeIds.Add(c.Id);
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

    /// <summary>Sets the slide size to 16:9 widescreen (12192000 × 6858000 EMU). Undoable.</summary>
    public void SetSlideSize16x9()
        => SetSlideSizeCustom(12192000L, 6858000L);

    /// <summary>Sets the slide size to 4:3 standard (9144000 × 6858000 EMU). Undoable.</summary>
    public void SetSlideSize4x3()
        => SetSlideSizeCustom(9144000L, 6858000L);

    /// <summary>
    /// Overload alias — same as <see cref="SetSlideSizeCustom"/> but named per the spec contract.
    /// </summary>
    public void SetSlideSize(long cxEmu, long cyEmu)
        => SetSlideSizeCustom(cxEmu, cyEmu);

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

    // ── Insert chart ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and inserts a default <see cref="ChartShape"/> of the given
    /// <paramref name="chartType"/> with three sample categories and two series so it
    /// renders immediately.  Undoable.
    /// </summary>
    public SlideShape InsertChart(ChartType chartType = ChartType.ColumnClustered)
    {
        var (x, y, cx, cy) = DefaultShapeBounds();

        var chart = new ChartShape
        {
            ChartType = chartType,
            Title     = "Chart Title",
            Legend    = LegendPosition.Bottom,
        };

        // Default sample data — 3 categories, 2 series.
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);

        var s1 = new ChartSeries { Name = "Series 1" };
        s1.Values.AddRange([4.3, 2.5, 3.5]);
        chart.Series.Add(s1);

        var s2 = new ChartSeries { Name = "Series 2" };
        s2.Values.AddRange([2.4, 4.4, 1.8]);
        chart.Series.Add(s2);

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

        var source = slide.Shapes.FirstOrDefault(s => s.Id == _selectedShapeIds[0]);
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

    // ── Private helpers ───────────────────────────────────────────────────────────

    private enum RunToggleKind { Bold, Italic, Underline }

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
        _                       => false
    };

    private IPresentationCommand MakeToggleCommand(RunToggleKind k, int si, uint id, int pi, int ri) => k switch
    {
        RunToggleKind.Bold      => new ToggleRunBoldCommand(si, id, pi, ri),
        RunToggleKind.Italic    => new ToggleRunItalicCommand(si, id, pi, ri),
        RunToggleKind.Underline => new ToggleRunUnderlineCommand(si, id, pi, ri),
        _                       => throw new ArgumentOutOfRangeException(nameof(k))
    };

    private void TogglePropOnSelection(RunToggleKind kind)
    {
        if (CurrentSlide is null) return;

        // Collect all runs from selected shapes.
        var allRuns = new List<(int si, uint shapeId, int pi, int ri, Run run)>();
        foreach (var id in _selectedShapeIds)
        {
            var s = CurrentSlide.Shapes.FirstOrDefault(sh => sh.Id == id);
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
