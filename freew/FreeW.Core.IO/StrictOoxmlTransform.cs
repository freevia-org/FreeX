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
    private const string StrictRelationshipPrefix = "http://purl.oclc.org/ooxml/officeDocument/relationships/";
    private const string TransitionalRelationshipPrefix = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string LegacyStrictPackageRelationshipNamespace = "http://purl.oclc.org/ooxml/relationships";
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

            // Document properties use camel-case Strict names, unlike Transitional names.
            ["http://purl.oclc.org/ooxml/officeDocument/customProperties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/extendedProperties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/customXml"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/customXml",
            ["http://purl.oclc.org/ooxml/officeDocument/customXmlDataProps"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/customXmlDataProps",
            ["http://purl.oclc.org/ooxml/drawingml/chartDrawing"]
                = "http://schemas.openxmlformats.org/drawingml/2006/chartDrawing",
            ["http://purl.oclc.org/ooxml/drawingml/spreadsheetDrawing"]
                = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing",
            ["http://purl.oclc.org/ooxml/drawingml/lockedCanvas"]
                = "http://schemas.openxmlformats.org/drawingml/2006/lockedCanvas",
            ["http://purl.oclc.org/ooxml/officeDocument/sharedTypes"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/sharedTypes",

            // Read aliases emitted by older FreeW Strict saves.
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

    // OPC content-type/relationship namespaces remain the same in both conformance classes.
    // Accept old FreeW aliases on read, but never write them or the legacy property spellings.
    private static readonly IReadOnlyDictionary<string, string> TransitionalToStrict =
        StrictToTransitional
            .Where(pair => pair.Key is not ("http://purl.oclc.org/ooxml/contentTypes" or LegacyStrictPackageRelationshipNamespace))
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

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
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/extendedProperties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/customProperties"]
                = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            ["http://purl.oclc.org/ooxml/officeDocument/relationships/metadata/thumbnail"]
                = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail",
            // Legacy FreeW aliases are read-only; core properties use the OPC URI in Strict too.
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
        StrictRelTypeToTransitional
            .Where(pair => pair.Value != "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties")
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);

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
        using var inputArchive = new ZipArchive(strictStream, ZipArchiveMode.Read, leaveOpen: true);
        return RewriteStrictToTransitional(inputArchive);
    }

    internal static MemoryStream RewriteStrictToTransitional(ZipArchive inputArchive)
    {
        var output = new MemoryStream();
        using (var outputArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputArchive.Entries)
            {
                var outEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var inStream = entry.Open();
                using var outStream = outEntry.Open();

                if (IsXmlEntry(entry.FullName))
                    RewriteXmlEntry(inStream, outStream, StrictToTransitional, toStrict: false);
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
                    RewriteXmlEntry(inStream, outStream, TransitionalToStrict, toStrict: true);
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
        bool toStrict)
    {
        using var original = new MemoryStream();
        inputStream.CopyTo(original);
        original.Position = 0;
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(original, SecureXmlReaderSettings.Create());
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // Not valid XML — copy bytes verbatim (shouldn't happen for well-formed OOXML).
            original.Position = 0;
            original.CopyTo(outputStream);
            return;
        }

        if (!RewriteNode(doc.Root, nsMap, toStrict))
        {
            original.Position = 0;
            original.CopyTo(outputStream);
            return;
        }

        using var writer = new XmlTextWriter(outputStream, Encoding.UTF8) { Formatting = Formatting.None };
        doc.Save(writer);
    }

    private static bool RewriteNode(
        XElement? element,
        IReadOnlyDictionary<string, string> nsMap,
        bool toStrict)
    {
        if (element is null)
            return false;

        // We need to visit every element in the tree and fix namespace URIs.
        // XLinq tracks namespace URIs in XName.Namespace; to rewrite them we must reconstruct the
        // XName with the mapped namespace.  We walk the tree bottom-up so parent renames don't
        // interfere with already-processed children.
        var changed = false;
        foreach (var node in element.DescendantsAndSelf().Reverse())
            changed |= RewriteElementInPlace(node, nsMap, toStrict);
        return changed;
    }

    private static bool RewriteElementInPlace(
        XElement element,
        IReadOnlyDictionary<string, string> nsMap,
        bool toStrict)
    {
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
                if (attr.Name == "Type" && element.Name.LocalName == "Relationship"
                    && element.Name.NamespaceName is PackageRelationshipNamespace or LegacyStrictPackageRelationshipNamespace)
                    attrValue = MapRelationshipType(attrValue, toStrict);

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
            return true;
        }
        return false;
    }

    private static string MapRelationshipType(string uri, bool toStrict)
    {
        var map = toStrict ? TransitionalRelTypeToStrict : StrictRelTypeToTransitional;
        if (map.TryGetValue(uri, out var mapped))
            return mapped;
        var sourcePrefix = toStrict ? TransitionalRelationshipPrefix : StrictRelationshipPrefix;
        var targetPrefix = toStrict ? StrictRelationshipPrefix : TransitionalRelationshipPrefix;
        return uri.StartsWith(sourcePrefix, StringComparison.Ordinal)
            ? targetPrefix + uri[sourcePrefix.Length..]
            : uri;
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
