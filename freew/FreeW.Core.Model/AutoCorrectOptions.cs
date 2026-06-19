using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// The settings behind Word's <em>AutoCorrect</em> tab (File &gt; Options &gt; Proofing &gt; AutoCorrect
/// Options), as opposed to the separate <em>AutoFormat As You Type</em> tab modelled by
/// <see cref="AutoFormatOptions"/>. These rules fire when a word is <em>completed</em> (a separator is typed
/// after it): the two-initial-capitals fix, day-name capitalization, and the user-editable
/// "replace text as you type" table (<c>teh</c>→<c>the</c>, <c>(c)</c>→©, …).
///
/// <para>Pure, WPF-free and JSON round-trippable (parameterless ctor, mutable collection) so it can live in
/// the persisted <c>FreeWOptions</c> and be unit-tested headlessly. Every flag defaults to on, matching
/// Word's out-of-the-box behaviour; the replace table seeds with Word's most common built-in entries.</para>
/// </summary>
public sealed class AutoCorrectOptions
{
    /// <summary>Correct two initial capitals (<c>TWo</c> → <c>Two</c>).</summary>
    public bool CorrectTwoInitialCapitals { get; set; } = true;

    /// <summary>Capitalize the first letter of the names of days (<c>monday</c> → <c>Monday</c>).</summary>
    public bool CapitalizeDayNames { get; set; } = true;

    /// <summary>Replace text as you type, using the <see cref="Replacements"/> table.</summary>
    public bool ReplaceText { get; set; } = true;

    /// <summary>
    /// The "replace text as you type" table: each entry maps a typed token (matched case-insensitively on a
    /// word boundary) to its replacement. Mutable and JSON round-trippable so the AutoCorrect dialog can
    /// add/remove rows. Defaults to Word's most common built-in corrections.
    /// </summary>
    public List<AutoCorrectReplacement> Replacements { get; set; } = DefaultReplacements();

    /// <summary>Every rule enabled with the default replace table — Word's out-of-the-box configuration.</summary>
    public static AutoCorrectOptions Default => new();

    /// <summary>Every rule disabled (and an empty table) — a base for "only rule X on" test setups.</summary>
    public static AutoCorrectOptions AllOff => new()
    {
        CorrectTwoInitialCapitals = false,
        CapitalizeDayNames = false,
        ReplaceText = false,
        Replacements = new List<AutoCorrectReplacement>(),
    };

    /// <summary>Normalize a loaded value: never-null table, no blank/duplicate keys (last write wins).</summary>
    public void Normalize()
    {
        Replacements ??= new List<AutoCorrectReplacement>();
        var seen = new Dictionary<string, AutoCorrectReplacement>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var r in Replacements)
        {
            var key = r?.Replace?.Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(r!.With))
                continue;
            seen[key] = new AutoCorrectReplacement(key, r.With);
        }
        Replacements = seen.Values.OrderBy(r => r.Replace, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Word's most common built-in "replace text as you type" entries: a handful of frequent typo fixes plus
    // the parenthesised symbols and the arrow/emoticon glyphs that live on the AutoCorrect tab (note these
    // are distinct from the AutoFormat (c)/(r)/(tm) rule — here they round-trip through the editable table).
    private static List<AutoCorrectReplacement> DefaultReplacements() => new()
    {
        new("(c)", "©"),   // ©
        new("(r)", "®"),   // ®
        new("(tm)", "™"),  // ™
        new("-->", "→"),   // →
        new("<--", "←"),   // ←
        new("==>", "⇒"),   // ⇒
        new("<==", "⇐"),   // ⇐
        new(":)", "☺"),    // ☺
        new(":(", "☹"),    // ☹
        new("...", "…"),   // …
        new("teh", "the"),
        new("adn", "and"),
        new("recieve", "receive"),
        new("seperate", "separate"),
        new("definately", "definitely"),
        new("occured", "occurred"),
        new("alot", "a lot"),
        new("thier", "their"),
        new("wich", "which"),
        new("becuase", "because"),
    };
}

/// <summary>
/// One row of the AutoCorrect "replace text as you type" table: replace the typed token <see cref="Replace"/>
/// (matched case-insensitively on a word boundary) with <see cref="With"/>. A record so the table is value-
/// comparable and round-trips through JSON with a parameterless ctor.
/// </summary>
public sealed record AutoCorrectReplacement(string Replace, string With)
{
    /// <summary>Parameterless ctor for the JSON serializer; real values come from the property setters.</summary>
    public AutoCorrectReplacement() : this(string.Empty, string.Empty) { }

    /// <summary>The token typed by the user (the left column). Matched case-insensitively.</summary>
    public string Replace { get; init; } = Replace;

    /// <summary>The replacement text inserted in its place (the right column).</summary>
    public string With { get; init; } = With;
}
