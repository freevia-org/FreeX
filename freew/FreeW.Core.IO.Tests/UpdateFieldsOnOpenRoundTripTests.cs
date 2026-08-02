using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class UpdateFieldsOnOpenRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Pr = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void EnabledSetting_EmitsExactPackageXmlAndReopensStably()
    {
        var document = new TextDocument { UpdateFieldsOnOpen = true };
        document.Blocks.Add(new Paragraph("Field-bearing document"));

        var first = Write(document);
        var firstSettings = ReadXml(first, "word/settings.xml");

        firstSettings.Root!.Elements().Should().ContainSingle();
        firstSettings.Root.Element(W + "updateFields")!.Attribute(W + "val")!.Value.Should().Be("true");
        ReadXml(first, "[Content_Types].xml").Root!.Elements(Ct + "Override")
            .Should().ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/word/settings.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml");
        ReadXml(first, "word/_rels/document.xml.rels").Root!.Elements(Pr + "Relationship")
            .Should().ContainSingle(element =>
                (string?)element.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" &&
                (string?)element.Attribute("Target") == "settings.xml");

        var reopened = Read(first);
        reopened.UpdateFieldsOnOpen.Should().BeTrue();

        var second = Write(reopened);
        XNode.DeepEquals(firstSettings, ReadXml(second, "word/settings.xml")).Should().BeTrue();
        Read(second).UpdateFieldsOnOpen.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).UpdateFieldsOnOpen.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void WordAuthoredOnOffForms_ReopenAndCanonicalize(string? token, bool expected)
    {
        var source = BuildWordAuthoredPackage(token);

        var reopened = Read(source);
        reopened.UpdateFieldsOnOpen.Should().Be(expected);

        var saved = Write(reopened);
        var updateFields = ReadXml(saved, "word/settings.xml").Root!.Element(W + "updateFields");
        if (expected)
            updateFields!.Attribute(W + "val")!.Value.Should().Be("true");
        else
            updateFields.Should().BeNull();

        Read(saved).UpdateFieldsOnOpen.Should().Be(expected);
    }

    private static byte[] BuildWordAuthoredPackage(string? token)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "word/document.xml",
                new XDocument(new XElement(W + "document",
                    new XElement(W + "body", new XElement(W + "p")))));
            AddXml(archive, "word/settings.xml",
                new XDocument(new XElement(W + "settings",
                    new XElement(W + "updateFields",
                        token is null ? null : new XAttribute(W + "val", token)))));
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
