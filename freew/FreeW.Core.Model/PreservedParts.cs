using System.Xml.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// One package part FreeW does not model but preserves verbatim across a docx round-trip — e.g. a
/// <c>customXml/itemN.xml</c> (and its <c>itemPropsN.xml</c> / <c>customXml/_rels/itemN.xml.rels</c>) or
/// <c>word/webSettings.xml</c>. The raw <see cref="Bytes"/> are re-emitted byte-for-byte, and — when the
/// part carries them — its content-type declaration and the document relationship that points at it travel
/// with it so the part stays referenced and typed on write.
/// </summary>
/// <param name="PartName">
/// The absolute OPC part name (e.g. <c>/customXml/item1.xml</c> or <c>/word/webSettings.xml</c>).
/// </param>
/// <param name="Bytes">The part's raw bytes, re-emitted verbatim.</param>
/// <param name="ContentTypeOverride">
/// The part's <c>[Content_Types].xml</c> Override content type, or null when the part is covered by a
/// Default (by extension) and needs no Override (e.g. a customXml/_rels/*.rels part).
/// </param>
/// <param name="RelationshipType">
/// The relationship type of the document→part relationship (e.g. the customXml or webSettings rel type),
/// or null when no document relationship points at this part (e.g. an itemProps part is referenced from its
/// item's own _rels, not from document.xml.rels).
/// </param>
public sealed record PreservedPart(
    string PartName,
    byte[] Bytes,
    string? ContentTypeOverride = null,
    string? RelationshipType = null);

/// <summary>
/// The package parts FreeW preserves but does not model, captured on read so they survive a write
/// (preserve-and-re-emit / pass-through). Empty (the default) for a document FreeW authored from scratch, so
/// such a document emits no settings/customXml/webSettings parts and round-trips byte-equivalently to before.
///
/// <para>
/// <see cref="OriginalSettings"/> is the original <c>word/settings.xml</c> <c>w:settings</c> element (when
/// the source package had one). The writer starts from it and overlays FreeW's modelled toggles
/// (documentProtection / autoHyphenation / evenAndOddHeaders / displayBackgroundShape / embedTrueTypeFonts)
/// in CT_Settings schema order, so unmodelled settings (compat flags, default tab stop, rsids, proofing, …)
/// survive while FreeW's own features still apply. When null, the writer emits a fresh minimal settings part
/// exactly as before — and only when one of FreeW's features needs it.
/// </para>
///
/// <para>
/// <see cref="Parts"/> are arbitrary unmodelled parts re-emitted verbatim (customXml items + their props +
/// their _rels, and webSettings), each carrying the content-type Override and document relationship needed to
/// keep it referenced and typed.
/// </para>
/// </summary>
public sealed class PreservedParts
{
    /// <summary>
    /// The original <c>word/settings.xml</c> root element (<c>w:settings</c>) captured on read, used as the
    /// base the writer overlays FreeW's modelled toggles onto. Null when the source package had no settings
    /// part (an authored-from-scratch document), in which case the writer emits a fresh minimal part as before.
    /// </summary>
    public XElement? OriginalSettings { get; set; }

    /// <summary>
    /// The unmodelled parts preserved verbatim (customXml items / props / their rels, webSettings), in the
    /// order they were captured. Empty for an authored-from-scratch document so nothing extra is emitted.
    /// </summary>
    public List<PreservedPart> Parts { get; } = [];

    /// <summary>True when nothing is preserved — the authored-from-scratch case.</summary>
    public bool IsEmpty => OriginalSettings is null && Parts.Count == 0;
}
