using System;
using System.Collections.Generic;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free implementation of Word's <em>AutoCorrect</em> tab rules — the word-completion corrections
/// that fire when a separator (space, tab, newline, or sentence punctuation) ends the word just typed:
/// <list type="bullet">
/// <item><b>Replace text as you type:</b> the just-completed word is looked up (case-insensitively) in the
///   user-editable <see cref="AutoCorrectOptions.Replacements"/> table and swapped for its replacement
///   (<c>teh</c>→<c>the</c>, <c>(c)</c>→©, <c>--&gt;</c>→→, …). The replacement preserves a leading capital
///   (<c>Teh</c>→<c>The</c>).</item>
/// <item><b>Correct TWo INitial CApitals:</b> a word that starts with two capitals followed by a lowercase
///   letter (<c>TWo</c>) has its second capital lower-cased (<c>Two</c>).</item>
/// <item><b>Capitalize names of days:</b> a lowercase weekday name (<c>monday</c>) is title-cased
///   (<c>Monday</c>).</item>
/// </list>
/// Each rule is a deterministic function of the text immediately before the caret plus the single separator
/// character that completed the word, mirroring the <see cref="AutoCorrect"/> (AutoFormat) engine's contract
/// — it returns an <see cref="AutoCorrectResult"/> so the editor's typing path can apply both engines with
/// one delete-back/insert idiom. The separator character is <em>not</em> consumed (the engine re-emits it as
/// the tail of <see cref="AutoCorrectResult.Insert"/>), so the caret lands after it exactly as if it had
/// been typed normally.
/// </summary>
public static class AutoCorrectEngine
{
    /// <summary>The English weekday names AutoCorrect capitalizes (Word's built-in day list).</summary>
    public static readonly IReadOnlyList<string> DayNames = new[]
    {
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
    };

    /// <summary>
    /// Evaluate the AutoCorrect rules for typing <paramref name="justTyped"/> at the end of
    /// <paramref name="textBefore"/> (the text immediately preceding the caret, within the current
    /// paragraph). A correction fires only when <paramref name="justTyped"/> is a word separator and the
    /// just-completed word matches a rule; otherwise <see cref="AutoCorrectResult.None"/> is returned and
    /// the keystroke proceeds normally. Disabled rules (per <paramref name="options"/>) are skipped.
    /// <para>
    /// When a correction applies, the returned result deletes the typed-word characters and inserts the
    /// corrected word followed by the just-typed separator, so the editor performs a single delete/insert
    /// edit and the caret ends after the separator.
    /// </para>
    /// </summary>
    public static AutoCorrectResult Evaluate(string? textBefore, char justTyped, AutoCorrectOptions? options)
    {
        textBefore ??= string.Empty;
        options ??= AutoCorrectOptions.Default;

        // AutoCorrect-tab rules all fire on word completion: only a separator triggers them.
        if (!IsWordSeparator(justTyped))
            return AutoCorrectResult.None;

        // The replace table is matched first (the user's authoritative corrections), against the trailing
        // token that may itself contain punctuation ("(c)", "-->", ":)"); it can't be found by the plain
        // alphabetic trailing-word scan, so we match each key as a boundary-anchored suffix of the text.
        if (options.ReplaceText
            && TryReplaceFromTable(textBefore, options.Replacements, out var matched, out var replacement))
        {
            return Correction(matched, replacement, justTyped);
        }

        // The capitalization fixes operate on the plain trailing word (letters only, no embedded symbols).
        var word = TrailingWord(textBefore);
        if (word.Length == 0)
            return AutoCorrectResult.None;

        if (options.CorrectTwoInitialCapitals && TryFixTwoInitialCaps(word, out var fixedCaps))
            return Correction(word, fixedCaps, justTyped);

        if (options.CapitalizeDayNames && TryCapitalizeDay(word, out var day))
            return Correction(word, day, justTyped);

        return AutoCorrectResult.None;
    }

    /// <summary>Convenience overload using <see cref="AutoCorrectOptions.Default"/> (every rule on).</summary>
    public static AutoCorrectResult Evaluate(string? textBefore, char justTyped) =>
        Evaluate(textBefore, justTyped, AutoCorrectOptions.Default);

    // Find the table entry whose key is a boundary-anchored, case-insensitive suffix of <textBefore> (so a
    // key may contain its own punctuation: "(c)", "-->"). The longest matching key wins, so a more specific
    // entry beats a shorter one. <matchedToken> is the exact typed text matched (its length is what the
    // editor deletes); <replacement> is the table value with leading-capital casing re-applied.
    private static bool TryReplaceFromTable(
        string textBefore, IReadOnlyList<AutoCorrectReplacement> table, out string matchedToken, out string replacement)
    {
        matchedToken = string.Empty;
        replacement = string.Empty;
        if (table is null || textBefore.Length == 0)
            return false;

        var bestLen = 0;
        foreach (var entry in table)
        {
            var key = entry?.Replace;
            if (string.IsNullOrEmpty(key) || entry!.With is null)
                continue;
            if (key.Length > textBefore.Length || key.Length <= bestLen)
                continue;
            if (!textBefore.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                continue;

            // Require a word boundary before the token so "matehs" isn't corrected by an "ehs" entry. A
            // letter/digit key must be preceded by a non-letter/digit; a key that already opens with a
            // separator (e.g. "-->", "(c)") supplies its own boundary.
            var beforeIndex = textBefore.Length - key.Length - 1;
            var keyStartsOnWordChar = char.IsLetterOrDigit(key[0]);
            if (keyStartsOnWordChar && beforeIndex >= 0)
            {
                var prev = textBefore[beforeIndex];
                if (char.IsLetterOrDigit(prev) || prev == '_')
                    continue;
            }

            var typed = textBefore[^key.Length..];
            matchedToken = typed;
            replacement = MatchLeadingCase(typed, key, entry.With);
            bestLen = key.Length;
        }
        return bestLen > 0;
    }

    /// <summary>
    /// True (with <paramref name="corrected"/> set) when <paramref name="word"/> begins with two capital
    /// letters followed by a lowercase letter (<c>TWo</c>, <c>INitial</c>) — the classic "caps-lock slip".
    /// The fix lower-cases the second capital. An all-caps word (<c>USA</c>) or a single leading capital
    /// (<c>The</c>) is left alone, as are words with an embedded digit/symbol after the caps.
    /// </summary>
    public static bool TryFixTwoInitialCaps(string word, out string corrected)
    {
        corrected = string.Empty;
        if (word.Length < 3)
            return false;
        if (!char.IsUpper(word[0]) || !char.IsUpper(word[1]) || !char.IsLower(word[2]))
            return false;
        // Guard against ALL-CAPS prefixes longer than two (e.g. "ABCd" is not a two-initial-caps slip).
        // word[2] is lowercase, so the cap run is exactly the first two — good. Lower-case the second.
        corrected = word[0] + char.ToLowerInvariant(word[1]).ToString() + word[2..];
        return true;
    }

    /// <summary>
    /// True (with <paramref name="corrected"/> set) when <paramref name="word"/> is a lowercase English
    /// weekday name; the fix title-cases it (<c>monday</c>→<c>Monday</c>). An already-capitalized day is left
    /// untouched (no correction needed).
    /// </summary>
    public static bool TryCapitalizeDay(string word, out string corrected)
    {
        corrected = string.Empty;
        foreach (var day in DayNames)
        {
            if (!string.Equals(day, word, StringComparison.OrdinalIgnoreCase))
                continue;
            // Only correct when the first letter is not already capitalized.
            if (char.IsUpper(word[0]))
                return false;
            corrected = char.ToUpperInvariant(word[0]) + word[1..];
            return true;
        }
        return false;
    }

    /// <summary>True when <paramref name="c"/> ends a word: whitespace or sentence/clause punctuation.</summary>
    public static bool IsWordSeparator(char c) =>
        char.IsWhiteSpace(c) || c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '}' or '"' or '\'';

    // Build the correction: delete the typed word (length characters), insert the corrected word followed by
    // the separator the user just typed (re-emitted so the caret ends past it, as if typing had proceeded).
    private static AutoCorrectResult Correction(string typedWord, string corrected, char separator) =>
        new(typedWord.Length, corrected + separator);

    // The trailing run of non-separator characters immediately before the caret (the word the separator just
    // ended). Empty when the caret is already at a separator (e.g. a double space).
    private static string TrailingWord(string textBefore)
    {
        var end = textBefore.Length;
        var start = end;
        while (start > 0 && !IsWordSeparator(textBefore[start - 1]))
            start--;
        return textBefore[start..end];
    }

    // Re-apply the typed word's leading-capital casing to a lowercase replacement so "Teh" → "The". Only
    // applies when the matched key is itself lowercase-leading and the replacement is lowercase-leading
    // (so symbol/arrow entries like "(c)"→© and case-significant entries are left exactly as stored).
    private static string MatchLeadingCase(string typedWord, string key, string with)
    {
        if (with.Length == 0 || typedWord.Length == 0)
            return with;
        if (key.Length == 0 || char.IsUpper(key[0]))
            return with; // key is already capitalized — honour the table's exact casing
        if (!char.IsLetter(with[0]) || char.IsUpper(with[0]))
            return with; // replacement isn't a lowercase-leading word (e.g. a glyph) — leave as-is
        if (!char.IsUpper(typedWord[0]))
            return with; // user typed it lowercase — keep the lowercase replacement
        return char.ToUpperInvariant(with[0]) + with[1..];
    }
}
