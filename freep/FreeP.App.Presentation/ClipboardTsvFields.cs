using System.Runtime.InteropServices;
using System.Text;

namespace FreeP.App.Compositor;

/// <summary>
/// Quote-aware field splitting for the tab-delimited plain text FreeX's clipboard serializer
/// writes (<c>ClipboardSerializer.AppendTsvCell</c> / <c>RequiresTsvQuoting</c> in
/// FreeX.Core.Commands), which wraps any cell containing a tab, a quote, a CR or an LF in
/// RFC4180 quotes. A tab inside a genuinely quoted field is cell content, not a column boundary.
/// <para>
/// r172 follow-up: <see cref="ClipboardTablePlanner"/> already reconstructed such a cell when
/// splitting a row body, but <see cref="PresentationClipboardContent.HasTabularText"/> counted
/// columns with a raw <c>'\t'</c> split, so a range whose cell contained a literal tab produced a
/// mismatched field count for that row and was rejected as non-tabular -- pasting as the flat
/// tab-riddled text box that the shape check exists to prevent. Both callers now share this one
/// implementation instead of keeping two closely related quote scanners in two files.
/// </para>
/// </summary>
internal static class ClipboardTsvFields
{
    /// <summary>
    /// Mirrors <c>ClipboardSerializer.IsProperlyQuotedField</c>: the quote at
    /// <paramref name="quoteIndex"/> (already known to be the first character of a field) opens
    /// genuine RFC4180 quoting only if scanning forward -- treating a doubled quote as an escaped
    /// literal -- reaches a closing quote immediately followed by the next tab or the end of the
    /// row. Otherwise it is a literal quote character (typed by a user, or produced by a rich-text
    /// source that never CSV-quotes its cells) and must be preserved as data rather than consumed
    /// as CSV syntax.
    /// </summary>
    internal static bool OpensQuotedField(List<char> chars, int quoteIndex) =>
        OpensQuotedField(CollectionsMarshal.AsSpan(chars), quoteIndex);

    /// <inheritdoc cref="OpensQuotedField(List{char}, int)"/>
    internal static bool OpensQuotedField(ReadOnlySpan<char> chars, int quoteIndex)
    {
        for (var i = quoteIndex + 1; i < chars.Length; i++)
        {
            if (chars[i] != '"')
                continue;

            if (i + 1 < chars.Length && chars[i + 1] == '"')
            {
                i++;
                continue;
            }

            var next = i + 1;
            return next >= chars.Length || chars[next] == '\t';
        }

        return false;
    }

    /// <summary>
    /// Splits one row into its unquoted field values on tab boundaries, honoring the quoting rule
    /// above: a tab inside a quoted field stays in the field's text, a doubled quote collapses to a
    /// single literal quote, and the field's own wrapping quotes are dropped. Always returns at
    /// least one field.
    /// </summary>
    internal static List<string> SplitFields(string row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;

        for (var i = 0; i < row.Length; i++)
        {
            var character = row[i];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (i + 1 < row.Length && row[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = false;
                    atFieldStart = false;
                    continue;
                }

                field.Append(character);
                continue;
            }

            if (character == '"' && atFieldStart && OpensQuotedField(row.AsSpan(), i))
            {
                inQuotes = true;
                atFieldStart = false;
                continue;
            }

            if (character == '\t')
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                continue;
            }

            field.Append(character);
            atFieldStart = false;
        }

        fields.Add(field.ToString());
        return fields;
    }
}
