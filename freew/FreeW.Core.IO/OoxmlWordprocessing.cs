using System.Globalization;
using System.Xml.Linq;

namespace FreeW.Core.IO;

/// <summary>WordprocessingML namespaces and unit helpers shared by the docx reader/writer.</summary>
internal static class Ooxml
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    // DrawingML namespaces used by inline pictures (w:drawing/wp:inline/.../a:blip).
    public static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    // OPC core properties (docProps/core.xml): Dublin Core + the cp / dcterms / xsi vocabularies.
    public static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";
    public static readonly XNamespace DcmiType = "http://purl.org/dc/dcmitype/";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public const string CorePropertiesContentType = "application/vnd.openxmlformats-package.core-properties+xml";
    public const string CorePropertiesRelType = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string CorePropertiesPartName = "/docProps/core.xml";

    public const string NumberingContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
    public const string NumberingRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string NumberingPartName = "/word/numbering.xml";

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

    public static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

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
