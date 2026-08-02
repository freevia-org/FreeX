using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class PageBorderSurroundSettingsRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EnabledCombinations_EmitCanonicalXmlReopenAndSaveStably(bool excludeHeader, bool excludeFooter)
    {
        var document = new TextDocument
        {
            PageBordersDoNotSurroundHeader = excludeHeader,
            PageBordersDoNotSurroundFooter = excludeFooter
        };
        var expected = SettingsDocument(
            excludeHeader ? new XElement(W + "bordersDoNotSurroundHeader") : null,
            excludeFooter ? new XElement(W + "bordersDoNotSurroundFooter") : null);

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expected, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expected, firstSettings);
        AssertValues(Read(firstSave), excludeHeader, excludeFooter);
        SchemaErrors(firstSave).Should().BeEmpty();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        AssertValues(Read(secondSave), excludeHeader, excludeFooter);
    }

    [Fact]
    public void DefaultsOff_OmitSettingsPartAndReopenOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        AssertValues(Read(bytes), expectedHeader: false, expectedFooter: false);
    }

    public static TheoryData<string, string?, bool> WordOnOffForms => new()
    {
        { "bordersDoNotSurroundHeader", null, true },
        { "bordersDoNotSurroundHeader", "1", true },
        { "bordersDoNotSurroundHeader", "true", true },
        { "bordersDoNotSurroundHeader", "on", true },
        { "bordersDoNotSurroundHeader", "0", false },
        { "bordersDoNotSurroundHeader", "false", false },
        { "bordersDoNotSurroundHeader", "off", false },
        { "bordersDoNotSurroundFooter", null, true },
        { "bordersDoNotSurroundFooter", "1", true },
        { "bordersDoNotSurroundFooter", "true", true },
        { "bordersDoNotSurroundFooter", "on", true },
        { "bordersDoNotSurroundFooter", "0", false },
        { "bordersDoNotSurroundFooter", "false", false },
        { "bordersDoNotSurroundFooter", "off", false }
    };

    [Theory]
    [MemberData(nameof(WordOnOffForms))]
    public void WordAuthoredOnOffForms_ReopenCanonicalizeAndSaveStably(
        string settingName,
        string? token,
        bool expected)
    {
        var sourceElement = new XElement(W + settingName,
            token is null ? null : new XAttribute(W + "val", token));
        var reopened = Read(BuildWordAuthoredPackage(SettingsDocument(sourceElement)));
        var expectedHeader = settingName == "bordersDoNotSurroundHeader" && expected;
        var expectedFooter = settingName == "bordersDoNotSurroundFooter" && expected;

        AssertValues(reopened, expectedHeader, expectedFooter);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(expected ? new XElement(W + settingName) : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        AssertValues(Read(firstSave), expectedHeader, expectedFooter);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        AssertValues(Read(secondSave), expectedHeader, expectedFooter);
    }

    [Fact]
    public void EnabledSettings_OverlayAtCtSettingsSchemaPositions()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "saveSubsetFonts"),
            new XElement(W + "alignBordersAndEdges"),
            new XElement(W + "gutterAtTop")));
        var document = Read(source);
        document.PageBordersDoNotSurroundHeader = true;
        document.PageBordersDoNotSurroundFooter = true;

        var saved = Write(document);
        var expected = SettingsDocument(
            new XElement(W + "saveSubsetFonts"),
            new XElement(W + "alignBordersAndEdges"),
            new XElement(W + "bordersDoNotSurroundHeader"),
            new XElement(W + "bordersDoNotSurroundFooter"),
            new XElement(W + "gutterAtTop"));

        XNode.DeepEquals(expected, ReadXml(saved, "word/settings.xml")).Should().BeTrue();
        SchemaErrors(saved).Should().BeEmpty();
        AssertValues(Read(saved), expectedHeader: true, expectedFooter: true);
    }

    [Fact]
    public void DisabledSettings_RemovePreservedValuesAndRetainNeighbours()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "alignBordersAndEdges"),
            new XElement(W + "bordersDoNotSurroundHeader"),
            new XElement(W + "bordersDoNotSurroundFooter"),
            new XElement(W + "gutterAtTop")));
        var document = Read(source);
        AssertValues(document, expectedHeader: true, expectedFooter: true);

        document.PageBordersDoNotSurroundHeader = false;
        document.PageBordersDoNotSurroundFooter = false;

        var firstSave = Write(document);
        var expected = SettingsDocument(
            new XElement(W + "alignBordersAndEdges"),
            new XElement(W + "gutterAtTop"));

        XNode.DeepEquals(expected, ReadXml(firstSave, "word/settings.xml")).Should().BeTrue();
        AssertValues(Read(firstSave), expectedHeader: false, expectedFooter: false);

        var secondSave = Write(Read(firstSave));
        XNode.DeepEquals(expected, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        AssertValues(Read(secondSave), expectedHeader: false, expectedFooter: false);
    }

    private static void AssertValues(TextDocument document, bool expectedHeader, bool expectedFooter)
    {
        document.PageBordersDoNotSurroundHeader.Should().Be(expectedHeader);
        document.PageBordersDoNotSurroundFooter.Should().Be(expectedFooter);
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
