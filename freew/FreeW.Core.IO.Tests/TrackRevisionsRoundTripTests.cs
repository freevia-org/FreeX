using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class TrackRevisionsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void WordAuthoredSetting_ReopensCanonicalizesAndSavesStably()
    {
        const string sourceSettingsXml =
            "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:trackRevisions w:val=\"on\"/>" +
            "</w:settings>";
        var expectedCanonicalSettings = new XDocument(
            new XElement(W + "settings",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "trackRevisions")));

        var source = BuildWordAuthoredPackage(sourceSettingsXml);
        var reopened = Read(source);

        reopened.TrackRevisions.Should().BeTrue();

        var firstSave = Write(reopened);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");
        XNode.DeepEquals(expectedCanonicalSettings, firstSettings).Should().BeTrue();
        Read(firstSave).TrackRevisions.Should().BeTrue();

        var secondSave = Write(Read(firstSave));
        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).TrackRevisions.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPart()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).TrackRevisions.Should().BeFalse();
    }

    private static byte[] BuildWordAuthoredPackage(string settingsXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "word/document.xml",
                new XDocument(new XElement(W + "document",
                    new XElement(W + "body", new XElement(W + "p")))));
            AddXml(archive, "word/settings.xml", XDocument.Parse(settingsXml));
        }

        return stream.ToArray();
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static XDocument ReadXml(byte[] bytes, string entryName)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(entry);
    }

    private static bool HasEntry(byte[] bytes, string entryName)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.GetEntry(entryName) is not null;
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        document.Save(writer);
    }
}
