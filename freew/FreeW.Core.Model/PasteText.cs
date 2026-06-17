using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free text normalization for FreeW's "paste special" commands (Paste Text Only and
/// Merge Formatting). The editor reads the system clipboard's text and runs it through
/// <see cref="Normalize"/> before dropping it at the caret, so the helper that decides what the
/// pasted characters look like is deterministic and unit-testable in the model project.
/// </summary>
public static class PasteText
{
    /// <summary>
    /// Canonicalize raw clipboard text for insertion into the editor:
    /// <list type="bullet">
    /// <item>Line endings are normalized to a single <c>\n</c> (CRLF and lone CR both collapse to LF),
    /// matching the newline the RichTextBox insert path expects.</item>
    /// <item>Control characters are stripped, except tab (<c>\t</c>) and the normalized newline
    /// (<c>\n</c>), so invisible/garbage control codes from the source never enter the document.</item>
    /// </list>
    /// Trailing whitespace and surrounding blank lines are intentionally <em>preserved</em>: paste
    /// should reproduce the clipboard's text faithfully (just stripped of formatting and control noise),
    /// so we do not trim per line. A null input yields the empty string.
    /// </summary>
    public static string Normalize(string? clipboardText)
    {
        if (string.IsNullOrEmpty(clipboardText))
            return string.Empty;

        var result = new StringBuilder(clipboardText.Length);
        for (var i = 0; i < clipboardText.Length; i++)
        {
            var c = clipboardText[i];
            switch (c)
            {
                case '\r':
                    // Collapse CRLF and a lone CR to a single LF; skip the LF of a CRLF pair.
                    result.Append('\n');
                    if (i + 1 < clipboardText.Length && clipboardText[i + 1] == '\n')
                        i++;
                    break;
                case '\n':
                    result.Append('\n');
                    break;
                case '\t':
                    result.Append('\t');
                    break;
                default:
                    // Drop other control characters (C0 controls and DEL); keep everything else.
                    if (!char.IsControl(c))
                        result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }
}
