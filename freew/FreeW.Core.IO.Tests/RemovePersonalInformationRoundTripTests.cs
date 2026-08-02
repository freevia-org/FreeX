using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class RemovePersonalInformationRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    [Fact]
    public void EnabledSetting_EmitsCanonicalXmlAndSavesStably()
    {
        var document = new TextDocument { RemovePersonalInformation = true };
        var expectedSettings = SettingsDocument(new XElement(W + "removePersonalInformation"));

        var firstSave = Write(document);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).RemovePersonalInformation.Should().BeTrue();

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).RemovePersonalInformation.Should().BeTrue();
    }

    [Fact]
    public void DefaultOff_OmitsSettingsPartAndReopensOff()
    {
        var bytes = Write(new TextDocument());

        HasEntry(bytes, "word/settings.xml").Should().BeFalse();
        Read(bytes).RemovePersonalInformation.Should().BeFalse();
    }

    [Fact]
    public void EnabledSetting_OverlaysAtCtSettingsSchemaPosition()
    {
        var source = BuildWordAuthoredPackage(SettingsDocument(
            new XElement(W + "zoom", new XAttribute(W + "percent", "100")),
            new XElement(W + "doNotDisplayPageBoundaries")));
        var document = Read(source);
        document.RemovePersonalInformation = true;

        var settings = ReadXml(Write(document), "word/settings.xml");

        settings.Root!.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "zoom",
            "removePersonalInformation",
            "doNotDisplayPageBoundaries");
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
        var sourceElement = new XElement(W + "removePersonalInformation",
            token is null ? null : new XAttribute(W + "val", token));
        var source = BuildWordAuthoredPackage(SettingsDocument(sourceElement));

        var reopened = Read(source);

        reopened.RemovePersonalInformation.Should().Be(expected);

        var firstSave = Write(reopened);
        var expectedSettings = SettingsDocument(
            expected ? new XElement(W + "removePersonalInformation") : null);
        var firstSettings = ReadXml(firstSave, "word/settings.xml");

        XNode.DeepEquals(expectedSettings, firstSettings).Should().BeTrue(
            "expected {0} but found {1}", expectedSettings, firstSettings);
        Read(firstSave).RemovePersonalInformation.Should().Be(expected);

        var secondSave = Write(Read(firstSave));

        XNode.DeepEquals(firstSettings, ReadXml(secondSave, "word/settings.xml")).Should().BeTrue();
        Read(secondSave).RemovePersonalInformation.Should().Be(expected);
    }

    [Fact]
    public void EnabledSetting_AnonymizesGeneratedPersonalMetadataWithoutMutatingModel()
    {
        var document = PersonalizedDocument(removePersonalInformation: true);

        var bytes = Write(document);

        var core = ReadXml(bytes, "docProps/core.xml");
        core.Root!.Element(Dc + "creator").Should().BeNull();
        core.Root.Element(Cp + "lastModifiedBy").Should().NotBeNull();
        core.Root.Element(Cp + "lastModifiedBy")!.Value.Should().BeEmpty();

        var documentXml = ReadXml(bytes, "word/document.xml");
        documentXml.Descendants().Attributes(W + "author")
            .Select(attribute => attribute.Value)
            .Should().HaveCount(3).And.OnlyContain(author => author == "Author");

        var comments = ReadXml(bytes, "word/comments.xml");
        comments.Descendants(W + "comment").Attributes(W + "author")
            .Select(attribute => attribute.Value)
            .Should().Equal("Author", "Author");
        comments.Descendants(W + "comment").Attributes(W + "initials")
            .Select(attribute => attribute.Value)
            .Should().Equal("A", "A");

        document.Properties.Author.Should().Be("Core Alice");
        document.Properties.LastModifiedBy.Should().Be("Core Bob");
        document.Comments[1].Author.Should().Be("Editor");
        document.Comments[1].Replies.Single().Author.Should().Be("Lead Author");
        document.Paragraphs.Single().Runs.Single().RevisionAuthor.Should().Be("Revision Alice");
    }

    [Fact]
    public void DisabledSetting_PreservesGeneratedPersonalMetadata()
    {
        var bytes = Write(PersonalizedDocument(removePersonalInformation: false));

        var core = ReadXml(bytes, "docProps/core.xml");
        core.Root!.Element(Dc + "creator")!.Value.Should().Be("Core Alice");
        core.Root.Element(Cp + "lastModifiedBy")!.Value.Should().Be("Core Bob");

        var documentXml = ReadXml(bytes, "word/document.xml");
        documentXml.Descendants().Attributes(W + "author")
            .Select(attribute => attribute.Value)
            .Should().Equal("Paragraph Carol", "Revision Alice", "Format Bob");

        var comments = ReadXml(bytes, "word/comments.xml");
        comments.Descendants(W + "comment").Attributes(W + "author")
            .Select(attribute => attribute.Value)
            .Should().Equal("Editor", "Lead Author");
        comments.Descendants(W + "comment").Attributes(W + "initials")
            .Select(attribute => attribute.Value)
            .Should().Equal("ED", "LA");
    }

    private static TextDocument PersonalizedDocument(bool removePersonalInformation)
    {
        var document = TextDocument.CreateEmpty();
        document.RemovePersonalInformation = removePersonalInformation;
        document.Properties.Author = "Core Alice";
        document.Properties.LastModifiedBy = "Core Bob";
        document.Blocks.Clear();

        var paragraph = new Paragraph
        {
            ParagraphFormatRevision = new ParagraphFormatRevision(
                ParagraphFormatting.Default,
                "Paragraph Carol",
                "2026-08-02T04:00:00Z")
        };
        paragraph.Runs.Add(new Run("tracked")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Revision Alice",
            RevisionDateXml = "2026-08-02T04:01:00Z",
            Formatting = RunFormatting.Default with { Bold = true },
            FormatRevision = new FormatRevision(
                RunFormatting.Default,
                "Format Bob",
                "2026-08-02T04:02:00Z")
        });
        document.Blocks.Add(paragraph);

        var comment = new Comment(1, "First", "Editor", "ED");
        comment.AddReply(2, "Reply", "Lead Author", "LA");
        document.Comments[comment.Id] = comment;
        paragraph.Runs[0].CommentId = comment.Id;
        return document;
    }

    private static XDocument SettingsDocument(params XElement?[] settings)
    {
        var root = new XElement(W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            settings);
        if (settings.Length == 0 || settings.All(setting => setting is null))
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
