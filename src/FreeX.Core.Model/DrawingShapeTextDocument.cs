using System.Collections.Immutable;

namespace FreeX.Core.Model;

/// <summary>Content kinds within one shape-text paragraph.</summary>
public enum DrawingShapeTextInlineKind { Run, Field, Break }

/// <summary>Explicit non-solid or otherwise unsupported DrawingML text fill.</summary>
public enum DrawingShapeTextFillKind { NoFill, Unsupported }

/// <summary>A fill override; unsupported fills retain their original DrawingML element.</summary>
public sealed record DrawingShapeTextFillOverride(DrawingShapeTextFillKind Kind, string? SourceXml = null);

/// <summary>Explicit run properties. Null values inherit their formatting context.</summary>
public sealed record DrawingShapeTextRunStyle(
    string? FontFamily = null,
    string? ThemeFont = null,
    double? FontSizePoints = null,
    bool? Bold = null,
    bool? Italic = null,
    string? Underline = null,
    int? Baseline = null,
    CellColor? Color = null,
    WorkbookThemeColorReference? ThemeColor = null,
    string? Language = null,
    DrawingObjectHyperlink? Hyperlink = null)
{
    /// <summary>Explicit no-fill or a fill that cannot yet be resolved as a solid color.</summary>
    public DrawingShapeTextFillOverride? FillOverride { get; init; }
}

/// <summary>Text defaults for a DrawingML list level or its default paragraph properties.</summary>
public sealed record DrawingShapeTextParagraphDefaults(
    DrawingShapeTextHAlign? Alignment = null,
    DrawingShapeTextRunStyle? RunStyle = null);

/// <summary>Defaults for a zero-based DrawingML paragraph level (0 through 8).</summary>
public sealed record DrawingShapeTextListLevel(int Level, DrawingShapeTextParagraphDefaults Defaults);

/// <summary>One text run, field, or explicit line break.</summary>
public sealed class DrawingShapeTextInline
{
    public DrawingShapeTextInline(DrawingShapeTextInlineKind kind, string? text = null, string? fieldId = null,
        string? fieldType = null, DrawingShapeTextRunStyle? style = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == DrawingShapeTextInlineKind.Break && (!string.IsNullOrEmpty(text) || fieldId is not null || fieldType is not null))
            throw new ArgumentException("A line break cannot contain text or field metadata.", nameof(kind));
        if (kind != DrawingShapeTextInlineKind.Field && (fieldId is not null || fieldType is not null))
            throw new ArgumentException("Field metadata requires a field node.", nameof(kind));
        Kind = kind;
        Text = kind == DrawingShapeTextInlineKind.Break ? "" : text ?? "";
        FieldId = fieldId;
        FieldType = fieldType;
        Style = style;
    }

    public DrawingShapeTextInlineKind Kind { get; }
    public string Text { get; }
    public string? FieldId { get; }
    public string? FieldType { get; }
    public DrawingShapeTextRunStyle? Style { get; }
    internal string? SourceXml { get; init; }
}

/// <summary>An immutable paragraph, including explicit line breaks and fields.</summary>
public sealed class DrawingShapeTextParagraph
{
    private readonly ImmutableArray<DrawingShapeTextInline> _inlines;

    public DrawingShapeTextParagraph(IEnumerable<DrawingShapeTextInline> inlines, DrawingShapeTextHAlign? alignment = null,
        DrawingShapeTextRunStyle? defaultRunStyle = null, int? level = null)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        if (level is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(level));
        _inlines = ImmutableArray.CreateRange(inlines);
        if (_inlines.Any(item => item is null)) throw new ArgumentException("Paragraph nodes cannot be null.", nameof(inlines));
        Alignment = alignment;
        DefaultRunStyle = defaultRunStyle;
        Level = level;
    }

    public IReadOnlyList<DrawingShapeTextInline> Inlines => _inlines;
    public DrawingShapeTextHAlign? Alignment { get; }
    public DrawingShapeTextRunStyle? DefaultRunStyle { get; }
    public int? Level { get; }
    internal string? SourceXml { get; init; }
}

/// <summary>Immutable structured shape text and its legacy plain-text projection.</summary>
public sealed class DrawingShapeTextDocument
{
    private readonly ImmutableArray<DrawingShapeTextParagraph> _paragraphs;
    private readonly ImmutableArray<DrawingShapeTextListLevel> _listLevels;

    public DrawingShapeTextDocument(IEnumerable<DrawingShapeTextParagraph> paragraphs,
        DrawingShapeTextParagraphDefaults? defaultParagraph = null,
        IEnumerable<DrawingShapeTextListLevel>? listLevels = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        _paragraphs = ImmutableArray.CreateRange(paragraphs);
        if (_paragraphs.Any(item => item is null)) throw new ArgumentException("Paragraphs cannot be null.", nameof(paragraphs));
        _listLevels = listLevels is null ? [] : ImmutableArray.CreateRange(listLevels);
        if (_listLevels.Any(item => item is null || item.Defaults is null || item.Level is < 0 or > 8) ||
            _listLevels.Select(item => item.Level).Distinct().Count() != _listLevels.Length)
            throw new ArgumentException("List levels must be unique, non-null, and between 0 and 8.", nameof(listLevels));
        DefaultParagraph = defaultParagraph;
        PlainText = string.Join("\n", _paragraphs.Select(paragraph => string.Concat(paragraph.Inlines.Select(node =>
            node.Kind == DrawingShapeTextInlineKind.Break ? "\n" : node.Text))));
    }

    public IReadOnlyList<DrawingShapeTextParagraph> Paragraphs => _paragraphs;
    public DrawingShapeTextParagraphDefaults? DefaultParagraph { get; }
    public IReadOnlyList<DrawingShapeTextListLevel> ListLevels => _listLevels;
    public string PlainText { get; }
    internal string? SourceXml { get; init; }
    internal Guid SourceOwnershipId { get; init; } = Guid.NewGuid();

    /// <summary>Rewrites hyperlinks without changing text, formatting, or retained source XML.</summary>
    public DrawingShapeTextDocument RewriteHyperlinks(Func<DrawingObjectHyperlink, DrawingObjectHyperlink> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        var changed = false;
        DrawingShapeTextRunStyle? RewriteStyle(DrawingShapeTextRunStyle? style)
        {
            if (style?.Hyperlink is not { } link) return style;
            var updated = rewrite(link) ?? throw new InvalidOperationException("A shape text hyperlink cannot be null.");
            if (updated == link) return style;
            changed = true;
            return style with { Hyperlink = updated };
        }
        DrawingShapeTextParagraphDefaults? RewriteDefaults(DrawingShapeTextParagraphDefaults? defaults) =>
            defaults is null ? null : defaults with { RunStyle = RewriteStyle(defaults.RunStyle) };
        var defaultParagraph = RewriteDefaults(DefaultParagraph);
        var levels = ListLevels.Select(level => level with { Defaults = RewriteDefaults(level.Defaults)! }).ToArray();
        var paragraphs = Paragraphs.Select(paragraph =>
            new DrawingShapeTextParagraph(paragraph.Inlines.Select(inline =>
                new DrawingShapeTextInline(inline.Kind, inline.Text, inline.FieldId, inline.FieldType, RewriteStyle(inline.Style))
                    { SourceXml = inline.SourceXml }), paragraph.Alignment, RewriteStyle(paragraph.DefaultRunStyle), paragraph.Level)
                { SourceXml = paragraph.SourceXml }).ToArray();
        return changed ? new DrawingShapeTextDocument(paragraphs, defaultParagraph, levels)
            { SourceXml = SourceXml, SourceOwnershipId = SourceOwnershipId } : this;
    }
}
