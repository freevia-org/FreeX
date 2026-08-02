using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class EmbedSystemFontsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlReopensAndSavesStably()
    {
        var firstSave = Write(new TextDocument { EmbedSystemFonts = true });
        var expected = SettingsDocument(new XElement(W + "embedSystemFonts"));

        XNode.DeepEquals(expected, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        Read(firstSave).EmbedSystemFonts.Should().BeTrue();
        SchemaErrors(firstSave).Should().BeEmpty();

        var secondSave = Write(Read(firstSave));
        XNode.DeepEquals(expected, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).EmbedSystemFonts.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).EmbedSystemFonts.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void WordAuthoredOnOffForms_CanonicalizeAndRemainStable(string? token, bool expected)
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "embedSystemFonts",
                token is null ? null : new XAttribute(W + "val", token))));

        var reopened = Read(source);
        reopened.EmbedSystemFonts.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(expected ? new XElement(W + "embedSystemFonts") : null);
        XNode.DeepEquals(expectedSettings, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();

        var secondSave = Write(Read(firstSave));
        XNode.DeepEquals(expectedSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
    }

    [Fact]
    public void ModelAuthority_PreservesSchemaOrderAndRemovesDisabledPreservedValue()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "printFormsData"),
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveSubsetFonts")));
        var document = Read(source);
        document.EmbedSystemFonts.Should().BeTrue();

        document.EmbedSystemFonts = false;
        var disabledSave = Write(document);
        var disabledExpected = SettingsDocument(
            new XElement(W + "printFormsData"),
            new XElement(W + "saveSubsetFonts"));
        XNode.DeepEquals(disabledExpected, ReadXml(disabledSave, "word/settings.xml")).Should().BeTrue();

        var reopened = Read(disabledSave);
        reopened.EmbedSystemFonts = true;
        var enabledSave = Write(reopened);
        var enabledExpected = SettingsDocument(
            new XElement(W + "printFormsData"),
            new XElement(W + "embedSystemFonts"),
            new XElement(W + "saveSubsetFonts"));
        XNode.DeepEquals(enabledExpected, ReadXml(enabledSave, "word/settings.xml")).Should().BeTrue();
        SchemaErrors(enabledSave).Should().BeEmpty();
    }

    [Fact]
    public void EmbeddedFontBytes_RetainExactlyAcrossReopenAndSecondSave()
    {
        var fontBytes = Enumerable.Range(0, 64).Select(i => (byte)(i * 5)).ToArray();
        var document = new TextDocument { EmbedSystemFonts = true };
        document.EmbeddedFonts.Add(new EmbeddedFont("Common Policy Sans", Regular: fontBytes));

        var firstSave = Write(document);
        XNode.DeepEquals(SettingsDocument(
                new XElement(W + "embedTrueTypeFonts"),
                new XElement(W + "embedSystemFonts")),
            ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        var reopened = Read(firstSave);
        reopened.EmbedSystemFonts.Should().BeTrue();
        reopened.EmbeddedFonts.Single().Regular.Should().Equal(fontBytes);

        var secondSave = Write(reopened);
        Read(secondSave).EmbeddedFonts.Single().Regular.Should().Equal(fontBytes);
        SchemaErrors(secondSave).Should().BeEmpty();
    }

    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static XDocument SettingsDocument(params XElement?[] settings)
    {
        var root = new XElement(W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), settings);
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
