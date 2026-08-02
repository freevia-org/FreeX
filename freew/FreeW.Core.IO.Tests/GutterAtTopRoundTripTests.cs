using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class GutterAtTopRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlAndSavesStably()
    {
        var document = new TextDocument();
        document.Page.GutterPt = 36;
        document.Page.GutterAtTop = true;
        var expectedSettings = SettingsDocument(new XElement(W + "gutterAtTop"));

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        var reopened = Read(firstSave);
        reopened.Page.GutterAtTop.Should().BeTrue();
        reopened.Page.GutterPt.Should().BeApproximately(36, 0.01);

        var secondSave = Write(reopened);

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).Page.GutterAtTop.Should().BeTrue();
    }

    [Fact]
    public void DefaultSideGutter_OmitsSettingsPartAndReopensOff()
    {
        var document = new TextDocument();
        document.Page.GutterPt = 36;

        var bytes = Write(document);

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        var reopened = Read(bytes);
        reopened.Page.GutterAtTop.Should().BeFalse();
        reopened.Page.GutterPt.Should().BeApproximately(36, 0.01);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void WordAuthoredOnOffForms_ReopenCanonicalizeAndSaveStably(string? token, bool expected)
    {
        var sourceElement = new XElement(W + "gutterAtTop",
            token is null ? null : new XAttribute(W + "val", token));
        var source = BuildWordAuthoredPackage(SettingsDocument(sourceElement));

        var reopened = Read(source);

        reopened.Page.GutterAtTop.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(expected ? new XElement(W + "gutterAtTop") : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).Page.GutterAtTop.Should().Be(expected);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).Page.GutterAtTop.Should().Be(expected);
    }

    [Fact]
    public void EnabledSetting_OverlaysAtCtSettingsSchemaPosition()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "bordersDoNotSurroundFooter"),
            new XElement(W + "hideSpellingErrors")));
        var document = Read(source);

        document.Page.GutterAtTop = true;

        var saved = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "bordersDoNotSurroundFooter"),
            new XElement(W + "gutterAtTop"),
            new XElement(W + "hideSpellingErrors"));

        XNode.DeepEquals(expectedSettings, ReadXml(saved, "word/settings.xml")).Should().BeTrue();
        Read(saved).Page.GutterAtTop.Should().BeTrue();
    }

    private static XDocument SettingsDocument(params XElement?[] settings)
    {
        var root = new XElement(W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            settings);
        if (settings.All(setting => setting is null))
            root.Value = string.Empty;
        return new XDocument(root);
    }

    private static byte[] BuildWordAuthoredPackage(XDocument settings)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "word/document.xml",
                new XDocument(new XElement(W + "document",
                    new XElement(W + "body", new XElement(W + "p")))));
            AddXml(archive, "word/settings.xml", settings);
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
