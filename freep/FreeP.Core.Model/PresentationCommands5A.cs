namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// WAVE 5A COMMANDS  — clipboard, theme, slide size, insert table/chart, format painter
// ════════════════════════════════════════════════════════════════════════════════

// ── Clipboard — paste multiple shapes ────────────────────────────────────────

/// <summary>
/// Adds a list of shapes (clipboard contents) to the slide at <paramref name="slideIndex"/>,
/// capturing them for undo by reference.
/// </summary>
public sealed class PasteShapesCommand : IPresentationCommand
{
    private readonly int                  _slideIndex;
    private readonly List<SlideShape>     _shapes;

    public PasteShapesCommand(int slideIndex, IEnumerable<SlideShape> shapes)
    {
        _slideIndex = slideIndex;
        _shapes     = shapes.ToList();
    }

    public string Label => "Paste";

    public int EstimatedBytes => 256 + _shapes.Count * 512;

    public void Apply(Presentation p)
    {
        var list = ShapeListOrNull(p);
        if (list is null) return;
        foreach (var s in _shapes)
            list.Add(s);
    }

    public void Revert(Presentation p)
    {
        var list = ShapeListOrNull(p);
        if (list is null) return;
        foreach (var s in _shapes)
            list.Remove(s);
    }

    private List<SlideShape>? ShapeListOrNull(Presentation p) =>
        _slideIndex >= 0 && _slideIndex < p.Slides.Count
            ? p.Slides[_slideIndex].Shapes
            : null;
}

// ── Clipboard — paste a slide ─────────────────────────────────────────────────

/// <summary>
/// Inserts a cloned slide at <paramref name="insertAt"/> (a deep-clone is passed in at
/// construction time). Revert removes by reference.
/// </summary>
public sealed class PasteSlideCommand : IPresentationCommand
{
    private readonly int   _insertAt;
    private readonly Slide _slide;
    private List<SectionSnapshot>? _beforeSections;

    public PasteSlideCommand(int insertAt, Slide slide)
    {
        _insertAt = insertAt;
        _slide    = slide;
    }

    public string Label => "Paste Slide";

    public void Apply(Presentation p)
    {
        var idx = Math.Clamp(_insertAt, 0, p.Slides.Count);

        if (_beforeSections is null)
        {
            _beforeSections = p.Sections
                .Select(section => new SectionSnapshot(
                    section.Id,
                    section.Name,
                    section.SlideIds.ToArray()))
                .ToList();
        }

        p.Slides.Insert(idx, _slide);
        AddSlideToNeighborSection(p, idx, _slide.Id);
    }

    public void Revert(Presentation p)
    {
        p.Slides.Remove(_slide);
        RestoreSections(p, _beforeSections);
    }

    private static void AddSlideToNeighborSection(
        Presentation p,
        int insertedIndex,
        string insertedSlideId)
    {
        if (p.Slides.Count <= 1)
            return;

        if (insertedIndex > 0)
        {
            var previousSlideId = p.Slides[insertedIndex - 1].Id;
            foreach (var section in p.Sections)
            {
                var neighborIndex = section.SlideIds.FindIndex(id =>
                    string.Equals(id, previousSlideId, StringComparison.Ordinal));
                if (neighborIndex < 0)
                    continue;

                section.SlideIds.Insert(neighborIndex + 1, insertedSlideId);
                return;
            }
        }
        else
        {
            var nextSlideId = p.Slides[1].Id;
            foreach (var section in p.Sections)
            {
                var neighborIndex = section.SlideIds.FindIndex(id =>
                    string.Equals(id, nextSlideId, StringComparison.Ordinal));
                if (neighborIndex < 0)
                    continue;

                section.SlideIds.Insert(neighborIndex, insertedSlideId);
                return;
            }
        }
    }

    private static void RestoreSections(
        Presentation p,
        IReadOnlyList<SectionSnapshot>? snapshots)
    {
        if (snapshots is null)
            return;

        p.Sections.Clear();
        foreach (var snapshot in snapshots)
        {
            var section = new PresentationSection
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
            };
            section.SlideIds.AddRange(snapshot.SlideIds);
            p.Sections.Add(section);
        }
    }

    private sealed record SectionSnapshot(string Id, string Name, IReadOnlyList<string> SlideIds);
}

// ── Theme ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Swaps the presentation's <see cref="Presentation.Theme"/>. Captures the old theme
/// instance for undo.
/// </summary>
public sealed class SetThemeCommand : IPresentationCommand
{
    private readonly PresentationTheme _newTheme;
    private PresentationTheme?         _oldTheme;

    public SetThemeCommand(PresentationTheme newTheme) => _newTheme = newTheme;

    public string Label => "Set Theme";

    public void Apply(Presentation p)
    {
        _oldTheme = p.Theme;
        p.Theme   = _newTheme;
    }

    public void Revert(Presentation p)
    {
        if (_oldTheme is not null)
            p.Theme = _oldTheme;
    }
}

// ── Slide size ────────────────────────────────────────────────────────────────

/// <summary>
/// Sets the slide dimensions (<see cref="Presentation.SlideSizeCxEmu"/> /
/// <see cref="Presentation.SlideSizeCyEmu"/>). Captures old values for undo.
/// </summary>
public sealed class SetSlideSizeCommand : IPresentationCommand
{
    private readonly long _newCx;
    private readonly long _newCy;
    private long          _oldCx;
    private long          _oldCy;

    public SetSlideSizeCommand(long cxEmu, long cyEmu)
    {
        _newCx = cxEmu;
        _newCy = cyEmu;
    }

    public string Label => "Set Slide Size";

    public void Apply(Presentation p)
    {
        _oldCx = p.SlideSizeCxEmu;
        _oldCy = p.SlideSizeCyEmu;
        p.SlideSizeCxEmu = _newCx;
        p.SlideSizeCyEmu = _newCy;
    }

    public void Revert(Presentation p)
    {
        p.SlideSizeCxEmu = _oldCx;
        p.SlideSizeCyEmu = _oldCy;
    }
}

// ── Format painter — shape fill + outline + run defaults ─────────────────────

/// <summary>Sets or clears the explicit background fill of one slide.</summary>
public sealed class SetSlideBackgroundCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly ShapeFill? _newFill;
    private ShapeFill? _oldFill;

    public SetSlideBackgroundCommand(int slideIndex, ShapeFill? fill)
    {
        _slideIndex = slideIndex;
        _newFill = fill;
    }

    public string Label => _newFill is null ? "Reset Slide Background" : "Set Slide Background";

    public bool HasEffect(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return false;

        var current = p.Slides[_slideIndex].Background;
        if (_newFill is null)
            return current is not null;
        if (current is not ShapeFill.Solid currentSolid || _newFill is not ShapeFill.Solid nextSolid)
            return true;

        return currentSolid.Color.Resolved != nextSolid.Color.Resolved ||
               currentSolid.Color.Alpha != nextSolid.Color.Alpha;
    }

    public void Apply(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count)
            return;

        _oldFill = p.Slides[_slideIndex].Background;
        p.Slides[_slideIndex].Background = _newFill;
    }

    public void Revert(Presentation p)
    {
        if (_slideIndex >= 0 && _slideIndex < p.Slides.Count)
            p.Slides[_slideIndex].Background = _oldFill;
    }
}

/// <summary>
/// Applies a captured fill/outline and run-format snapshot to all selected shapes.
/// Stores per-shape old values (fill, outline, textBody) for undo.
/// </summary>
public sealed class ApplyFormatPainterCommand : IPresentationCommand
{
    private readonly int                       _slideIndex;
    private readonly IReadOnlyList<uint>       _targetIds;
    private readonly ShapeFill?                _fill;
    private readonly ShapeOutline?             _outline;
    private readonly RunFormatSnapshot?        _runFormat;

    // Captured for undo
    private readonly record struct ShapeSnapshot(uint Id, ShapeFill? Fill, ShapeOutline? Outline, TextBody? OldBody);
    private List<ShapeSnapshot> _undo = new();

    public ApplyFormatPainterCommand(
        int slideIndex,
        IEnumerable<uint> targetIds,
        ShapeFill? fill,
        ShapeOutline? outline,
        RunFormatSnapshot? runFormat)
    {
        _slideIndex = slideIndex;
        _targetIds  = targetIds.ToList();
        _fill       = fill;
        _outline    = outline;
        _runFormat  = runFormat;
    }

    public string Label => "Format Painter";

    public void Apply(Presentation p)
    {
        _undo.Clear();
        var slide = SlideOrNull(p);
        if (slide is null) return;

        foreach (var id in _targetIds)
        {
            var shape = FindShape(slide.Shapes, id);
            if (shape is null) continue;

            _undo.Add(new ShapeSnapshot(id, shape.Fill, shape.Outline, shape.TextBody));

            if (_fill    is not null) shape.Fill    = _fill;
            if (_outline is not null) shape.Outline = _outline;

            if (_runFormat is not null && shape.TextBody is not null)
                ApplyRunFormat(shape.TextBody, _runFormat);
        }
    }

    public void Revert(Presentation p)
    {
        var slide = SlideOrNull(p);
        if (slide is null) return;

        foreach (var snap in _undo)
        {
            var shape = FindShape(slide.Shapes, snap.Id);
            if (shape is null) continue;
            shape.Fill     = snap.Fill;
            shape.Outline  = snap.Outline;
            shape.TextBody = snap.OldBody;
        }
    }

    private Slide? SlideOrNull(Presentation p) =>
        _slideIndex >= 0 && _slideIndex < p.Slides.Count ? p.Slides[_slideIndex] : null;

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint id)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == id) return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, id) is { } child)
                return child;
        }

        return null;
    }

    private static void ApplyRunFormat(TextBody body, RunFormatSnapshot fmt)
    {
        foreach (var para in body.Paragraphs)
        {
            foreach (var run in para.Runs)
            {
                if (fmt.FontFamily is not null) run.FontFamily = fmt.FontFamily;
                if (fmt.FontSizePt.HasValue)    run.FontSizePt = fmt.FontSizePt;
                if (fmt.Color is not null)       run.Color      = fmt.Color;
                if (fmt.Bold.HasValue)   { run.Bold   = fmt.Bold.Value;   run.BoldSet   = true; }
                if (fmt.Italic.HasValue) { run.Italic = fmt.Italic.Value; run.ItalicSet = true; }
            }
        }
    }
}

/// <summary>
/// A snapshot of run-level formatting captured by the format painter from a source shape.
/// Null fields mean "don't apply".
/// </summary>
public sealed class RunFormatSnapshot
{
    public string?          FontFamily { get; init; }
    public double?          FontSizePt { get; init; }
    public ThemeAwareColor? Color      { get; init; }
    public bool?            Bold       { get; init; }
    public bool?            Italic     { get; init; }
}
