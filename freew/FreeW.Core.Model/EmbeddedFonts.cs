namespace FreeW.Core.Model;

/// <summary>
/// An embedded font family: the typeface <see cref="Family"/> name (matching a run's
/// <see cref="RunFormatting.FontFamily"/>) and the raw font-file bytes for each of the four embeddable
/// styles. Each style is optional (null when that style is not embedded) — a document typically embeds
/// only the styles it actually uses. Maps onto a <c>w:font w:name="…"</c> entry in
/// <c>word/fontTable.xml</c>, whose <c>w:embedRegular</c>/<c>w:embedBold</c>/<c>w:embedItalic</c>/
/// <c>w:embedBoldItalic</c> children each reference an obfuscated font part
/// (<c>word/fonts/fontN.odttf</c>). Modelled as an immutable record so it round-trips cleanly and the
/// bytes are the original (de-obfuscated) font bytes — the ODTTF XOR obfuscation is applied only at the
/// docx boundary (see the writer/reader), never stored in the model.
/// </summary>
public sealed record EmbeddedFont(
    string Family,
    byte[]? Regular = null,
    byte[]? Bold = null,
    byte[]? Italic = null,
    byte[]? BoldItalic = null)
{
    /// <summary>True when at least one of the four styles carries embedded bytes.</summary>
    public bool HasAnyStyle =>
        Regular is { Length: > 0 }
        || Bold is { Length: > 0 }
        || Italic is { Length: > 0 }
        || BoldItalic is { Length: > 0 };
}
