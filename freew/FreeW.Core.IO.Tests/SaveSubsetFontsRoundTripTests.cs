using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class SaveSubsetFontsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlAndSavesStably()
    {
        var document = new TextDocument { SaveSubsetFonts = true };
        var expectedSettings = SettingsDocument(new XElement(W + "saveSubsetFonts"));

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).SaveSubsetFonts.Should().BeTrue();
        SchemaErrors(firstSave).Should().BeEmpty();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).SaveSubsetFonts.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).SaveSubsetFonts.Should().BeFalse();
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
        var sourceElement = new XElement(W + "saveSubsetFonts",
            token is null ? null : new XAttribute(W + "val", token));
        var source = BuildWordAuthoredPackage(SettingsDocument(sourceElement));

        var reopened = Read(source);

        reopened.SaveSubsetFonts.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(expected ? new XElement(W + "saveSubsetFonts") : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).SaveSubsetFonts.Should().Be(expected);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).SaveSubsetFonts.Should().Be(expected);
    }

    [Fact]
    public void EnabledSetting_OverlaysAtCtSettingsSchemaPosition()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "printFormsData"),
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveFormsData")));
        var document = Read(source);

        document.SaveSubsetFonts = true;

        var saved = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "printFormsData"),
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveSubsetFonts"),
            new XElement(W + "saveFormsData"));

        XNode.DeepEquals(expectedSettings, ReadXml(saved, "word/settings.xml")).Should().BeTrue();
        Read(saved).SaveSubsetFonts.Should().BeTrue();
    }

    [Fact]
    public void DisabledSetting_RemovesPreservedValueAndRetainsUnmodelledNeighbours()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveSubsetFonts"),
            new XElement(W + "saveFormsData")));
        var document = Read(source);
        document.SaveSubsetFonts.Should().BeTrue();

        document.SaveSubsetFonts = false;

        var firstSave = Write(document);
        var expectedSettings = SettingsDocument(
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveFormsData"));
        XNode.DeepEquals(expectedSettings, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        Read(firstSave).SaveSubsetFonts.Should().BeFalse();

        var secondSave = Write(Read(firstSave));
        XNode.DeepEquals(expectedSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).SaveSubsetFonts.Should().BeFalse();
    }

    [Fact]
    public void EmbeddedFontBytes_RetainExactlyAcrossReopenAndSecondSave()
    {
        var fontBytes = Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray();
        var document = new TextDocument { SaveSubsetFonts = true };
        document.Page.BackgroundColorHex = "#FFFFFF";
        document.EmbeddedFonts.Add(new EmbeddedFont("Subset Policy Sans", Regular: fontBytes));
        var expectedSettings = SettingsDocument(
            new XElement(W + "displayBackgroundShape"),
            new XElement(W + "embedTrueTypeFonts"),
            new XElement(W + "saveSubsetFonts"));

        var firstSave = Write(document);
        XNode.DeepEquals(expectedSettings, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        SchemaErrors(firstSave).Should().BeEmpty();

        var reopened = Read(firstSave);
        reopened.SaveSubsetFonts.Should().BeTrue();
        reopened.EmbeddedFonts.Should().ContainSingle();
        reopened.EmbeddedFonts[0].Regular.Should().Equal(fontBytes);

        var secondSave = Write(reopened);
        XNode.DeepEquals(expectedSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        var reopenedAgain = Read(secondSave);
        reopenedAgain.SaveSubsetFonts.Should().BeTrue();
        reopenedAgain.EmbeddedFonts[0].Regular.Should().Equal(fontBytes);
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
