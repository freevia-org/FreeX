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

    // Real content estimate (pictures, OLE/SmartArt/preserved-object payloads, table cells, text)
    // rather than a flat per-shape heuristic: this is the literal Ctrl+V path, so a pasted image
    // or embedded object must actually count toward the 50MB undo budget.
    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(
        _shapes.Select(PresentationCommandSizeEstimator.EstimateBytes));

    public void Apply(Presentation p)
    {
        var list = ShapeListOrNull(p);
        if (list is null) return;
        foreach (var s in _shapes)
            list.Add(s);
    }

    public void Revert(Presentation p)
    {
        // r176: remove by shape Id. ShapeListOrNull already re-resolves the LIST from the live
        // presentation, but List<SlideShape>.Remove is reference equality and a slide swap
        // (Insert > Header and Footer) replaces every shape object in that list with a clone,
        // which would leave every pasted shape stranded on the slide after undo.
        var list = ShapeListOrNull(p);
        if (list is null) return;
        foreach (var s in _shapes)
        {
            var index = list.FindIndex(shape => shape.Id == s.Id);
            if (index >= 0)
                list.RemoveAt(index);
        }
    }

    private List<SlideShape>? ShapeListOrNull(Presentation p) =>
        _slideIndex >= 0 && _slideIndex < p.Slides.Count
            ? p.Slides[_slideIndex].Shapes
            : null;
}

// ── Clipboard — paste a slide ─────────────────────────────────────────────────

/// <summary>
/// Inserts a cloned slide at <paramref name="insertAt"/> (a deep-clone is passed in at
/// construction time). Revert removes by Slide.Id, not by reference.
/// </summary>
public sealed class PasteSlideCommand : IPresentationCommand
{
    private readonly int   _insertAt;
    private readonly Slide _slide;
    private PresentationSectionMembershipSnapshot? _beforeSections;

    public PasteSlideCommand(int insertAt, Slide slide)
    {
        _insertAt = insertAt;
        _slide    = slide;
    }

    public string Label => "Paste Slide";

    public int EstimatedBytes => PresentationCommandSizeEstimator.EstimateBytes(_slide);

    public void Apply(Presentation p)
    {
        var idx = Math.Clamp(_insertAt, 0, p.Slides.Count);

        _beforeSections ??= PresentationSectionMembershipSnapshot.Capture(p);

        p.Slides.Insert(idx, _slide);
        PresentationSectionMembershipSnapshot.AddInsertedSlide(p, idx, _slide.Id);
    }

    public void Revert(Presentation p)
    {
        // r176 remediation: resolve by identity, not by reference. A Header/Footer edit clones and
        // swaps the slide objects, so the reference captured at Apply time is no longer the one in
        // the list and List<Slide>.Remove silently does nothing -- leaving the pasted slide behind
        // after undo. Same defect and same fix as DuplicateSlideCommand and InsertSlideCommand this
        // round; an auditor reproduced this one at runtime (paste, header/footer edit, undo twice,
        // slide count stayed 2 instead of 1).
        var index = p.Slides.FindIndex(slide => string.Equals(slide.Id, _slide.Id, StringComparison.Ordinal));
        if (index >= 0)
            p.Slides.RemoveAt(index);
        _beforeSections?.Restore(p);
    }
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

    // Per-master state cleared on Apply, paired with the values to restore on Revert. Only
    // populated the first time Apply runs (Redo must not re-capture already-cleared values as
    // the "old" state).
    private List<(SlideMaster Master, PresentationTheme? OldMasterTheme, string? OldThemePartPath)>? _clearedMasterState;

    public SetThemeCommand(PresentationTheme newTheme) => _newTheme = newTheme;

    public string Label => "Set Theme";

    public void Apply(Presentation p)
    {
        _oldTheme = p.Theme;
        p.Theme   = _newTheme;

        // Both rendering (SlideCompositor) and saving (PptxPackageWriter) resolve a master's
        // effective theme as (master.Theme ?? presentation.Theme). A master read from a real
        // .pptx has its own Theme populated per-master (PptxPackageReader), so merely swapping
        // Presentation.Theme above has no visible or saved effect for any deck that has ever
        // been opened from disk -- master.Theme keeps shadowing the new pick. This is a single,
        // deck-wide "apply this theme" entry point (there is no per-master theme picker), so
        // every master must fall through to the newly chosen Presentation.Theme: null out
        // master.Theme unconditionally.
        //
        // Separately, a master that has no parsed Theme but DOES carry a ThemePartPath is the
        // corrupted-but-preserved-theme-part case: the writer's preservation guard re-emits the
        // ORIGINAL theme part bytes verbatim instead of the resolved theme, to protect a
        // damaged-but-untouched theme part across saves. That guard must not survive the user
        // explicitly picking a new theme here either, or the save would silently discard the
        // pick and keep the stale/damaged bytes. Clear ThemePartPath too.
        //
        // Capture the previous values so Revert (undo) can restore both the per-master theme and
        // preservation if this command is undone. First Apply: capture+clear. Later Apply calls
        // are redos after an intervening Revert restored the values — re-clear the SAME captured
        // masters rather than recapturing (which would just re-read the already-restored old
        // values, but skip any master whose state a since-undone/redone earlier command already
        // cleared).
        if (_clearedMasterState is null)
            _clearedMasterState = CaptureAndClearMasterThemes(p);
        else
            foreach (var (master, _, _) in _clearedMasterState)
            {
                master.Theme         = null;
                master.ThemePartPath = null;
            }
    }

    public void Revert(Presentation p)
    {
        if (_oldTheme is not null)
            p.Theme = _oldTheme;

        if (_clearedMasterState is not null)
        {
            foreach (var (master, oldTheme, oldPath) in _clearedMasterState)
            {
                master.Theme         = oldTheme;
                master.ThemePartPath = oldPath;
            }
        }
    }

    private static List<(SlideMaster Master, PresentationTheme? OldMasterTheme, string? OldThemePartPath)> CaptureAndClearMasterThemes(Presentation p)
    {
        var cleared = new List<(SlideMaster, PresentationTheme?, string?)>();
        foreach (var master in p.Masters)
        {
            if (master.Theme is not null || master.ThemePartPath is not null)
            {
                cleared.Add((master, master.Theme, master.ThemePartPath));
                master.Theme         = null;
                master.ThemePartPath = null;
            }
        }
        return cleared;
    }
}

// ── Slide size ────────────────────────────────────────────────────────────────

/// <summary>
/// Sets the slide dimensions (<see cref="Presentation.SlideSizeCxEmu"/> /
/// <see cref="Presentation.SlideSizeCyEmu"/>). Captures old values for undo.
/// </summary>
/// <remarks>
/// Also rescales every shape's position/size ("Ensure Fit" semantics: a single uniform factor —
/// the smaller of the new-width/old-width and new-height/old-height ratios — applied to every
/// shape's OffsetX/Y and ExtentCx/Cy from the slide origin). Without this, shrinking the slide
/// (e.g. 16:9 -&gt; 4:3) leaves shapes at their old absolute EMU coordinates, so anything beyond the
/// new, narrower canvas is cropped/off-slide in the editor, Slide Show, and PDF/print export; a
/// uniform min-ratio scale guarantees any shape that fit inside the old canvas still fits inside
/// the new one. Group children store absolute slide-space offsets that SlideCompositor's
/// <c>TransformGroupChild</c> reconciles against the group's own ChildOffset/ChildExtent (when
/// present), so descendants and those Child* fields are scaled by the same factor to keep that
/// reconciliation consistent -- see <see cref="ScaleShapeTree"/>.
/// </remarks>
public sealed class SetSlideSizeCommand : IPresentationCommand
{
    private readonly long _newCx;
    private readonly long _newCy;
    private long          _oldCx;
    private long          _oldCy;

    private List<(SlideShape Shape, long OldOffX, long OldOffY, long OldExtCx, long OldExtCy,
        long? OldChOffX, long? OldChOffY, long? OldChExtCx, long? OldChExtCy)>? _scaled;

    // Per-table snapshot of column widths / row heights, captured alongside the shape's own
    // Offset/Extent in _scaled whenever a scaled shape carries a Table (see ScaleShapeTree).
    private List<(TableShape Table, List<long> OldColumnWidthsEmu, List<long> OldRowHeightsEmu)>? _scaledTables;

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

        if (_oldCx > 0 && _oldCy > 0 && (_newCx != _oldCx || _newCy != _oldCy))
        {
            double scale = Math.Min((double)_newCx / _oldCx, (double)_newCy / _oldCy);
            if (scale > 0 && scale != 1.0)
            {
                _scaled = new List<(SlideShape, long, long, long, long, long?, long?, long?, long?)>();
                _scaledTables = new List<(TableShape, List<long>, List<long>)>();

                foreach (var slide in p.Slides)
                    foreach (var shape in slide.Shapes)
                        ScaleShapeTree(shape, scale, _scaled, _scaledTables);

                // Placeholder shapes very commonly omit their own xfrm and inherit position/size
                // from the slide layout (or, failing that, the slide master) -- see
                // PlaceholderResolver.ResolveAnchor. That inherited geometry must scale with the
                // canvas too, or every placeholder using it (title/body boxes on ordinary slides)
                // keeps its old absolute EMU coordinates and overflows the new, narrower canvas.
                foreach (var layout in p.Layouts)
                    foreach (var placeholder in layout.Placeholders)
                        ScaleShapeTree(placeholder, scale, _scaled, _scaledTables);

                foreach (var master in p.Masters)
                    foreach (var placeholder in master.Placeholders)
                        ScaleShapeTree(placeholder, scale, _scaled, _scaledTables);
            }
        }

        p.SlideSizeCxEmu = _newCx;
        p.SlideSizeCyEmu = _newCy;
    }

    public void Revert(Presentation p)
    {
        p.SlideSizeCxEmu = _oldCx;
        p.SlideSizeCyEmu = _oldCy;

        if (_scaled is not null)
        {
            foreach (var s in _scaled)
            {
                s.Shape.OffsetXEmu = s.OldOffX;
                s.Shape.OffsetYEmu = s.OldOffY;
                s.Shape.ExtentCxEmu = s.OldExtCx;
                s.Shape.ExtentCyEmu = s.OldExtCy;
                s.Shape.ChildOffsetXEmu = s.OldChOffX;
                s.Shape.ChildOffsetYEmu = s.OldChOffY;
                s.Shape.ChildExtentCxEmu = s.OldChExtCx;
                s.Shape.ChildExtentCyEmu = s.OldChExtCy;
            }
            _scaled = null;
        }

        if (_scaledTables is not null)
        {
            foreach (var t in _scaledTables)
            {
                t.Table.ColumnWidthsEmu.Clear();
                t.Table.ColumnWidthsEmu.AddRange(t.OldColumnWidthsEmu);

                for (int i = 0; i < t.Table.Rows.Count && i < t.OldRowHeightsEmu.Count; i++)
                    t.Table.Rows[i].HeightEmu = t.OldRowHeightsEmu[i];
            }
            _scaledTables = null;
        }
    }

    /// <summary>
    /// Records <paramref name="shape"/>'s pre-scale geometry into <paramref name="saved"/>, scales
    /// its own Offset/Extent (and Child* fields, when present, for a Group) by
    /// <paramref name="scale"/>, then recurses into <see cref="SlideShape.Children"/>. All fields
    /// scale by the same factor so the group-child reconciliation in
    /// <c>SlideCompositor.TransformGroupChild</c> (absolute = groupOff + (raw - chOff) *
    /// (groupExt / chExt)) stays numerically consistent -- scaling numerator and denominator of
    /// that ratio by the same factor leaves the ratio, and therefore every descendant's resolved
    /// absolute position, correctly scaled too.
    /// </summary>
    private static void ScaleShapeTree(
        SlideShape shape,
        double scale,
        List<(SlideShape Shape, long OldOffX, long OldOffY, long OldExtCx, long OldExtCy,
            long? OldChOffX, long? OldChOffY, long? OldChExtCx, long? OldChExtCy)> saved,
        List<(TableShape Table, List<long> OldColumnWidthsEmu, List<long> OldRowHeightsEmu)> savedTables)
    {
        saved.Add((shape, shape.OffsetXEmu, shape.OffsetYEmu, shape.ExtentCxEmu, shape.ExtentCyEmu,
            shape.ChildOffsetXEmu, shape.ChildOffsetYEmu, shape.ChildExtentCxEmu, shape.ChildExtentCyEmu));

        shape.OffsetXEmu  = (long)Math.Round(shape.OffsetXEmu  * scale);
        shape.OffsetYEmu  = (long)Math.Round(shape.OffsetYEmu  * scale);
        shape.ExtentCxEmu = (long)Math.Round(shape.ExtentCxEmu * scale);
        shape.ExtentCyEmu = (long)Math.Round(shape.ExtentCyEmu * scale);

        if (shape.ChildOffsetXEmu is { } chOffX) shape.ChildOffsetXEmu = (long)Math.Round(chOffX * scale);
        if (shape.ChildOffsetYEmu is { } chOffY) shape.ChildOffsetYEmu = (long)Math.Round(chOffY * scale);
        if (shape.ChildExtentCxEmu is { } chExtCx) shape.ChildExtentCxEmu = (long)Math.Round(chExtCx * scale);
        if (shape.ChildExtentCyEmu is { } chExtCy) shape.ChildExtentCyEmu = (long)Math.Round(chExtCy * scale);

        // SlideCompositor.ComposeTable ignores the shape's own ExtentCx/CyEmu for layout purposes
        // and instead derives the table's actual drawn width/height purely from
        // TableShape.ColumnWidthsEmu and TableRow.HeightEmu -- so a table's outer frame scaling
        // above (which only moves/resizes ops.DrawOp.Shape's bounding box) has no effect on how
        // the table itself renders/exports unless these are scaled too.
        if (shape.Table is { } table)
        {
            savedTables.Add((table, new List<long>(table.ColumnWidthsEmu),
                table.Rows.Select(r => r.HeightEmu).ToList()));

            for (int i = 0; i < table.ColumnWidthsEmu.Count; i++)
                table.ColumnWidthsEmu[i] = (long)Math.Round(table.ColumnWidthsEmu[i] * scale);

            foreach (var row in table.Rows)
                row.HeightEmu = (long)Math.Round(row.HeightEmu * scale);
        }

        foreach (var child in shape.Children)
            ScaleShapeTree(child, scale, saved, savedTables);
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

    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(new[]
    {
        PresentationCommandSizeEstimator.EstimateBytes(_newFill),
        PresentationCommandSizeEstimator.EstimateBytes(_oldFill),
    });

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

    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(
        new[] { PresentationCommandSizeEstimator.EstimateBytes(_fill) }
            .Concat(_undo.Select(snap =>
                PresentationCommandSizeEstimator.Combine(new[]
                {
                    PresentationCommandSizeEstimator.EstimateBytes(snap.Fill),
                    PresentationCommandSizeEstimator.EstimateBytes(snap.OldBody),
                }))));

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
