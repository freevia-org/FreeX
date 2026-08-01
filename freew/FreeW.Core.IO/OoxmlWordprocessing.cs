using System.Globalization;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>WordprocessingML namespaces and unit helpers shared by the docx reader/writer.</summary>
internal static class Ooxml
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// The Office 2010 WordprocessingML extension namespace (w14), used here for the checkbox content
    /// control element (w14:checkbox) inside a content control's w:sdtPr.
    /// </summary>
    public static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>
    /// The OOXML math namespace (m), used for inline equations (m:oMath / m:r / m:t / m:sSup / m:f).
    /// Declared on the document root so inline equations serialise and parse like any other run feature.
    /// </summary>
    public static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    public static readonly XNamespace Ct = OpcMediaTypes.ContentTypesNamespace;
    public static readonly XNamespace Rel = OpcRelationships.Namespace;

    // DrawingML namespaces used by inline pictures (w:drawing/wp:inline/.../a:blip).
    public static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>
    /// The Office 2010 WordprocessingShape namespace (wps), used for inline DrawingML shapes / text boxes
    /// (w:drawing/wp:inline/a:graphic/a:graphicData[uri=wps]/wps:wsp). Declared on the document root so
    /// shapes serialise and parse like the other inline run features.
    /// </summary>
    public static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    /// <summary>
    /// The Office 2010 WordprocessingGroup namespace (wpg), used for floating drawing groups
    /// (<c>w:drawing/wp:anchor/a:graphic/a:graphicData[uri=wpg]/wpg:wgp</c>). The group element
    /// (<c>wpg:wgp</c>) wraps child drawing elements inside a shared coordinate frame defined by
    /// <c>a:grpSpPr</c>.
    /// </summary>
    public static readonly XNamespace Wpg = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";

    /// <summary>The a:graphicData/@uri that marks a DrawingML graphic frame as a drawing group.</summary>
    public const string GroupGraphicDataUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";

    /// <summary>
    /// The DrawingML chart namespace (c), used by the chart part (c:chartSpace / c:barChart / c:ser / …)
    /// and by the a:graphicData that references the chart from the inline w:drawing.
    /// </summary>
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public static readonly XNamespace Cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    public const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    public const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    public const string ChartRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";

    /// <summary>
    /// The Office 2014 "chartex" extended-chart relationship type (e.g. sunburst / treemap / box-and-whisker /
    /// waterfall, the chart kinds FreeW does not model). A <c>w:drawing</c> referencing one of these is captured
    /// verbatim (drawing + chartex part + its <c>_rels</c> + media) rather than dropped — see
    /// <c>DocxReader.CaptureUnmodelledChartDrawing</c>.
    /// </summary>
    public const string ChartExRelType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";

    /// <summary>
    /// The OPC content type + relationship type for a chart's embedded companion workbook (the editable-data
    /// xlsx referenced by c:externalData). The "package" relationship type is what Word's "Edit Data" follows
    /// from the chart part's own _rels to reopen the spreadsheet behind the chart.
    /// </summary>
    public const string SpreadsheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string ExternalDataRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    /// <summary>The a:graphicData/@uri that marks a DrawingML graphic frame as a chart.</summary>
    public const string ChartGraphicDataUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// The VML namespaces used by a classic embedded OLE object's presentation markup. <c>v</c> carries the
    /// <c>v:shape</c>/<c>v:imagedata</c> (the on-page icon) and <c>o</c> carries the <c>o:OLEObject</c>
    /// (Type/ProgID/ShapeID/relationship). Declared on the document root so embedded objects serialise and
    /// parse like the other inline run features.
    /// </summary>
    public static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    public static readonly XNamespace O = "urn:schemas-microsoft-com:office:office";
    public static readonly XNamespace W10 = "urn:schemas-microsoft-com:office:word";

    /// <summary>The OPC content type + relationship type for an embedded OLE object's binary part.</summary>
    public const string OleObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";
    public const string OleObjectRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";

    /// <summary>The relationship type for an inline image / OLE presentation media part (shared with pictures).</summary>
    public const string ImageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    /// <summary>The document relationship type used by body-level <c>w:altChunk</c> import payloads.</summary>
    public const string AltChunkRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/aFChunk";

    /// <summary>The external relationship type used by Word master-document <c>w:subDoc</c> anchors.</summary>
    public const string SubDocumentRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/subDocument";

    /// <summary>
    /// The DrawingML diagram namespace (dgm / "diagram"), used by the SmartArt data/layout/quickStyle/colors
    /// parts (dgm:dataModel / dgm:ptLst / dgm:pt / dgm:t / …) and the dgm:relIds element that references the
    /// four diagram parts from the inline w:drawing.
    /// </summary>
    public static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    // The four SmartArt diagram parts each have their own content type + relationship type. The data part
    // carries the dgm:dataModel (node text + structure); layout/quickStyle/colors are stock-but-valid.
    public const string DiagramDataContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
    public const string DiagramLayoutContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
    public const string DiagramStyleContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
    public const string DiagramColorsContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";

    public const string DiagramDataRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    public const string DiagramLayoutRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    public const string DiagramStyleRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    public const string DiagramColorsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";

    /// <summary>The a:graphicData/@uri that marks a DrawingML graphic frame as a SmartArt diagram.</summary>
    public const string DiagramGraphicDataUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>
    /// The Microsoft "diagram drawing" namespace (dsp), root of the SmartArt rendered-geometry part
    /// (word/diagrams/drawingN.xml — dsp:drawing/dsp:spTree/dsp:sp). This fifth diagram part carries
    /// pre-laid-out shapes (text + a:xfrm offsets/extents) so a viewer can show the diagram without re-running
    /// SmartArt auto-layout. It is referenced from the DATA part via a diagramDrawing relationship plus a
    /// dgm:dataModelExt element inside the dgm:dataModel.
    /// </summary>
    public static readonly XNamespace Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";

    /// <summary>Content type of the SmartArt rendered-geometry part (word/diagrams/drawingN.xml).</summary>
    public const string DiagramDrawingContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

    /// <summary>Relationship type (data part → drawing part) for the SmartArt rendered-geometry part.</summary>
    public const string DiagramDrawingRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    // OPC core properties (docProps/core.xml): Dublin Core + the cp / dcterms / xsi vocabularies.
    public static readonly XNamespace Cp = OpcDocumentProperties.CorePropertiesNamespace;
    public static readonly XNamespace Dc = OpcDocumentProperties.DublinCoreNamespace;
    public static readonly XNamespace DcTerms = OpcDocumentProperties.DublinCoreTermsNamespace;
    public static readonly XNamespace DcmiType = OpcDocumentProperties.DublinCoreTypeNamespace;
    public static readonly XNamespace Xsi = OpcDocumentProperties.XmlSchemaInstanceNamespace;

    public const string CorePropertiesContentType = OpcPackageProperties.CorePropertiesContentType;
    public const string CorePropertiesRelType = OpcPackageProperties.CorePropertiesRelationshipType;
    public const string CorePropertiesPartName = OpcPackageProperties.CorePropertiesPartName;

    // OPC custom properties (docProps/custom.xml): used best-effort to persist the page watermark text.
    public static readonly XNamespace CustomProps = OpcCustomDocumentProperties.CustomPropertiesNamespace;
    public static readonly XNamespace VtVariant = OpcCustomDocumentProperties.VariantTypesNamespace;
    public const string CustomPropertiesContentType = OpcPackageProperties.CustomPropertiesContentType;
    public const string CustomPropertiesRelType = OpcPackageProperties.CustomPropertiesRelationshipType;
    public const string CustomPropertiesPartName = OpcPackageProperties.CustomPropertiesPartName;

    // OPC extended properties (docProps/app.xml): application/company/template and other package metadata
    // Word stores outside the core/custom property model.
    public const string ExtendedPropertiesContentType = OpcPackageProperties.ExtendedPropertiesContentType;
    public const string ExtendedPropertiesRelType = OpcPackageProperties.ExtendedPropertiesRelationshipType;
    public const string ExtendedPropertiesPartName = OpcPackageProperties.ExtendedPropertiesPartName;

    /// <summary>The custom-property name under which the FreeW page watermark text is persisted.</summary>
    public const string WatermarkPropertyName = "FreeWWatermark";

    /// <summary>Custom-property name for watermark font family (part of WatermarkOptions round-trip).</summary>
    public const string WatermarkFontFamilyPropertyName = "FreeWWatermarkFont";

    /// <summary>Custom-property name for watermark font colour hex (part of WatermarkOptions round-trip).</summary>
    public const string WatermarkColorPropertyName = "FreeWWatermarkColor";

    /// <summary>Custom-property name for watermark layout ("Diagonal" or "Horizontal") (part of WatermarkOptions round-trip).</summary>
    public const string WatermarkLayoutPropertyName = "FreeWWatermarkLayout";

    /// <summary>Custom-property name for watermark opacity fraction in [0,1] (part of WatermarkOptions round-trip).</summary>
    public const string WatermarkOpacityPropertyName = "FreeWWatermarkOpacity";

    /// <summary>
    /// Custom-property name for image-watermark bytes (base-64 encoded, vt:lpwstr). Non-null when the
    /// watermark is a picture rather than text. Persisted alongside the other WatermarkOptions properties.
    /// </summary>
    public const string WatermarkImagePropertyName = "FreeWWatermarkImage";

    /// <summary>
    /// Custom-property name for the picture-watermark scale percentage (vt:lpwstr, integer string).
    /// 0 means Auto. Only meaningful when WatermarkImage is set.
    /// </summary>
    public const string WatermarkScalePropertyName = "FreeWWatermarkScale";

    /// <summary>
    /// The custom-property name under which Word's "Mark as Final" flag is persisted (a boolean
    /// <c>vt:bool</c> custom document property). This is the Word convention.
    /// </summary>
    public const string MarkAsFinalPropertyName = "_MarkAsFinal";

    public const string NumberingContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    public const string NumberingRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string NumberingPartName = "/word/numbering.xml";

    public const string FootnotesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml";
    public const string FootnotesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
    public const string FootnotesPartName = "/word/footnotes.xml";

    public const string EndnotesContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";
    public const string EndnotesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
    public const string EndnotesPartName = "/word/endnotes.xml";

    public const string CommentsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
    public const string CommentsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    public const string CommentsPartName = "/word/comments.xml";

    /// <summary>
    /// The Office 2012 (w15) WordprocessingML extension namespace, used by word/commentsExtended.xml
    /// (w15:commentsEx / w15:commentEx) to thread modern comments (w15:paraId / w15:paraIdParent) and
    /// mark them resolved (w15:done). The w14 paraId attributes on the comment paragraphs use <see cref="W14"/>.
    /// </summary>
    public static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    // word/commentsExtended.xml — the threading + resolved-state side-part for modern (threaded) comments.
    public const string CommentsExtendedContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml";
    public const string CommentsExtendedRelType = "http://schemas.microsoft.com/office/2011/relationships/commentsExtended";
    public const string CommentsExtendedPartName = "/word/commentsExtended.xml";

    public const string SettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    public const string SettingsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    public const string SettingsPartName = "/word/settings.xml";

    // word/stylesWithEffects.xml is Word 2013+'s supplemental rich-style payload. FreeW's modeled styles.xml
    // remains authoritative; this second style part is retained verbatim for Word to rehydrate on reopen.
    public const string StylesWithEffectsRelType = "http://schemas.microsoft.com/office/2007/relationships/stylesWithEffects";
    public const string StylesWithEffectsContentType = "application/vnd.ms-word.stylesWithEffects+xml";

    // word/people.xml carries Office 2013+ contact metadata for comment and revision authors.
    public const string PeopleRelType = "http://schemas.microsoft.com/office/2011/relationships/people";
    public const string PeopleContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml";

    public const string CommentsIdsRelType = "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds";
    public const string CommentsExtensibleRelType = "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible";
    public const string KeyMapCustomizationRelType = "http://schemas.microsoft.com/office/2006/relationships/keyMapCustomizations";
    public const string DocumentTasksRelType = "http://schemas.microsoft.com/office/2019/05/relationships/documenttasks";

    /// <summary>
    /// The bibliography namespace (b), used by word/bibliography/sources.xml — the legacy mirror for the
    /// document's citation sources (b:Sources/b:Source) and selected bibliography style. Word's active
    /// current-source store is the matching b:Sources custom XML item emitted by DocxWriter.
    /// </summary>
    public static readonly XNamespace B = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
    public const string BibliographyContentType = "application/vnd.openxmlformats-officedocument.bibliography+xml";
    public const string BibliographyRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/bibliography";
    public const string BibliographyPartName = "/word/bibliography/sources.xml";

    // word/webSettings.xml carries web-page-export settings FreeW does not model; preserved verbatim.
    public const string WebSettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml";
    public const string WebSettingsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings";

    // word/glossary/document.xml carries Word building blocks / AutoText FreeW does not model; preserved verbatim.
    public const string GlossaryDocumentContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml";
    public const string GlossaryDocumentRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/glossaryDocument";
    public const string GlossaryDocumentPartName = "/word/glossary/document.xml";

    // customXml/itemN.xml stores arbitrary custom XML data parts FreeW does not model; preserved verbatim.
    // Each item references its own customXml/itemPropsN.xml (the data-store item id/schema) via the item's
    // own customXml/_rels/itemN.xml.rels. The document→item relationship uses the customXml rel type.
    public const string CustomXmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    public const string CustomXmlPropsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps";
    public static readonly XNamespace CustomXmlDataStore = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    public const string CustomXmlItemContentType = "application/xml";
    public const string CustomXmlPropsContentType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";

    // word/vbaProject.bin (+ word/vbaData.xml and the part-local word/_rels/vbaProject.bin.rels) carry a
    // document's VBA macro project. FreeW does not model — let alone execute — macros; they are preserved
    // verbatim so a .docm/.dotm round-trips its macros, and dropped when saving a non-macro variant.
    public const string VbaProjectRelType = "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
    public const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";
    public const string VbaDataContentType = "application/vnd.ms-word.vbaData+xml";

    // word/fontTable.xml lists the embedded font families (w:font/w:embedRegular/…). Each embed references an
    // obfuscated font part (word/fonts/fontN.odttf, content type obfuscatedFont) via the fontTable's own rels.
    public const string FontTableContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";
    public const string FontTableRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    public const string FontTablePartName = "/word/fontTable.xml";

    public const string ObfuscatedFontContentType = "application/vnd.openxmlformats-officedocument.obfuscatedFont";
    public const string FontRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/font";

    /// <summary>
    /// Applies the ODTTF font obfuscation (the OOXML "embedded TrueType" transform) to <paramref name="font"/>
    /// using the 16-byte key derived from <paramref name="fontKey"/> (a GUID string such as
    /// <c>{XXXXXXXX-XXXX-…}</c>). The first 32 bytes are XOR-ed with the key (cycled), bytes 32+ are copied
    /// verbatim. The transform is its own inverse, so the same call de-obfuscates an already-obfuscated part.
    /// </summary>
    public static byte[] ObfuscateFont(byte[] font, string fontKey)
    {
        var key = FontKeyToBytes(fontKey);
        var result = (byte[])font.Clone();
        var limit = Math.Min(32, result.Length);
        for (var i = 0; i < limit; i++)
            result[i] = (byte)(result[i] ^ key[i % 16]);
        return result;
    }

    /// <summary>
    /// Derives the 16-byte ODTTF obfuscation key from a fontKey GUID string: strip braces/dashes to get 16
    /// hex byte pairs, then reverse the byte order. E.g. <c>{XXXXXXXX-…}</c> → 16 bytes, reversed.
    /// </summary>
    public static byte[] FontKeyToBytes(string fontKey)
    {
        var hex = new System.Text.StringBuilder(32);
        foreach (var c in fontKey)
            if (Uri.IsHexDigit(c))
                hex.Append(c);
        if (hex.Length != 32)
            throw new ArgumentException("fontKey must contain 16 hex byte pairs (a GUID).", nameof(fontKey));

        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
            bytes[i] = byte.Parse(hex.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Array.Reverse(bytes);
        return bytes;
    }

    /// <summary>
    /// Derives a deterministic fontKey GUID string (<c>{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}</c>) from
    /// <paramref name="seed"/> (the font family + style), so the writer never calls <c>Guid.NewGuid()</c> and
    /// the obfuscated output is reproducible. Uses a stable FNV-1a hash to fill the 16 GUID bytes.
    /// </summary>
    public static string DeterministicFontKey(string seed)
    {
        var bytes = new byte[16];
        // FNV-1a over the UTF-8 seed, re-seeded per byte so all 16 bytes vary deterministically.
        var utf8 = System.Text.Encoding.UTF8.GetBytes(seed);
        for (var b = 0; b < 16; b++)
        {
            ulong hash = 14695981039346656037UL + (ulong)b * 1099511628211UL;
            foreach (var t in utf8)
            {
                hash ^= t;
                hash *= 1099511628211UL;
            }
            bytes[b] = (byte)(hash & 0xFF);
        }
        return new Guid(bytes).ToString("B").ToUpperInvariant();
    }

    public const string ThemeContentType = "application/vnd.openxmlformats-officedocument.theme+xml";
    public const string ThemeRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    public const string ThemePartName = "/word/theme/theme1.xml";

    /// <summary>
    /// The OPC content type for a media-part image extension (png/jpeg/gif/bmp/tiff/emf/wmf), used to emit the
    /// matching <c>[Content_Types].xml</c> Default for each image format a document actually carries. The
    /// extension is the lower-case file extension (no dot) produced by <c>InlineImage.ExtensionFor</c>.
    /// Defaults to <c>image/png</c> for an unrecognised extension (the historical behaviour).
    /// </summary>
    public static string ImageContentTypeForExtension(string extension) =>
        OpcMediaTypes.TryGetDefaultContentType(extension, out var contentType) &&
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? contentType
            : "image/png";

    /// <summary>W3CDTF as used by dcterms:created/modified (UTC, second precision, trailing 'Z').</summary>
    public static string ToW3CDtf(DateTimeOffset value) => OpcPackageProperties.ToW3CDtf(value);

    public static DateTimeOffset? ParseW3CDtf(string? value) => OpcPackageProperties.ParseW3CDtf(value);

    // ── Unit conversions — delegates to the shared tier (Free.Shared.Opc.DrawingMlUnits) ───────────

    /// <summary>DrawingML "EMU" = English Metric Units; 914400 per inch, 12700 per point.</summary>
    public const long EmuPerPoint = DrawingMlUnits.EmuPerPoint;

    public static long PointsToEmu(double points) => DrawingMlUnits.PointsToEmu(points);

    public static double EmuToPoints(string? value) => DrawingMlUnits.EmuToPoints(value);

    /// <summary>OOXML "dxa" = twentieths of a point.</summary>
    public static double DxaToPoints(string? value) => DrawingMlUnits.DxaToPoints(value);

    public static int PointsToDxa(double points) => DrawingMlUnits.PointsToDxa(points);

    /// <summary>Run font size is in half-points.</summary>
    public static double? HalfPointsToPoints(string? value) => DrawingMlUnits.HalfPointsToPoints(value);

    public static int PointsToHalfPoints(double points) => DrawingMlUnits.PointsToHalfPoints(points);

    /// <summary>Border widths (w:sz on w:pBdr / w:tblBorders edges) are in eighths of a point.</summary>
    public static double EighthPointsToPoints(string? value) => DrawingMlUnits.EighthPointsToPoints(value);

    public static int PointsToEighthPoints(double points) => DrawingMlUnits.PointsToEighthPoints(points);

    public static int ParseInt(string? value) => DrawingMlUnits.ParseInt(value);

    /// <summary>
    /// Maps a <see cref="ProtectionMode"/> to the w:documentProtection/@w:edit token, or null for
    /// <see cref="ProtectionMode.None"/> (no protection element is emitted). ReadOnly→"readOnly",
    /// CommentsOnly→"comments", TrackChangesOnly→"trackedChanges", FillingForms→"forms".
    /// </summary>
    public static string? ProtectionEditToken(ProtectionMode mode) => mode switch
    {
        ProtectionMode.ReadOnly => "readOnly",
        ProtectionMode.CommentsOnly => "comments",
        ProtectionMode.TrackChangesOnly => "trackedChanges",
        ProtectionMode.FillingForms => "forms",
        _ => null
    };

    /// <summary>
    /// Maps a w:documentProtection/@w:edit token back to a <see cref="ProtectionMode"/>. Any unknown
    /// or absent token (including "none") maps to <see cref="ProtectionMode.None"/>.
    /// </summary>
    public static ProtectionMode ProtectionModeFromEditToken(string? edit) => edit switch
    {
        "readOnly" => ProtectionMode.ReadOnly,
        "comments" => ProtectionMode.CommentsOnly,
        "trackedChanges" => ProtectionMode.TrackChangesOnly,
        "forms" => ProtectionMode.FillingForms,
        _ => ProtectionMode.None
    };

    /// <summary>
    /// Maps a <see cref="LigatureMode"/> to its w14:ligatures/@w14:val token, or null for
    /// <see cref="LigatureMode.None"/> (no element is emitted). <see cref="LigatureMode.NoneExplicit"/>
    /// maps to the explicit "none" token.
    /// </summary>
    public static string? LigaturesToken(LigatureMode mode) => mode switch
    {
        LigatureMode.NoneExplicit => "none",
        LigatureMode.Standard => "standard",
        LigatureMode.Contextual => "contextual",
        LigatureMode.StandardContextual => "standardContextual",
        LigatureMode.Historical => "historical",
        LigatureMode.Discretional => "discretional",
        LigatureMode.StandardHistorical => "standardHistorical",
        LigatureMode.ContextualHistorical => "contextualHistorical",
        LigatureMode.StandardContextualHistorical => "standardContextualHistorical",
        LigatureMode.ContextualDiscretional => "contextualDiscretional",
        LigatureMode.StandardDiscretional => "standardDiscretional",
        LigatureMode.StandardContextualDiscretional => "standardContextualDiscretional",
        LigatureMode.HistoricalDiscretional => "historicalDiscretional",
        LigatureMode.StandardHistoricalDiscretional => "standardHistoricalDiscretional",
        LigatureMode.ContextualHistoricalDiscretional => "contextualHistoricalDiscretional",
        LigatureMode.All => "all",
        _ => null
    };

    /// <summary>
    /// Maps a w14:ligatures/@w14:val token back to a <see cref="LigatureMode"/>. The "none" token maps to
    /// <see cref="LigatureMode.NoneExplicit"/>; an unknown/absent token maps to <see cref="LigatureMode.None"/>.
    /// </summary>
    public static LigatureMode LigatureModeFromToken(string? token) => token switch
    {
        "none" => LigatureMode.NoneExplicit,
        "standard" => LigatureMode.Standard,
        "contextual" => LigatureMode.Contextual,
        "standardContextual" => LigatureMode.StandardContextual,
        "historical" => LigatureMode.Historical,
        "discretional" => LigatureMode.Discretional,
        "standardHistorical" => LigatureMode.StandardHistorical,
        "contextualHistorical" => LigatureMode.ContextualHistorical,
        "standardContextualHistorical" => LigatureMode.StandardContextualHistorical,
        "contextualDiscretional" => LigatureMode.ContextualDiscretional,
        "standardDiscretional" => LigatureMode.StandardDiscretional,
        "standardContextualDiscretional" => LigatureMode.StandardContextualDiscretional,
        "historicalDiscretional" => LigatureMode.HistoricalDiscretional,
        "standardHistoricalDiscretional" => LigatureMode.StandardHistoricalDiscretional,
        "contextualHistoricalDiscretional" => LigatureMode.ContextualHistoricalDiscretional,
        "all" => LigatureMode.All,
        _ => LigatureMode.None
    };

    /// <summary>Maps a <see cref="NumberForm"/> to its w14:numForm/@w14:val token, or null for the default.</summary>
    public static string? NumberFormToken(NumberForm form) => form switch
    {
        NumberForm.Lining => "lining",
        NumberForm.OldStyle => "oldStyle",
        _ => null
    };

    /// <summary>Maps a w14:numForm token back to a <see cref="NumberForm"/> (unknown/absent → Default).</summary>
    public static NumberForm NumberFormFromToken(string? token) => token switch
    {
        "lining" => NumberForm.Lining,
        "oldStyle" => NumberForm.OldStyle,
        _ => NumberForm.Default
    };

    /// <summary>Maps a <see cref="NumberSpacing"/> to its w14:numSpacing/@w14:val token, or null for the default.</summary>
    public static string? NumberSpacingToken(NumberSpacing spacing) => spacing switch
    {
        NumberSpacing.Proportional => "proportional",
        NumberSpacing.Tabular => "tabular",
        _ => null
    };

    /// <summary>Maps a w14:numSpacing token back to a <see cref="NumberSpacing"/> (unknown/absent → Default).</summary>
    public static NumberSpacing NumberSpacingFromToken(string? token) => token switch
    {
        "proportional" => NumberSpacing.Proportional,
        "tabular" => NumberSpacing.Tabular,
        _ => NumberSpacing.Default
    };

    /// <summary>Reads an OOXML on/off toggle element (e.g. &lt;w:b/&gt;): present and not explicitly off.</summary>
    public static bool ReadToggle(XElement? parent, string localName)
    {
        var element = parent?.Element(W + localName);
        if (element is null)
            return false;
        return ReadOnOffValue(element.Attribute(W + "val")?.Value, defaultValue: true);
    }

    /// <summary>Reads a WordprocessingML <c>ST_OnOff</c> lexical value, keeping a caller-provided absent default.</summary>
    public static bool ReadOnOffValue(string? value, bool defaultValue = false) =>
        value is null ? defaultValue : value is "1" or "true" or "on";
}
