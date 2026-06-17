namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free helpers for the editor's "Show Formatting Marks" feature: the glyphs used to make
/// whitespace and paragraph boundaries visible (pilcrow <c>¶</c> for a paragraph end, middle dot
/// <c>·</c> for a space, right arrow <c>→</c> for a tab), and an <see cref="Annotate"/> routine that
/// substitutes those glyphs into a plain string for a non-editing preview.
///
/// This lives in the model project purely so the substitution logic is unit-testable without WPF. It
/// is intentionally <em>not</em> used on the live editing path — the editor draws the same glyphs as
/// non-editable overlay decorations so they never enter the document model/text. <see cref="Annotate"/>
/// is a display-only transform (e.g. for a read-only preview surface or a test): feeding its output
/// back into the model would corrupt the text, exactly as it must never do in the editor.
/// </summary>
public static class FormattingMarks
{
    /// <summary>The pilcrow glyph shown at a paragraph end (U+00B6).</summary>
    public const char Pilcrow = '¶';

    /// <summary>The middle-dot glyph shown in place of a space (U+00B7).</summary>
    public const char SpaceDot = '·';

    /// <summary>The rightwards-arrow glyph shown in place of a tab (U+2192).</summary>
    public const char TabArrow = '→';

    /// <summary>
    /// Produce a display-only annotation of <paramref name="text"/> for a single paragraph's worth of
    /// content: every space becomes <see cref="SpaceDot"/>, every tab becomes <see cref="TabArrow"/>,
    /// and a trailing <see cref="Pilcrow"/> is appended to mark the paragraph end. A null input is
    /// treated as empty (so the result is just the pilcrow).
    ///
    /// This is a preview transform only — the substituted glyphs must never be written back into the
    /// model, mirroring how the editor keeps the marks as overlay decorations rather than real runs.
    /// </summary>
    public static string Annotate(string? text)
    {
        var source = text ?? string.Empty;
        var builder = new System.Text.StringBuilder(source.Length + 1);
        foreach (var c in source)
        {
            builder.Append(c switch
            {
                ' ' => SpaceDot,
                '\t' => TabArrow,
                _ => c
            });
        }
        builder.Append(Pilcrow);
        return builder.ToString();
    }
}
