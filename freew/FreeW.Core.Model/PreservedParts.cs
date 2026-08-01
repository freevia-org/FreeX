using System;
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
/// <param name="PackageRelationshipType">
/// The relationship type from package-root <c>_rels/.rels</c> to this part, or null when the part is not
/// package-rooted (e.g. Word custom Ribbon XML is package-rooted while document drawings are not).
/// </param>
public sealed record PreservedPart(
    string PartName,
    byte[] Bytes,
    string? ContentTypeOverride = null,
    string? RelationshipType = null,
    string? PackageRelationshipType = null);

/// <summary>
/// One reference from a verbatim-preserved inline drawing (see <see cref="PreservedDrawing"/>) to a
/// preserved package part: the relationship id the drawing's XML used on read (e.g. the
/// <c>c:chart/@r:id</c> or <c>cx:chart/@r:id</c>) paired with the absolute name of the
/// <see cref="PreservedPart"/> it pointed at. The writer assigns that preserved part a fresh document
/// relationship id and rewrites the matching <c>r:id</c>/<c>r:embed</c> inside the drawing's XML to it, so
/// the reference resolves against the re-emitted package instead of dangling.
/// </summary>
/// <param name="OriginalRelId">The relationship id used inside the preserved drawing XML on read.</param>
/// <param name="PreservedPartName">The absolute part name of the <see cref="PreservedPart"/> it referenced.</param>
/// <param name="RelationshipType">
/// The relationship type required by a story-local preserved drawing. Null lets the writer derive the type from
/// the part content type (the normal chart/SmartArt path); explicit types cover relationships such as OLE icon
/// media, whose OPC content type is supplied through a Default rather than an Override.
/// </param>
public readonly record struct PreservedDrawingReference(
    string OriginalRelId,
    string PreservedPartName,
    string? RelationshipType = null);

/// <summary>
/// A verbatim-preserved inline <c>w:drawing</c> FreeW does not model — e.g. a <c>w:drawing</c> that references
/// a chart (or <c>chartex</c>) part whose structure FreeW's reader does not turn into a <see cref="Chart"/>.
/// Rather than dropping the run (and with it the chart parts + media it references), the whole drawing's XML is
/// captured here and re-emitted byte-for-byte inside the run, while the chart part(s) + their <c>_rels</c> + the
/// media they reference travel as <see cref="PreservedParts.Parts"/>. <see cref="References"/> ties the
/// drawing's relationship ids to those preserved parts so the writer can re-point them at freshly assigned
/// document relationships.
/// </summary>
/// <param name="Xml">The drawing's serialised XML (a <c>w:drawing</c> element), re-emitted verbatim.</param>
/// <param name="References">
/// The drawing's references into preserved parts (relationship id → preserved part name), rewritten on write.
/// </param>
public sealed record PreservedDrawing(string Xml, IReadOnlyList<PreservedDrawingReference> References)
{
    /// <summary>Creates an independent copy of the reference collection for document merge.</summary>
    public PreservedDrawing Duplicate() => new(Xml, [.. References]);
}

/// <summary>
/// One relationship reference from a verbatim-preserved document-level element to a preserved package part.
/// </summary>
public readonly record struct PreservedDocumentReference(string OriginalRelId, string PreservedPartName);

/// <summary>
/// The original <c>w:webExtensions</c> document child carrying Word task-pane add-in references. Its XML is
/// re-emitted verbatim while the document relationship ids in <see cref="References"/> are remapped on write.
/// </summary>
public sealed record PreservedWebExtensions(string Xml, IReadOnlyList<PreservedDocumentReference> References);

/// <summary>
/// The original <c>w:numPr</c> a paragraph carried on read that FreeW does not model as one of its own lists:
/// the source <c>w:numId</c> and <c>w:ilvl</c>. Captured per paragraph (see
/// <see cref="Paragraph.PreservedNumbering"/>) so the writer can re-emit the paragraph's numbering pointing at
/// the preserved <see cref="PreservedParts.OriginalNumbering"/> definition (after a disjoint-id remap that keeps
/// it clear of FreeW's own fixed list ids).
/// </summary>
/// <param name="NumId">The original <c>w:numPr/w:numId/@w:val</c>.</param>
/// <param name="Ilvl">The original <c>w:numPr/w:ilvl/@w:val</c> (0 when absent).</param>
public readonly record struct PreservedNumbering(int NumId, int Ilvl);

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
    /// The original package-level <c>docProps/core.xml</c> root captured on read. The writer rebuilds
    /// FreeW's modeled fields and merges unmodeled source properties back into that result.
    /// </summary>
    public XElement? OriginalCoreProperties { get; set; }

    /// <summary>
    /// The original <c>word/settings.xml</c> root element (<c>w:settings</c>) captured on read, used as the
    /// base the writer overlays FreeW's modelled toggles onto. Null when the source package had no settings
    /// part (an authored-from-scratch document), in which case the writer emits a fresh minimal part as before.
    /// </summary>
    public XElement? OriginalSettings { get; set; }

    /// <summary>
    /// The original <c>word/numbering.xml</c> root element (<c>w:numbering</c>) captured on read when the
    /// source package had one. The writer merges its <c>w:abstractNum</c>/<c>w:num</c> definitions alongside
    /// FreeW's own — under a disjoint <c>numId</c>/<c>abstractNumId</c> range so the two never collide — and
    /// re-emits the paragraphs' preserved <see cref="Paragraph.PreservedNumbering"/> pointing at the
    /// (remapped) original definitions. Null when the source had no numbering part (an authored-from-scratch
    /// document), in which case the writer emits only FreeW's own numbering exactly as before — so such a
    /// document round-trips byte-equivalently.
    /// </summary>
    public XElement? OriginalNumbering { get; set; }

    /// <summary>
    /// The original package-level <c>docProps/custom.xml</c> root element captured on read. The writer uses it
    /// as the base for custom document properties so unmodelled properties survive while FreeW overlays its
    /// modelled watermark and mark-as-final properties.
    /// </summary>
    public XElement? OriginalCustomProperties { get; set; }

    /// <summary>
    /// The original <c>w:webExtensions</c> document child, preserved so Word task-pane add-ins remain attached
    /// after FreeW saves the document. Null when the source package did not contain one.
    /// </summary>
    public PreservedWebExtensions? WebExtensions { get; set; }

    /// <summary>
    /// The unmodelled parts preserved verbatim (customXml items / props / their rels, webSettings, and the
    /// chart/chartex parts + media referenced by a verbatim-preserved inline drawing), in the order they were
    /// captured. Empty for an authored-from-scratch document so nothing extra is emitted.
    /// </summary>
    public List<PreservedPart> Parts { get; } = [];

    /// <summary>
    /// <c>[Content_Types].xml</c> <c>Default</c> declarations (extension → content type) a preserved part relies
    /// on but FreeW would not otherwise emit — e.g. the <c>png</c>/<c>emf</c> Default a verbatim-preserved chart's
    /// media part needs when the document carries no other image of that kind. Captured on read so the writer
    /// re-emits the Default, keeping the preserved part typed. Empty for an authored-from-scratch document.
    /// </summary>
    public Dictionary<string, string> ContentTypeDefaults { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when nothing is preserved — the authored-from-scratch case.</summary>
    public bool IsEmpty => OriginalCoreProperties is null && OriginalSettings is null && OriginalNumbering is null && OriginalCustomProperties is null
        && WebExtensions is null && Parts.Count == 0;

    /// <summary>
    /// Replaces this preserved-package snapshot with a deep copy of <paramref name="source"/> so derived
    /// documents can carry the same unmodelled package safety state without sharing mutable byte/XML buffers.
    /// </summary>
    public void CopyFrom(PreservedParts source)
    {
        ArgumentNullException.ThrowIfNull(source);

        OriginalCoreProperties = source.OriginalCoreProperties is null ? null : new XElement(source.OriginalCoreProperties);
        OriginalSettings = source.OriginalSettings is null ? null : new XElement(source.OriginalSettings);
        OriginalNumbering = source.OriginalNumbering is null ? null : new XElement(source.OriginalNumbering);
        OriginalCustomProperties = source.OriginalCustomProperties is null ? null : new XElement(source.OriginalCustomProperties);
        WebExtensions = source.WebExtensions is null
            ? null
            : new PreservedWebExtensions(source.WebExtensions.Xml, source.WebExtensions.References.ToArray());

        Parts.Clear();
        foreach (var part in source.Parts)
        {
            Parts.Add(new PreservedPart(
                part.PartName,
                (byte[])part.Bytes.Clone(),
                part.ContentTypeOverride,
                part.RelationshipType,
                part.PackageRelationshipType));
        }

        ContentTypeDefaults.Clear();
        foreach (var (extension, contentType) in source.ContentTypeDefaults)
            ContentTypeDefaults[extension] = contentType;
    }
}
