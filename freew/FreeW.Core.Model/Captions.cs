using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The kind of object a caption labels. Covers Word's common built-in labels while string overloads
/// handle custom labels.
/// </summary>
public enum CaptionLabel
{
    Figure,
    Table,
    Equation
}

/// <summary>
/// Pure, WPF-free helpers for document captions and sequential caption numbering. Lives in the
/// model project so it is fully unit-testable without any UI.
/// <para>
/// A caption is a <see cref="Paragraph"/> carrying the built-in <see cref="StyleId"/> style whose
/// visible text starts with a label prefix and a 1-based ordinal, e.g.
/// <c>"Figure 1: My diagram"</c>. The ordinal is authored as a native Word <c>SEQ</c> complex field,
/// with the supplied number retained as its cached result.
/// </para>
/// <para>
/// Numbering is computed by counting the captions of the same label already present in a document
/// (recognised by the <c>Caption</c> style plus the label's leading prefix) and returning the next
/// ordinal. The helpers are deterministic and side-effect free — they never mutate the document.
/// </para>
/// </summary>
public static class Captions
{
    /// <summary>The style id (and name) of the built-in caption paragraph style.</summary>
    public const string StyleId = "Caption";

    public const string FigureLabelText = "Figure";
    public const string TableLabelText = "Table";
    public const string EquationLabelText = "Equation";

    public static readonly IReadOnlyList<string> BuiltInLabelTexts =
    [
        FigureLabelText,
        TableLabelText,
        EquationLabelText
    ];

    /// <summary>The separator between a caption's number and its descriptive text.</summary>
    private const string Separator = ": ";

    /// <summary>The label word that prefixes a caption of <paramref name="label"/> (e.g. "Figure").</summary>
    public static string LabelText(CaptionLabel label) => label switch
    {
        CaptionLabel.Figure => FigureLabelText,
        CaptionLabel.Table => TableLabelText,
        CaptionLabel.Equation => EquationLabelText,
        _ => label.ToString()
    };

    public static string NormalizeLabelText(string labelText)
    {
        var normalized = labelText?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ArgumentException("Caption label text cannot be empty.", nameof(labelText));
        return normalized;
    }

    /// <summary>
    /// Returns the next 1-based ordinal for a caption of <paramref name="label"/> in
    /// <paramref name="document"/>: one more than the number of existing caption paragraphs of that
    /// label. Existing captions are recognised by the <c>Caption</c> style plus a leading
    /// "<c>Figure </c>"/"<c>Table </c>" prefix, so an empty document yields 1. Deterministic and
    /// side-effect free.
    /// </summary>
    public static int NextCaptionNumber(TextDocument document, CaptionLabel label)
    {
        return NextCaptionNumber(document, LabelText(label));
    }

    /// <summary>
    /// Returns the next 1-based ordinal for a caption with the given label text. This supports Word's
    /// built-in labels plus custom labels created through Insert Caption > New Label.
    /// </summary>
    public static int NextCaptionNumber(TextDocument document, string labelText)
    {
        ArgumentNullException.ThrowIfNull(document);
        var label = NormalizeLabelText(labelText);

        var count = DocumentBodyParagraphs.Enumerate(document)
            .Count(location => IsCaptionOf(location.Paragraph, label));
        return count + 1;
    }

    /// <summary>
    /// Returns the next 1-based ordinal for a caption of <paramref name="label"/> that will be inserted
    /// as a new top-level block at <paramref name="insertionBlockIndex"/>. Unlike the position-agnostic
    /// overload, this counts only the existing captions of that label which appear <em>before</em> the
    /// insertion point (i.e. whose top-level block index is less than <paramref name="insertionBlockIndex"/>),
    /// so inserting a caption between two existing captions numbers it for its own position instead of
    /// appending it after every caption already in the document.
    /// </summary>
    public static int NextCaptionNumber(TextDocument document, CaptionLabel label, int insertionBlockIndex)
    {
        return NextCaptionNumber(document, LabelText(label), insertionBlockIndex);
    }

    /// <summary>
    /// Returns the next 1-based ordinal for a caption with the given label text that will be inserted as
    /// a new top-level block at <paramref name="insertionBlockIndex"/>. See the <see cref="CaptionLabel"/>
    /// overload for the position-aware counting rule.
    /// </summary>
    public static int NextCaptionNumber(TextDocument document, string labelText, int insertionBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        var label = NormalizeLabelText(labelText);

        var count = DocumentBodyParagraphs.Enumerate(document)
            .Count(location => location.BlockIndex < insertionBlockIndex && IsCaptionOf(location.Paragraph, label));
        return count + 1;
    }

    /// <summary>
    /// Builds a <c>Caption</c>-styled paragraph reading "<c>{Label} {number}</c>" optionally followed
    /// by "<c>: {text}</c>" when <paramref name="text"/> is non-empty. The number is the cached result
    /// of a native <c>SEQ</c> field and is formatted invariantly. Never mutates input.
    /// </summary>
    public static Paragraph BuildCaption(CaptionLabel label, int number, string text)
    {
        return BuildCaption(LabelText(label), number, text);
    }

    /// <summary>
    /// Builds a <c>Caption</c>-styled paragraph for a built-in or custom caption label.
    /// </summary>
    public static Paragraph BuildCaption(string labelText, int number, string text)
    {
        var label = NormalizeLabelText(labelText);
        var trimmed = text?.Trim() ?? string.Empty;
        var paragraph = new Paragraph { StyleId = StyleId };
        paragraph.Runs.Add(new Run(label + " "));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            SequenceInstructionFor(label),
            number.ToString(CultureInfo.InvariantCulture)));
        if (trimmed.Length > 0)
            paragraph.Runs.Add(new Run(Separator + trimmed));
        return paragraph;
    }

    /// <summary>The native Word sequence instruction used by captions of <paramref name="labelText"/>.</summary>
    public static string SequenceInstructionFor(string labelText)
    {
        var label = NormalizeLabelText(labelText);
        var argument = label.Any(char.IsWhiteSpace) || label.IndexOfAny(['"', '\\']) >= 0
            ? $"\"{EscapeFieldArgument(label)}\""
            : label;
        return $" SEQ {argument} \\* ARABIC ";
    }

    internal static string EscapeFieldArgument(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="block"/> is a paragraph carrying the <c>Caption</c> style. Used to
    /// recognise a caption region regardless of its label.
    /// </summary>
    public static bool IsCaptionParagraph(Block block) =>
        block is Paragraph paragraph && IsCaptionStyle(paragraph.StyleId);

    /// <summary>
    /// Registers the built-in <c>Caption</c> style in <paramref name="document"/>'s style catalog if not
    /// already present, so inserted caption paragraphs resolve their formatting. Idempotent.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Styles.TryAdd(StyleId, BuildCaptionStyle());
    }

    /// <summary>The built-in caption style definition (also used by <see cref="TextDocument"/>'s built-ins).</summary>
    internal static DocumentStyle BuildCaptionStyle() => new()
    {
        Id = StyleId,
        Name = "Caption",
        BasedOnStyleId = "Normal",
        // A small italic, muted caption, mirroring Word's built-in Caption style.
        Run = new RunFormatting { Italic = true, FontSizePt = 9, ColorHex = "#44546A" },
        Paragraph = new ParagraphFormatting { SpaceBeforePt = 6, SpaceAfterPt = 10 }
    };

    private static bool IsCaptionStyle(string? styleId) =>
        string.Equals(styleId, StyleId, StringComparison.Ordinal);

    public static bool IsCaptionOf(Paragraph paragraph, CaptionLabel label) =>
        IsCaptionOf(paragraph, LabelText(label));

    // True when the paragraph is a caption carrying the given label (style + leading "Label " prefix).
    public static bool IsCaptionOf(Paragraph paragraph, string labelText) =>
        IsCaptionStyle(paragraph.StyleId)
        && paragraph.PlainText.StartsWith(NormalizeLabelText(labelText) + " ", StringComparison.Ordinal);
}
