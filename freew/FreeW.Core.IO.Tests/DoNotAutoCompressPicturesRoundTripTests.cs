using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class DoNotAutoCompressPicturesRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlReopensAndSavesStably()
    {
        var document = new TextDocument { DoNotAutoCompressPictures = true };
        var expectedSettings = SettingsDocument(new XElement(W + "doNotAutoCompressPictures"));

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).DoNotAutoCompressPictures.Should().BeTrue();
        SchemaErrors(firstSave).Should().BeEmpty();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).DoNotAutoCompressPictures.Should().BeTrue();
    }

    [Fact]
    public void DefaultCompressionPolicy_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).DoNotAutoCompressPictures.Should().BeFalse();
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
        var sourceElement = new XElement(W + "doNotAutoCompressPictures",
            token is null ? null : new XAttribute(W + "val", token));
        var source = BuildWordAuthoredPackage(SettingsDocument(sourceElement));

        var reopened = Read(source);

        reopened.DoNotAutoCompressPictures.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(expected ? new XElement(W + "doNotAutoCompressPictures") : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).DoNotAutoCompressPictures.Should().Be(expected);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).DoNotAutoCompressPictures.Should().Be(expected);
    }

    [Fact]
    public void EnabledSetting_OverlaysAtLateCtSettingsPositionAndPreservesNeighbors()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "hideSpellingErrors"),
            new XElement(W + "compat", new XElement(W + "doNotExpandShiftReturn")),
            new XElement(W + "doNotIncludeSubdocsInStats"),
            new XElement(W + "forceUpgrade"),
            new XElement(W + "decimalSymbol", new XAttribute(W + "val", ","))));
        var document = Read(source);

        document.DoNotAutoCompressPictures = true;

        var saved = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "hideSpellingErrors"),
            new XElement(W + "compat", new XElement(W + "doNotExpandShiftReturn")),
            new XElement(W + "doNotIncludeSubdocsInStats"),
            new XElement(W + "doNotAutoCompressPictures"),
            new XElement(W + "forceUpgrade"),
            new XElement(W + "decimalSymbol", new XAttribute(W + "val", ",")));

        XNode.DeepEquals(expectedSettings, ReadXml(saved, "word/settings.xml")).Should().BeTrue();
        Read(saved).DoNotAutoCompressPictures.Should().BeTrue();
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void ExplicitlyDisabledSetting_IsRemovedWithoutChangingNeighboringSettings()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "doNotIncludeSubdocsInStats"),
            new XElement(W + "doNotAutoCompressPictures"),
            new XElement(W + "forceUpgrade")));
        var document = Read(source);

        document.DoNotAutoCompressPictures = false;

        var firstSave = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "doNotIncludeSubdocsInStats"),
            new XElement(W + "forceUpgrade"));

        XNode.DeepEquals(expectedSettings, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        Read(firstSave).DoNotAutoCompressPictures.Should().BeFalse();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(expectedSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
    }

    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
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
