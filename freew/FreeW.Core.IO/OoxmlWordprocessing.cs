using System.Globalization;
using System.Xml.Linq;
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
    public static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

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
    /// The DrawingML chart namespace (c), used by the chart part (c:chartSpace / c:barChart / c:ser / …)
    /// and by the a:graphicData that references the chart from the inline w:drawing.
    /// </summary>
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
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

    /// <summary>The OPC content type + relationship type for an embedded OLE object's binary part.</summary>
    public const string OleObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";
    public const string OleObjectRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";

    /// <summary>The relationship type for an inline image / OLE presentation media part (shared with pictures).</summary>
    public const string ImageRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

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
    public static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";
    public static readonly XNamespace DcmiType = "http://purl.org/dc/dcmitype/";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public const string CorePropertiesContentType = "application/vnd.openxmlformats-package.core-properties+xml";
    public const string CorePropertiesRelType = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string CorePropertiesPartName = "/docProps/core.xml";

    // OPC custom properties (docProps/custom.xml): used best-effort to persist the page watermark text.
    public static readonly XNamespace CustomProps = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    public static readonly XNamespace VtVariant = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    public const string CustomPropertiesContentType = "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    public const string CustomPropertiesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    public const string CustomPropertiesPartName = "/docProps/custom.xml";

    /// <summary>The custom-property name under which the FreeW page watermark text is persisted.</summary>
    public const string WatermarkPropertyName = "FreeWWatermark";

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

    /// <summary>
    /// The bibliography namespace (b), used by word/bibliography/sources.xml — Word's store for the
    /// document's citation sources (b:Sources/b:Source) and the selected bibliography style
    /// (b:Sources/@SelectedStyle). FreeW persists its <see cref="Source"/> list and
    /// <see cref="TextDocument.BibliographyStyle"/> here so both survive a save/load. The part is referenced
    /// from word/document.xml.rels via the bibliography relationship type.
    /// </summary>
    public static readonly XNamespace B = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography";
    public const string BibliographyContentType = "application/vnd.openxmlformats-officedocument.bibliography+xml";
    public const string BibliographyRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/bibliography";
    public const string BibliographyPartName = "/word/bibliography/sources.xml";

    // word/webSettings.xml carries web-page-export settings FreeW does not model; preserved verbatim.
    public const string WebSettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml";
    public const string WebSettingsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings";

    // customXml/itemN.xml stores arbitrary custom XML data parts FreeW does not model; preserved verbatim.
    // Each item references its own customXml/itemPropsN.xml (the data-store item id/schema) via the item's
    // own customXml/_rels/itemN.xml.rels. The document→item relationship uses the customXml rel type.
    public const string CustomXmlRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
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
    public static string ImageContentTypeForExtension(string extension) => extension switch
    {
        "jpeg" or "jpg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "tiff" or "tif" => "image/tiff",
        "emf" => "image/x-emf",
        "wmf" => "image/x-wmf",
        _ => "image/png"
    };

    /// <summary>W3CDTF as used by dcterms:created/modified (UTC, second precision, trailing 'Z').</summary>
    public static string ToW3CDtf(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseW3CDtf(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v) ? v : null;

    /// <summary>DrawingML "EMU" = English Metric Units; 914400 per inch, 12700 per point.</summary>
    public const long EmuPerPoint = 12700;

    public static long PointsToEmu(double points) => (long)Math.Round(points * EmuPerPoint);

    public static double EmuToPoints(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v / (double)EmuPerPoint : 0;

    /// <summary>OOXML "dxa" = twentieths of a point.</summary>
    public static double DxaToPoints(string? value) => ParseInt(value) / 20.0;

    public static int PointsToDxa(double points) => (int)Math.Round(points * 20.0);

    /// <summary>Run font size is in half-points.</summary>
    public static double? HalfPointsToPoints(string? value) => ParseInt(value) is var v && v != 0 ? v / 2.0 : null;

    public static int PointsToHalfPoints(double points) => (int)Math.Round(points * 2.0);

    /// <summary>Border widths (w:sz on w:pBdr / w:tblBorders edges) are in eighths of a point.</summary>
    public static double EighthPointsToPoints(string? value) => ParseInt(value) / 8.0;

    public static int PointsToEighthPoints(double points) => Math.Max(1, (int)Math.Round(points * 8.0));

    public static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

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
        var val = element.Attribute(W + "val")?.Value;
        return val is null or "1" or "true" or "on";
    }
}
