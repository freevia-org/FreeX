using System.Xml.Linq;

namespace Free.Shared.IO;

/// <summary>
/// The OpenDocument (ODF) XML namespaces the apps' ODF adapters use. FreeX's ODS adapter and FreeW's ODT
/// adapter each declared their own copy of these URI literals; they are defined once here and each
/// adapter keeps its own field names for the subset it touches.
/// </summary>
public static class OdfNamespaces
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    public static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    public static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    public static readonly XNamespace Number = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";

    /// <summary>The OpenFormula namespace, which a document must declare to use <c>of:=</c> formula prefixes.</summary>
    public static readonly XNamespace OpenFormula = "urn:oasis:names:tc:opendocument:xmlns:of:1.2";

    /// <summary>XLink, where ODF puts a hyperlink or image target (<c>xlink:href</c>).</summary>
    public static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    /// <summary>Dublin Core, used for document metadata (<c>dc:title</c>, <c>dc:creator</c>).</summary>
    public static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
}
