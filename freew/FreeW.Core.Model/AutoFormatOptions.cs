namespace FreeW.Core.Model;

/// <summary>
/// The per-rule toggles behind Word's "AutoFormat As You Type" proofing tab. Each flag turns one
/// as-you-type rule of <see cref="AutoCorrect.Evaluate(string?, char, AutoFormatOptions)"/> on or off; a
/// disabled rule is a no-op (the raw keystroke proceeds unchanged). Pure, WPF-free, and JSON round-trippable
/// so it can live in the persisted <c>FreeWOptions</c> and be unit-tested headlessly.
///
/// <para>An immutable record so a tweaked copy is <c>options with { Dashes = false }</c>; every flag
/// defaults to on, matching Word's out-of-the-box behaviour.</para>
/// </summary>
public sealed record AutoFormatOptions
{
    /// <summary>Straight quotes (<c>"</c> / <c>'</c>) become curly “smart” quotes.</summary>
    public bool SmartQuotes { get; init; } = true;

    /// <summary>A double hyphen (<c>--</c>) becomes an en/em dash.</summary>
    public bool Dashes { get; init; } = true;

    /// <summary>Three periods (<c>...</c>) become an ellipsis (…).</summary>
    public bool Ellipsis { get; init; } = true;

    /// <summary>Parenthesised symbols: <c>(c)</c>→©, <c>(r)</c>→®, <c>(tm)</c>→™.</summary>
    public bool Symbols { get; init; } = true;

    /// <summary>The first letter of a sentence is capitalised.</summary>
    public bool Capitalization { get; init; } = true;

    /// <summary>A line starting <c>* </c> / <c>- </c> becomes a bulleted list.</summary>
    public bool BulletedLists { get; init; } = true;

    /// <summary>A line starting <c>1. </c> becomes a numbered list.</summary>
    public bool NumberedLists { get; init; } = true;

    /// <summary>Ordinals (<c>1st</c>) get a super-scripted suffix (1<sup>st</sup>).</summary>
    public bool Ordinals { get; init; } = true;

    /// <summary>Common fractions (<c>1/2</c>) become a single glyph (½).</summary>
    public bool Fractions { get; init; } = true;

    /// <summary>Internet and e-mail addresses become clickable hyperlinks.</summary>
    public bool Hyperlinks { get; init; } = true;

    /// <summary>Every rule enabled — Word's default AutoFormat-As-You-Type configuration.</summary>
    public static AutoFormatOptions Default { get; } = new();

    /// <summary>Every rule disabled — a convenient base for "only rule X on" test setups.</summary>
    public static AutoFormatOptions AllOff { get; } = new()
    {
        SmartQuotes = false,
        Dashes = false,
        Ellipsis = false,
        Symbols = false,
        Capitalization = false,
        BulletedLists = false,
        NumberedLists = false,
        Ordinals = false,
        Fractions = false,
        Hyperlinks = false,
    };
}
