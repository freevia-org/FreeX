using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Implements Excel's "AutoComplete for cell values" (Options &gt; Advanced &gt; Editing options):
/// while typing a plain text entry, offers to complete it from an existing text entry already
/// present in the contiguous block of the same column -- scanning both up and down from the cell
/// being edited, stopping at the first blank cell in each direction, exactly as Excel restricts the
/// feature to the "list" the active cell currently sits in.
/// <para>
/// Only cells holding a <see cref="TextValue"/> ever become candidates; numbers, dates, booleans,
/// formula results and errors take no part -- they neither match nor stop the contiguous scan
/// (Excel keeps looking past them for the next text entry).
/// </para>
/// </summary>
public static class CellValueAutoCompleteSuggester
{
    /// <summary>
    /// Returns the single existing column entry that <paramref name="typedText"/> is an
    /// unambiguous, case-insensitive, proper prefix of, or null when there is no match, the typed
    /// text already equals a candidate, or the match is ambiguous (two differing candidates both
    /// extend the same typed prefix) -- Excel never guesses between competing completions.
    /// </summary>
    public static string? Suggest(IReadOnlyList<string?> columnTextEntries, string typedText)
    {
        ArgumentNullException.ThrowIfNull(columnTextEntries);

        if (string.IsNullOrEmpty(typedText))
            return null;

        string? match = null;
        foreach (var candidate in columnTextEntries)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;
            // A candidate no longer than what's already typed offers nothing further to complete.
            if (candidate.Length <= typedText.Length)
                continue;
            if (!candidate.StartsWith(typedText, StringComparison.OrdinalIgnoreCase))
                continue;

            if (match is null)
                match = candidate;
            else if (!string.Equals(match, candidate, StringComparison.OrdinalIgnoreCase))
                return null; // Ambiguous: two different completions match the same typed prefix.
        }

        return match;
    }

    /// <summary>
    /// Collects the text entries from <paramref name="activeCell"/>'s column that AutoComplete
    /// draws candidates from: the contiguous run of non-blank cells immediately above and below
    /// the active cell's row (the active cell's own row is never scanned), bounded by the sheet's
    /// used range so an edit near the top of a huge, mostly-empty column doesn't walk millions of
    /// rows.
    /// </summary>
    public static IReadOnlyList<string> CollectContiguousColumnTextEntries(Sheet sheet, CellAddress activeCell)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var entries = new List<string>();
        if (sheet.GetUsedRange() is not { } used)
            return entries;

        if (activeCell.Row > used.Start.Row)
        {
            var row = activeCell.Row - 1;
            while (true)
            {
                // GetValue (not GetCell) so a spill member -- which has no Cell of its own in
                // the sheet's cell dictionary, only an entry in the spill overlay -- is seen as
                // its actual text/blank value instead of unconditionally reading as null and
                // truncating the scan (and dropping the spilled text as a candidate).
                var value = sheet.GetValue(row, activeCell.Col);
                if (value is BlankValue)
                    break;
                if (value is TextValue text)
                    entries.Add(text.Value);

                if (row <= used.Start.Row)
                    break;
                row--;
            }
        }

        for (var row = activeCell.Row + 1; row <= used.End.Row; row++)
        {
            var value = sheet.GetValue(row, activeCell.Col);
            if (value is BlankValue)
                break;
            if (value is TextValue text)
                entries.Add(text.Value);
        }

        return entries;
    }
}
