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

    public const string SettingsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml";
    public const string SettingsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    public const string SettingsPartName = "/word/settings.xml";

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
    /// CommentsOnly→"comments", TrackChangesOnly→"trackedChanges".
    /// </summary>
    public static string? ProtectionEditToken(ProtectionMode mode) => mode switch
    {
        ProtectionMode.ReadOnly => "readOnly",
        ProtectionMode.CommentsOnly => "comments",
        ProtectionMode.TrackChangesOnly => "trackedChanges",
        _ => null
    };

    /// <summary>
    /// Maps a w:documentProtection/@w:edit token back to a <see cref="ProtectionMode"/>. Any unknown
    /// or absent token (including "forms"/"none") maps to <see cref="ProtectionMode.None"/>.
    /// </summary>
    public static ProtectionMode ProtectionModeFromEditToken(string? edit) => edit switch
    {
        "readOnly" => ProtectionMode.ReadOnly,
        "comments" => ProtectionMode.CommentsOnly,
        "trackedChanges" => ProtectionMode.TrackChangesOnly,
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
