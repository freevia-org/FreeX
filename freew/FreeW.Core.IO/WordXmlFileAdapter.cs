using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Reads and writes Word's Flat OPC XML ("Word XML Document", <c>.xml</c>): a single XML file whose
/// <c>&lt;pkg:part&gt;</c> elements inline the very OOXML parts a <c>.docx</c> ZIP would contain. Rather than
/// re-implementing the WordprocessingML mapping, this adapter transcodes between the flat XML and an
/// in-memory OPC ZIP and delegates to the existing <see cref="DocxReader"/>/<see cref="DocxWriter"/> — so the
/// full engine (incl. <c>PreservedParts</c>) is reused without touching it. The older Word 2003 WordML
/// schema (<c>&lt;w:wordDocument&gt;</c> root) also uses <c>.xml</c> but is a different format; <see cref="Load"/>
/// sniffs the root element and dispatches it to the read-only <see cref="Wordml2003Reader"/>. <see cref="Save"/>
/// always writes Flat OPC (the 2003 schema is read-only).
/// </summary>
public sealed class WordXmlFileAdapter : IDocumentFileAdapter
{
    private static readonly XNamespace Pkg = "http://schemas.microsoft.com/office/2006/xmlPackage";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsContentType = "application/vnd.openxmlformats-package.relationships+xml";

    public string Extension => ".xml";
    public string FormatName => "Word XML Document";

    public TextDocument Load(Stream stream)
    {
        XDocument flat;
        using (var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create()))
            flat = XDocument.Load(reader);

        var root = flat.Root;

        // Both Flat OPC and the older Word 2003 WordML schema use the .xml extension; sniff the root element
        // to dispatch. <w:wordDocument> is the read-only 2003 schema; <pkg:package> is Flat OPC (below).
        if (root is not null && root.Name == Wordml2003Reader.RootName)
            return Wordml2003Reader.Read(root);

        if (root is null || root.Name != Pkg + "package")
        {
            throw new InvalidDataException(
                "Not a recognised Word XML document: the root element is neither <pkg:package> (Flat OPC) " +
                "nor <w:wordDocument> (Word 2003 WordprocessingML).");
        }

        // Rehydrate the inline parts into an in-memory .docx package, then hand it to the existing reader.
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var contentTypes = new XElement(Ct + "Types");

            foreach (var part in root.Elements(Pkg + "part"))
            {
                var name = (string?)part.Attribute(Pkg + "name");
                if (string.IsNullOrEmpty(name))
                    continue;

                var entry = archive.CreateEntry(name.TrimStart('/'), CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    var xmlData = part.Element(Pkg + "xmlData");
                    var binaryData = part.Element(Pkg + "binaryData");
                    if (xmlData?.Elements().FirstOrDefault() is { } payload)
                    {
                        using var xw = XmlWriter.Create(entryStream, XmlPartWriterSettings());
                        new XDocument(payload).Save(xw);
                    }
                    else if (binaryData is not null)
                    {
                        // Convert.FromBase64String ignores embedded whitespace/newlines.
                        var bytes = Convert.FromBase64String(binaryData.Value);
                        entryStream.Write(bytes, 0, bytes.Length);
                    }
                }

                // Flat OPC has no [Content_Types].xml part — content types live on each part's attribute. Re-emit
                // each as a per-part Override (valid OPC and unambiguous) so the synthesized package is typed.
                var contentType = (string?)part.Attribute(Pkg + "contentType");
                if (!string.IsNullOrEmpty(contentType))
                    contentTypes.Add(new XElement(Ct + "Override",
                        new XAttribute("PartName", name),
                        new XAttribute("ContentType", contentType)));
            }

            contentTypes.AddFirst(
                new XElement(Ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", RelationshipsContentType)),
                new XElement(Ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));

            var ctEntry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
            using var ctStream = ctEntry.Open();
            using var ctWriter = XmlWriter.Create(ctStream, XmlPartWriterSettings());
            new XDocument(contentTypes).Save(ctWriter);
        }

        buffer.Position = 0;
        return DocxReader.Read(buffer);
    }

    public void Save(TextDocument document, Stream stream)
    {
        // Produce a normal .docx in memory, then re-frame its parts as Flat OPC <pkg:part> elements.
        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;

        var package = new XElement(Pkg + "package", new XAttribute(XNamespace.Xmlns + "pkg", Pkg.NamespaceName));

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true))
        {
            var (defaults, overrides) = ReadContentTypes(archive);

            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith('/'))
                    continue;
                if (string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                    continue; // implicit in Flat OPC

                var partName = "/" + entry.FullName;
                var contentType = ResolveContentType(entry.FullName, defaults, overrides);

                byte[] bytes;
                using (var es = entry.Open())
                using (var ms = new MemoryStream())
                {
                    es.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                var partElement = new XElement(Pkg + "part",
                    new XAttribute(Pkg + "name", partName),
                    new XAttribute(Pkg + "contentType", contentType));

                if (IsXmlContentType(contentType))
                {
                    XDocument partXml;
                    using (var ms = new MemoryStream(bytes))
                    using (var reader = XmlReader.Create(ms, SecureXmlReaderSettings.Create()))
                        partXml = XDocument.Load(reader);
                    partElement.Add(new XElement(Pkg + "xmlData", partXml.Root));
                }
                else
                {
                    partElement.Add(new XElement(Pkg + "binaryData", Convert.ToBase64String(bytes)));
                }

                package.Add(partElement);
            }
        }

        var flat = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XProcessingInstruction("mso-application", "progid=\"Word.Document\""),
            package);

        using var xw = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        flat.Save(xw);
    }

    private static XmlWriterSettings XmlPartWriterSettings() =>
        new() { Encoding = new UTF8Encoding(false), CloseOutput = false };

    private static (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides) ReadContentTypes(ZipArchive archive)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null)
            return (defaults, overrides);

        XDocument doc;
        using (var es = entry.Open())
        using (var reader = XmlReader.Create(es, SecureXmlReaderSettings.Create()))
            doc = XDocument.Load(reader);

        foreach (var element in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (element.Name == Ct + "Default")
            {
                var ext = (string?)element.Attribute("Extension");
                var ct = (string?)element.Attribute("ContentType");
                if (!string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(ct))
                    defaults[ext] = ct;
            }
            else if (element.Name == Ct + "Override")
            {
                var partName = (string?)element.Attribute("PartName");
                var ct = (string?)element.Attribute("ContentType");
                if (!string.IsNullOrEmpty(partName) && !string.IsNullOrEmpty(ct))
                    overrides[partName] = ct;
            }
        }

        return (defaults, overrides);
    }

    private static string ResolveContentType(
        string entryFullName,
        Dictionary<string, string> defaults,
        Dictionary<string, string> overrides)
    {
        if (overrides.TryGetValue("/" + entryFullName, out var byOverride))
            return byOverride;

        var ext = Path.GetExtension(entryFullName).TrimStart('.');
        if (ext.Length > 0 && defaults.TryGetValue(ext, out var byDefault))
            return byDefault;

        return "application/octet-stream";
    }

    private static bool IsXmlContentType(string contentType) =>
        contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
}
