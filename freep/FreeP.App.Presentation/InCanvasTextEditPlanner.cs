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
    InCanvasTextEditPlanner? EditPlanner)
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

        var screenRect = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(shape, presentation, transform);
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            screenRect,
            minimumWidth,
            minimumHeight,
            shape.RotationDeg,
            shape.FlipH,
            shape.FlipV);
        var originalBody = TextBodyModelCloner.CloneTextBody(shape.TextBody);
        var initialSelection = TableCellEditPlanner.PlanInitialSelection(originalBody);
        var richTextPlan = TableCellEditPlanner.PlanRichTextEdit(originalBody, initialSelection);
        var planner = kind == InCanvasTextEditKind.RichText
            ? BeginRichText(slideIndex, shapeId, shape.TextBody)
            : BeginPlainText(slideIndex, shapeId, shape.TextBody);

        return new InCanvasTextEditStartPlan(
            InCanvasTextEditStartStatus.Ready,
            shapeId,
            kind,
            placement,
            initialSelection,
            richTextPlan,
            originalBody,
            ExtractPlainText(originalBody),
            planner);
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
            var selectedRuns = SplitRunsAtSelection(editedBody, r.Start, r.End);
            targetValue = selectedRuns.Count == 0 || !selectedRuns.All(run => GetRunFormat(run, kind));
            foreach (var run in selectedRuns)
                SetRunFormat(run, kind, targetValue);
        }
        else
        {
            targetValue = !sourceRuns.All(run => GetRunFormat(run, kind));
            foreach (var run in editedBody.Paragraphs.SelectMany(p => p.Runs))
                SetRunFormat(run, kind, targetValue);
        }

        MergeAdjacentRunsWithSameFormat(editedBody);
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
            ? SplitRunsAtSelection(editedBody, r.Start, r.End)
            : editedBody.Paragraphs.SelectMany(p => p.Runs).ToList();

        foreach (var run in targetRuns)
            SetRunValueFormat(run, kind, value);

        MergeAdjacentRunsWithSameFormat(editedBody);
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
        var selectedRuns = SplitRunsAtSelection(copy, r.Start, r.End);
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

        foreach (var run in SplitRunsAtSelection(editedBody, r.Start, r.End))
            run.Hyperlink = CloneHyperlink(hyperlink);

        MergeAdjacentRunsWithSameFormat(editedBody);
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

    private static List<Run> SplitRunsAtSelection(TextBody body, int start, int end)
    {
        var selected = new List<Run>();
        int cursor = 0;

        for (int pi = 0; pi < body.Paragraphs.Count; pi++)
        {
            if (pi > 0)
                cursor += 1;

            var paragraph = body.Paragraphs[pi];
            var newRuns = new List<Run>();

            foreach (var run in paragraph.Runs)
            {
                int runStart = cursor;
                int runLen = run.Text.Length;
                int runEnd = runStart + runLen;
                cursor = runEnd;

                int overlapStart = Math.Max(runStart, start);
                int overlapEnd = Math.Min(runEnd, end);

                if (overlapEnd <= overlapStart)
                {
                    newRuns.Add(run);
                    continue;
                }

                int beforeLen = overlapStart - runStart;
                int selectedLen = overlapEnd - overlapStart;
                int afterLen = runEnd - overlapEnd;

                if (beforeLen > 0)
                    newRuns.Add(CloneRunWithText(run, run.Text.Substring(0, beforeLen)));

                var middle = CloneRunWithText(run, run.Text.Substring(beforeLen, selectedLen));
                newRuns.Add(middle);
                selected.Add(middle);

                if (afterLen > 0)
                    newRuns.Add(CloneRunWithText(run, run.Text.Substring(beforeLen + selectedLen, afterLen)));
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(newRuns);
        }

        return selected;
    }

    private static Run CloneRunWithText(Run source, string text) => new()
    {
        Text = text,
        InlineImage = source.InlineImage is { } image && text == source.Text
            ? new ImagePart { Bytes = image.Bytes.ToArray(), ContentType = image.ContentType }
            : null,
        InlineImageWidthEmu = source.InlineImage is not null && text == source.Text
            ? source.InlineImageWidthEmu
            : null,
        InlineImageHeightEmu = source.InlineImage is not null && text == source.Text
            ? source.InlineImageHeightEmu
            : null,
        InlineOleObject = source.InlineOleObject is { } ole && text == source.Text
            ? CloneInlineOleObject(ole)
            : null,
        InlineTable = source.InlineTable is { } table && text == source.Text
            ? table.Clone()
            : null,
        FontFamily = source.FontFamily,
        FontSizePt = source.FontSizePt,
        BaselineOffset = source.BaselineOffset,
        Bold = source.Bold,
        Italic = source.Italic,
        BoldSet = source.BoldSet,
        ItalicSet = source.ItalicSet,
        Underline = source.Underline,
        Strikethrough = source.Strikethrough,
        RightToLeft = source.RightToLeft,
        Caps = source.Caps,
        Color = source.Color,
        Hyperlink = source.Hyperlink,
        Field = source.Field,
        TextFill = source.TextFill,
        TextOutline = source.TextOutline,
        TextShadow = source.TextShadow,
        TextReflection = source.TextReflection,
        TextGlow = source.TextGlow,
        TextSoftEdge = source.TextSoftEdge,
        Math = source.Math,
    };

    private static bool RunFormatEquals(Run a, Run b) =>
        ImagePartsEqual(a.InlineImage, b.InlineImage)
        && a.InlineImageWidthEmu == b.InlineImageWidthEmu
        && a.InlineImageHeightEmu == b.InlineImageHeightEmu
        && InlineOleObjectsEqual(a.InlineOleObject, b.InlineOleObject)
        && TextBodyModelCloner.InlineTablesEqual(a.InlineTable, b.InlineTable)
        && a.FontFamily == b.FontFamily
        && a.FontSizePt == b.FontSizePt
        && a.BaselineOffset == b.BaselineOffset
        && a.Bold == b.Bold
        && a.Italic == b.Italic
        && a.BoldSet == b.BoldSet
        && a.ItalicSet == b.ItalicSet
        && a.Underline == b.Underline
        && a.Strikethrough == b.Strikethrough
        && a.RightToLeft == b.RightToLeft
        && a.Caps == b.Caps
        && TextBodyModelCloner.ColorsEqual(a.Color, b.Color)
        && HyperlinksEqual(a.Hyperlink, b.Hyperlink)
        && a.Field == b.Field
        && a.TextFill == b.TextFill
        && a.TextOutline == b.TextOutline
        && a.TextShadow == b.TextShadow
        && a.TextReflection == b.TextReflection
        && a.TextGlow == b.TextGlow
        && a.TextSoftEdge == b.TextSoftEdge
        && a.Math == b.Math;

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

    private static InlineOleObjectInfo? CloneInlineOleObject(InlineOleObjectInfo? source) =>
        source is null
            ? null
            : new InlineOleObjectInfo
            {
                EmbeddedBytes = source.EmbeddedBytes.ToArray(),
                FileName = source.FileName,
                ClassName = source.ClassName,
            };

    internal static void MergeAdjacentRunsWithSameFormat(TextBody body)
    {
        foreach (var paragraph in body.Paragraphs)
        {
            var merged = new List<Run>();
            foreach (var run in paragraph.Runs)
            {
                if (merged.Count > 0 && RunFormatEquals(merged[^1], run))
                    merged[^1].Text += run.Text;
                else
                    merged.Add(run);
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(merged);
        }
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

    private static bool GetRunFormat(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
        TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
        _ => false,
    };

    private static void SetRunFormat(Run run, TableCellTextFormatKind kind, bool value)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.Bold = value;
                run.BoldSet = true;
                break;
            case TableCellTextFormatKind.Italic:
                run.Italic = value;
                run.ItalicSet = true;
                break;
            case TableCellTextFormatKind.Underline:
                run.Underline = value;
                break;
            case TableCellTextFormatKind.Superscript:
                run.BaselineOffset = value ? 10000 : null;
                break;
            case TableCellTextFormatKind.Subscript:
                run.BaselineOffset = value ? -10000 : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static void SetRunValueFormat(Run run, TableCellTextValueFormatKind kind, object? value)
    {
        switch (kind)
        {
            case TableCellTextValueFormatKind.FontFamily:
                run.FontFamily = (string?)value;
                break;
            case TableCellTextValueFormatKind.FontSize:
                run.FontSizePt = (double?)value;
                break;
            case TableCellTextValueFormatKind.Color:
                run.Color = (ThemeAwareColor?)value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
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

internal static class TextBodyModelCloner
{
    internal static bool InlineTablesEqual(InlineTableInfo? a, InlineTableInfo? b)
    {
        if (a is null || b is null)
            return a is null && b is null;

        var left = a.Table;
        var right = b.Table;
        if (left.RichTextAlignment != right.RichTextAlignment
            || left.RichTextLeftIndentPt != right.RichTextLeftIndentPt
            || left.RichTextCellSpacingPt != right.RichTextCellSpacingPt
            || !left.ColumnWidthsEmu.SequenceEqual(right.ColumnWidthsEmu)
            || left.Rows.Count != right.Rows.Count)
            return false;

        for (int rowIndex = 0; rowIndex < left.Rows.Count; rowIndex++)
        {
            var leftRow = left.Rows[rowIndex];
            var rightRow = right.Rows[rowIndex];
            if (leftRow.HeightEmu != rightRow.HeightEmu
                || leftRow.HeightRule != rightRow.HeightRule
                || leftRow.Cells.Count != rightRow.Cells.Count)
                return false;

            for (int cellIndex = 0; cellIndex < leftRow.Cells.Count; cellIndex++)
            {
                var leftCell = leftRow.Cells[cellIndex];
                var rightCell = rightRow.Cells[cellIndex];
                if (leftCell.GridSpan != rightCell.GridSpan
                    || leftCell.RowSpan != rightCell.RowSpan
                    || leftCell.HMerge != rightCell.HMerge
                    || leftCell.VMerge != rightCell.VMerge
                    || !TextBodiesEqualForInlineTable(leftCell.TextBody, rightCell.TextBody))
                    return false;
            }
        }

        return true;
    }

    private static bool TextBodiesEqualForInlineTable(TextBody? a, TextBody? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a.Paragraphs.Count != b.Paragraphs.Count)
            return false;

        for (int paragraphIndex = 0; paragraphIndex < a.Paragraphs.Count; paragraphIndex++)
        {
            var left = a.Paragraphs[paragraphIndex];
            var right = b.Paragraphs[paragraphIndex];
            if (left.Align != right.Align || left.Runs.Count != right.Runs.Count)
                return false;
            for (int runIndex = 0; runIndex < left.Runs.Count; runIndex++)
            {
                var leftRun = left.Runs[runIndex];
                var rightRun = right.Runs[runIndex];
                if (leftRun.Text != rightRun.Text
                    || !InlineTablesEqual(leftRun.InlineTable, rightRun.InlineTable))
                    return false;
            }
        }

        return true;
    }

    internal static TextBody? CloneTextBody(TextBody? source)
    {
        if (source is null)
            return null;

        var copy = new TextBody
        {
            Anchor = source.Anchor,
            DefaultParaAlign = source.DefaultParaAlign,
            DefaultParaRightToLeft = source.DefaultParaRightToLeft,
            InsetLeftPt = source.InsetLeftPt,
            InsetRightPt = source.InsetRightPt,
            InsetTopPt = source.InsetTopPt,
            InsetBottomPt = source.InsetBottomPt,
            Wrap = source.Wrap,
            AutoFitKind = source.AutoFitKind,
            FontScalePPT = source.FontScalePPT,
            LnSpcReductionPPT = source.LnSpcReductionPPT,
            LstStyle = CloneTextStyleLevels(source.LstStyle),
            VerticalType = source.VerticalType,
            WarpPreset = source.WarpPreset,
            ColumnCount = source.ColumnCount,
            ColumnSpacingEmu = source.ColumnSpacingEmu,
        };

        foreach (var adjust in source.WarpAdjusts)
            copy.WarpAdjusts.Add(adjust);

        foreach (var paragraph in source.Paragraphs)
            copy.Paragraphs.Add(CloneParagraph(paragraph));

        return copy;
    }

    internal static bool ColorsEqual(ThemeAwareColor? a, ThemeAwareColor? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Resolved != b.Resolved)
            return false;

        return SchemeColorRefsEqual(a.SchemeColor, b.SchemeColor);
    }

    internal static Paragraph CloneParagraph(Paragraph source)
    {
        var copy = new Paragraph
        {
            Align = source.Align,
            RightToLeft = source.RightToLeft,
            Level = source.Level,
            BulletKind = source.BulletKind,
            BulletSuppressed = source.BulletSuppressed,
            BulletChar = source.BulletChar,
            BulletImage = CloneImagePart(source.BulletImage),
            AutoNumType = source.AutoNumType,
            AutoNumStartAt = source.AutoNumStartAt,
            AutoNumStartAtSpecified = source.AutoNumStartAtSpecified,
            AutoNumTextTemplate = source.AutoNumTextTemplate,
            MarginLeftEmu = source.MarginLeftEmu,
            IndentEmu = source.IndentEmu,
            BulletColor = source.BulletColor,
            BulletColorFollowsText = source.BulletColorFollowsText,
            BulletSizePct = source.BulletSizePct,
            BulletSizePt = source.BulletSizePt,
            BulletSizeFollowsText = source.BulletSizeFollowsText,
            BulletFontFamily = source.BulletFontFamily,
            BulletFontFollowsText = source.BulletFontFollowsText,
            SpaceBeforePt = source.SpaceBeforePt,
            SpaceAfterPt = source.SpaceAfterPt,
        };

        foreach (var tabStop in source.TabStops)
            copy.TabStops.Add(new TabStop
            {
                PositionEmu = tabStop.PositionEmu,
                Alignment = tabStop.Alignment,
                Leader = tabStop.Leader,
            });

        foreach (var run in source.Runs)
            copy.Runs.Add(CloneRun(run));

        return copy;
    }

    private static ImagePart? CloneImagePart(ImagePart? source) =>
        source is null
            ? null
            : new ImagePart
            {
                Bytes = source.Bytes.ToArray(),
                ContentType = source.ContentType
            };

    private static InlineOleObjectInfo? CloneInlineOleObject(InlineOleObjectInfo? source) =>
        source is null
            ? null
            : new InlineOleObjectInfo
            {
                EmbeddedBytes = source.EmbeddedBytes.ToArray(),
                FileName = source.FileName,
                ClassName = source.ClassName,
            };

    internal static Run CloneRun(Run source) => new()
    {
        Text = source.Text,
        InlineImage = CloneImagePart(source.InlineImage),
        InlineImageWidthEmu = source.InlineImageWidthEmu,
        InlineImageHeightEmu = source.InlineImageHeightEmu,
        InlineOleObject = CloneInlineOleObject(source.InlineOleObject),
        InlineTable = source.InlineTable?.Clone(),
        FontFamily = source.FontFamily,
        FontSizePt = source.FontSizePt,
        BaselineOffset = source.BaselineOffset,
        Bold = source.Bold,
        Italic = source.Italic,
        BoldSet = source.BoldSet,
        ItalicSet = source.ItalicSet,
        Underline = source.Underline,
        Strikethrough = source.Strikethrough,
        RightToLeft = source.RightToLeft,
        Caps = source.Caps,
        Color = CloneThemeAwareColor(source.Color),
        Hyperlink = CloneHyperlink(source.Hyperlink),
        Field = CloneField(source.Field),
        TextFill = CloneShapeFill(source.TextFill),
        TextOutline = CloneShapeOutline(source.TextOutline),
        TextShadow = CloneRunShadow(source.TextShadow),
        TextReflection = CloneRunReflection(source.TextReflection),
        TextGlow = CloneRunGlow(source.TextGlow),
        TextSoftEdge = CloneRunSoftEdge(source.TextSoftEdge),
        Math = CloneMath(source.Math),
    };

    private static FieldRun? CloneField(FieldRun? source) =>
        source is null
            ? null
            : new FieldRun
            {
                FieldType = source.FieldType,
                CachedText = source.CachedText,
                FontFamily = source.FontFamily,
                FontSizePt = source.FontSizePt,
                Bold = source.Bold,
                Italic = source.Italic,
                Color = source.Color,
            };

    private static Hyperlink? CloneHyperlink(Hyperlink? source) =>
        source is null
            ? null
            : new Hyperlink
            {
                Url = source.Url,
                TargetSlideId = source.TargetSlideId,
                Tooltip = source.Tooltip,
            };

    private static MathRunInfo? CloneMath(MathRunInfo? source) =>
        source is null
            ? null
            : new MathRunInfo
            {
                RawXml = source.RawXml,
                IsAlternateContent = source.IsAlternateContent,
            };

    private static RunTextShadow? CloneRunShadow(RunTextShadow? source) =>
        source is null
            ? null
            : new RunTextShadow
            {
                Color = CloneThemeAwareColor(source.Color)!,
                Alpha = source.Alpha,
                BlurPt = source.BlurPt,
                DistPt = source.DistPt,
                DirDeg = source.DirDeg,
            };

    private static RunTextReflection? CloneRunReflection(RunTextReflection? source) =>
        source is null
            ? null
            : new RunTextReflection
            {
                Alpha = source.Alpha,
                BlurPt = source.BlurPt,
                DistPt = source.DistPt,
                DirDeg = source.DirDeg,
                ScaleY = source.ScaleY,
                EndPos = source.EndPos,
            };

    private static RunTextGlow? CloneRunGlow(RunTextGlow? source) =>
        source is null
            ? null
            : new RunTextGlow
            {
                Color = CloneThemeAwareColor(source.Color)!,
                Alpha = source.Alpha,
                RadiusPt = source.RadiusPt,
            };

    private static RunTextSoftEdge? CloneRunSoftEdge(RunTextSoftEdge? source) =>
        source is null
            ? null
            : new RunTextSoftEdge
            {
                RadiusPt = source.RadiusPt,
            };

    private static ThemeAwareColor? CloneThemeAwareColor(ThemeAwareColor? source) =>
        source is null
            ? null
            : source.SchemeColor is { } scheme
                ? new ThemeAwareColor(
                    source.Resolved,
                    new SchemeColorRef
                    {
                        RoleName = scheme.RoleName,
                        Slot = scheme.Slot,
                        LumMod = scheme.LumMod,
                        LumOff = scheme.LumOff,
                        Tint = scheme.Tint,
                        Shade = scheme.Shade,
                    },
                    source.Alpha)
                : new ThemeAwareColor(source.Resolved, source.Alpha);

    private static ShapeFill? CloneShapeFill(ShapeFill? source) => source switch
    {
        null => null,
        ShapeFill.None => ShapeFill.None.Instance,
        ShapeFill.Solid solid => new ShapeFill.Solid(CloneThemeAwareColor(solid.Color)!),
        ShapeFill.Gradient gradient => new ShapeFill.Gradient(
            gradient.Stops.Select(stop => new GradientStop(
                stop.Position,
                CloneThemeAwareColor(stop.Color)!)).ToArray(),
            gradient.Kind,
            gradient.AngleDegrees),
        ShapeFill.Picture picture => new ShapeFill.Picture(
            picture.ImageBytes.ToArray(),
            picture.ContentType,
            picture.Tile),
        ShapeFill.Pattern pattern => new ShapeFill.Pattern(
            pattern.Preset,
            CloneThemeAwareColor(pattern.ForegroundColor)!,
            CloneThemeAwareColor(pattern.BackgroundColor)!),
        _ => throw new NotSupportedException($"Unsupported text fill type '{source.GetType().FullName}'."),
    };

    private static ShapeOutline? CloneShapeOutline(ShapeOutline? source) => source switch
    {
        null => null,
        ShapeOutline.None => ShapeOutline.None.Instance,
        ShapeOutline.Visible visible => new ShapeOutline.Visible(
            CloneThemeAwareColor(visible.Color)!,
            visible.WidthPt,
            visible.Dash,
            CloneLineEnd(visible.BeginLineEnd),
            CloneLineEnd(visible.EndLineEnd)),
        ShapeOutline.GradientVisible gradient => new ShapeOutline.GradientVisible(
            (ShapeFill.Gradient)CloneShapeFill(gradient.Gradient)!,
            gradient.WidthPt,
            gradient.Dash,
            CloneLineEnd(gradient.BeginLineEnd),
            CloneLineEnd(gradient.EndLineEnd)),
        _ => throw new NotSupportedException($"Unsupported text outline type '{source.GetType().FullName}'."),
    };

    private static ShapeLineEnd? CloneLineEnd(ShapeLineEnd? source) =>
        source is null ? null : new ShapeLineEnd(source.Kind);

    private static TextStyleLevels? CloneTextStyleLevels(TextStyleLevels? source)
    {
        if (source is null)
            return null;

        var copy = new TextStyleLevels();
        for (int i = 0; i < 9; i++)
            copy[i] = CloneTextStyleLevel(source[i]);

        return copy;
    }

    private static TextStyleLevel? CloneTextStyleLevel(TextStyleLevel? source) =>
        source is null
            ? null
            : new TextStyleLevel
            {
                Align = source.Align,
                RightToLeft = source.RightToLeft,
                MarginLeftEmu = source.MarginLeftEmu,
                IndentEmu = source.IndentEmu,
                FontSizePt = source.FontSizePt,
                Bold = source.Bold,
                Italic = source.Italic,
                Color = source.Color,
                LatinFont = source.LatinFont,
                BulletKind = source.BulletKind,
                BulletChar = source.BulletChar,
                AutoNumType = source.AutoNumType,
                BulletColor = source.BulletColor,
                BulletColorFollowsText = source.BulletColorFollowsText,
                BulletSizePct = source.BulletSizePct,
                BulletSizePt = source.BulletSizePt,
                BulletSizeFollowsText = source.BulletSizeFollowsText,
                BulletFontFamily = source.BulletFontFamily,
                BulletFontFollowsText = source.BulletFontFollowsText,
            };

    private static bool SchemeColorRefsEqual(SchemeColorRef? a, SchemeColorRef? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;

        return a.RoleName == b.RoleName
            && a.Slot == b.Slot
            && a.LumMod == b.LumMod
            && a.LumOff == b.LumOff
            && a.Tint == b.Tint
            && a.Shade == b.Shade;
    }
}
