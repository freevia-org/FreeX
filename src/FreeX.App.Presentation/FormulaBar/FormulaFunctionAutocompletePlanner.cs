namespace FreeX.App.Presentation.FormulaBar;

/// <summary>
/// Portable, UI-free planner behind Excel's "function name AutoComplete" dropdown: while typing a
/// formula, as soon as the caret sits inside an identifier-shaped token (<c>=SU</c>), this filters
/// the built-in function list (plus any defined names / table names the caller supplies) down to the
/// candidates that start with the typed prefix, and can commit the chosen candidate back into the
/// formula text with the trailing opening parenthesis Excel always inserts on Tab/Enter.
/// </summary>
/// <remarks>
/// Deliberately has no dependency on <c>BuiltInFunctions</c> or any shell/UI-framework type: callers pass in
/// whatever name sequences are relevant (function names, defined names, table names) so this planner
/// stays reusable from either shell's formula editor and is directly unit-testable without a live
/// window. See <see cref="FormulaSignatureHelpPlanner"/> for the companion "live argument tooltip"
/// planner that activates once the user commits past the opening parenthesis.
/// </remarks>
public static class FormulaFunctionAutocompletePlanner
{
    private const int DefaultCandidateLimit = 50;

    /// <summary>
    /// Locates the identifier-shaped token immediately to the left of the caret, if any. An
    /// "identifier" character is a letter, digit, underscore, period, or backslash -- the character
    /// set Excel allows in function names, defined names, and table names. Returns false when the
    /// caret is not inside/just after such a token (e.g. right after an operator, a space, or an
    /// opening parenthesis) -- callers use this to decide the AutoComplete popup should be dismissed.
    /// </summary>
    public static bool TryGetActiveToken(string? text, int caretIndex, out int tokenStart, out int tokenLength)
    {
        tokenStart = 0;
        tokenLength = 0;
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
            return false;

        var index = caretIndex;
        while (index > 0 && IsIdentifierChar(text[index - 1]))
            index--;

        if (index == caretIndex)
            return false;

        // A token starting with a digit (e.g. a plain number, or a cell reference like "A1") is not
        // a candidate for function-name completion -- Excel does not offer AutoComplete for those.
        if (char.IsDigit(text[index]))
            return false;

        tokenStart = index;
        tokenLength = caretIndex - index;
        return true;
    }

    /// <summary>
    /// Whether the AutoComplete popup should be showing for the given formula text/caret position,
    /// out-parameter the active token's bounds and text when it should. Requires formula context
    /// (text starting with "=") since Excel never offers function-name completion in a plain value.
    /// </summary>
    public static bool ShouldShowAutocomplete(
        string? text,
        int caretIndex,
        out int tokenStart,
        out int tokenLength,
        out string prefix)
    {
        prefix = "";
        if (string.IsNullOrEmpty(text) || !text.StartsWith("=", StringComparison.Ordinal))
        {
            tokenStart = 0;
            tokenLength = 0;
            return false;
        }

        if (!TryGetActiveToken(text, caretIndex, out tokenStart, out tokenLength))
            return false;

        prefix = text.Substring(tokenStart, tokenLength);
        return prefix.Length > 0;
    }

    /// <summary>
    /// Builds the filtered, alphabetically sorted candidate list for the typed prefix: every supplied
    /// name (function names, defined names, table names -- combined and de-duplicated case-
    /// insensitively) that starts with the prefix, ordinal-ignore-case, capped at <paramref
    /// name="limit"/> entries to keep the popup a fixed-height Excel-style list rather than an
    /// unbounded scroll for a one- or two-letter prefix.
    /// </summary>
    public static IReadOnlyList<string> BuildCandidates(
        string prefix,
        IEnumerable<string>? functionNames,
        IEnumerable<string>? definedNames = null,
        IEnumerable<string>? tableNames = null,
        int limit = DefaultCandidateLimit)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix.Length == 0)
            return [];

        var all = new List<string>();
        if (functionNames is not null) all.AddRange(functionNames);
        if (definedNames is not null) all.AddRange(definedNames);
        if (tableNames is not null) all.AddRange(tableNames);

        return all
            .Where(name => !string.IsNullOrEmpty(name) &&
                           name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(limit, 0))
            .ToArray();
    }

    /// <summary>
    /// Commits the chosen candidate: replaces the typed prefix token with <paramref
    /// name="chosenName"/>, and returns the new full text plus the caret index that lands right after
    /// the inserted name. When <paramref name="isFunction"/> is true the name is followed by an
    /// opening parenthesis (matching Excel's Tab/Enter behavior for callable functions); when false
    /// (a defined name or structured-table name -- never callable) the bare name is inserted with no
    /// trailing "(", since appending one would produce a syntactically broken formula (e.g.
    /// "=SalesTotal(" instead of "=SalesTotal"). Use <see cref="IsFunctionCandidate"/> to determine
    /// this flag from the same <c>functionNames</c> sequence passed to <see cref="BuildCandidates"/>.
    /// </summary>
    public static (string Text, int CaretIndex) Commit(
        string text,
        int tokenStart,
        int tokenLength,
        string chosenName,
        bool isFunction)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(chosenName);
        if (tokenStart < 0 || tokenLength < 0 || tokenStart + tokenLength > text.Length)
            throw new ArgumentOutOfRangeException(nameof(tokenStart));

        var replacement = isFunction ? chosenName + "(" : chosenName;
        var newText = string.Concat(
            text.AsSpan(0, tokenStart),
            replacement,
            text.AsSpan(tokenStart + tokenLength));
        return (newText, tokenStart + replacement.Length);
    }

    /// <summary>
    /// Whether <paramref name="candidateName"/> is one of the supplied built-in function names (as
    /// opposed to a defined name or structured-table name that merely happened to share the
    /// AutoComplete candidate list). Callers pass the same <paramref name="functionNames"/> sequence
    /// given to <see cref="BuildCandidates"/> so <see cref="Commit"/> knows whether to append the
    /// callable "(" -- Excel never appends one for a plain name reference.
    /// </summary>
    public static bool IsFunctionCandidate(string candidateName, IEnumerable<string>? functionNames)
    {
        ArgumentNullException.ThrowIfNull(candidateName);
        if (functionNames is null)
            return false;

        foreach (var name in functionNames)
        {
            if (string.Equals(name, candidateName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Steps the selected candidate index by <paramref name="delta"/> (+1 for Down, -1 for Up),
    /// wrapping around at either end -- matching the arrow-key navigation of Excel's own popup.
    /// Returns -1 (no selection) when there are no candidates.
    /// </summary>
    public static int MoveSelection(int currentIndex, int candidateCount, int delta)
    {
        if (candidateCount <= 0)
            return -1;

        var baseIndex = currentIndex < 0 ? (delta > 0 ? -1 : 0) : currentIndex;
        var next = (baseIndex + delta) % candidateCount;
        if (next < 0)
            next += candidateCount;
        return next;
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '.' or '\\';
}
