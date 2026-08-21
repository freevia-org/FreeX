using Free.Shared.Drawing;

namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// WAVE 12A: Group / Ungroup / Align / Distribute / BringToFront / SendToBack
// ════════════════════════════════════════════════════════════════════════════════

// ── Internal helpers (file-scoped) ────────────────────────────────────────────

file static class ShapeHelper12A
{
    internal static SlideShape? Find(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return Find(p.Slides[slideIndex].Shapes, shapeId);
    }

    private static SlideShape? Find(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId) return shape;
            if (shape.Children.Count > 0 && Find(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    internal static List<SlideShape>? ContainingShapes(Presentation p, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return FindContainingList(p.Slides[slideIndex].Shapes, shapeId);
    }

    internal static List<SlideShape>? CommonContainingShapes(
        Presentation p,
        int slideIndex,
        IReadOnlyList<uint> shapeIds)
    {
        if (shapeIds.Count == 0) return null;
        var shapes = ContainingShapes(p, slideIndex, shapeIds[0]);
        return shapes is not null && shapeIds.All(id => shapes.Any(shape => shape.Id == id))
            ? shapes
            : null;
    }

    internal static uint NextShapeId(Presentation p)
    {
        uint max = 0;
        foreach (var shape in EnumerateShapes(p.Slides.SelectMany(slide => slide.Shapes)))
            max = Math.Max(max, shape.Id);
        return max == uint.MaxValue ? 1u : max + 1u;
    }

    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            if (shape.Children.Count > 0)
            {
                foreach (var child in EnumerateShapes(shape.Children))
                    yield return child;
            }
        }
    }

    private static List<SlideShape>? FindContainingList(List<SlideShape> shapes, uint shapeId)
    {
        if (shapes.Any(shape => shape.Id == shapeId)) return shapes;
        foreach (var shape in shapes)
        {
            if (shape.Children.Count > 0 && FindContainingList(shape.Children, shapeId) is { } childList)
                return childList;
        }

        return null;
    }

    internal static List<SlideShape>? Shapes(Presentation p, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count) return null;
        return p.Slides[slideIndex].Shapes;
    }
}

// ── Composite command ─────────────────────────────────────────────────────────

/// <summary>
/// Wraps multiple <see cref="IPresentationCommand"/>s as a single undoable step.
/// Used for align/distribute so the whole operation reverts in one undo.
/// </summary>
public sealed class BatchCommand : IPresentationCommand
{
    private readonly List<IPresentationCommand> _commands;
    public string Label { get; }

    public BatchCommand(string label, IEnumerable<IPresentationCommand> commands)
    {
        Label     = label;
        _commands = commands.ToList();
    }

    /// <summary>
    /// The sum of every child command's own <see cref="IPresentationCommand.EstimatedBytes"/>,
    /// read after <see cref="Apply"/> has run each child (so children like DeleteShapeCommand
    /// have already captured what they retain). Without this, the bus only ever saw this batch's
    /// own flat 256-byte default no matter how many image-heavy slides or shapes it deleted or
    /// duplicated in one multi-select gesture.
    /// </summary>
    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(
        _commands.Select(c => c.EstimatedBytes));

    public void Apply(Presentation p)
    {
        foreach (var cmd in _commands)
            cmd.Apply(p);
    }

    public void Revert(Presentation p)
    {
        // Revert in reverse order.
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Revert(p);
    }

    public bool HasEffect(Presentation p) => _commands.Any(c => c.HasEffect(p));
}

// ── Group ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Wraps <paramref name="selectedIds"/> into a new Group shape whose anchor is the
/// union bounding box of the selected shapes.  Children keep their absolute offsets
/// (the compositor and writer both work in absolute coords for grouped children via
/// a chOff=0/chExt=identity mapping).
/// Revert removes the group and restores the original shapes at their original z-indices.
/// </summary>
public sealed class GroupShapesCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly List<uint> _selectedIds;

    // Captured on Apply for Revert.
    private SlideShape?          _group;
    private List<(int zIdx, SlideShape shape)>? _removed;
    private List<SlideShape>? _parentShapes;

    public uint? GroupId => _group?.Id;

    public GroupShapesCommand(int slideIndex, IEnumerable<uint> selectedIds)
    {
        _slideIndex  = slideIndex;
        _selectedIds = selectedIds.ToList();
    }

    public string Label => "Group";

    public int EstimatedBytes => PresentationCommandSizeEstimator.EstimateBytes(_group);

    public bool HasEffect(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _selectedIds);
        return shapes is not null && _selectedIds.Count >= 2
            && _selectedIds.All(id => shapes.Any(s => s.Id == id));
    }

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _selectedIds);
        if (shapes is null || _selectedIds.Count < 2) return;
        _parentShapes = shapes;

        // Collect selected shapes and their z-indices (in z-order).
        var selected = new List<(int zIdx, SlideShape shape)>();
        for (int i = 0; i < shapes.Count; i++)
        {
            if (_selectedIds.Contains(shapes[i].Id))
                selected.Add((i, shapes[i]));
        }
        if (selected.Count < 2) return;

        _removed = selected;

        // Compute union bounding box.
        long minX = selected.Min(t => t.shape.OffsetXEmu);
        long minY = selected.Min(t => t.shape.OffsetYEmu);
        long maxX = selected.Max(t => t.shape.OffsetXEmu + t.shape.ExtentCxEmu);
        long maxY = selected.Max(t => t.shape.OffsetYEmu + t.shape.ExtentCyEmu);

        // Build the group shape.
        uint newId = ShapeHelper12A.NextShapeId(p);
        _group = new SlideShape
        {
            Id          = newId,
            Name        = "Group",
            Kind        = SlideShapeKind.Group,
            OffsetXEmu  = minX,
            OffsetYEmu  = minY,
            ExtentCxEmu = maxX - minX,
            ExtentCyEmu = maxY - minY,
        };

        // Children keep absolute offsets (compositor + writer treat chOff=0).
        foreach (var (_, shape) in selected)
            _group.Children.Add(shape);

        // Remove originals (highest z-index first to keep indices valid).
        foreach (var (zIdx, _) in selected.OrderByDescending(t => t.zIdx))
            shapes.RemoveAt(zIdx);

        // Insert the group at the position of the lowest-z-index original.
        int insertAt = Math.Clamp(selected.Min(t => t.zIdx), 0, shapes.Count);
        shapes.Insert(insertAt, _group);
    }

    public void Revert(Presentation p)
    {
        var shapes = _parentShapes;
        if (shapes is null || _group is null || _removed is null) return;

        // Remove the group.
        shapes.Remove(_group);

        // Re-insert the originals at their original z-indices (lowest first).
        foreach (var (zIdx, shape) in _removed.OrderBy(t => t.zIdx))
        {
            int idx = Math.Clamp(zIdx, 0, shapes.Count);
            shapes.Insert(idx, shape);
        }

        _group   = null;
        _removed = null;
        _parentShapes = null;
    }
}

// ── Ungroup ───────────────────────────────────────────────────────────────────

/// <summary>
/// Replaces a Group shape with its children.  Children already carry absolute offsets
/// (same coordinate space as the slide) so no adjustment is needed.
/// Revert removes the freed children and re-inserts the original group.
/// </summary>
public sealed class UngroupShapeCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _groupId;

    private SlideShape?        _group;
    private int                _groupZIdx;
    private List<SlideShape>?  _children;
    private List<SlideShape>?  _parentShapes;
    private List<(SlideShape Connector, ConnectorAttachment? Start, ConnectorAttachment? End)>?
        _capturedConnectorAttachments;
    private List<ShapeAnimation>? _capturedAnimations;
    private string?            _capturedBuildListXml;
    private List<DeleteShapeCommand.CommentAnchorSnapshot>? _capturedOrphanedCommentAnchors;

    public UngroupShapeCommand(int slideIndex, uint groupId)
    {
        _slideIndex = slideIndex;
        _groupId    = groupId;
    }

    public string Label => "Ungroup";

    public int EstimatedBytes => PresentationCommandSizeEstimator.EstimateBytes(_group);

    public bool HasEffect(Presentation p)
    {
        var shape = ShapeHelper12A.Find(p, _slideIndex, _groupId);
        return shape?.Kind == SlideShapeKind.Group && shape.Children.Count > 0;
    }

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _groupId);
        if (shapes is null) return;

        _groupZIdx = shapes.FindIndex(s => s.Id == _groupId);
        if (_groupZIdx < 0) return;

        _group    = shapes[_groupZIdx];
        if (_group.Kind != SlideShapeKind.Group) return;

        _children = _group.Children.ToList();
        _parentShapes = shapes;
        _capturedConnectorAttachments = ShapeHelper.All(p, _slideIndex)
            .Where(shape => shape.Kind == SlideShapeKind.Connector &&
                (shape.ConnectionStart?.ShapeId == _groupId ||
                 shape.ConnectionEnd?.ShapeId == _groupId))
            .Select(connector =>
                (connector, connector.ConnectionStart, connector.ConnectionEnd))
            .ToList();

        var slide = p.Slides[_slideIndex];
        _capturedAnimations = slide.Animations.ToList();
        _capturedBuildListXml = slide.AnimationBuildListXml;

        // A modern (p188) comment thread can be anchored directly to the group shape (deMkLst/
        // txMkLst carrying the group's id). Ungrouping destroys that id the same way deleting the
        // shape would (see the comment below), so give it the identical treatment DeleteShapeCommand
        // already gives a deleted shape's comment anchors: clear the anchor rather than silently
        // leaving it pointing at an id nothing in the slide's tree carries anymore. Reusing
        // ModernAnchorKind/ModernAnchorXml = "" makes the writer fall back to <p188:unknownAnchor/>
        // (BuildModernAnchorElement) instead of inventing a second "orphaned" representation, and
        // undo restores the original anchor exactly like DeleteShapeCommand's Revert does.
        var groupIdSet = new HashSet<uint> { _groupId };
        _capturedOrphanedCommentAnchors = new List<DeleteShapeCommand.CommentAnchorSnapshot>();
        for (var commentIndex = 0; commentIndex < slide.Comments.Count; commentIndex++)
        {
            var comment = slide.Comments[commentIndex];
            if (!DeleteShapeCommand.CommentAnchorReferencesShape(comment, groupIdSet))
                continue;

            _capturedOrphanedCommentAnchors.Add(new DeleteShapeCommand.CommentAnchorSnapshot(
                DeleteShapeCommand.NormalizeModernCommentId(comment.ModernCommentId),
                commentIndex,
                comment.ModernAnchorKind,
                comment.ModernAnchorXml));
            comment.ModernAnchorKind = string.Empty;
            comment.ModernAnchorXml = string.Empty;
        }

        shapes.RemoveAt(_groupZIdx);

        // Insert children at the group's former z-position (in order).
        for (int i = 0; i < _children.Count; i++)
        {
            int idx = Math.Clamp(_groupZIdx + i, 0, shapes.Count);
            shapes.Insert(idx, _children[i]);
        }

        // The group's own shape id is destroyed by ungrouping (its children keep their own,
        // already-independent ids - they never carried the group's id). PowerPoint does not
        // hand a group's animation down to its former children when you ungroup; it simply
        // drops it. Leaving the stale entry instead would point a build-list/timing node at a
        // shape id absent from the slide's tree, and (like DeleteShapeCommand's identical
        // handling below) a dangling TriggerShapeId could later rebind to whatever unrelated
        // shape NextShapeId hands out that freed id to next.
        slide.Animations.RemoveAll(animation =>
            animation.ShapeId == _groupId ||
            (animation.TriggerShapeId is { } triggerShapeId && triggerShapeId == _groupId));
        // If the group's own animation was the main-sequence head, whatever animation is now
        // first needs its stored trigger corrected to On Click (see ShapeAnimationAnchorFix) --
        // otherwise the Animation Pane keeps showing a stale With/After Previous label. The group
        // could independently have headed some unrelated trigger group too (a second animation on
        // the group shape, keyed by a different TriggerShapeId), so that group's head needs the
        // identical check. Revert below restores the whole captured list wholesale, so no undo
        // bookkeeping is needed for either correction here.
        ShapeAnimationAnchorFix.NormalizeMainSequenceHead(slide.Animations);
        ShapeAnimationAnchorFix.NormalizeTriggerGroupHeads(slide.Animations);
        slide.AnimationBuildListXml = DeleteShapeCommand.RemoveBuildListEntriesForShapes(
            slide.AnimationBuildListXml,
            new HashSet<uint> { _groupId });

        if (_capturedConnectorAttachments is not null)
        {
            foreach (var (connector, _, _) in _capturedConnectorAttachments)
            {
                if (connector.ConnectionStart?.ShapeId == _groupId)
                    connector.ConnectionStart = null;
                if (connector.ConnectionEnd?.ShapeId == _groupId)
                    connector.ConnectionEnd = null;
            }
        }
    }

    public void Revert(Presentation p)
    {
        var shapes = _parentShapes;
        if (shapes is null || _group is null || _children is null) return;

        // Remove freed children.
        foreach (var child in _children)
            shapes.Remove(child);

        // Re-insert the group.
        int idx = Math.Clamp(_groupZIdx, 0, shapes.Count);
        shapes.Insert(idx, _group);

        if (_slideIndex >= 0 && _slideIndex < p.Slides.Count && _capturedAnimations is not null)
        {
            var slide = p.Slides[_slideIndex];
            slide.Animations.Clear();
            slide.Animations.AddRange(_capturedAnimations);
            slide.AnimationBuildListXml = _capturedBuildListXml;
        }

        if (_slideIndex >= 0 && _slideIndex < p.Slides.Count && _capturedOrphanedCommentAnchors is not null)
        {
            var slide = p.Slides[_slideIndex];
            foreach (var snapshot in _capturedOrphanedCommentAnchors)
            {
                var comment = DeleteShapeCommand.ResolveCommentAnchorTarget(slide.Comments, snapshot);
                if (comment is null)
                    continue;

                comment.ModernAnchorKind = snapshot.AnchorKind;
                comment.ModernAnchorXml = snapshot.AnchorXml;
            }
        }

        if (_capturedConnectorAttachments is not null)
        {
            foreach (var (connector, start, end) in _capturedConnectorAttachments)
            {
                connector.ConnectionStart = start;
                connector.ConnectionEnd = end;
            }
        }

        _group    = null;
        _children = null;
        _parentShapes = null;
        _capturedAnimations = null;
        _capturedBuildListXml = null;
        _capturedOrphanedCommentAnchors = null;
    }
}

// ── Align ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Alignment kinds supported by <see cref="AlignShapesCommand"/>.
/// </summary>
public enum AlignKind
{
    Left, CenterH, Right, Top, Middle, Bottom
}

/// <summary>
/// Aligns the selected shapes by moving each shape's edge/center to the bounding-box
/// edge/center of the selection.  Each shape gets an individual absolute-position set;
/// the batch is wrapped in a <see cref="BatchCommand"/> by the caller so undo is one step.
/// </summary>
public sealed class AlignShapesCommand : IPresentationCommand
{
    private readonly int            _slideIndex;
    private readonly List<uint>     _shapeIds;
    private readonly AlignKind      _kind;

    // Per-shape (shapeId → old position) captured on Apply.
    private List<(uint id, long oldX, long oldY)>? _saved;

    public AlignShapesCommand(int slideIndex, IEnumerable<uint> shapeIds, AlignKind kind)
    {
        _slideIndex = slideIndex;
        _shapeIds   = shapeIds.ToList();
        _kind       = kind;
    }

    public string Label => $"Align {_kind}";

    public bool HasEffect(Presentation p) => _shapeIds.Count >= 1;

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _shapeIds);
        if (shapes is null || _shapeIds.Count == 0) return;

        var targets = _shapeIds
            .Select(id => shapes.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .ToList();

        if (targets.Count == 0) return;

        // Compute bounding box of selection.
        long bboxMinX = targets.Min(s => s!.OffsetXEmu);
        long bboxMinY = targets.Min(s => s!.OffsetYEmu);
        long bboxMaxX = targets.Max(s => s!.OffsetXEmu + s!.ExtentCxEmu);
        long bboxMaxY = targets.Max(s => s!.OffsetYEmu + s!.ExtentCyEmu);
        long bboxCx   = bboxMaxX - bboxMinX;
        long bboxCy   = bboxMaxY - bboxMinY;

        _saved = new List<(uint, long, long)>();

        foreach (var s in targets)
        {
            long oldX = s!.OffsetXEmu, oldY = s.OffsetYEmu;
            _saved.Add((s.Id, oldX, oldY));

            long newX = oldX, newY = oldY;
            switch (_kind)
            {
                case AlignKind.Left:
                    newX = bboxMinX;
                    break;
                case AlignKind.CenterH:
                    newX = bboxMinX + (bboxCx - s.ExtentCxEmu) / 2;
                    break;
                case AlignKind.Right:
                    newX = bboxMaxX - s.ExtentCxEmu;
                    break;
                case AlignKind.Top:
                    newY = bboxMinY;
                    break;
                case AlignKind.Middle:
                    newY = bboxMinY + (bboxCy - s.ExtentCyEmu) / 2;
                    break;
                case AlignKind.Bottom:
                    newY = bboxMaxY - s.ExtentCyEmu;
                    break;
            }

            // s may be a Group: its children store absolute (slide-space) coordinates, not
            // coordinates relative to the group (see GroupShapesCommand.Apply), so translate
            // every descendant by the same delta -- the same helper MoveShapeCommand uses --
            // or the group's own position moves while its members stay behind.
            SlideShapeTraversal.TranslateWithDescendants(s, newX - oldX, newY - oldY);
        }
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _shapeIds);
        if (shapes is null || _saved is null) return;

        foreach (var (id, oldX, oldY) in _saved)
        {
            var s = shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            SlideShapeTraversal.TranslateWithDescendants(s, oldX - s.OffsetXEmu, oldY - s.OffsetYEmu);
        }
        _saved = null;
    }
}

// ── Distribute ────────────────────────────────────────────────────────────────

/// <summary>Distribute direction.</summary>
public enum DistributeKind { Horizontal, Vertical }

/// <summary>
/// Evenly spaces ≥3 selected shapes along the given axis within the selection's bounding box.
/// For &lt;3 shapes this is a no-op. Undo restores original positions.
/// </summary>
/// <summary>
/// Aligns each selected shape against the slide canvas rather than the selection bounds.
/// This is the PowerPoint "Align to Slide" mode and is undoable as one command.
/// </summary>
public sealed class AlignShapesToSlideCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly List<uint> _shapeIds;
    private readonly AlignKind _kind;
    private List<(uint id, long oldX, long oldY)>? _saved;

    public AlignShapesToSlideCommand(int slideIndex, IEnumerable<uint> shapeIds, AlignKind kind)
    {
        _slideIndex = slideIndex;
        _shapeIds = shapeIds.ToList();
        _kind = kind;
    }

    public string Label => $"Align {_kind} to Slide";

    public bool HasEffect(Presentation p) => _shapeIds.Count > 0 &&
        p.SlideSizeCxEmu > 0 && p.SlideSizeCyEmu > 0;

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.Shapes(p, _slideIndex);
        if (shapes is null || _shapeIds.Count == 0) return;

        var targets = _shapeIds
            .Select(id => shapes.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .ToList();
        if (targets.Count == 0) return;

        _saved = new List<(uint, long, long)>();
        foreach (var s in targets)
        {
            long oldX = s!.OffsetXEmu, oldY = s.OffsetYEmu;
            _saved.Add((s.Id, oldX, oldY));
            long newX = oldX, newY = oldY;
            switch (_kind)
            {
                case AlignKind.Left: newX = 0; break;
                case AlignKind.CenterH: newX = (p.SlideSizeCxEmu - s.ExtentCxEmu) / 2; break;
                case AlignKind.Right: newX = p.SlideSizeCxEmu - s.ExtentCxEmu; break;
                case AlignKind.Top: newY = 0; break;
                case AlignKind.Middle: newY = (p.SlideSizeCyEmu - s.ExtentCyEmu) / 2; break;
                case AlignKind.Bottom: newY = p.SlideSizeCyEmu - s.ExtentCyEmu; break;
            }

            // s may be a Group: translate every descendant by the same delta (see
            // SlideShapeTraversal.TranslateWithDescendants) so group members move with it.
            SlideShapeTraversal.TranslateWithDescendants(s, newX - oldX, newY - oldY);
        }
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper12A.Shapes(p, _slideIndex);
        if (shapes is null || _saved is null) return;
        foreach (var (id, oldX, oldY) in _saved)
        {
            var s = shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            SlideShapeTraversal.TranslateWithDescendants(s, oldX - s.OffsetXEmu, oldY - s.OffsetYEmu);
        }
        _saved = null;
    }
}

public sealed class DistributeShapesCommand : IPresentationCommand
{
    private readonly int              _slideIndex;
    private readonly List<uint>       _shapeIds;
    private readonly DistributeKind   _kind;

    private List<(uint id, long oldX, long oldY)>? _saved;

    public DistributeShapesCommand(int slideIndex, IEnumerable<uint> shapeIds, DistributeKind kind)
    {
        _slideIndex = slideIndex;
        _shapeIds   = shapeIds.ToList();
        _kind       = kind;
    }

    public string Label => $"Distribute {_kind}";

    public bool HasEffect(Presentation p) => _shapeIds.Count >= 3;

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _shapeIds);
        if (shapes is null || _shapeIds.Count < 3) return;

        var targets = _shapeIds
            .Select(id => shapes.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .ToList();

        if (targets.Count < 3) return;

        _saved = targets.Select(s => (s!.Id, s.OffsetXEmu, s.OffsetYEmu)).ToList();

        if (_kind == DistributeKind.Horizontal)
        {
            // Sort by left edge.
            var sorted = targets.OrderBy(s => s!.OffsetXEmu).ToList();
            long totalWidth  = sorted.Sum(s => s!.ExtentCxEmu);
            long spanLeft    = sorted.First()!.OffsetXEmu;
            long spanRight   = sorted.Last()!.OffsetXEmu + sorted.Last()!.ExtentCxEmu;
            long spanWidth   = spanRight - spanLeft;
            long gapTotal    = spanWidth - totalWidth;
            long gaps        = sorted.Count - 1;
            if (gaps <= 0) return;
            long gapPerSlot  = gapTotal / gaps;
            long x = spanLeft;
            foreach (var s in sorted)
            {
                // s may be a Group: translate every descendant by the same delta (see
                // SlideShapeTraversal.TranslateWithDescendants) so group members move with it.
                SlideShapeTraversal.TranslateWithDescendants(s!, x - s!.OffsetXEmu, 0);
                x += s.ExtentCxEmu + gapPerSlot;
            }
        }
        else
        {
            // Sort by top edge.
            var sorted = targets.OrderBy(s => s!.OffsetYEmu).ToList();
            long totalHeight = sorted.Sum(s => s!.ExtentCyEmu);
            long spanTop     = sorted.First()!.OffsetYEmu;
            long spanBottom  = sorted.Last()!.OffsetYEmu + sorted.Last()!.ExtentCyEmu;
            long spanHeight  = spanBottom - spanTop;
            long gapTotal    = spanHeight - totalHeight;
            long gaps        = sorted.Count - 1;
            if (gaps <= 0) return;
            long gapPerSlot  = gapTotal / gaps;
            long y = spanTop;
            foreach (var s in sorted)
            {
                // s may be a Group: translate every descendant by the same delta (see
                // SlideShapeTraversal.TranslateWithDescendants) so group members move with it.
                SlideShapeTraversal.TranslateWithDescendants(s!, 0, y - s!.OffsetYEmu);
                y += s.ExtentCyEmu + gapPerSlot;
            }
        }
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper12A.CommonContainingShapes(p, _slideIndex, _shapeIds);
        if (shapes is null || _saved is null) return;

        foreach (var (id, oldX, oldY) in _saved)
        {
            var s = shapes.FirstOrDefault(sh => sh.Id == id);
            if (s is null) continue;
            SlideShapeTraversal.TranslateWithDescendants(s, oldX - s.OffsetXEmu, oldY - s.OffsetYEmu);
        }
        _saved = null;
    }
}

// ── BringToFront / SendToBack ─────────────────────────────────────────────────

/// <summary>
/// Moves a shape to the front of z-order (last in the Shapes list = topmost).
/// Revert restores the original z-index.
/// </summary>
public sealed class BringToFrontCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private int           _oldZIndex;

    public BringToFrontCommand(int slideIndex, uint shapeId)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
    }

    public string Label => "Bring to Front";

    public bool HasEffect(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null) return false;
        var idx = shapes.FindIndex(s => s.Id == _shapeId);
        return idx >= 0 && idx < shapes.Count - 1;
    }

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null) return;
        _oldZIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_oldZIndex < 0) return;
        var shape = shapes[_oldZIndex];
        shapes.RemoveAt(_oldZIndex);
        shapes.Add(shape);
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null || _oldZIndex < 0) return;
        // Currently at the end — move back to _oldZIndex.
        var shape = shapes[shapes.Count - 1];
        shapes.RemoveAt(shapes.Count - 1);
        int idx = Math.Clamp(_oldZIndex, 0, shapes.Count);
        shapes.Insert(idx, shape);
    }
}

/// <summary>
/// Moves a shape to the back of z-order (index 0 = bottommost).
/// Revert restores the original z-index.
/// </summary>
public sealed class SendToBackCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private int           _oldZIndex;

    public SendToBackCommand(int slideIndex, uint shapeId)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
    }

    public string Label => "Send to Back";

    public bool HasEffect(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null) return false;
        var idx = shapes.FindIndex(s => s.Id == _shapeId);
        return idx > 0;
    }

    public void Apply(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null) return;
        _oldZIndex = shapes.FindIndex(s => s.Id == _shapeId);
        if (_oldZIndex <= 0) return;
        var shape = shapes[_oldZIndex];
        shapes.RemoveAt(_oldZIndex);
        shapes.Insert(0, shape);
    }

    public void Revert(Presentation p)
    {
        var shapes = ShapeHelper12A.ContainingShapes(p, _slideIndex, _shapeId);
        if (shapes is null || _oldZIndex <= 0) return;
        // Currently at index 0 — move back to _oldZIndex.
        var shape = shapes[0];
        shapes.RemoveAt(0);
        int idx = Math.Clamp(_oldZIndex, 0, shapes.Count);
        shapes.Insert(idx, shape);
    }
}
