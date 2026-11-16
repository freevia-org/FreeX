using System.Collections.Immutable;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

/// <summary>Resolved properties of a shape text inline; absent values require host defaults.</summary>
/// <remarks>Font size is in points; baseline remains the authored DrawingML value. This model
/// describes formatting only and does not measure, shape, wrap, or position glyphs.</remarks>
public sealed record ResolvedShapeTextStyle(
    string? FontFamily,
    string? ThemeFont,
    double? FontSizePoints,
    bool? Bold,
    bool? Italic,
    string? Underline,
    int? Baseline,
    CellColor? Color,
    WorkbookThemeColorReference? ThemeColor,
    string? Language,
    DrawingObjectHyperlink? Hyperlink);

/// <summary>A source inline and its independently resolved formatting.</summary>
public sealed record ResolvedShapeTextInline(
    DrawingShapeTextInlineKind Kind,
    string Text,
    string? FieldId,
    string? FieldType,
    ResolvedShapeTextStyle Style);

/// <summary>A paragraph retaining its alignment, level, and inline boundaries.</summary>
public sealed record ResolvedShapeTextParagraph(
    DrawingShapeTextHAlign? Alignment,
    int Level,
    ResolvedShapeTextStyle DefaultStyle,
    ImmutableArray<ResolvedShapeTextInline> Inlines);

/// <summary>Resolves DrawingML text defaults without inheriting formatting from sibling runs.</summary>
public static class ShapeTextStyleResolver
{
    /// <param name="document">The structured text to resolve.</param>
    /// <param name="theme">The current workbook theme; pass it again when the theme changes.</param>
    /// <param name="baseStyle">True shape/theme defaults known by the caller. Legacy shape properties
    /// derived from the first run are not defaults for other runs and must not be supplied here.</param>
    /// <param name="baseAlignment">Alignment from the surrounding shape style, when known.</param>
    public static ImmutableArray<ResolvedShapeTextParagraph> Resolve(
        DrawingShapeTextDocument document,
        WorkbookTheme theme,
        DrawingShapeTextRunStyle? baseStyle = null,
        DrawingShapeTextHAlign? baseAlignment = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(theme);
        var result = ImmutableArray.CreateBuilder<ResolvedShapeTextParagraph>(document.Paragraphs.Count);
        foreach (var paragraph in document.Paragraphs)
        {
            var level = paragraph.Level ?? 0;
            var levelDefaults = document.ListLevels.FirstOrDefault(item => item.Level == level)?.Defaults;
            var defaults = Merge(Merge(Merge(baseStyle, document.DefaultParagraph?.RunStyle), levelDefaults?.RunStyle), paragraph.DefaultRunStyle);
            var alignment = paragraph.Alignment ?? levelDefaults?.Alignment ?? document.DefaultParagraph?.Alignment ?? baseAlignment;
            var inlines = ImmutableArray.CreateBuilder<ResolvedShapeTextInline>(paragraph.Inlines.Count);
            foreach (var inline in paragraph.Inlines)
            {
                inlines.Add(new ResolvedShapeTextInline(inline.Kind, inline.Text, inline.FieldId, inline.FieldType,
                    ResolveStyle(Merge(defaults, inline.Style), theme)));
            }
            result.Add(new ResolvedShapeTextParagraph(alignment, level, ResolveStyle(defaults, theme), inlines.MoveToImmutable()));
        }
        return result.MoveToImmutable();
    }

    private static ResolvedShapeTextStyle ResolveStyle(DrawingShapeTextRunStyle? style, WorkbookTheme theme)
    {
        var font = style?.ThemeFont switch
        {
            "+mj-lt" => theme.MajorFontName,
            "+mn-lt" => theme.MinorFontName,
            not null => null,
            _ => style?.FontFamily
        };
        return new ResolvedShapeTextStyle(font, style?.ThemeFont, style?.FontSizePoints, style?.Bold,
            style?.Italic, style?.Underline, style?.Baseline, style?.ThemeColor?.Resolve(theme) ?? style?.Color,
            style?.ThemeColor, style?.Language, style?.Hyperlink);
    }

    private static DrawingShapeTextRunStyle? Merge(DrawingShapeTextRunStyle? inherited, DrawingShapeTextRunStyle? specified)
    {
        if (specified is null) return inherited;
        if (inherited is null) return specified;
        // A font or color supplied by a closer scope replaces the entire corresponding choice.
        // In particular an explicit RGB color must not inherit a lower-scope theme color.
        var specifiesFont = specified.ThemeFont is not null || specified.FontFamily is not null;
        var specifiesColor = specified.ThemeColor is not null || specified.Color is not null;
        return new DrawingShapeTextRunStyle(
            specifiesFont ? specified.FontFamily : inherited.FontFamily,
            specifiesFont ? specified.ThemeFont : inherited.ThemeFont,
            specified.FontSizePoints ?? inherited.FontSizePoints,
            specified.Bold ?? inherited.Bold,
            specified.Italic ?? inherited.Italic,
            specified.Underline ?? inherited.Underline,
            specified.Baseline ?? inherited.Baseline,
            specifiesColor ? specified.Color : inherited.Color,
            specifiesColor ? specified.ThemeColor : inherited.ThemeColor,
            specified.Language ?? inherited.Language,
            specified.Hyperlink ?? inherited.Hyperlink);
    }
}
