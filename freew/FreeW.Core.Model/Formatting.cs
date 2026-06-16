namespace FreeW.Core.Model;

/// <summary>Paragraph horizontal alignment.</summary>
public enum TextAlignment { Left, Center, Right, Justify }

/// <summary>List decoration for a paragraph.</summary>
public enum ListKind { None, Bullet, Number }

/// <summary>
/// Immutable character formatting for a run. Null members inherit from the paragraph style /
/// document default, mirroring how Word resolves run properties (rPr).
/// </summary>
public sealed record RunFormatting
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSizePt { get; init; }
    public string? ColorHex { get; init; }

    public static readonly RunFormatting Default = new();
}

/// <summary>
/// Immutable paragraph formatting (pPr): alignment, spacing, indents, list. Points throughout,
/// matching the docx unit model once divided/multiplied by the OOXML twentieths.
/// </summary>
public sealed record ParagraphFormatting
{
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; } = 8;
    public double LineSpacing { get; init; } = 1.15;
    public double IndentLeftPt { get; init; }
    public double IndentRightPt { get; init; }
    public double FirstLineIndentPt { get; init; }
    public ListKind ListKind { get; init; } = ListKind.None;
    public int ListLevel { get; init; }

    public static readonly ParagraphFormatting Default = new();
}
