namespace FreeW.Core.Model;

/// <summary>
/// The kind of extra, beyond-plain-text behaviour an AutoFormat rule asks the editor to perform in
/// addition to (or instead of) the simple delete/insert text edit carried by <see cref="AutoCorrectResult"/>.
/// Plain text rules (smart quotes, dashes, ellipsis, symbols, capitalization, fractions) use
/// <see cref="None"/>; the richer AutoFormat-As-You-Type rules use the other kinds.
/// </summary>
public enum AutoFormatOutcomeKind
{
    /// <summary>Just the plain delete/insert text edit — no extra formatting.</summary>
    None,

    /// <summary>Super-script the trailing letters of the inserted text (ordinals: <c>1st</c> → 1<sup>st</sup>).</summary>
    SuperscriptSuffix,

    /// <summary>Turn the current paragraph into a bulleted list (the typed <c>* </c> / <c>- </c> marker is removed).</summary>
    BulletList,

    /// <summary>Turn the current paragraph into a numbered list (the typed <c>1. </c> marker is removed).</summary>
    NumberList,

    /// <summary>Wrap the just-completed word (a URL or e-mail address) in a hyperlink.</summary>
    Hyperlink,
}

/// <summary>
/// The outcome of an as-you-type AutoCorrect / AutoFormat rule: replace the <see cref="DeleteBefore"/>
/// characters immediately before the caret (NOT counting the just-typed character, which has not yet been
/// inserted) plus the just-typed character with <see cref="Insert"/>.
/// <para>
/// The convention is "the just-typed character is consumed by the rule". For example, when the user
/// types <c>-</c> right after an existing <c>-</c>, the rule reports <c>DeleteBefore = 1</c> (the prior
/// dash) and <c>Insert = "—"</c> (the em dash that replaces both dashes). When the rule only transforms
/// the just-typed character itself (e.g. a straight quote becomes a curly quote), <c>DeleteBefore = 0</c>
/// and <c>Insert</c> is the replacement character.
/// </para>
/// <para>
/// Most rules are pure text edits, but the richer AutoFormat-As-You-Type rules also carry an
/// <see cref="Outcome"/>: a structured request for the editor to apply formatting the plain delete/insert
/// cannot express (super-scripting an ordinal suffix, converting the paragraph to a list, or hyperlinking a
/// URL). <see cref="SuffixLength"/> / <see cref="LinkTarget"/> parameterise those outcomes.
/// </para>
/// <see cref="None"/> means "no correction; let the keystroke proceed normally".
/// </summary>
public readonly record struct AutoCorrectResult(int DeleteBefore, string Insert)
{
    /// <summary>No correction applies; the keystroke should proceed unmodified.</summary>
    public static readonly AutoCorrectResult None = new(-1, string.Empty);

    /// <summary>True when a correction applies (i.e. this is not <see cref="None"/>).</summary>
    public bool Applies => DeleteBefore >= 0;

    /// <summary>The extra formatting behaviour the editor should perform; <see cref="AutoFormatOutcomeKind.None"/> for plain text edits.</summary>
    public AutoFormatOutcomeKind Outcome { get; init; } = AutoFormatOutcomeKind.None;

    /// <summary>
    /// For <see cref="AutoFormatOutcomeKind.SuperscriptSuffix"/>: how many trailing characters of
    /// <see cref="Insert"/> to super-script (e.g. 2 for the <c>st</c> of <c>1st</c>). Unused otherwise.
    /// </summary>
    public int SuffixLength { get; init; }

    /// <summary>
    /// For <see cref="AutoFormatOutcomeKind.Hyperlink"/>: the absolute URL to link to (for an e-mail
    /// address this is the <c>mailto:</c> form). Null for every other outcome.
    /// </summary>
    public string? LinkTarget { get; init; }
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
/// <item><c>--</c> (two hyphens) becomes a dash: hugging a word (<c>word--word</c>) yields an em dash
///   <c>—</c> (U+2014), matching Word's "type -- for a dash" behaviour; a space-flanked double hyphen
///   (<c>word -- word</c>) yields an en dash <c>–</c> (U+2013) in place of the hyphens, spaces retained.</item>
/// <item><c>(c)</c> → <c>©</c>, <c>(r)</c> → <c>®</c>, <c>(tm)</c> → <c>™</c> (case-insensitive),
///   completed by typing the closing <c>)</c>.</item>
/// <item>Ellipsis: <c>...</c> (three periods) becomes <c>…</c> (U+2026), completed by the third period.</item>
/// <item>Sentence capitalization: a lowercase letter typed at the start of a paragraph, or after a
///   sentence terminator (<c>. </c> / <c>! </c> / <c>? </c>), is upper-cased.</item>
/// </list>
/// </summary>
public static class AutoCorrect
{
    /// <summary>En dash (U+2013); the replacement for a space-flanked double hyphen (<c>word -- word</c>).</summary>
    public const char EnDash = '–';

    /// <summary>Em dash (U+2014); the replacement for a double hyphen between words with no surrounding spaces.</summary>
    public const char EmDash = '—';

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
    /// Uses <see cref="AutoFormatOptions.Default"/> (every rule on); call the
    /// <see cref="Evaluate(string?, char, AutoFormatOptions)"/> overload to honour the user's toggles.
    /// </summary>
    public static AutoCorrectResult Evaluate(string? textBefore, char justTyped) =>
        Evaluate(textBefore, justTyped, AutoFormatOptions.Default);

    /// <summary>
    /// Decide whether typing <paramref name="justTyped"/> triggers a correction, honouring the per-rule
    /// <paramref name="options"/> (Word's "AutoFormat As You Type" toggles). Returns
    /// <see cref="AutoCorrectResult.None"/> when no enabled rule fires. Rules key off distinct trigger
    /// characters / boundaries so at most one fires per keystroke; a disabled rule is a no-op.
    /// </summary>
    public static AutoCorrectResult Evaluate(string? textBefore, char justTyped, AutoFormatOptions? options)
    {
        textBefore ??= string.Empty;
        options ??= AutoFormatOptions.Default;

        // Word-boundary rules (ordinals, fractions, hyperlinks, list markers) complete when a space or the
        // closing marker is typed; the per-character rules transform on their own trigger character. Each
        // rule consults its own toggle so a disabled rule simply falls through to "no correction".
        return justTyped switch
        {
            '"' or '\'' => options.SmartQuotes ? SmartQuote(textBefore, justTyped) : AutoCorrectResult.None,
            '-' => options.Dashes ? DoubleHyphen(textBefore) : AutoCorrectResult.None,
            '.' => options.Ellipsis ? EllipsisOrNone(textBefore) : AutoCorrectResult.None,
            ')' => options.Symbols ? Symbol(textBefore) : AutoCorrectResult.None,
            ' ' => OnSpace(textBefore, options),
            _ when IsLowercaseLetter(justTyped) && options.Capitalization => Capitalize(textBefore, justTyped),
            _ => AutoCorrectResult.None
        };
    }

    // Rules that fire when a space ends a "word" or a list/ordinal/URL marker. Evaluated in priority order;
    // the just-typed space is part of the trigger and (for ordinals/fractions/hyperlinks) is re-emitted as
    // part of the insert, while the list rules consume the marker entirely.
    private static AutoCorrectResult OnSpace(string textBefore, AutoFormatOptions options)
    {
        if (options.BulletedLists)
        {
            var bullet = BulletListMarker(textBefore);
            if (bullet.Applies)
                return bullet;
        }
        if (options.NumberedLists)
        {
            var number = NumberListMarker(textBefore);
            if (number.Applies)
                return number;
        }
        if (options.Ordinals)
        {
            var ordinal = Ordinal(textBefore);
            if (ordinal.Applies)
                return ordinal;
        }
        if (options.Fractions)
        {
            var fraction = Fraction(textBefore);
            if (fraction.Applies)
                return fraction;
        }
        if (options.Hyperlinks)
        {
            var link = HyperlinkOnSpace(textBefore);
            if (link.Applies)
                return link;
        }
        return AutoCorrectResult.None;
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

    /// <summary>
    /// Two consecutive hyphens collapse into a dash: delete the prior hyphen, insert the dash. As in Word,
    /// <c>word--</c> (the dashes hug a word, no surrounding spaces) yields an <see cref="EmDash"/>, while a
    /// space-flanked <c>word --</c> yields an <see cref="EnDash"/> (the spaces themselves are left alone;
    /// only the two hyphens are replaced).
    /// </summary>
    public static AutoCorrectResult DoubleHyphen(string textBefore)
    {
        if (!EndsWith(textBefore, '-'))
            return AutoCorrectResult.None;
        // The character before the prior hyphen decides en vs. em: a space (or paragraph start) → en dash.
        var beforeDash = textBefore.Length >= 2 ? textBefore[^2] : ' ';
        var dash = char.IsWhiteSpace(beforeDash) ? EnDash : EmDash;
        return new AutoCorrectResult(1, dash.ToString());
    }

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

    // ── AutoFormat-As-You-Type rules (fire on a word/line boundary — the typed space) ───────────────────

    /// <summary>
    /// When a space is typed right after a lone bullet marker at the very start of the paragraph
    /// (<c>* </c>, <c>- </c>, or <c>&gt; </c> with nothing else before it), strip the marker and ask the
    /// editor to convert the paragraph to a bulleted list. The whole <c>"&lt;marker&gt;"</c> already before
    /// the caret is deleted and nothing is inserted (the space is consumed too).
    /// </summary>
    public static AutoCorrectResult BulletListMarker(string textBefore)
    {
        // Only at paragraph start: "* " / "- " / "> " with the marker being the sole prior character.
        if (textBefore is "*" or "-" or ">" or "•")
            return new AutoCorrectResult(textBefore.Length, string.Empty) { Outcome = AutoFormatOutcomeKind.BulletList };
        return AutoCorrectResult.None;
    }

    /// <summary>
    /// When a space is typed right after a leading <c>"1."</c> (or <c>"1)"</c>) at the very start of the
    /// paragraph, strip the marker and ask the editor to convert the paragraph to a numbered list. Only
    /// the start value <c>1</c> auto-starts a list (matching Word), so an in-progress edit is not hijacked.
    /// </summary>
    public static AutoCorrectResult NumberListMarker(string textBefore)
    {
        if (textBefore is "1." or "1)")
            return new AutoCorrectResult(textBefore.Length, string.Empty) { Outcome = AutoFormatOutcomeKind.NumberList };
        return AutoCorrectResult.None;
    }

    /// <summary>
    /// Super-script the ordinal suffix when a number-plus-suffix word (<c>1st</c>, <c>2nd</c>, <c>3rd</c>,
    /// <c>4th</c>, <c>11th</c> …) is completed by a space. The digits + suffix already before the caret are
    /// re-emitted (delete the word, insert "<c>1st </c>") with the trailing letters flagged for
    /// super-scripting via <see cref="AutoFormatOutcomeKind.SuperscriptSuffix"/>.
    /// </summary>
    public static AutoCorrectResult Ordinal(string textBefore)
    {
        // Walk back over the trailing two suffix letters then the digits; require a word boundary before.
        var n = textBefore.Length;
        if (n < 3)
            return AutoCorrectResult.None;
        var suffix = textBefore[(n - 2)..].ToLowerInvariant();

        // Find where the digit run starts.
        var i = n - 2;
        while (i > 0 && char.IsDigit(textBefore[i - 1]))
            i--;
        var digits = textBefore[i..(n - 2)];
        if (digits.Length == 0)
            return AutoCorrectResult.None;
        // Must be preceded by a word boundary (start / space / opening punctuation), not glued to letters.
        if (i > 0 && (char.IsLetterOrDigit(textBefore[i - 1]) || textBefore[i - 1] == '_'))
            return AutoCorrectResult.None;
        if (!IsOrdinalSuffix(digits, suffix))
            return AutoCorrectResult.None;

        var word = digits + suffix;
        // Replace the word with itself + the just-typed space, and super-script the two suffix letters.
        return new AutoCorrectResult(word.Length, word + " ")
        {
            Outcome = AutoFormatOutcomeKind.SuperscriptSuffix,
            SuffixLength = suffix.Length,
        };
    }

    /// <summary>
    /// Replace a typed simple fraction (<c>1/2</c>, <c>1/4</c>, <c>3/4</c>) with the matching Unicode
    /// vulgar-fraction glyph (½, ¼, ¾) when completed by a space. Only the three fractions with a
    /// dedicated single glyph are mapped (matching Word's default); others are left untouched. The fraction
    /// before the caret is deleted and the glyph plus the typed space are inserted.
    /// </summary>
    public static AutoCorrectResult Fraction(string textBefore)
    {
        foreach (var (text, glyph) in FractionGlyphs)
        {
            if (!textBefore.EndsWith(text, StringComparison.Ordinal))
                continue;
            // Require a word boundary before the fraction so "11/2" or "x1/2" is left alone.
            var before = textBefore.Length - text.Length;
            if (before > 0 && (char.IsLetterOrDigit(textBefore[before - 1]) || textBefore[before - 1] == '/'))
                continue;
            return new AutoCorrectResult(text.Length, glyph + " ");
        }
        return AutoCorrectResult.None;
    }

    /// <summary>
    /// When a space completes a word that is a URL (<c>http://…</c>, <c>https://…</c>, <c>www.…</c>) or a
    /// bare e-mail address, re-emit the word followed by the typed space and ask the editor to wrap the
    /// word in a hyperlink (<see cref="AutoFormatOutcomeKind.Hyperlink"/>; <see cref="AutoCorrectResult.LinkTarget"/>
    /// carries the absolute URL — the <c>mailto:</c> form for an e-mail).
    /// </summary>
    public static AutoCorrectResult HyperlinkOnSpace(string textBefore)
    {
        var word = LastWord(textBefore);
        if (word.Length == 0)
            return AutoCorrectResult.None;
        var target = LinkTargetFor(word);
        if (target is null)
            return AutoCorrectResult.None;
        return new AutoCorrectResult(word.Length, word + " ")
        {
            Outcome = AutoFormatOutcomeKind.Hyperlink,
            LinkTarget = target,
        };
    }

    /// <summary>
    /// The absolute URL a bare word should link to, or null when the word is not a recognised URL or
    /// e-mail. <c>www.x.com</c> → <c>http://www.x.com</c>; an e-mail → <c>mailto:</c>; an already-absolute
    /// http(s) URL is returned as-is. Pure (no toggles) so it is reusable and unit-testable on its own.
    /// </summary>
    public static string? LinkTargetFor(string word)
    {
        if (string.IsNullOrEmpty(word))
            return null;

        if (word.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || word.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.IsWellFormedUriString(word, UriKind.Absolute) ? word : null;
        }

        if (word.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && LooksLikeDomain(word))
            return "http://" + word;

        if (LooksLikeEmail(word))
            return "mailto:" + word;

        return null;
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

    // The three vulgar fractions with a dedicated single Unicode glyph (Word's default fraction set).
    private static readonly (string Text, string Glyph)[] FractionGlyphs =
    {
        ("1/2", "½"),
        ("1/4", "¼"),
        ("3/4", "¾"),
    };

    // The trailing run of non-whitespace characters before the caret (the word the just-typed space ended),
    // trimmed of a single trailing sentence-punctuation char so "http://x.com." links "http://x.com".
    private static string LastWord(string textBefore)
    {
        var end = textBefore.Length;
        var start = end;
        while (start > 0 && !char.IsWhiteSpace(textBefore[start - 1]))
            start--;
        var word = textBefore[start..end];
        if (word.Length > 1 && word[^1] is '.' or ',' or ';' or ':' or '!' or '?')
            word = word[..^1];
        return word;
    }

    // True when "<digits><suffix>" is a valid English ordinal (1st, 2nd, 3rd, 4th, 11th, 12th, 13th, 21st…).
    private static bool IsOrdinalSuffix(string digits, string suffix)
    {
        if (digits.Length == 0 || suffix.Length != 2)
            return false;
        var lastTwo = digits.Length >= 2 ? int.Parse(digits[^2..]) : int.Parse(digits);
        var lastDigit = digits[^1] - '0';
        var expected = (lastTwo is >= 11 and <= 13)
            ? "th"
            : lastDigit switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return suffix == expected;
    }

    // A coarse "looks like a domain" check: at least one dot, and every dot-separated label is non-empty.
    private static bool LooksLikeDomain(string host)
    {
        var afterScheme = host;
        var slash = afterScheme.IndexOf('/');
        if (slash >= 0)
            afterScheme = afterScheme[..slash];
        if (!afterScheme.Contains('.'))
            return false;
        var labels = afterScheme.Split('.');
        return labels.Length >= 2 && Array.TrueForAll(labels, l => l.Length > 0);
    }

    // A coarse "looks like an e-mail" check: exactly one @, a non-empty local part, and a domain with a dot.
    private static bool LooksLikeEmail(string word)
    {
        var at = word.IndexOf('@');
        if (at <= 0 || at != word.LastIndexOf('@') || at == word.Length - 1)
            return false;
        return LooksLikeDomain(word[(at + 1)..]);
    }
}
