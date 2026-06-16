using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// The kind of object a caption labels. Today figures and tables, mirroring Word's two built-in
/// caption labels; the enum is the single point of extension for future labels (e.g. equations).
/// </summary>
public enum CaptionLabel
{
    Figure,
    Table
}

/// <summary>
/// Pure, WPF-free helpers for document captions and sequential figure/table numbering. Lives in the
/// model project so it is fully unit-testable without any UI.
/// <para>
/// A caption is just an ordinary <see cref="Paragraph"/> carrying the <see cref="StyleId"/>
/// <see cref="StyleId"/> (a built-in <c>Caption</c> style, registered in
/// <see cref="TextDocument.CreateEmpty"/>), whose text starts with a label prefix and a 1-based
/// ordinal, e.g. <c>"Figure 1: My diagram"</c>. Because it is a plain styled paragraph it round-trips
/// through docx unchanged — no I/O changes are needed.
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

    /// <summary>The separator between a caption's number and its descriptive text.</summary>
    private const string Separator = ": ";

    /// <summary>The label word that prefixes a caption of <paramref name="label"/> (e.g. "Figure").</summary>
    public static string LabelText(CaptionLabel label) => label switch
    {
        CaptionLabel.Figure => "Figure",
        CaptionLabel.Table => "Table",
        _ => label.ToString()
    };

    /// <summary>
    /// Returns the next 1-based ordinal for a caption of <paramref name="label"/> in
    /// <paramref name="document"/>: one more than the number of existing caption paragraphs of that
    /// label. Existing captions are recognised by the <c>Caption</c> style plus a leading
    /// "<c>Figure </c>"/"<c>Table </c>" prefix, so an empty document yields 1. Deterministic and
    /// side-effect free.
    /// </summary>
    public static int NextCaptionNumber(TextDocument document, CaptionLabel label)
    {
        ArgumentNullException.ThrowIfNull(document);

        var count = 0;
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph && IsCaptionOf(paragraph, label))
                count++;
        }
        return count + 1;
    }

    /// <summary>
    /// Builds a <c>Caption</c>-styled paragraph reading "<c>{Label} {number}</c>" optionally followed
    /// by "<c>: {text}</c>" when <paramref name="text"/> is non-empty — e.g. <c>"Figure 1: My diagram"</c>
    /// or, with no text, <c>"Table 2"</c>. The number is formatted invariantly. Never mutates input.
    /// </summary>
    public static Paragraph BuildCaption(CaptionLabel label, int number, string text)
    {
        var prefix = $"{LabelText(label)} {number.ToString(CultureInfo.InvariantCulture)}";
        var trimmed = text?.Trim() ?? string.Empty;
        var full = trimmed.Length > 0 ? $"{prefix}{Separator}{trimmed}" : prefix;
        return new Paragraph(full) { StyleId = StyleId };
    }

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

    // True when the paragraph is a caption carrying the given label (style + leading "Label " prefix).
    private static bool IsCaptionOf(Paragraph paragraph, CaptionLabel label) =>
        IsCaptionStyle(paragraph.StyleId)
        && paragraph.PlainText.StartsWith(LabelText(label) + " ", StringComparison.Ordinal);
}
