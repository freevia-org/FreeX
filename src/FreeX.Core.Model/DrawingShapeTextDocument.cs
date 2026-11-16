using System.Collections.Immutable;

namespace FreeX.Core.Model;

/// <summary>Content kinds within one shape-text paragraph.</summary>
public enum DrawingShapeTextInlineKind { Run, Field, Break }

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
    DrawingObjectHyperlink? Hyperlink = null);

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

    public DrawingShapeTextParagraph(IEnumerable<DrawingShapeTextInline> inlines, DrawingShapeTextHAlign? alignment = null)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        _inlines = ImmutableArray.CreateRange(inlines);
        if (_inlines.Any(item => item is null)) throw new ArgumentException("Paragraph nodes cannot be null.", nameof(inlines));
        Alignment = alignment;
    }

    public IReadOnlyList<DrawingShapeTextInline> Inlines => _inlines;
    public DrawingShapeTextHAlign? Alignment { get; }
    internal string? SourceXml { get; init; }
}

/// <summary>Immutable structured shape text and its legacy plain-text projection.</summary>
public sealed class DrawingShapeTextDocument
{
    private readonly ImmutableArray<DrawingShapeTextParagraph> _paragraphs;

    public DrawingShapeTextDocument(IEnumerable<DrawingShapeTextParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        _paragraphs = ImmutableArray.CreateRange(paragraphs);
        if (_paragraphs.Any(item => item is null)) throw new ArgumentException("Paragraphs cannot be null.", nameof(paragraphs));
        PlainText = string.Join("\n", _paragraphs.Select(paragraph => string.Concat(paragraph.Inlines.Select(node =>
            node.Kind == DrawingShapeTextInlineKind.Break ? "\n" : node.Text))));
    }

    public IReadOnlyList<DrawingShapeTextParagraph> Paragraphs => _paragraphs;
    public string PlainText { get; }
    internal string? SourceXml { get; init; }
    internal Guid SourceOwnershipId { get; } = Guid.NewGuid();
}
