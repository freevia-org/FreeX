using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.IO;

namespace Free.Shared.Opc;

public static class OpcXml
{
    public static XDocument LoadXml(
        ZipArchiveEntry entry,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        using var stream = entry.Open();
        return LoadXml(stream, maxCharactersInDocument);
    }

    public static XDocument LoadXml(
        ZipArchiveEntry entry,
        LoadOptions loadOptions,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        using var stream = entry.Open();
        return LoadXml(stream, loadOptions, maxCharactersInDocument);
    }

    public static XDocument LoadXml(
        Stream stream,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create(maxCharactersInDocument));
        return XDocument.Load(reader);
    }

    public static XDocument LoadXml(
        Stream stream,
        LoadOptions loadOptions,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create(maxCharactersInDocument));
        return XDocument.Load(reader, loadOptions);
    }

    public static XDocument? LoadXmlOrNull(
        ZipArchive archive,
        string entryPath,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        var entry = archive.GetEntry(entryPath);
        return entry is null ? null : LoadXml(entry, maxCharactersInDocument);
    }

    public static XDocument? TryLoadXml(
        ZipArchive archive,
        string entryPath,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        try
        {
            return LoadXmlOrNull(archive, entryPath, maxCharactersInDocument);
        }
        catch
        {
            return null;
        }
    }

    public static XDocument? TryLoadXml(
        byte[] bytes,
        long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return LoadXml(stream, maxCharactersInDocument);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="document"/> over any existing entries named <paramref name="entryName"/>.
    /// <para>
    /// The document is sanitized (see <see cref="XmlTextSanitizer.SanitizeInPlace"/>) on the way out: the
    /// hand-rolled part writers above this build elements straight from model text, and one C0 control
    /// code or lone surrogate anywhere in that text makes <c>XDocument.Save</c> throw and takes the
    /// WHOLE document save down with it. Doing it here, at the one boundary every such writer funnels
    /// through, is what keeps a write site added later from reintroducing the crash.
    /// </para>
    /// </summary>
    public static void ReplaceXmlEntry(
        ZipArchive archive,
        string entryName,
        XDocument document,
        SaveOptions saveOptions = SaveOptions.DisableFormatting)
    {
        foreach (var existing in archive.Entries
                     .Where(entry => string.Equals(entry.FullName, entryName, StringComparison.Ordinal))
                     .ToList())
        {
            existing.Delete();
        }

        XmlTextSanitizer.SanitizeInPlace(document);

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, saveOptions);
    }

    public static void WriteXmlEntry(
        ZipArchive archive,
        string entryPath,
        XDocument document,
        DateTimeOffset? lastWriteTime = null)
    {
        XmlTextSanitizer.SanitizeInPlace(document);

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        if (lastWriteTime is { } timestamp)
            entry.LastWriteTime = timestamp;
        using var stream = entry.Open();
        document.Save(stream);
    }
}
