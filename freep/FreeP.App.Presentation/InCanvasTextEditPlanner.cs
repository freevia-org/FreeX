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

public readonly record struct InCanvasTextEditDecision(
    InCanvasTextEditOutcome Outcome,
    IPresentationCommand? Command);

public sealed record InCanvasTextEditStartPlan(
    InCanvasTextEditStartStatus Status,
    uint ShapeId,
    InCanvasTextEditKind Kind,
    InCanvasEditorPlacement? Placement,
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

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape is null)
            return NotReady(InCanvasTextEditStartStatus.ShapeNotFound, shapeId, kind);
        if (shape.TextBody is null)
            return NotReady(InCanvasTextEditStartStatus.MissingTextBody, shapeId, kind);

        var screenRect = SlideCanvasGeometryPlanner.ShapeBoundsToScreen(shape, presentation, transform);
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            screenRect,
            minimumWidth,
            minimumHeight);
        var planner = kind == InCanvasTextEditKind.RichText
            ? BeginRichText(slideIndex, shapeId, shape.TextBody)
            : BeginPlainText(slideIndex, shapeId, shape.TextBody);
        var originalBody = TextBodyModelCloner.CloneTextBody(shape.TextBody);

        return new InCanvasTextEditStartPlan(
            InCanvasTextEditStartStatus.Ready,
            shapeId,
            kind,
            placement,
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

    private static InCanvasTextEditStartPlan NotReady(
        InCanvasTextEditStartStatus status,
        uint shapeId,
        InCanvasTextEditKind kind) =>
        new(status, shapeId, kind, null, null, string.Empty, null);

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
            if (compareParagraphAlignment && pa.Align != pb.Align)
                return false;

            for (int ri = 0; ri < pa.Runs.Count; ri++)
            {
                var ra = pa.Runs[ri];
                var rb = pb.Runs[ri];
                if (ra.Text != rb.Text
                    || ra.Bold != rb.Bold
                    || ra.Italic != rb.Italic
                    || ra.Underline != rb.Underline
                    || ra.Strikethrough != rb.Strikethrough
                    || ra.FontFamily != rb.FontFamily
                    || ra.FontSizePt != rb.FontSizePt
                    || !TextBodyModelCloner.ColorsEqual(ra.Color, rb.Color))
                    return false;
            }
        }

        return true;
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

        return presentation.Slides[_slideIndex].Shapes.FirstOrDefault(s => s.Id == _shapeId);
    }
}

internal static class TextBodyModelCloner
{
    internal static TextBody? CloneTextBody(TextBody? source)
    {
        if (source is null)
            return null;

        var copy = new TextBody
        {
            Anchor = source.Anchor,
            DefaultParaAlign = source.DefaultParaAlign,
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

    private static Paragraph CloneParagraph(Paragraph source)
    {
        var copy = new Paragraph
        {
            Align = source.Align,
            Level = source.Level,
            BulletKind = source.BulletKind,
            BulletSuppressed = source.BulletSuppressed,
            BulletChar = source.BulletChar,
            AutoNumType = source.AutoNumType,
            AutoNumStartAt = source.AutoNumStartAt,
            MarginLeftEmu = source.MarginLeftEmu,
            IndentEmu = source.IndentEmu,
            BulletColor = source.BulletColor,
            BulletSizePct = source.BulletSizePct,
            BulletFontFamily = source.BulletFontFamily,
            SpaceBeforePt = source.SpaceBeforePt,
            SpaceAfterPt = source.SpaceAfterPt,
        };

        foreach (var tabStop in source.TabStops)
            copy.TabStops.Add(new TabStop
            {
                PositionEmu = tabStop.PositionEmu,
                Alignment = tabStop.Alignment,
            });

        foreach (var run in source.Runs)
            copy.Runs.Add(CloneRun(run));

        return copy;
    }

    private static Run CloneRun(Run source) => new()
    {
        Text = source.Text,
        FontFamily = source.FontFamily,
        FontSizePt = source.FontSizePt,
        Bold = source.Bold,
        Italic = source.Italic,
        BoldSet = source.BoldSet,
        ItalicSet = source.ItalicSet,
        Underline = source.Underline,
        Strikethrough = source.Strikethrough,
        Color = source.Color,
        Hyperlink = CloneHyperlink(source.Hyperlink),
        Field = CloneField(source.Field),
        TextFill = source.TextFill,
        TextOutline = source.TextOutline,
        TextShadow = CloneRunShadow(source.TextShadow),
        TextReflection = CloneRunReflection(source.TextReflection),
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
                Color = source.Color,
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
            };

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
                BulletSizePct = source.BulletSizePct,
                BulletFontFamily = source.BulletFontFamily,
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
