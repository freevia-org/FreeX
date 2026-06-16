namespace FreeW.Core.Model;

/// <summary>
/// The outcome of an as-you-type AutoCorrect rule: replace the <see cref="DeleteBefore"/> characters
/// immediately before the caret (NOT counting the just-typed character, which has not yet been inserted)
/// plus the just-typed character with <see cref="Insert"/>.
/// <para>
/// The convention is "the just-typed character is consumed by the rule". For example, when the user
/// types <c>-</c> right after an existing <c>-</c>, the rule reports <c>DeleteBefore = 1</c> (the prior
/// dash) and <c>Insert = "–"</c> (the en dash that replaces both dashes). When the rule only transforms
/// the just-typed character itself (e.g. a straight quote becomes a curly quote), <c>DeleteBefore = 0</c>
/// and <c>Insert</c> is the replacement character.
/// </para>
/// <see cref="None"/> means "no correction; let the keystroke proceed normally".
/// </summary>
public readonly record struct AutoCorrectResult(int DeleteBefore, string Insert)
{
    /// <summary>No correction applies; the keystroke should proceed unmodified.</summary>
    public static readonly AutoCorrectResult None = new(-1, string.Empty);

    /// <summary>True when a correction applies (i.e. this is not <see cref="None"/>).</summary>
    public bool Applies => DeleteBefore >= 0;
}

/// <summary>
/// Pure, WPF-free as-you-type text corrections (a.k.a. smart typing / AutoFormat). Every rule is a
/// deterministic function of the text immediately before the caret plus the single character the user
/// just typed, so each can be unit-tested in isolation without an editor.
///
/// <para>Rules (all applied on the triggering keystroke only):</para>
/// <list type="bullet">
/// <item>Smart quotes: a straight double (<c>"</c>) or single (<c>'</c>) quote becomes a curly quote.
///   It opens (<c>“</c>/<c>‘</c>) when preceded by the start of text, whitespace, or an opening
///   punctuation character; otherwise it closes (<c>”</c>/<c>’</c>).</item>
/// <item><c>--</c> (two hyphens) becomes an en dash <c>–</c> (U+2013). We choose the en dash because it
///   is the conventional AutoCorrect result for a double hyphen surrounded by text.</item>
/// <item><c>(c)</c> → <c>©</c>, <c>(r)</c> → <c>®</c>, <c>(tm)</c> → <c>™</c> (case-insensitive),
///   completed by typing the closing <c>)</c>.</item>
/// <item>Ellipsis: <c>...</c> (three periods) becomes <c>…</c> (U+2026), completed by the third period.</item>
/// <item>Sentence capitalization: a lowercase letter typed at the start of a paragraph, or after a
///   sentence terminator (<c>. </c> / <c>! </c> / <c>? </c>), is upper-cased.</item>
/// </list>
/// </summary>
public static class AutoCorrect
{
    /// <summary>En dash (U+2013); the chosen replacement for a double hyphen.</summary>
    public const char EnDash = '–';

    /// <summary>Horizontal ellipsis (U+2026).</summary>
    public const char Ellipsis = '…';

    /// <summary>Curly opening double quote (U+201C).</summary>
    public const char LeftDoubleQuote = '“';

    /// <summary>Curly closing double quote (U+201D).</summary>
    public const char RightDoubleQuote = '”';

    /// <summary>Curly opening single quote (U+2018).</summary>
    public const char LeftSingleQuote = '‘';

    /// <summary>Curly closing single quote (U+2019).</summary>
    public const char RightSingleQuote = '’';

    /// <summary>
    /// Decide whether typing <paramref name="justTyped"/> at the end of <paramref name="textBefore"/>
    /// (the text immediately preceding the caret, within the current paragraph) triggers a correction.
    /// Returns <see cref="AutoCorrectResult.None"/> when no rule fires. Rules are evaluated in a fixed
    /// order; at most one fires per keystroke.
    /// </summary>
    public static AutoCorrectResult Evaluate(string? textBefore, char justTyped)
    {
        textBefore ??= string.Empty;

        // Order matters only in that each rule keys off a distinct trigger character, so they are
        // mutually exclusive in practice. Symbols and ellipsis complete on punctuation; the dash
        // completes on a second hyphen; quotes transform the quote itself; capitalization fires on a
        // typed letter.
        return justTyped switch
        {
            '"' or '\'' => SmartQuote(textBefore, justTyped),
            '-' => DoubleHyphen(textBefore),
            '.' => EllipsisOrNone(textBefore),
            ')' => Symbol(textBefore),
            _ when IsLowercaseLetter(justTyped) => Capitalize(textBefore, justTyped),
            _ => AutoCorrectResult.None
        };
    }

    /// <summary>
    /// Map a straight quote to a curly one. Opens when at the start of the paragraph or after whitespace
    /// or an opening punctuation character ((, [, {, or a curly/straight open quote); otherwise closes.
    /// The just-typed straight quote is replaced in place (delete 0, insert 1).
    /// </summary>
    public static AutoCorrectResult SmartQuote(string textBefore, char quote)
    {
        var open = OpensQuote(LastChar(textBefore));
        var replacement = quote == '"'
            ? (open ? LeftDoubleQuote : RightDoubleQuote)
            : (open ? LeftSingleQuote : RightSingleQuote);
        return new AutoCorrectResult(0, replacement.ToString());
    }

    /// <summary>Two consecutive hyphens collapse into an en dash: delete the prior hyphen, insert the dash.</summary>
    public static AutoCorrectResult DoubleHyphen(string textBefore) =>
        EndsWith(textBefore, '-')
            ? new AutoCorrectResult(1, EnDash.ToString())
            : AutoCorrectResult.None;

    /// <summary>Three consecutive periods collapse into an ellipsis: delete the prior two, insert <c>…</c>.</summary>
    public static AutoCorrectResult EllipsisOrNone(string textBefore) =>
        textBefore.EndsWith("..", StringComparison.Ordinal)
            ? new AutoCorrectResult(2, Ellipsis.ToString())
            : AutoCorrectResult.None;

    /// <summary>
    /// Complete a parenthesised symbol when the closing <c>)</c> is typed: <c>(c)</c> → ©, <c>(r)</c> → ®,
    /// <c>(tm)</c> → ™ (case-insensitive). Deletes the already-typed opening text and inserts the symbol.
    /// </summary>
    public static AutoCorrectResult Symbol(string textBefore)
    {
        // The ")" has not been inserted yet; we match the "(x" / "(tm" already before the caret.
        if (EndsWithIgnoreCase(textBefore, "(c"))
            return new AutoCorrectResult(2, "©"); // ©
        if (EndsWithIgnoreCase(textBefore, "(r"))
            return new AutoCorrectResult(2, "®"); // ®
        if (EndsWithIgnoreCase(textBefore, "(tm"))
            return new AutoCorrectResult(3, "™"); // ™
        return AutoCorrectResult.None;
    }

    /// <summary>
    /// Upper-case <paramref name="justTyped"/> when it begins a sentence: at the start of the paragraph,
    /// or after a sentence terminator (<c>.</c>/<c>!</c>/<c>?</c>) followed by exactly one space. The
    /// just-typed letter is replaced in place (delete 0, insert the upper-case letter).
    /// </summary>
    public static AutoCorrectResult Capitalize(string textBefore, char justTyped)
    {
        if (!IsLowercaseLetter(justTyped))
            return AutoCorrectResult.None;
        if (!StartsSentence(textBefore))
            return AutoCorrectResult.None;
        return new AutoCorrectResult(0, char.ToUpperInvariant(justTyped).ToString());
    }

    /// <summary>True when the text before the caret is a sentence boundary that should be capitalised next.</summary>
    private static bool StartsSentence(string textBefore)
    {
        // Start of the paragraph (or only whitespace so far) begins a sentence.
        if (textBefore.Length == 0)
            return true;

        // After "<terminator> " — exactly one trailing space preceded by . ! or ?.
        if (textBefore[^1] != ' ')
            return false;
        if (textBefore.Length < 2)
            return false;
        var terminator = textBefore[^2];
        return terminator is '.' or '!' or '?';
    }

    // True when a straight quote typed after this character should be an OPENING (curly-open) quote.
    private static bool OpensQuote(char? previous)
    {
        if (previous is null)
            return true; // start of paragraph
        var c = previous.Value;
        if (char.IsWhiteSpace(c))
            return true;
        return c is '(' or '[' or '{'
            or LeftDoubleQuote or LeftSingleQuote
            or '"' or '\'';
    }

    private static bool IsLowercaseLetter(char c) => char.IsLower(c);

    private static char? LastChar(string text) => text.Length > 0 ? text[^1] : null;

    private static bool EndsWith(string text, char c) => text.Length > 0 && text[^1] == c;

    private static bool EndsWithIgnoreCase(string text, string suffix) =>
        text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
