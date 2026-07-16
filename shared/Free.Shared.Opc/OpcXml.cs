using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public static class OpcXml
{
    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicZipTimestamp;
        using var stream = entry.Open();
        document.Save(stream, saveOptions);
    }

    public static void WriteXmlEntry(ZipArchive archive, string entryPath, XDocument document)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicZipTimestamp;
        using var stream = entry.Open();
        document.Save(stream);
    }
}
