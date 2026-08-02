using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class HideGrammaticalErrorsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlAndSavesStably()
    {
        var document = new TextDocument { HideGrammaticalErrors = true };
        var expectedSettings = SettingsDocument(new XElement(W + "hideGrammaticalErrors"));

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).HideGrammaticalErrors.Should().BeTrue();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).HideGrammaticalErrors.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).HideGrammaticalErrors.Should().BeFalse();
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
        var sourceElement = new XElement(W + "hideGrammaticalErrors",
            token is null ? null : new XAttribute(W + "val", token));
        var source = BuildWordAuthoredPackage(SettingsDocument(sourceElement));

        var reopened = Read(source);

        reopened.HideGrammaticalErrors.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(
            expected ? new XElement(W + "hideGrammaticalErrors") : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).HideGrammaticalErrors.Should().Be(expected);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).HideGrammaticalErrors.Should().Be(expected);
    }

    [Fact]
    public void EnabledSetting_OverlaysAtCtSettingsSchemaPosition()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "hideSpellingErrors"),
            new XElement(W + "activeWritingStyle")));
        var document = Read(source);

        document.HideGrammaticalErrors = true;

        var saved = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "hideSpellingErrors"),
            new XElement(W + "hideGrammaticalErrors"),
            new XElement(W + "activeWritingStyle"));

        XNode.DeepEquals(expectedSettings, ReadXml(saved, "word/settings.xml")).Should().BeTrue();
        Read(saved).HideGrammaticalErrors.Should().BeTrue();
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
