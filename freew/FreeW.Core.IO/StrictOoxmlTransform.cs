using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeW.Core.IO;

/// <summary>
/// Bidirectional namespace-rewriting transform between ISO/IEC 29500 Strict OOXML and the Transitional
/// OOXML namespace family used internally by FreeW's DocxReader/DocxWriter engine.
///
/// <para>
/// Strict OOXML uses "purl.oclc.org/ooxml/*" namespace URIs, while Transitional uses the
/// "schemas.openxmlformats.org/*" family.  This class rewrites every XML part's namespace
/// declarations and attribute values so the mature transitional engine can be reused unchanged.
/// </para>
/// </summary>
internal static class StrictOoxmlTransform
{
    // -------------------------------------------------------------------------
    // Namespace map: strict URI (key) → transitional URI (value)
    // -------------------------------------------------------------------------
    // Strict URIs follow the "http://purl.oclc.org/ooxml/*" pattern defined in
    // ISO/IEC 29500-1:2012 Annex F, Table F.1.
    // -------------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, string> StrictToTransitional =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // WordprocessingML main
            ["http://purl.oclc.org/ooxml/wordprocessingml/main"]
                = "http://schemas.openxmlformats.org/wordprocessingml/2006/main",

            // DrawingML main
            ["http://purl.oclc.org/ooxml/drawingml/main"]
                = "http://schemas.openxmlformats.org/drawingml/2006/main",

            // DrawingML wordprocessingDrawing (wp)
            ["http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing"]
                = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",

            // DrawingML picture (pic)
            ["http://purl.oclc.org/ooxml/drawingml/picture"]
                = "http://schemas.openxmlformats.org/drawingml/2006/picture",

            // DrawingML chart (c)
            ["http://purl.oclc.org/ooxml/drawingml/chart"]
                = "http://schemas.openxmlformats.org/drawingml/2006/chart",

            // DrawingML diagram (dgm)
            ["http://purl.oclc.org/ooxml/drawingml/diagram"]
                = "http://schemas.openxmlformats.org/drawingml/2006/diagram",

            // OfficeDocument relationships
            ["http://purl.oclc.org/ooxml/officeDocument/relationships"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships",

            // Math (m)
            ["http://purl.oclc.org/ooxml/officeDocument/math"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/math",

            // Bibliography (b)
            ["http://purl.oclc.org/ooxml/officeDocument/bibliography"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/bibliography",

            // Custom properties
            ["http://purl.oclc.org/ooxml/officeDocument/custom-properties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties",

            // Doc property variant types (vt)
            ["http://purl.oclc.org/ooxml/officeDocument/docPropsVTypes"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes",

            // Package content-types (strict version)
            ["http://purl.oclc.org/ooxml/contentTypes"]
                = "http://schemas.openxmlformats.org/package/2006/content-types",

            // Package relationships (strict version)
            ["http://purl.oclc.org/ooxml/relationships"]
                = "http://schemas.openxmlformats.org/package/2006/relationships",
        };

    // Reverse map: transitional → strict
    private static readonly IReadOnlyDictionary<string, string> TransitionalToStrict =
        StrictToTransitional.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    // -------------------------------------------------------------------------
    // Content-type & relationship-type maps
    // -------------------------------------------------------------------------
    // Strict OOXML uses different content-type and relationship-type strings. The
    // OPC spec (ECMA-376 Part 2) defines these in Clause 9.
    // -------------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, string> StrictContentTypeToTransitional =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Main document
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml; charset=UTF-8"]
                = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document.main.xml"]
                = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        };

    // Strict OPC relationship types (used in _rels files) → Transitional
    private static readonly IReadOnlyDictionary<string, string> StrictRelTypeToTransitional =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/styles"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/image"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/hyperlink"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/subDocument"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/subDocument",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/numbering"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/footnotes"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/endnotes"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/comments"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/settings"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/webSettings"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webSettings",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/fontTable"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/theme"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/header"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/footer"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/chart"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/package"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/oleObject"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/bibliography"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/bibliography",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/diagramData"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/diagramLayout"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/diagramQuickStyle"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/diagramColors"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/customXml"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/extended-properties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/custom-properties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/core-properties"]
                = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
            // Strict package relationship types
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/font"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/font",
        };

    // Reverse: transitional rel-types → strict (built from StrictRelTypeToTransitional)
    private static readonly IReadOnlyDictionary<string, string> TransitionalRelTypeToStrict =
        StrictRelTypeToTransitional.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Strict package detector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when the stream appears to be a Strict OOXML .docx package,
    /// by sniffing (a) the root namespace of <c>word/document.xml</c> or (b) the
    /// <c>[Content_Types].xml</c> namespace.  The stream is left positioned at its start.
    /// </summary>
    public static bool IsStrict(Stream stream)
    {
        var startPos = stream.Position;
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            // Strategy 1: peek at word/document.xml root namespace
            var docEntry = archive.GetEntry("word/document.xml");
            if (docEntry is not null)
            {
                using var entryStream = docEntry.Open();
                var ns = PeekRootNamespace(entryStream);
                if (!string.IsNullOrEmpty(ns))
                    return StrictToTransitional.ContainsKey(ns);
            }

            // Strategy 2: peek at [Content_Types].xml namespace
            var ctEntry = archive.GetEntry("[Content_Types].xml");
            if (ctEntry is not null)
            {
                using var entryStream = ctEntry.Open();
                var ns = PeekRootNamespace(entryStream);
                if (!string.IsNullOrEmpty(ns))
                    return StrictToTransitional.ContainsKey(ns);
            }

            return false;
        }
        finally
        {
            stream.Position = startPos;
        }
    }

    /// <summary>
    /// Returns the namespace URI of the document element, or <see langword="null"/> on any error.
    /// </summary>
    private static string? PeekRootNamespace(Stream stream)
    {
        try
        {
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.NamespaceURI;
            }
        }
        catch (XmlException) { }
        return null;
    }

    // -------------------------------------------------------------------------
    // Read path: strict → transitional
    // -------------------------------------------------------------------------

    /// <summary>
    /// Given a Strict OOXML .docx stream, returns a new <see cref="MemoryStream"/> whose zip entries
    /// have had all strict XML namespace URIs rewritten to their transitional equivalents.
    /// The original stream is left open and is not disposed.
    /// </summary>
    public static MemoryStream RewriteStrictToTransitional(Stream strictStream)
    {
        var output = new MemoryStream();
        using (var inputArchive = new ZipArchive(strictStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var outputArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputArchive.Entries)
            {
                var outEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var inStream = entry.Open();
                using var outStream = outEntry.Open();

                if (IsXmlEntry(entry.FullName))
                    RewriteXmlEntry(inStream, outStream, StrictToTransitional, StrictRelTypeToTransitional);
                else
                    inStream.CopyTo(outStream);
            }
        }
        output.Position = 0;
        return output;
    }

    // -------------------------------------------------------------------------
    // Write path: transitional → strict
    // -------------------------------------------------------------------------

    /// <summary>
    /// Given a Transitional OOXML .docx stream, returns a new <see cref="MemoryStream"/> whose zip
    /// entries have had all transitional XML namespace URIs rewritten to their strict equivalents.
    /// The original stream is left open and is not disposed.
    /// </summary>
    public static MemoryStream RewriteTransitionalToStrict(Stream transitionalStream)
    {
        var output = new MemoryStream();
        using (var inputArchive = new ZipArchive(transitionalStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var outputArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputArchive.Entries)
            {
                var outEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var inStream = entry.Open();
                using var outStream = outEntry.Open();

                if (IsXmlEntry(entry.FullName))
                    RewriteXmlEntry(inStream, outStream, TransitionalToStrict, TransitionalRelTypeToStrict);
                else
                    inStream.CopyTo(outStream);
            }
        }
        output.Position = 0;
        return output;
    }

    // -------------------------------------------------------------------------
    // Core XML rewriting
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when the OPC entry name looks like an XML part that may carry
    /// namespace declarations.  Binary parts (images, fonts, VBA blobs) are skipped.
    /// </summary>
    private static bool IsXmlEntry(string fullName) =>
        fullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        || fullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads an XML stream, rewrites every namespace URI found in <paramref name="nsMap"/> (element
    /// namespace, attribute namespace, and the <c>Type</c> attribute value in <c>.rels</c> files), and
    /// writes the result to <paramref name="outputStream"/>.
    /// </summary>
    private static void RewriteXmlEntry(
        Stream inputStream,
        Stream outputStream,
        IReadOnlyDictionary<string, string> nsMap,
        IReadOnlyDictionary<string, string> relTypeMap)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(inputStream, SecureXmlReaderSettings.Create());
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // Not valid XML — copy bytes verbatim (shouldn't happen for well-formed OOXML).
            inputStream.CopyTo(outputStream);
            return;
        }

        RewriteNode(doc.Root, nsMap, relTypeMap);

        using var writer = new XmlTextWriter(outputStream, Encoding.UTF8) { Formatting = Formatting.None };
        doc.Save(writer);
    }

    private static void RewriteNode(
        XElement? element,
        IReadOnlyDictionary<string, string> nsMap,
        IReadOnlyDictionary<string, string> relTypeMap)
    {
        if (element is null)
            return;

        // We need to visit every element in the tree and fix namespace URIs.
        // XLinq tracks namespace URIs in XName.Namespace; to rewrite them we must reconstruct the
        // XName with the mapped namespace.  We walk the tree bottom-up so parent renames don't
        // interfere with already-processed children.
        RewriteElementInPlace(element, nsMap, relTypeMap);
    }

    private static void RewriteElementInPlace(
        XElement element,
        IReadOnlyDictionary<string, string> nsMap,
        IReadOnlyDictionary<string, string> relTypeMap)
    {
        // Recurse children first (bottom-up)
        foreach (var child in element.Elements())
            RewriteElementInPlace(child, nsMap, relTypeMap);

        // Rewrite element name namespace
        var mappedNs = MapNamespace(element.Name.Namespace.NamespaceName, nsMap);
        var newName = mappedNs != element.Name.Namespace.NamespaceName
            ? XName.Get(element.Name.LocalName, mappedNs)
            : element.Name;

        // Collect rewritten attributes
        var newAttribs = new List<XAttribute>();
        foreach (var attr in element.Attributes())
        {
            XAttribute newAttr;
            if (attr.IsNamespaceDeclaration)
            {
                // Rewrite the namespace URI declared in xmlns:prefix="uri" or xmlns="uri"
                var mappedUri = MapNamespace(attr.Value, nsMap);
                newAttr = new XAttribute(attr.Name, mappedUri);
            }
            else
            {
                // Rewrite the attribute's own namespace prefix if it has one
                var attrNsMapped = MapNamespace(attr.Name.Namespace.NamespaceName, nsMap);
                var attrName = attrNsMapped != attr.Name.Namespace.NamespaceName
                    ? XName.Get(attr.Name.LocalName, attrNsMapped)
                    : attr.Name;

                // For .rels files: rewrite the Type attribute value (relationship type URI)
                var attrValue = attr.Value;
                if (attr.Name.LocalName == "Type" && relTypeMap.TryGetValue(attrValue, out var mappedRel))
                    attrValue = mappedRel;

                newAttr = new XAttribute(attrName, attrValue);
            }
            newAttribs.Add(newAttr);
        }

        if (newName != element.Name || HasChanges(element.Attributes(), newAttribs))
        {
            element.Name = newName;
            // Replace all attributes
            element.Attributes().Remove();
            foreach (var a in newAttribs)
                element.Add(a);
        }
    }

    private static string MapNamespace(string uri, IReadOnlyDictionary<string, string> nsMap) =>
        nsMap.TryGetValue(uri, out var mapped) ? mapped : uri;

    private static bool HasChanges(IEnumerable<XAttribute> original, IReadOnlyList<XAttribute> rewritten)
    {
        var orig = original.ToList();
        if (orig.Count != rewritten.Count)
            return true;
        for (var i = 0; i < orig.Count; i++)
            if (orig[i].Name != rewritten[i].Name || orig[i].Value != rewritten[i].Value)
                return true;
        return false;
    }
}
