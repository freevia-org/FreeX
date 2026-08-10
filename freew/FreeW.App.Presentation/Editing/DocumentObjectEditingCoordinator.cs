using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public enum DocumentObjectKind
{
    None,
    Image,
    Shape,
    Chart,
    SmartArt,
    WordArt,
    DrawingGroup
}

/// <summary>
/// Portable model coordinates resolved by a native renderer. A child path addresses an object inside a
/// drawing group while the block/run pair continues to identify the owning document run.
/// </summary>
public readonly record struct DocumentObjectTarget(
    int BlockIndex,
    int RunIndex,
    IReadOnlyList<int>? ChildPath = null)
{
    public bool IsNested => ChildPath is { Count: > 0 };
}

public sealed record DocumentShapePositionPlan(
    double HorizontalOffsetPt,
    double VerticalOffsetPt,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor,
    bool IsGroupLocal);

/// <summary>Reports whether a portable object mutation entered the shared undo history.</summary>
public readonly record struct DocumentObjectEditResult(
    bool Applied,
    DocumentObjectTarget Target,
    DocumentObjectKind Kind)
{
    internal static DocumentObjectEditResult NoChange(DocumentObjectTarget target) =>
        new(false, target, DocumentObjectKind.None);

    internal static DocumentObjectEditResult Changed(
        DocumentObjectTarget target,
        DocumentObjectKind kind) =>
        new(true, target, kind);
}

/// <summary>
/// Owns portable command construction and execution for document drawing objects. Renderers resolve native
/// selection/caret state to a <see cref="DocumentObjectTarget"/>, then retain projection and invalidation.
/// </summary>
public sealed class DocumentObjectEditingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentObjectEditingCoordinator(DocumentEditingSession session) => _session = session;

    /// <summary>Resolves the portable default used by both native WordArt insertion paths.</summary>
    public static WordArt PlanWordArtInsertion(WordArt? wordArt = null) =>
        wordArt ?? WordArt.Create("WordArt", WordArtStyle.GradientFill);

    /// <summary>
    /// Resolves the chart model inserted by either native editor. The default is materialized from the
    /// same portable state used by the Insert Chart dialog, keeping fallback insertion and dialog insertion
    /// aligned without copying category, series, title, or sizing defaults into renderer code.
    /// </summary>
    public static Chart PlanChartInsertion(Chart? chart = null)
    {
        if (chart is not null)
            return chart;

        var state = InsertChartDialogPlanner.BuildInitialState(null, CultureInfo.InvariantCulture);
        if (InsertChartDialogPlanner.TryBuildResult(
                state.Kind,
                state.Title,
                state.SeriesNames,
                state.Rows,
                CultureInfo.InvariantCulture,
                out var planned,
                out var errorMessage)
            && planned is not null)
        {
            return planned;
        }

        throw new InvalidOperationException(
            errorMessage ?? "The default chart insertion preset could not be materialized.");
    }

    public DocumentShapePositionPlan? GetShapePosition(DocumentObjectTarget target)
    {
        if (target.IsNested)
        {
            if (!TryResolveGroupChild(target, out var owningGroup, out var child, out _)
                || child is not Shape)
            {
                return null;
            }

            var childIndex = target.ChildPath![^1];
            var offset = childIndex < owningGroup.ChildOffsets.Count
                ? owningGroup.ChildOffsets[childIndex]
                : (X: 0d, Y: 0d);
            return new DocumentShapePositionPlan(
                offset.X,
                offset.Y,
                HorizontalAnchor.Column,
                VerticalAnchor.Paragraph,
                IsGroupLocal: true);
        }

        if (!TryResolve(target, out Shape? shape))
            return null;

        return new DocumentShapePositionPlan(
            shape.Placement?.HorizontalOffsetPt ?? 0,
            shape.Placement?.VerticalOffsetPt ?? 0,
            shape.Placement?.HorizontalAnchor ?? HorizontalAnchor.Column,
            shape.Placement?.VerticalAnchor ?? VerticalAnchor.Paragraph,
            IsGroupLocal: false);
    }

    /// <summary>Appends an object-carrying run to a body paragraph as one shared undo entry.</summary>
    public bool InsertObjectRun(int paragraphIndex, Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!IsObjectRun(run)
            || paragraphIndex < 0
            || paragraphIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[paragraphIndex] is not Paragraph)
        {
            return false;
        }

        _session.Commands.Execute(new InsertObjectRunCommand(paragraphIndex, run));
        return true;
    }

    public DocumentObjectEditResult SetImageSize(
        DocumentObjectTarget target,
        double widthPt,
        double heightPt = 0)
    {
        if (target.IsNested || widthPt <= 0 || !TryResolve(target, out InlineImage? image))
            return DocumentObjectEditResult.NoChange(target);

        var finalHeight = heightPt > 0
            ? heightPt
            : (image.WidthPt > 0 ? image.HeightPt / image.WidthPt : 1) * widthPt;
        return Execute(
            target,
            DocumentObjectKind.Image,
            new SetImageSizeCommand(target.BlockIndex, target.RunIndex, widthPt, finalHeight));
    }

    public DocumentObjectEditResult SetImageAltText(DocumentObjectTarget target, string? altText) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageAltTextCommand(target.BlockIndex, target.RunIndex, NormalizeAltText(altText)));

    public DocumentObjectEditResult SetImageRotation(
        DocumentObjectTarget target,
        double angleDeg,
        bool flipH,
        bool flipV) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageRotationCommand(target.BlockIndex, target.RunIndex, angleDeg, flipH, flipV));

    public DocumentObjectEditResult SetImageCrop(
        DocumentObjectTarget target,
        double left,
        double right,
        double top,
        double bottom) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageCropCommand(target.BlockIndex, target.RunIndex, left, right, top, bottom));

    public DocumentObjectEditResult SetImageAdjust(
        DocumentObjectTarget target,
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageAdjustCommand(
                target.BlockIndex,
                target.RunIndex,
                brightnessPct,
                contrastPct,
                saturationPct,
                transparencyPct));

    public DocumentObjectEditResult SetImageBorder(
        DocumentObjectTarget target,
        string? colorHex,
        double widthPt,
        string? dash = null) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageBorderCommand(target.BlockIndex, target.RunIndex, colorHex, widthPt, dash));

    public DocumentObjectEditResult SetImageEffect(
        DocumentObjectTarget target,
        int shadowPreset,
        double glowSizePt,
        string? glowColorHex,
        int reflectionPreset,
        double softEdgePt,
        int bevelPreset) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageEffectCommand(
                target.BlockIndex,
                target.RunIndex,
                shadowPreset,
                glowSizePt,
                glowColorHex,
                reflectionPreset,
                softEdgePt,
                bevelPreset));

    public DocumentObjectEditResult SetImageRecolor(
        DocumentObjectTarget target,
        ImageRecolorMode mode,
        double colorTemperature = 0) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageRecolorCommand(target.BlockIndex, target.RunIndex, mode, colorTemperature));

    public DocumentObjectEditResult SetImageArtisticEffect(
        DocumentObjectTarget target,
        ImageArtisticEffect effect) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageArtisticEffectCommand(target.BlockIndex, target.RunIndex, effect));

    public DocumentObjectEditResult SetImageStyle(
        DocumentObjectTarget target,
        PictureStylePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageStyleCommand(target.BlockIndex, target.RunIndex, preset));
    }

    public DocumentObjectEditResult SetImageStyle(
        DocumentObjectTarget target,
        int stylePreset,
        string? borderColorHex,
        double borderWidthPt,
        string? borderDash,
        int shadowPreset,
        int reflectionPreset,
        double softEdgePt) =>
        ExecuteForDirect<InlineImage>(
            target,
            DocumentObjectKind.Image,
            new SetImageStyleCommand(
                target.BlockIndex,
                target.RunIndex,
                stylePreset,
                borderColorHex,
                borderWidthPt,
                borderDash,
                shadowPreset,
                reflectionPreset,
                softEdgePt));

    public DocumentObjectEditResult ResetImage(DocumentObjectTarget target)
    {
        if (target.IsNested || !TryResolve(target, out InlineImage? image))
            return DocumentObjectEditResult.NoChange(target);

        var naturalSize = ImageResetCommandPlanner.BuildNaturalSize(
            image.OriginalPixelWidth,
            image.OriginalPixelHeight,
            image.WidthPt,
            image.HeightPt);
        return Execute(
            target,
            DocumentObjectKind.Image,
            new ResetImageSizeCommand(
                target.BlockIndex,
                target.RunIndex,
                naturalSize.WidthPt,
                naturalSize.HeightPt));
    }

    public DocumentObjectEditResult SetShapeKind(DocumentObjectTarget target, ShapeKind kind) =>
        ExecuteForShape(
            target,
            new SetShapeKindCommand(
                target.BlockIndex,
                target.RunIndex,
                kind,
                NestedPath(target)));

    public DocumentObjectEditResult ConvertShapeToFreeform(DocumentObjectTarget target)
    {
        if (!TryResolve(target, out Shape? shape) || shape.HasCustomGeometry)
            return DocumentObjectEditResult.NoChange(target);

        var geometry = shape.Kind switch
        {
            ShapeKind.Ellipse => CustomGeometry.EllipsePoly(),
            ShapeKind.RoundedRectangle => CustomGeometry.RoundedRectPoly(),
            _ => CustomGeometry.RectanglePoly()
        };
        return Execute(
            target,
            DocumentObjectKind.Shape,
            new SetShapeCustomGeometryCommand(
                target.BlockIndex,
                target.RunIndex,
                geometry,
                NestedPath(target)));
    }

    public DocumentObjectEditResult MoveShapeEditPoint(
        DocumentObjectTarget target,
        int segmentIndex,
        long x,
        long y) =>
        ExecuteForShape(
            target,
            new MoveShapeEditPointCommand(
                target.BlockIndex,
                target.RunIndex,
                segmentIndex,
                x,
                y,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeFill(DocumentObjectTarget target, string? colorHex) =>
        ExecuteForShape(
            target,
            new SetShapeFillCommand(
                target.BlockIndex,
                target.RunIndex,
                colorHex,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeOutline(
        DocumentObjectTarget target,
        string? colorHex,
        double widthPt,
        string? dash = null) =>
        ExecuteForShape(
            target,
            new SetShapeOutlineCommand(
                target.BlockIndex,
                target.RunIndex,
                colorHex,
                widthPt,
                dash,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeSize(
        DocumentObjectTarget target,
        double widthPt,
        double heightPt)
    {
        if (widthPt <= 0 || heightPt <= 0 || !TryResolve(target, out Shape? _))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand command = target.IsNested
            ? new SetDrawingGroupChildSizeCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                widthPt,
                heightPt)
            : new SetShapeSizeCommand(target.BlockIndex, target.RunIndex, widthPt, heightPt);
        return Execute(target, DocumentObjectKind.Shape, command);
    }

    public DocumentObjectEditResult SetShapeAltText(DocumentObjectTarget target, string? altText) =>
        ExecuteForShape(
            target,
            new SetShapeAltTextCommand(
                target.BlockIndex,
                target.RunIndex,
                NormalizeAltText(altText),
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeTextDirection(
        DocumentObjectTarget target,
        ShapeTextDirection direction) =>
        ExecuteForShape(
            target,
            new SetShapeTextDirectionCommand(
                target.BlockIndex,
                target.RunIndex,
                direction,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeAlignment(
        DocumentObjectTarget target,
        TextAlignment alignment)
    {
        if (!TryResolve(target, out Shape? shape))
            return DocumentObjectEditResult.NoChange(target);

        if (target.IsNested)
        {
            if (!ShapeTextFormattingPlanner.CanApplyParagraphAlignment(shape))
                return DocumentObjectEditResult.NoChange(target);

            return Execute(
                target,
                DocumentObjectKind.Shape,
                new SetShapeTextParagraphAlignmentCommand(
                    target.BlockIndex,
                    target.RunIndex,
                    alignment,
                    target.ChildPath));
        }

        if (!TryGetRun(target, out _, out var paragraph))
            return DocumentObjectEditResult.NoChange(target);

        return Execute(
            target,
            DocumentObjectKind.Shape,
            new SetParagraphFormattingCommand(
                target.BlockIndex,
                paragraph.Formatting with { Alignment = alignment }));
    }

    public DocumentObjectEditResult SetImageAlignment(
        DocumentObjectTarget target,
        TextAlignment alignment)
    {
        if (target.IsNested
            || !TryResolve(target, out InlineImage? _)
            || !TryGetRun(target, out _, out var paragraph))
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        return Execute(
            target,
            DocumentObjectKind.Image,
            new SetParagraphFormattingCommand(
                target.BlockIndex,
                paragraph.Formatting with { Alignment = alignment }));
    }

    public DocumentObjectEditResult ApplyShapeStyle(
        DocumentObjectTarget target,
        ShapeStylePreset preset) =>
        ExecuteForShape(
            target,
            new ApplyShapeStyleCommand(
                target.BlockIndex,
                target.RunIndex,
                preset,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeExtendedFill(
        DocumentObjectTarget target,
        ShapeFill? fill) =>
        ExecuteForShape(
            target,
            new SetShapeExtendedFillCommand(
                target.BlockIndex,
                target.RunIndex,
                fill,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapeEffects(
        DocumentObjectTarget target,
        ShapeEffectLst? effects) =>
        ExecuteForShape(
            target,
            new SetShapeEffectsCommand(
                target.BlockIndex,
                target.RunIndex,
                effects,
                NestedPath(target)));

    public DocumentObjectEditResult SetShapePosition(
        DocumentObjectTarget target,
        double horizontalOffsetPt,
        double verticalOffsetPt,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor)
    {
        if (!TryResolve(target, out Shape? _))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand command = target.IsNested
            ? new SetDrawingGroupChildPositionCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                horizontalOffsetPt,
                verticalOffsetPt)
            : new SetShapePositionCommand(
                target.BlockIndex,
                target.RunIndex,
                horizontalOffsetPt,
                verticalOffsetPt,
                horizontalAnchor,
                verticalAnchor);
        return Execute(target, DocumentObjectKind.Shape, command);
    }

    public DocumentObjectEditResult SetWrap(
        DocumentObjectTarget target,
        ImageWrapping wrapping)
    {
        if (target.IsNested || !TryResolve(target, out _, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        return Execute(
            target,
            kind,
            new SetFloatingWrapCommand(target.BlockIndex, target.RunIndex, wrapping));
    }

    public DocumentObjectEditResult SetPosition(
        DocumentObjectTarget target,
        double horizontalOffsetPt,
        double verticalOffsetPt,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor)
    {
        if (target.IsNested || !TryResolve(target, out var modelObject, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand command = modelObject is InlineImage
            ? new SetImagePositionCommand(
                target.BlockIndex,
                target.RunIndex,
                horizontalOffsetPt,
                verticalOffsetPt,
                horizontalAnchor,
                verticalAnchor)
            : new SetFloatingPositionCommand(
                target.BlockIndex,
                target.RunIndex,
                horizontalOffsetPt,
                verticalOffsetPt,
                horizontalAnchor,
                verticalAnchor);
        return Execute(target, kind, command);
    }

    public DocumentObjectEditResult MoveBy(
        DocumentObjectTarget target,
        double horizontalDeltaPt,
        double verticalDeltaPt)
    {
        if (target.IsNested || !TryResolve(target, out var modelObject, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand? command = modelObject switch
        {
            InlineImage image when image.IsFloating => new NudgeImagePositionCommand(
                target.BlockIndex,
                target.RunIndex,
                image.HorizontalOffsetPt + horizontalDeltaPt,
                image.VerticalOffsetPt + verticalDeltaPt),
            _ when TryGetFloatingPlacement(modelObject, out var placement) =>
                new SetFloatingPositionCommand(
                    target.BlockIndex,
                    target.RunIndex,
                    placement.HorizontalOffsetPt + horizontalDeltaPt,
                    placement.VerticalOffsetPt + verticalDeltaPt,
                    placement.HorizontalAnchor,
                    placement.VerticalAnchor),
            _ => null
        };
        return command is null
            ? DocumentObjectEditResult.NoChange(target)
            : Execute(target, kind, command);
    }

    public DocumentObjectEditResult ResizeAndMove(
        DocumentObjectTarget target,
        double widthPt,
        double heightPt,
        double horizontalDeltaPt,
        double verticalDeltaPt)
    {
        if (target.IsNested
            || widthPt <= 0
            || heightPt <= 0
            || !TryResolve(target, out var modelObject, out var kind)
            || modelObject is WordArt)
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        if (Math.Abs(horizontalDeltaPt) <= 0.01 && Math.Abs(verticalDeltaPt) <= 0.01)
            return SetSize(target, widthPt, heightPt);

        var size = new SetFloatingSizeCommand(
            target.BlockIndex,
            target.RunIndex,
            widthPt,
            heightPt);
        IDocumentCommand? position = modelObject switch
        {
            InlineImage image when image.IsFloating => new NudgeImagePositionCommand(
                target.BlockIndex,
                target.RunIndex,
                image.HorizontalOffsetPt + horizontalDeltaPt,
                image.VerticalOffsetPt + verticalDeltaPt),
            _ when TryGetFloatingPlacement(modelObject, out var placement) =>
                new SetFloatingPositionCommand(
                    target.BlockIndex,
                    target.RunIndex,
                    placement.HorizontalOffsetPt + horizontalDeltaPt,
                    placement.VerticalOffsetPt + verticalDeltaPt,
                    placement.HorizontalAnchor,
                    placement.VerticalAnchor),
            _ => null
        };
        return position is null
            ? DocumentObjectEditResult.NoChange(target)
            : Execute(
                target,
                kind,
                new CompositeDocumentCommand("Resize", [size, position]));
    }

    public DocumentObjectEditResult SetSize(
        DocumentObjectTarget target,
        double widthPt,
        double heightPt)
    {
        if (target.IsNested
            || widthPt <= 0
            || heightPt <= 0
            || !TryResolve(target, out var modelObject, out var kind)
            || modelObject is WordArt)
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        return Execute(
            target,
            kind,
            new SetFloatingSizeCommand(target.BlockIndex, target.RunIndex, widthPt, heightPt));
    }

    public DocumentObjectEditResult SetAltText(DocumentObjectTarget target, string? altText)
    {
        if (!TryResolve(target, out var modelObject, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        var normalized = NormalizeAltText(altText);
        IDocumentCommand? command = modelObject switch
        {
            InlineImage when !target.IsNested =>
                new SetImageAltTextCommand(target.BlockIndex, target.RunIndex, normalized),
            Shape => new SetShapeAltTextCommand(
                target.BlockIndex,
                target.RunIndex,
                normalized,
                NestedPath(target)),
            WordArt when !target.IsNested =>
                new SetWordArtAltTextCommand(target.BlockIndex, target.RunIndex, normalized),
            _ => null
        };
        return command is null
            ? DocumentObjectEditResult.NoChange(target)
            : Execute(target, kind, command);
    }

    public DocumentObjectEditResult SetWordArtStyle(
        DocumentObjectTarget target,
        WordArtStyle style) =>
        ExecuteForDirect<WordArt>(
            target,
            DocumentObjectKind.WordArt,
            new SetWordArtStyleCommand(target.BlockIndex, target.RunIndex, style));

    public DocumentObjectEditResult SetWordArtWarp(
        DocumentObjectTarget target,
        WordArtWarp warp) =>
        ExecuteForDirect<WordArt>(
            target,
            DocumentObjectKind.WordArt,
            new SetWordArtWarpCommand(target.BlockIndex, target.RunIndex, warp));

    public DocumentObjectEditResult SetRotation(
        DocumentObjectTarget target,
        double angleDeg,
        bool flipH,
        bool flipV)
    {
        if (!TryResolve(target, out _, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand command = target.IsNested
            ? new SetDrawingGroupChildRotationCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                angleDeg,
                flipH,
                flipV)
            : new SetFloatingRotationCommand(
                target.BlockIndex,
                target.RunIndex,
                angleDeg,
                flipH,
                flipV);
        return Execute(target, kind, command);
    }

    public DocumentObjectEditResult RotateBy(DocumentObjectTarget target, double deltaDegrees)
    {
        if (!TryGetTransform(target, out var angle, out var flipH, out var flipV))
            return DocumentObjectEditResult.NoChange(target);

        return SetRotation(target, AddRotation(angle, deltaDegrees), flipH, flipV);
    }

    public DocumentObjectEditResult Flip(DocumentObjectTarget target, bool horizontal)
    {
        if (!TryGetTransform(target, out var angle, out var flipH, out var flipV))
            return DocumentObjectEditResult.NoChange(target);

        return SetRotation(
            target,
            angle,
            horizontal ? !flipH : flipH,
            horizontal ? flipV : !flipV);
    }

    public DocumentObjectEditResult ChangeZOrder(
        DocumentObjectTarget target,
        ZOrderOperation operation)
    {
        if (!TryResolve(target, out _, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        IDocumentCommand command = target.IsNested
            ? new ChangeDrawingGroupChildZOrderCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                operation)
            : new ChangeZOrderCommand(target.BlockIndex, target.RunIndex, operation);
        return Execute(target, kind, command);
    }

    public DocumentObjectEditResult SetGroupChildPosition(
        DocumentObjectTarget target,
        double horizontalOffsetPt,
        double verticalOffsetPt)
    {
        if (!target.IsNested || !TryResolve(target, out _, out var kind))
            return DocumentObjectEditResult.NoChange(target);

        return Execute(
            target,
            kind,
            new SetDrawingGroupChildPositionCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                horizontalOffsetPt,
                verticalOffsetPt));
    }

    public DocumentObjectEditResult MoveGroupChildBy(
        DocumentObjectTarget target,
        double horizontalDeltaPt,
        double verticalDeltaPt)
    {
        if (!target.IsNested
            || !TryResolveGroupChild(target, out var owningGroup, out _, out _))
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        var childIndex = target.ChildPath![^1];
        var offset = childIndex < owningGroup.ChildOffsets.Count
            ? owningGroup.ChildOffsets[childIndex]
            : (X: 0d, Y: 0d);
        return SetGroupChildPosition(
            target,
            offset.X + horizontalDeltaPt,
            offset.Y + verticalDeltaPt);
    }

    public DocumentObjectEditResult ResizeGroupChild(
        DocumentObjectTarget target,
        double widthPt,
        double heightPt,
        double horizontalDeltaPt = 0,
        double verticalDeltaPt = 0)
    {
        if (!target.IsNested
            || widthPt <= 0
            || heightPt <= 0
            || !TryResolveGroupChild(target, out var owningGroup, out _, out var kind))
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        var commands = new List<IDocumentCommand>
        {
            new SetDrawingGroupChildSizeCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath!,
                widthPt,
                heightPt)
        };
        if (Math.Abs(horizontalDeltaPt) > 0.01 || Math.Abs(verticalDeltaPt) > 0.01)
        {
            var childIndex = target.ChildPath![^1];
            var offset = childIndex < owningGroup.ChildOffsets.Count
                ? owningGroup.ChildOffsets[childIndex]
                : (X: 0d, Y: 0d);
            commands.Add(new SetDrawingGroupChildPositionCommand(
                target.BlockIndex,
                target.RunIndex,
                target.ChildPath,
                offset.X + horizontalDeltaPt,
                offset.Y + verticalDeltaPt));
        }

        return Execute(
            target,
            kind,
            new CompositeDocumentCommand("Resize Group Child", commands));
    }

    public DocumentObjectEditResult SetChartKind(DocumentObjectTarget target, ChartKind kind) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartKindCommand(target.BlockIndex, target.RunIndex, kind));

    public DocumentObjectEditResult SetChartStyle(DocumentObjectTarget target, int styleId) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartStyleCommand(target.BlockIndex, target.RunIndex, styleId));

    public DocumentObjectEditResult SetChartColorScheme(
        DocumentObjectTarget target,
        string? colorSchemeId) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartColorSchemeCommand(target.BlockIndex, target.RunIndex, colorSchemeId));

    public DocumentObjectEditResult SetChartQuickLayout(
        DocumentObjectTarget target,
        ChartQuickLayout layout) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartQuickLayoutCommand(target.BlockIndex, target.RunIndex, layout));

    public DocumentObjectEditResult SetChartLegend(DocumentObjectTarget target, bool showLegend)
    {
        if (!TryResolve(target, out Chart? chart)
            || !ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart).CanToggleLegend)
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        return Execute(
            target,
            DocumentObjectKind.Chart,
            new SetChartLegendCommand(target.BlockIndex, target.RunIndex, showLegend));
    }

    public DocumentObjectEditResult ToggleChartLegend(DocumentObjectTarget target)
    {
        if (!TryResolve(target, out Chart? chart))
            return DocumentObjectEditResult.NoChange(target);

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        return state.CanToggleLegend
            ? SetChartLegend(target, !state.IsLegendVisible)
            : DocumentObjectEditResult.NoChange(target);
    }

    public DocumentObjectEditResult SetChartTitle(DocumentObjectTarget target, string? title) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartTitleCommand(target.BlockIndex, target.RunIndex, title));

    public DocumentObjectEditResult ToggleChartTitle(
        DocumentObjectTarget target,
        string defaultTitle = "Chart Title")
    {
        if (!TryResolve(target, out Chart? chart))
            return DocumentObjectEditResult.NoChange(target);

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        return SetChartTitle(target, state.HasChartTitle ? null : defaultTitle);
    }

    public DocumentObjectEditResult SetChartAxisTitles(
        DocumentObjectTarget target,
        string? categoryTitle,
        string? valueTitle) =>
        ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new SetChartAxisTitlesCommand(
                target.BlockIndex,
                target.RunIndex,
                categoryTitle,
                valueTitle));

    public DocumentObjectEditResult ToggleChartAxisTitles(
        DocumentObjectTarget target,
        string defaultCategoryTitle = "Category Axis",
        string defaultValueTitle = "Value Axis")
    {
        if (!TryResolve(target, out Chart? chart))
            return DocumentObjectEditResult.NoChange(target);

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        if (!state.CanEditAxisTitles)
            return DocumentObjectEditResult.NoChange(target);

        var hasStoredTitles = !string.IsNullOrWhiteSpace(chart.CategoryAxisTitle)
            || !string.IsNullOrWhiteSpace(chart.ValueAxisTitle);
        return SetChartAxisTitles(
            target,
            hasStoredTitles ? null : defaultCategoryTitle,
            hasStoredTitles ? null : defaultValueTitle);
    }

    public DocumentObjectEditResult ReplaceChartData(
        DocumentObjectTarget target,
        Chart replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return ExecuteForDirect<Chart>(
            target,
            DocumentObjectKind.Chart,
            new ReplaceChartDataCommand(target.BlockIndex, target.RunIndex, replacement));
    }

    public DocumentObjectEditResult SetSmartArtLayout(
        DocumentObjectTarget target,
        SmartArtKind kind,
        string? layoutId = null) =>
        ExecuteForDirect<SmartArt>(
            target,
            DocumentObjectKind.SmartArt,
            new SetSmartArtLayoutCommand(target.BlockIndex, target.RunIndex, kind, layoutId));

    public DocumentObjectEditResult SetSmartArtColor(
        DocumentObjectTarget target,
        string? colorSchemeId) =>
        ExecuteForDirect<SmartArt>(
            target,
            DocumentObjectKind.SmartArt,
            new SetSmartArtColorCommand(target.BlockIndex, target.RunIndex, colorSchemeId));

    public DocumentObjectEditResult SetSmartArtStyle(
        DocumentObjectTarget target,
        string? styleId) =>
        ExecuteForDirect<SmartArt>(
            target,
            DocumentObjectKind.SmartArt,
            new SetSmartArtStyleCommand(target.BlockIndex, target.RunIndex, styleId));

    public DocumentObjectEditResult MutateSmartArt(
        DocumentObjectTarget target,
        SmartArtStructureOperation operation)
    {
        if (!TryResolve(target, out SmartArt? smartArt)
            || !MutateSmartArtStructureCommand.CanApply(smartArt, operation))
        {
            return DocumentObjectEditResult.NoChange(target);
        }

        return Execute(
            target,
            DocumentObjectKind.SmartArt,
            new MutateSmartArtStructureCommand(target.BlockIndex, target.RunIndex, operation));
    }

    public DocumentObjectEditResult ReplaceSmartArt(
        DocumentObjectTarget target,
        SmartArt replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return ExecuteForDirect<SmartArt>(
            target,
            DocumentObjectKind.SmartArt,
            new ReplaceSmartArtContentCommand(target.BlockIndex, target.RunIndex, replacement));
    }

    public DocumentObjectEditResult Group(IReadOnlyList<DocumentObjectTarget> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var floatingLocations = ArrangeFloatingObjectsCommand
            .CollectFloatingObjectLocations(_session.Document)
            .ToHashSet();
        var locations = members
            .Where(target => !target.IsNested
                && floatingLocations.Contains((target.BlockIndex, target.RunIndex)))
            .Select(target => (target.BlockIndex, target.RunIndex))
            .Distinct()
            .ToArray();
        if (locations.Length < 2)
            return DocumentObjectEditResult.NoChange(default);

        _session.Commands.Execute(new GroupFloatingObjectsCommand(locations));
        return DocumentObjectEditResult.Changed(default, DocumentObjectKind.DrawingGroup);
    }

    public DocumentObjectEditResult Ungroup(DocumentObjectTarget target) =>
        ExecuteForDirect<DrawingGroup>(
            target,
            DocumentObjectKind.DrawingGroup,
            new UngroupFloatingObjectsCommand(target.BlockIndex, target.RunIndex));

    public bool CanArrange(
        FloatingObjectArrangeKind kind,
        IReadOnlyList<DocumentObjectTarget> members) =>
        ArrangeFloatingObjectsCommand.CountApplicableObjects(_session.Document, Locations(members))
            >= RequiredArrangeObjectCount(kind);

    public DocumentObjectEditResult Arrange(
        FloatingObjectArrangeKind kind,
        IReadOnlyList<DocumentObjectTarget> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var locations = Locations(members);
        if (ArrangeFloatingObjectsCommand.CountApplicableObjects(_session.Document, locations)
            < RequiredArrangeObjectCount(kind))
        {
            return DocumentObjectEditResult.NoChange(default);
        }

        _session.Commands.Execute(new ArrangeFloatingObjectsCommand(kind, locations));
        return DocumentObjectEditResult.Changed(default, DocumentObjectKind.None);
    }

    public IReadOnlyList<DocumentObjectTarget> CollectFloatingObjects() =>
        ArrangeFloatingObjectsCommand.CollectFloatingObjectLocations(_session.Document)
            .Select(location => new DocumentObjectTarget(location.BlockIndex, location.RunIndex))
            .ToArray();

    private DocumentObjectEditResult ExecuteForShape(
        DocumentObjectTarget target,
        IDocumentCommand command) =>
        TryResolve(target, out Shape? _)
            ? Execute(target, DocumentObjectKind.Shape, command)
            : DocumentObjectEditResult.NoChange(target);

    private DocumentObjectEditResult ExecuteForDirect<T>(
        DocumentObjectTarget target,
        DocumentObjectKind kind,
        IDocumentCommand command)
        where T : class =>
        TryResolve(target, out T? modelObject) && !target.IsNested
            ? Execute(target, kind, command)
            : DocumentObjectEditResult.NoChange(target);

    private DocumentObjectEditResult Execute(
        DocumentObjectTarget target,
        DocumentObjectKind kind,
        IDocumentCommand command)
    {
        _session.Commands.Execute(command);
        return DocumentObjectEditResult.Changed(target, kind);
    }

    private bool TryResolve<T>(
        DocumentObjectTarget target,
        [NotNullWhen(true)] out T? modelObject)
        where T : class
    {
        if (TryResolve(target, out var resolved, out _) && resolved is T typed)
        {
            modelObject = typed;
            return true;
        }

        modelObject = null;
        return false;
    }

    private bool TryResolve(
        DocumentObjectTarget target,
        out object modelObject,
        out DocumentObjectKind kind)
    {
        modelObject = null!;
        kind = DocumentObjectKind.None;
        if (!TryGetRun(target, out var run, out _))
            return false;

        if (target.IsNested)
            return TryResolveGroupChild(target, out _, out modelObject, out kind);

        if (run.Image is { } image)
            (modelObject, kind) = (image, DocumentObjectKind.Image);
        else if (run.Shape is { } shape)
            (modelObject, kind) = (shape, DocumentObjectKind.Shape);
        else if (run.Chart is { } chart)
            (modelObject, kind) = (chart, DocumentObjectKind.Chart);
        else if (run.SmartArt is { } smartArt)
            (modelObject, kind) = (smartArt, DocumentObjectKind.SmartArt);
        else if (run.WordArt is { } wordArt)
            (modelObject, kind) = (wordArt, DocumentObjectKind.WordArt);
        else if (run.DrawingGroup is { } group)
            (modelObject, kind) = (group, DocumentObjectKind.DrawingGroup);
        else
            return false;

        return true;
    }

    private bool TryResolveGroupChild(
        DocumentObjectTarget target,
        out DrawingGroup owningGroup,
        out object child,
        out DocumentObjectKind kind)
    {
        owningGroup = null!;
        child = null!;
        kind = DocumentObjectKind.None;
        if (!target.IsNested
            || !TryGetRun(target, out var run, out _)
            || run.DrawingGroup is not { } root
            || !DrawingGroupChildPathResolver.TryGetChild(
                root,
                target.ChildPath!,
                out owningGroup,
                out child))
        {
            return false;
        }

        kind = KindOf(child);
        return kind != DocumentObjectKind.None;
    }

    private bool TryGetRun(
        DocumentObjectTarget target,
        out Run run,
        out Paragraph paragraph)
    {
        run = null!;
        paragraph = null!;
        if (target.BlockIndex < 0
            || target.BlockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[target.BlockIndex] is not Paragraph resolvedParagraph
            || target.RunIndex < 0
            || target.RunIndex >= resolvedParagraph.Runs.Count)
        {
            return false;
        }

        paragraph = resolvedParagraph;
        run = paragraph.Runs[target.RunIndex];
        return true;
    }

    private bool TryGetTransform(
        DocumentObjectTarget target,
        out double angle,
        out bool flipH,
        out bool flipV)
    {
        angle = 0;
        flipH = false;
        flipV = false;
        if (!TryResolve(target, out var modelObject, out _))
            return false;

        (angle, flipH, flipV) = modelObject switch
        {
            InlineImage image => (image.RotationAngle, image.FlipH, image.FlipV),
            Shape shape => (shape.RotationAngle, shape.FlipH, shape.FlipV),
            Chart chart => (chart.RotationAngle, chart.FlipH, chart.FlipV),
            SmartArt smartArt => (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV),
            WordArt wordArt => (wordArt.RotationAngle, wordArt.FlipH, wordArt.FlipV),
            DrawingGroup group => (group.RotationAngle, group.FlipH, group.FlipV),
            _ => (double.NaN, false, false)
        };
        return !double.IsNaN(angle);
    }

    private static bool TryGetFloatingPlacement(
        object modelObject,
        [NotNullWhen(true)] out FloatingPlacement? placement)
    {
        placement = modelObject switch
        {
            Shape shape => shape.Placement ?? new FloatingPlacement(),
            Chart chart => chart.Placement ?? new FloatingPlacement { Wrapping = ImageWrapping.Square },
            SmartArt smartArt => smartArt.Placement ?? new FloatingPlacement { Wrapping = ImageWrapping.Square },
            WordArt wordArt => wordArt.Placement ?? new FloatingPlacement { Wrapping = ImageWrapping.Square },
            DrawingGroup group => group.Placement,
            _ => null
        };
        return placement is not null;
    }

    private static DocumentObjectKind KindOf(object modelObject) => modelObject switch
    {
        InlineImage => DocumentObjectKind.Image,
        Shape => DocumentObjectKind.Shape,
        Chart => DocumentObjectKind.Chart,
        SmartArt => DocumentObjectKind.SmartArt,
        WordArt => DocumentObjectKind.WordArt,
        DrawingGroup => DocumentObjectKind.DrawingGroup,
        _ => DocumentObjectKind.None
    };

    private static IReadOnlyList<int>? NestedPath(DocumentObjectTarget target) =>
        target.IsNested ? target.ChildPath : null;

    private static string? NormalizeAltText(string? altText) =>
        string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();

    private static bool IsObjectRun(Run run) =>
        run.Image is not null
        || run.Shape is not null
        || run.Chart is not null
        || run.WordArt is not null
        || run.SmartArt is not null
        || run.Equation is not null
        || run.EmbeddedObject is not null
        || run.DrawingGroup is not null;

    private static double AddRotation(double currentAngle, double delta) =>
        (currentAngle + delta + 360) % 360;

    private static int RequiredArrangeObjectCount(FloatingObjectArrangeKind kind) =>
        kind is FloatingObjectArrangeKind.DistributeHorizontal
            or FloatingObjectArrangeKind.DistributeVertical
            ? 2
            : 1;

    private static IReadOnlyList<(int BlockIndex, int RunIndex)> Locations(
        IReadOnlyList<DocumentObjectTarget> members) =>
        members
            .Where(target => !target.IsNested)
            .Select(target => (target.BlockIndex, target.RunIndex))
            .Distinct()
            .ToArray();

    private sealed class InsertObjectRunCommand(int paragraphIndex, Run run) : IDocumentCommand
    {
        private List<Run>? _previous;

        public string Label => run.Image is not null
            ? "Insert Picture"
            : run.Shape is { Kind: ShapeKind.TextBox }
                ? "Insert Text Box"
                : run.Shape is not null
                    ? "Insert Shape"
                    : run.Chart is not null
                        ? "Insert Chart"
                        : run.WordArt is not null
                            ? "Insert WordArt"
                            : run.SmartArt is not null
                                ? "Insert SmartArt"
                                : run.Equation is not null
                                    ? "Insert Equation"
                                    : run.EmbeddedObject is not null
                                        ? "Insert Object"
                                        : "Insert Drawing";

        public void Apply(IDocumentCommandContext context)
        {
            if (ParagraphAt(context) is not { } paragraph)
                return;

            _previous = [.. paragraph.Runs];
            paragraph.Runs.Add(run);
        }

        public void Revert(IDocumentCommandContext context)
        {
            if (_previous is null || ParagraphAt(context) is not { } paragraph)
                return;

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(_previous);
            _previous = null;
        }

        private Paragraph? ParagraphAt(IDocumentCommandContext context) =>
            paragraphIndex >= 0 && paragraphIndex < context.Document.Blocks.Count
                ? context.Document.Blocks[paragraphIndex] as Paragraph
                : null;
    }
}
