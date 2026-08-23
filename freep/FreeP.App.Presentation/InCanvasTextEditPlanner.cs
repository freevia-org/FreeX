using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum InCanvasTextEditKind
{
    PlainText,
    RichText,
}

public enum InCanvasTextEditOutcome
{
    Canceled,
    Unchanged,
    Commit,
}

public enum InCanvasTextEditStartStatus
{
    Ready,
    MissingPresentation,
    MissingSlide,
    ShapeNotFound,
    MissingTextBody,
}

public enum InCanvasShapeTextFormatStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    MissingTextBody,
    NoTextRuns,
}

public readonly record struct InCanvasTextEditDecision(
    InCanvasTextEditOutcome Outcome,
    IPresentationCommand? Command);

public readonly record struct InCanvasEditorTextSelection(
    int Start,
    int End)
{
    public bool IsCollapsed => Start == End;
}

public sealed record InCanvasShapeTextFormatPlan(
    InCanvasShapeTextFormatStatus Status,
    uint ShapeId,
    TableCellTextFormatKind Kind,
    bool? TargetValue,
    IPresentationCommand? Command)
{
    public bool IsReady => Status == InCanvasShapeTextFormatStatus.Ready && Command is not null;
}

public sealed record InCanvasShapeTextValueFormatPlan(
    InCanvasShapeTextFormatStatus Status,
    uint ShapeId,
    TableCellTextValueFormatKind Kind,
    object? Value,
    IPresentationCommand? Command)
{
    public bool IsReady => Status == InCanvasShapeTextFormatStatus.Ready && Command is not null;
}

public sealed record InCanvasTextEditStartPlan(
    InCanvasTextEditStartStatus Status,
    uint ShapeId,
    InCanvasTextEditKind Kind,
    InCanvasEditorPlacement? Placement,
    InCanvasEditorTextSelection InitialSelection,
    InCanvasTableCellRichTextEditPlan? RichTextPlan,
    TextBody? OriginalBody,
    string OriginalPlainText,
    InCanvasTextEditPlanner? EditPlanner,
    TextBody? InheritedLayoutBody = null,
    MasterTextStyles? InheritedMasterTextStyles = null,
    SlideCompositor.TextStyleCategory InheritedStyleCategory = SlideCompositor.TextStyleCategory.Other)
{
    public bool IsReady => Status == InCanvasTextEditStartStatus.Ready;
}

/// <summary>
/// Shared in-canvas text-edit policy for WPF and Avalonia renderers.
/// Renderers adapt framework controls into text payloads; this planner decides whether a payload commits.
/// </summary>
public sealed class InCanvasTextEditPlanner
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextBody? _originalBody;
    private readonly string _originalPlainText;
    private readonly InCanvasTextEditKind _kind;

    private InCanvasTextEditPlanner(
        int slideIndex,
        uint shapeId,
        TextBody? originalBody,
        InCanvasTextEditKind kind)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _originalBody = TextBodyModelCloner.CloneTextBody(originalBody);
        _originalPlainText = ExtractPlainText(originalBody);
        _kind = kind;
    }

    public string OriginalPlainText => _originalPlainText;

    public static InCanvasTextEditPlanner BeginPlainText(int slideIndex, uint shapeId, TextBody? originalBody) =>
        new(slideIndex, shapeId, originalBody, InCanvasTextEditKind.PlainText);

    public static InCanvasTextEditPlanner BeginRichText(int slideIndex, uint shapeId, TextBody? originalBody) =>
        new(slideIndex, shapeId, originalBody, InCanvasTextEditKind.RichText);

    public static InCanvasTextEditStartPlan BeginShapeEdit(
        int slideIndex,
        Presentation? presentation,
        Slide? slide,
        uint shapeId,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight,
        InCanvasTextEditKind kind)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumHeight);

        if (presentation is null)
            return NotReady(InCanvasTextEditStartStatus.MissingPresentation, shapeId, kind);
        if (slide is null)
            return NotReady(InCanvasTextEditStartStatus.MissingSlide, shapeId, kind);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return NotReady(InCanvasTextEditStartStatus.ShapeNotFound, shapeId, kind);
        if (shape.TextBody is null)
            return NotReady(InCanvasTextEditStartStatus.MissingTextBody, shapeId, kind);

        var screenRect = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(shape, slide, presentation, transform);
        // The live text editor mirrors the static-render fix: PowerPoint keeps a flipped
        // shape's text upright and left-to-right readable, so the editor placement carries
        // rotation only, never the flipH/flipV mirror. See
        // ShapeTransformPlanner.PlanShapeTextRenderTransform for the render-path counterpart.
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            screenRect,
            minimumWidth,
            minimumHeight,
            shape.RotationDeg,
            flipHorizontal: false,
            flipVertical: false);
        var originalBody = TextBodyModelCloner.CloneTextBody(shape.TextBody);
        var initialSelection = TableCellEditPlanner.PlanInitialSelection(originalBody);
        var richTextPlan = TableCellEditPlanner.PlanRichTextEdit(originalBody, initialSelection);
        var planner = kind == InCanvasTextEditKind.RichText
            ? BeginRichText(slideIndex, shapeId, shape.TextBody)
            : BeginPlainText(slideIndex, shapeId, shape.TextBody);

        // The overlay must preview the same per-property layout/master inherited run style
        // (color, font, weight, alignment, indent) that SlideCompositor uses for the static
        // render, or text visibly changes appearance the moment editing ends. See
        // SlideCompositor.ResolveInheritedTextStyleContext for the shared lookup.
        var (inheritedLayoutBody, inheritedMasterTextStyles, inheritedCategory) =
            SlideCompositor.ResolveInheritedTextStyleContext(shape, slide, presentation);

        return new InCanvasTextEditStartPlan(
            InCanvasTextEditStartStatus.Ready,
            shapeId,
            kind,
            placement,
            initialSelection,
            richTextPlan,
            originalBody,
            ExtractPlainText(originalBody),
            planner,
            inheritedLayoutBody,
            inheritedMasterTextStyles,
            inheritedCategory);
    }

    public InCanvasTextEditDecision Cancel() =>
        new(InCanvasTextEditOutcome.Canceled, null);

    public InCanvasTextEditDecision CommitPlainText(string? editedText)
    {
        var text = editedText ?? string.Empty;
        if (text == _originalPlainText)
            return new(InCanvasTextEditOutcome.Unchanged, null);

        var body = BuildPlainTextBody(_originalBody, text);
        return CreateCommitDecision(body);
    }

    public InCanvasTextEditDecision CommitRichText(TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);

        if (TextBodiesEqualForRichTextCommit(_originalBody, editedBody))
            return new(InCanvasTextEditOutcome.Unchanged, null);

        return CreateCommitDecision(editedBody);
    }

    private InCanvasTextEditDecision CreateCommitDecision(TextBody body) =>
        new(
            InCanvasTextEditOutcome.Commit,
            new SetShapeTextBodyCommand(_slideIndex, _shapeId, body, LabelForKind(_kind)));

    private static string LabelForKind(InCanvasTextEditKind kind) =>
        kind == InCanvasTextEditKind.RichText ? "Edit Rich Text" : "Edit Text";

    public static InCanvasShapeTextFormatPlan PlanTextFormat(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection = null)
    {
        var shapePlan = TryGetShapeTextBody(slide, shapeId);
        if (shapePlan.Status != InCanvasShapeTextFormatStatus.Ready || shapePlan.Body is null)
            return DisabledFormat(shapePlan.Status, shapeId, kind);

        if (!TextBodyRunMutationPlanner.HasTextRuns(shapePlan.Body))
            return DisabledFormat(InCanvasShapeTextFormatStatus.NoTextRuns, shapeId, kind);

        var editedBody = TextBodyRunMutationPlanner.ToggleTextFormat(
            shapePlan.Body,
            kind,
            selection,
            out var targetValue);

        return new InCanvasShapeTextFormatPlan(
            InCanvasShapeTextFormatStatus.Ready,
            shapeId,
            kind,
            targetValue,
            new SetShapeTextBodyCommand(slideIndex, shapeId, editedBody, "Edit Rich Text"));
    }

    public static InCanvasShapeTextValueFormatPlan PlanFontFamily(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        string? fontFamily,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            shapeId,
            TableCellTextValueFormatKind.FontFamily,
            fontFamily,
            selection);

    public static InCanvasShapeTextValueFormatPlan PlanFontSize(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        double? sizePt,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            shapeId,
            TableCellTextValueFormatKind.FontSize,
            sizePt,
            selection);

    public static InCanvasShapeTextValueFormatPlan PlanColor(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        ThemeAwareColor? color,
        (int Start, int End)? selection = null) =>
        PlanTextValueFormat(
            slideIndex,
            slide,
            shapeId,
            TableCellTextValueFormatKind.Color,
            color,
            selection);

    /// <summary>Returns the hyperlink when every selected text run shares one value.</summary>
    public static Hyperlink? GetSelectedRunHyperlink(
        TextBody body,
        (int Start, int End)? selection) =>
        TextBodyRunMutationPlanner.GetSelectedHyperlink(body, selection);

    /// <summary>Applies or clears a hyperlink on the selected text runs.</summary>
    public static TextBody ApplySelectedRunHyperlink(
        TextBody body,
        Hyperlink? hyperlink,
        (int Start, int End)? selection) =>
        TextBodyRunMutationPlanner.ApplyHyperlink(body, hyperlink, selection);

    private static InCanvasShapeTextValueFormatPlan PlanTextValueFormat(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection)
    {
        var shapePlan = TryGetShapeTextBody(slide, shapeId);
        if (shapePlan.Status != InCanvasShapeTextFormatStatus.Ready || shapePlan.Body is null)
            return DisabledValueFormat(shapePlan.Status, shapeId, kind, value);

        if (!TextBodyRunMutationPlanner.HasTextRuns(shapePlan.Body))
            return DisabledValueFormat(InCanvasShapeTextFormatStatus.NoTextRuns, shapeId, kind, value);

        var editedBody = TextBodyRunMutationPlanner.ApplyValueFormat(
            shapePlan.Body,
            kind,
            value,
            selection);

        return new InCanvasShapeTextValueFormatPlan(
            InCanvasShapeTextFormatStatus.Ready,
            shapeId,
            kind,
            value,
            new SetShapeTextBodyCommand(slideIndex, shapeId, editedBody, "Edit Rich Text"));
    }

    private static (InCanvasShapeTextFormatStatus Status, TextBody? Body) TryGetShapeTextBody(
        Slide? slide,
        uint shapeId)
    {
        if (slide is null)
            return (InCanvasShapeTextFormatStatus.MissingSlide, null);

        var shape = ShapeHitTester.FindShape(slide, shapeId);
        if (shape is null)
            return (InCanvasShapeTextFormatStatus.ShapeNotFound, null);
        if (shape.TextBody is null)
            return (InCanvasShapeTextFormatStatus.MissingTextBody, null);

        return (InCanvasShapeTextFormatStatus.Ready, shape.TextBody);
    }

    private static InCanvasShapeTextFormatPlan DisabledFormat(
        InCanvasShapeTextFormatStatus status,
        uint shapeId,
        TableCellTextFormatKind kind) =>
        new(status, shapeId, kind, null, null);

    private static InCanvasShapeTextValueFormatPlan DisabledValueFormat(
        InCanvasShapeTextFormatStatus status,
        uint shapeId,
        TableCellTextValueFormatKind kind,
        object? value) =>
        new(status, shapeId, kind, value, null);

    private static InCanvasTextEditStartPlan NotReady(
        InCanvasTextEditStartStatus status,
        uint shapeId,
        InCanvasTextEditKind kind) =>
        new(status, shapeId, kind, null, default, null, null, string.Empty, null);

    public static string ExtractPlainText(TextBody? body)
    {
        if (body is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0)
                sb.Append('\n');

            foreach (var run in body.Paragraphs[pi].Runs)
                sb.Append(run.Text);
        }

        return sb.ToString();
    }

    public static TextBody ApplyParagraphAlignment(
        TextBody source,
        TextAlign alignment,
        (int Start, int End)? selection) =>
        TableCellEditPlanner.ApplyParagraphAlignmentToBody(source, alignment, selection);

    public static TextBody ApplyParagraphBulletToggle(
        TextBody source,
        (int Start, int End)? selection) =>
        TableCellEditPlanner.ApplyParagraphBulletToggleToBody(source, selection);

    public static TextBody ApplyParagraphNumberingToggle(
        TextBody source,
        (int Start, int End)? selection) =>
        TableCellEditPlanner.ApplyParagraphNumberingToggleToBody(source, selection);

    public static TextBody ApplyParagraphListPreset(
        TextBody source,
        (int Start, int End)? selection,
        TableCellListPresetDescriptor preset) =>
        TableCellEditPlanner.ApplyParagraphListPresetToBody(source, selection, preset);

    public static TextBody ApplyParagraphPictureBullet(
        TextBody source,
        (int Start, int End)? selection,
        ImagePart image) =>
        TableCellEditPlanner.ApplyParagraphPictureBulletToBody(source, selection, image);

    public static TextBody ApplyParagraphIndent(
        TextBody source,
        bool increase,
        (int Start, int End)? selection) =>
        TableCellEditPlanner.ApplyParagraphIndentToBody(source, increase, selection);

    /// <summary>
    /// Applies a run-level format through the renderer-neutral mutation planner.
    /// WPF and Avalonia use this entry point while their native text overlays are
    /// active so grouped-child selection behavior stays identical.
    /// </summary>
    public static TextBody ApplyTextFormat(
        TextBody source,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        return TextBodyRunMutationPlanner.ToggleTextFormat(source, kind, selection, out _);
    }

    /// <summary>Applies a run-level value format through the shared planner.</summary>
    public static TextBody ApplyTextValueFormat(
        TextBody source,
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        return TextBodyRunMutationPlanner.ApplyValueFormat(source, kind, value, selection);
    }

    public static TextBody BuildPlainTextBody(TextBody? original, string text)
    {
        string fontFamily = "Calibri";
        double? fontSize = null;
        bool bold = false;
        bool italic = false;
        bool underline = false;
        ThemeAwareColor? color = null;

        if (original?.Paragraphs.Count > 0 && original.Paragraphs[0].Runs.Count > 0)
        {
            var r0 = original.Paragraphs[0].Runs[0];
            fontFamily = r0.FontFamily ?? fontFamily;
            fontSize = r0.FontSizePt;
            bold = r0.Bold;
            italic = r0.Italic;
            underline = r0.Underline;
            color = r0.Color;
        }

        var body = new TextBody
        {
            Wrap = original?.Wrap ?? true,
            Anchor = original?.Anchor ?? VerticalAnchor.Top,
            InsetLeftPt = original?.InsetLeftPt,
            InsetRightPt = original?.InsetRightPt,
            InsetTopPt = original?.InsetTopPt,
            InsetBottomPt = original?.InsetBottomPt,
        };

        foreach (var line in text.Split('\n'))
        {
            var para = new Paragraph();
            para.Runs.Add(new Run
            {
                Text = line,
                FontFamily = fontFamily,
                FontSizePt = fontSize,
                Bold = bold,
                Italic = italic,
                Underline = underline,
                Color = color,
            });
            body.Paragraphs.Add(para);
        }

        if (body.Paragraphs.Count == 0)
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = string.Empty });
            body.Paragraphs.Add(para);
        }

        return body;
    }

    public static bool TextBodiesEqualForRichTextCommit(TextBody? a, TextBody? b)
    {
        return TextBodiesEqualForRichTextCommitCore(a, b, compareParagraphAlignment: true);
    }

    public static bool TextBodiesEqualForTableCellCommit(TextBody? a, TextBody? b)
    {
        return TextBodiesEqualForRichTextCommitCore(a, b, compareParagraphAlignment: false);
    }

    private static bool TextBodiesEqualForRichTextCommitCore(
        TextBody? a,
        TextBody? b,
        bool compareParagraphAlignment)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Paragraphs.Count != b.Paragraphs.Count)
            return false;

        for (int pi = 0; pi < a.Paragraphs.Count; pi++)
        {
            var pa = a.Paragraphs[pi];
            var pb = b.Paragraphs[pi];
            if (pa.Runs.Count != pb.Runs.Count)
                return false;
            if (!ParagraphsEqualForRichTextCommit(pa, pb, compareParagraphAlignment))
                return false;

            for (int ri = 0; ri < pa.Runs.Count; ri++)
            {
                var ra = pa.Runs[ri];
                var rb = pb.Runs[ri];
                if (ra.Text != rb.Text
                    || !ImagePartsEqual(ra.InlineImage, rb.InlineImage)
                    || ra.InlineImageWidthEmu != rb.InlineImageWidthEmu
                    || ra.InlineImageHeightEmu != rb.InlineImageHeightEmu
                    || !InlineOleObjectsEqual(ra.InlineOleObject, rb.InlineOleObject)
                    || !TextBodyModelCloner.InlineTablesEqual(ra.InlineTable, rb.InlineTable)
                    || ra.Bold != rb.Bold
                    || ra.Italic != rb.Italic
                    || ra.Underline != rb.Underline
                    || ra.Strikethrough != rb.Strikethrough
                    || ra.FontFamily != rb.FontFamily
                    || ra.FontSizePt != rb.FontSizePt
                    || ra.BaselineOffset != rb.BaselineOffset
                    || !TextBodyModelCloner.ColorsEqual(ra.Color, rb.Color))
                    return false;
            }
        }

        return true;
    }

    private static bool ParagraphsEqualForRichTextCommit(
        Paragraph a,
        Paragraph b,
        bool compareAlignment) =>
        (!compareAlignment || a.Align == b.Align)
        && a.RightToLeft == b.RightToLeft
        && a.Level == b.Level
        && a.BulletKind == b.BulletKind
        && a.BulletSuppressed == b.BulletSuppressed
        && a.BulletChar == b.BulletChar
        && ImagePartsEqual(a.BulletImage, b.BulletImage)
        && a.AutoNumType == b.AutoNumType
        && a.AutoNumStartAt == b.AutoNumStartAt
        && a.AutoNumStartAtSpecified == b.AutoNumStartAtSpecified
        && string.Equals(a.AutoNumTextTemplate, b.AutoNumTextTemplate, StringComparison.Ordinal)
        && a.MarginLeftEmu == b.MarginLeftEmu
        && a.IndentEmu == b.IndentEmu
        && TextBodyModelCloner.ColorsEqual(a.BulletColor, b.BulletColor)
        && a.BulletColorFollowsText == b.BulletColorFollowsText
        && a.BulletSizePct == b.BulletSizePct
        && a.BulletSizePt == b.BulletSizePt
        && a.BulletSizeFollowsText == b.BulletSizeFollowsText
        && a.BulletFontFamily == b.BulletFontFamily
        && a.BulletFontFollowsText == b.BulletFontFollowsText
        && a.SpaceBeforePt == b.SpaceBeforePt
        && a.SpaceAfterPt == b.SpaceAfterPt
        && TabStopsEqual(a.TabStops, b.TabStops);

    private static bool ImagePartsEqual(ImagePart? a, ImagePart? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        return a.ContentType == b.ContentType && a.Bytes.AsSpan().SequenceEqual(b.Bytes);
    }

    private static bool InlineOleObjectsEqual(InlineOleObjectInfo? a, InlineOleObjectInfo? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        return a.FileName == b.FileName
            && a.ClassName == b.ClassName
            && a.EmbeddedBytes.AsSpan().SequenceEqual(b.EmbeddedBytes);
    }

    private static bool TabStopsEqual(IReadOnlyList<TabStop> a, IReadOnlyList<TabStop> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int index = 0; index < a.Count; index++)
        {
            if (a[index].PositionEmu != b[index].PositionEmu
                || a[index].Alignment != b[index].Alignment
                || a[index].Leader != b[index].Leader)
                return false;
        }

        return true;
    }
}

internal static class TextBodyRunMutationPlanner
{
    internal static bool HasTextRuns(TextBody body) =>
        body.Paragraphs.SelectMany(p => p.Runs).Any();

    internal static TextBody ToggleTextFormat(
        TextBody source,
        TableCellTextFormatKind kind,
        (int Start, int End)? selection,
        out bool targetValue)
    {
        var sourceRuns = source.Paragraphs.SelectMany(p => p.Runs).ToList();
        int textLength = GetPlainTextLength(source);
        var range = NormalizeSelection(selection, textLength);

        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        if (range is { } r)
        {
            var selectedRuns = TextBodyRunMutator.SplitRunsAtSelection(editedBody, r.Start, r.End);
            targetValue = selectedRuns.Count == 0 || !selectedRuns.All(run => TextRunFormattingPolicy.Get(run, kind));
            foreach (var run in selectedRuns)
                TextRunFormattingPolicy.Set(run, kind, targetValue);
        }
        else
        {
            targetValue = !sourceRuns.All(run => TextRunFormattingPolicy.Get(run, kind));
            foreach (var run in editedBody.Paragraphs.SelectMany(p => p.Runs))
                TextRunFormattingPolicy.Set(run, kind, targetValue);
        }

        TextBodyRunMutator.MergeAdjacentRunsWithSameFormat(editedBody);
        return editedBody;
    }

    internal static TextBody ApplyValueFormat(
        TextBody source,
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection)
    {
        int textLength = GetPlainTextLength(source);
        var range = NormalizeSelection(selection, textLength);

        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        var targetRuns = range is { } r
            ? TextBodyRunMutator.SplitRunsAtSelection(editedBody, r.Start, r.End)
            : editedBody.Paragraphs.SelectMany(p => p.Runs).ToList();

        foreach (var run in targetRuns)
            TextRunFormattingPolicy.SetValue(run, kind, value);

        TextBodyRunMutator.MergeAdjacentRunsWithSameFormat(editedBody);
        return editedBody;
    }

    internal static Hyperlink? GetSelectedHyperlink(
        TextBody source,
        (int Start, int End)? selection)
    {
        var range = NormalizeSelection(selection, GetPlainTextLength(source));
        if (range is not { } r)
            return null;

        var copy = TextBodyModelCloner.CloneTextBody(source)!;
        var selectedRuns = TextBodyRunMutator.SplitRunsAtSelection(copy, r.Start, r.End);
        if (selectedRuns.Count == 0)
            return null;

        var first = selectedRuns[0].Hyperlink;
        return selectedRuns.All(run => HyperlinksEqual(run.Hyperlink, first))
            ? CloneHyperlink(first)
            : null;
    }

    internal static TextBody ApplyHyperlink(
        TextBody source,
        Hyperlink? hyperlink,
        (int Start, int End)? selection)
    {
        var editedBody = TextBodyModelCloner.CloneTextBody(source)!;
        var range = NormalizeSelection(selection, GetPlainTextLength(source));
        if (range is not { } r)
            return editedBody;

        foreach (var run in TextBodyRunMutator.SplitRunsAtSelection(editedBody, r.Start, r.End))
            run.Hyperlink = CloneHyperlink(hyperlink);

        TextBodyRunMutator.MergeAdjacentRunsWithSameFormat(editedBody);
        return editedBody;
    }

    private static int GetPlainTextLength(TextBody body) =>
        body.Paragraphs.SelectMany(p => p.Runs).Sum(r => r.Text.Length)
        + Math.Max(0, body.Paragraphs.Count - 1);

    private static (int Start, int End)? NormalizeSelection((int Start, int End)? selection, int textLength)
    {
        if (selection is not { } s)
            return null;

        int start = Math.Min(s.Start, s.End);
        int end = Math.Max(s.Start, s.End);
        start = Math.Clamp(start, 0, textLength);
        end = Math.Clamp(end, 0, textLength);
        return end > start ? (start, end) : null;
    }

    private static bool HyperlinksEqual(Hyperlink? a, Hyperlink? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.Url == b.Url
                && a.TargetSlideId == b.TargetSlideId
                && a.Tooltip == b.Tooltip;

    private static Hyperlink? CloneHyperlink(Hyperlink? source) =>
        source is null
            ? null
            : new Hyperlink
            {
                Url = source.Url,
                TargetSlideId = source.TargetSlideId,
                Tooltip = source.Tooltip,
            };

}

/// <summary>
/// Shared in-canvas rich-text commit policy for table-cell overlays.
/// Renderers own framework controls; this planner owns snapshot, equality, and command creation.
/// </summary>
public sealed class InCanvasTableCellTextEditPlanner
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly TextBody? _originalBody;

    private InCanvasTableCellTextEditPlanner(
        int slideIndex,
        uint shapeId,
        int row,
        int col,
        TextBody? originalBody)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _originalBody = TextBodyModelCloner.CloneTextBody(originalBody);
    }

    public static InCanvasTableCellTextEditPlanner BeginRichText(
        int slideIndex,
        uint shapeId,
        int row,
        int col,
        TextBody? originalBody) =>
        new(slideIndex, shapeId, row, col, originalBody);

    public InCanvasTextEditDecision Cancel() =>
        new(InCanvasTextEditOutcome.Canceled, null);

    public InCanvasTextEditDecision CommitRichText(TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);

        if (InCanvasTextEditPlanner.TextBodiesEqualForTableCellCommit(_originalBody, editedBody))
            return new(InCanvasTextEditOutcome.Unchanged, null);

        return new(
            InCanvasTextEditOutcome.Commit,
            new SetTableCellTextCommand(_slideIndex, _shapeId, _row, _col, editedBody));
    }
}

/// <summary>
/// Deep-cloning command for rich text-body replacements from in-canvas editors.
/// </summary>
public sealed class SetShapeTextBodyCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly TextBody _newBody;
    private readonly string _label;
    private TextBody? _previousBody;

    public SetShapeTextBodyCommand(
        int slideIndex,
        uint shapeId,
        TextBody newBody,
        string label = "Edit Text")
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newBody = TextBodyModelCloner.CloneTextBody(newBody)
            ?? throw new ArgumentNullException(nameof(newBody));
        _label = string.IsNullOrWhiteSpace(label) ? "Edit Text" : label;
    }

    public string Label => _label;

    // This command holds two full TextBody clones, and rich-text editing is how inline pictures and
    // embedded objects enter a shape in the first place -- pasting into an open text box routes here
    // rather than through the canvas-level paste command. On the interface default of a few hundred
    // bytes an inline image would be invisible to the undo budget, so size both bodies for real.
    public int EstimatedBytes =>
        PresentationCommandSizeEstimator.EstimateBytes(_newBody)
        + PresentationCommandSizeEstimator.EstimateBytes(_previousBody);

    public void Apply(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null)
            return;

        _previousBody = TextBodyModelCloner.CloneTextBody(shape.TextBody);
        shape.TextBody = TextBodyModelCloner.CloneTextBody(_newBody);
    }

    public void Revert(Presentation presentation)
    {
        var shape = GetShape(presentation);
        if (shape is null)
            return;

        shape.TextBody = TextBodyModelCloner.CloneTextBody(_previousBody);
    }

    public static TextBody? CloneTextBody(TextBody? source) =>
        TextBodyModelCloner.CloneTextBody(source);

    private SlideShape? GetShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHitTester.FindShape(presentation.Slides[_slideIndex], _shapeId);
    }
}
