using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Free.Shared.IO;

public sealed record OpenDocumentXmlEntryOptions(
    bool OmitXmlDeclaration,
    bool Indent,
    string? NewLineChars,
    NewLineHandling NewLineHandling,
    bool CloseOutput);

public sealed record OpenDocumentManifestOptions(
    string Version,
    string? RootEntryVersion,
    bool IncludeXmlDeclaration);

public sealed record OpenDocumentManifestEntry(string Path, string MediaType);

/// <summary>Shared ODF ZIP and manifest mechanics; document serialization remains product-owned.</summary>
public static class OpenDocumentPackageWriter
{
    public const string ManifestNamespace = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public static void WriteMimeType(ZipArchive archive, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        var entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using var stream = entry.Open();
        var bytes = Encoding.ASCII.GetBytes(mediaType);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static void WriteXmlEntry(
        ZipArchive archive,
        string name,
        XDocument document,
        OpenDocumentXmlEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = options.CloseOutput,
            Indent = options.Indent,
            OmitXmlDeclaration = options.OmitXmlDeclaration,
            NewLineChars = options.NewLineChars ?? Environment.NewLine,
            NewLineHandling = options.NewLineHandling,
        });
        document.Save(writer);
    }

    public static XDocument BuildManifest(
        string packageMediaType,
        IEnumerable<OpenDocumentManifestEntry> entries,
        OpenDocumentManifestOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageMediaType);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Version);

        XNamespace manifest = ManifestNamespace;
        var root = new XElement(
            manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", manifest.NamespaceName),
            new XAttribute(manifest + "version", options.Version),
            CreateManifestEntry(manifest, "/", packageMediaType, options.RootEntryVersion));

        foreach (var entry in entries)
            root.Add(CreateManifestEntry(manifest, entry.Path, entry.MediaType, version: null));

        var document = new XDocument(root);
        if (options.IncludeXmlDeclaration)
            document.Declaration = new XDeclaration("1.0", "UTF-8", standalone: null);

        return document;
    }

    private static XElement CreateManifestEntry(
        XNamespace manifest,
        string path,
        string mediaType,
        string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mediaType);

        var entry = new XElement(
            manifest + "file-entry",
            new XAttribute(manifest + "full-path", path),
            new XAttribute(manifest + "media-type", mediaType));
        if (version is not null)
            entry.SetAttributeValue(manifest + "version", version);

        return entry;
    }
}
