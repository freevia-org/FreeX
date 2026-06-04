using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

internal static class XlsxPackageTestFixtures
{
    public static MemoryStream CreatePackage(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    public static XDocument LoadPackageXml(ZipArchive archive, string entryName, string? missingBecause = null)
    {
        var entry = archive.GetEntry(entryName);
        if (missingBecause is null)
            entry.Should().NotBeNull();
        else
            entry.Should().NotBeNull(missingBecause);

        return LoadPackageXml(entry!);
    }

    public static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    public static string RelationshipsXml(params string[] relationships) =>
        $$"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {{string.Join(Environment.NewLine, relationships)}}
        </Relationships>
        """;

    public static string Relationship(string id, string type, string target) =>
        $"""<Relationship Id="{id}" Type="{type}" Target="{target}" />""";
}
