namespace FreeW.Core.Model;

public enum FloatingObjectArrangeKind
{
    AlignToPage,
    AlignToMargin,
    AlignLeft,
    AlignCenter,
    AlignRight,
    DistributeHorizontal,
    DistributeVertical
}

/// <summary>
/// Aligns or distributes floating drawing objects identified by model run coordinates.
/// The command writes image placement fields and non-image <see cref="FloatingPlacement"/> values
/// through the same undoable path.
/// </summary>
public sealed class ArrangeFloatingObjectsCommand : IDocumentCommand
{
    private readonly FloatingObjectArrangeKind _kind;
    private readonly (int BlockIndex, int RunIndex)[] _members;
    private List<PlacementSnapshot>? _snapshots;

    public ArrangeFloatingObjectsCommand(
        FloatingObjectArrangeKind kind,
        IReadOnlyList<(int BlockIndex, int RunIndex)> members)
    {
        _kind = kind;
        _members = members
            .Distinct()
            .ToArray();
    }

    public string Label => _kind switch
    {
        FloatingObjectArrangeKind.AlignToPage => "Align to Page",
        FloatingObjectArrangeKind.AlignToMargin => "Align to Margin",
        FloatingObjectArrangeKind.AlignLeft => "Align Left",
        FloatingObjectArrangeKind.AlignCenter => "Align Center",
        FloatingObjectArrangeKind.AlignRight => "Align Right",
        FloatingObjectArrangeKind.DistributeHorizontal => "Distribute Horizontally",
        FloatingObjectArrangeKind.DistributeVertical => "Distribute Vertically",
        _ => "Arrange Floating Objects"
    };

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.BodyFormatting;

    public int EstimatedBytes => Math.Max(256, _members.Length * 64);

    public void Apply(IDocumentCommandContext context)
    {
        var targets = ResolveTargets(context.Document, _members);
        if (RequiresTwoObjects(_kind) && targets.Count < 2)
            return;

        if (targets.Count == 0)
            return;

        _snapshots = targets.Select(target => target.Capture()).ToList();

        switch (_kind)
        {
            case FloatingObjectArrangeKind.AlignToPage:
                Align(targets, 0, HorizontalAnchor.Page);
                break;
            case FloatingObjectArrangeKind.AlignToMargin:
                Align(targets, context.Document.Page.MarginLeftPt, HorizontalAnchor.Margin);
                break;
            case FloatingObjectArrangeKind.AlignLeft:
                AlignRelativeToMargins(targets, context.Document.Page, Alignment.Left);
                break;
            case FloatingObjectArrangeKind.AlignCenter:
                AlignRelativeToMargins(targets, context.Document.Page, Alignment.Center);
                break;
            case FloatingObjectArrangeKind.AlignRight:
                AlignRelativeToMargins(targets, context.Document.Page, Alignment.Right);
                break;
            case FloatingObjectArrangeKind.DistributeHorizontal:
                Distribute(targets, vertical: false);
                break;
            case FloatingObjectArrangeKind.DistributeVertical:
                Distribute(targets, vertical: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_kind), _kind, null);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_snapshots is null)
            return;

        foreach (var snapshot in _snapshots)
            snapshot.Restore(context.Document);
        _snapshots = null;
    }

    public static IReadOnlyList<(int BlockIndex, int RunIndex)> CollectFloatingObjectLocations(
        TextDocument document)
    {
        var result = new List<(int BlockIndex, int RunIndex)>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
            {
                if (TryGetTarget(paragraph.Runs[runIndex], out _))
                    result.Add((blockIndex, runIndex));
            }
        }

        return result;
    }

    public static int CountApplicableObjects(
        TextDocument document,
        IReadOnlyList<(int BlockIndex, int RunIndex)> members)
    {
        var count = 0;
        foreach (var (blockIndex, runIndex) in members.Distinct())
        {
            if (TryGetRun(document, blockIndex, runIndex, out var run)
                && TryGetTarget(run, out _))
            {
                count++;
            }
        }

        return count;
    }

    private static bool RequiresTwoObjects(FloatingObjectArrangeKind kind) =>
        kind is FloatingObjectArrangeKind.DistributeHorizontal
            or FloatingObjectArrangeKind.DistributeVertical;

    private static void Align(IReadOnlyList<FloatingTarget> targets, double offsetPt, HorizontalAnchor anchor)
    {
        foreach (var target in targets)
        {
            target.HorizontalOffsetPt = offsetPt;
            target.HorizontalAnchor = anchor;
        }
    }

    private static void AlignRelativeToMargins(
        IReadOnlyList<FloatingTarget> targets,
        PageSettings page,
        Alignment alignment)
    {
        var contentLeft = page.MarginLeftPt;
        var contentRight = Math.Max(contentLeft, page.WidthPt - page.MarginRightPt);
        var contentWidth = Math.Max(0, contentRight - contentLeft);

        foreach (var target in targets)
        {
            var offsetPt = alignment switch
            {
                Alignment.Left => contentLeft,
                Alignment.Center => contentLeft + Math.Max(0, contentWidth - target.WidthPt) / 2,
                Alignment.Right => contentRight - target.WidthPt,
                _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, null)
            };

            target.HorizontalOffsetPt = Math.Max(contentLeft, offsetPt);
            target.HorizontalAnchor = HorizontalAnchor.Margin;
        }
    }

    private static void Distribute(IReadOnlyList<FloatingTarget> targets, bool vertical)
    {
        var sorted = vertical
            ? targets.OrderBy(target => target.VerticalOffsetPt).ToArray()
            : targets.OrderBy(target => target.HorizontalOffsetPt).ToArray();

        var first = vertical ? sorted[0].VerticalOffsetPt : sorted[0].HorizontalOffsetPt;
        var last = vertical ? sorted[^1].VerticalOffsetPt : sorted[^1].HorizontalOffsetPt;
        var step = (last - first) / (sorted.Length - 1);

        for (var i = 0; i < sorted.Length; i++)
        {
            var offset = first + i * step;
            if (vertical)
                sorted[i].VerticalOffsetPt = offset;
            else
                sorted[i].HorizontalOffsetPt = offset;
        }
    }

    private static List<FloatingTarget> ResolveTargets(
        TextDocument document,
        IReadOnlyList<(int BlockIndex, int RunIndex)> members)
    {
        var result = new List<FloatingTarget>();
        foreach (var (blockIndex, runIndex) in members.Distinct())
        {
            if (!TryGetRun(document, blockIndex, runIndex, out var run)
                || !TryGetTarget(run, out var target))
            {
                continue;
            }

            result.Add(target with { BlockIndex = blockIndex, RunIndex = runIndex });
        }

        return result;
    }

    private static bool TryGetRun(TextDocument document, int blockIndex, int runIndex, out Run run)
    {
        run = null!;
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
            return false;
        if (document.Blocks[blockIndex] is not Paragraph paragraph)
            return false;
        if (runIndex < 0 || runIndex >= paragraph.Runs.Count)
            return false;

        run = paragraph.Runs[runIndex];
        return true;
    }

    private static bool TryGetTarget(Run run, out FloatingTarget target)
    {
        if (run.Image is { IsFloating: true } image)
        {
            target = new FloatingTarget(image, null, image.WidthPt);
            return true;
        }

        if (run.Shape is { IsFloating: true, Placement: { } shapePlacement })
        {
            target = new FloatingTarget(null, shapePlacement, run.Shape.WidthPt);
            return true;
        }

        if (run.Chart is { IsFloating: true, Placement: { } chartPlacement })
        {
            target = new FloatingTarget(null, chartPlacement, run.Chart.WidthPt);
            return true;
        }

        if (run.SmartArt is { IsFloating: true, Placement: { } smartArtPlacement })
        {
            target = new FloatingTarget(null, smartArtPlacement, run.SmartArt.WidthPt);
            return true;
        }

        if (run.WordArt is { IsFloating: true, Placement: { } wordArtPlacement })
        {
            var widthPt = run.WordArt.FontSizePt * Math.Max(1, run.WordArt.Text.Length) * 0.62;
            target = new FloatingTarget(null, wordArtPlacement, widthPt);
            return true;
        }

        if (run.DrawingGroup is { IsFloating: true } group)
        {
            target = new FloatingTarget(null, group.Placement, group.WidthPt);
            return true;
        }

        target = null!;
        return false;
    }

    private sealed record FloatingTarget(InlineImage? Image, FloatingPlacement? Placement, double WidthPt)
    {
        public int BlockIndex { get; init; }
        public int RunIndex { get; init; }

        public double HorizontalOffsetPt
        {
            get => Image?.HorizontalOffsetPt ?? Placement!.HorizontalOffsetPt;
            set
            {
                if (Image is not null)
                    Image.HorizontalOffsetPt = value;
                else
                    Placement!.HorizontalOffsetPt = value;
            }
        }

        public double VerticalOffsetPt
        {
            get => Image?.VerticalOffsetPt ?? Placement!.VerticalOffsetPt;
            set
            {
                if (Image is not null)
                    Image.VerticalOffsetPt = value;
                else
                    Placement!.VerticalOffsetPt = value;
            }
        }

        public HorizontalAnchor HorizontalAnchor
        {
            get => Image?.HorizontalAnchor ?? Placement!.HorizontalAnchor;
            set
            {
                if (Image is not null)
                    Image.HorizontalAnchor = value;
                else
                    Placement!.HorizontalAnchor = value;
            }
        }

        public VerticalAnchor VerticalAnchor
        {
            get => Image?.VerticalAnchor ?? Placement!.VerticalAnchor;
            set
            {
                if (Image is not null)
                    Image.VerticalAnchor = value;
                else
                    Placement!.VerticalAnchor = value;
            }
        }

        public PlacementSnapshot Capture() =>
            new(
                BlockIndex,
                RunIndex,
                HorizontalOffsetPt,
                VerticalOffsetPt,
                HorizontalAnchor,
                VerticalAnchor);

    }

    private enum Alignment
    {
        Left,
        Center,
        Right
    }

    private readonly record struct PlacementSnapshot(
        int BlockIndex,
        int RunIndex,
        double HorizontalOffsetPt,
        double VerticalOffsetPt,
        HorizontalAnchor HorizontalAnchor,
        VerticalAnchor VerticalAnchor)
    {
        public void Restore(TextDocument document)
        {
            if (!TryGetRun(document, BlockIndex, RunIndex, out var run)
                || !TryGetTarget(run, out var target))
            {
                return;
            }

            target.HorizontalOffsetPt = HorizontalOffsetPt;
            target.VerticalOffsetPt = VerticalOffsetPt;
            target.HorizontalAnchor = HorizontalAnchor;
            target.VerticalAnchor = VerticalAnchor;
        }
    }
}
