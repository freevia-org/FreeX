using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class NumberingStartingValueImportTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData(5, false)]
    [InlineData(42, false)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    public void FirstNumberedListUsesItsDeclaredStartingValue(int start, bool multilevel)
    {
        var document = Read(start, Item(10, "first") + Item(10, "second"), multilevel: multilevel);
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

        paragraphs.Select(paragraph => paragraph.Formatting.ListStartOverride).Should().Equal(start, null);
        paragraphs[0].Formatting.ListKind.Should().Be(multilevel ? ListKind.MultiLevel : ListKind.Number);

        var saved = Save(document);
        var numbering = Part(saved, "word/numbering.xml");
        numbering.Descendants(W + "startOverride").Should().Contain(element =>
            element.Attribute(W + "val")!.Value == start.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var reopened = new MemoryStream(saved);
        DocxReader.Read(reopened).Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.Formatting.ListStartOverride).Should().Equal(start, null);
    }

    [Fact]
    public void FirstListStartingAtOneKeepsTheImplicitDefault()
    {
        var document = Read(1, Item(10, "first") + Item(10, "second"));

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(new int?[] { null, null });
    }

    [Fact]
    public void OmittedAbstractStartUsesTheOoxmlDefaultOfZero()
    {
        var document = Read(null, Item(10, "zero") + Item(10, "one"));

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(0, null);
    }

    [Theory]
    [InlineData("-3")]
    [InlineData("invalid")]
    [InlineData("")]
    public void MalformedExplicitStartingValuesKeepTheSafeFallbackAtOne(string value)
    {
        var document = Read(5, Item(10, "one"),
            $"<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"{value}\"/></w:lvlOverride>");

        document.Blocks.OfType<Paragraph>().Single().Formatting.ListStartOverride.Should().Be(1);
    }

    [Fact]
    public void InterleavedInstancesResumeFromTheirOwnDeclaredStartingValue()
    {
        var document = Read(5,
            Item(10, "A five") + Item(10, "A six") + Item(20, "B five") + Item(20, "B six") + Item(10, "A seven"),
            additionalNumbers: "<w:num w:numId=\"20\"><w:abstractNumId w:val=\"1\"/></w:num>");

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(5, null, 5, null, 7);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(7, true)]
    public void ChildLevelReusesItsDeclaredStartAfterAParentLevelAdvances(int childStart, bool useOverride)
    {
        var levelOverride = useOverride
            ? $"<w:lvlOverride w:ilvl=\"1\"><w:startOverride w:val=\"{childStart}\"/></w:lvlOverride>"
            : "";
        var childLevel = $"""
            <w:lvl w:ilvl="1"><w:start w:val="{(useOverride ? 1 : childStart)}"/>
              <w:numFmt w:val="decimal"/><w:lvlText w:val="%1.%2."/></w:lvl>
            """;
        var document = Read(1,
            Item(10, "parent one") + Item(10, "first child", 1) + Item(10, "next child", 1)
            + Item(10, "parent two") + Item(10, "restarted child", 1),
            levelOverride, multilevel: true, additionalLevels: childLevel);

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(null, childStart, null, null, childStart);
        var expectedMarkers = new[] { "1.", $"1.{childStart}.", $"1.{childStart + 1}.", "2.", $"2.{childStart}." };
        Markers(document).Should().Equal(expectedMarkers);

        using var reopened = new MemoryStream(Save(document));
        // Saving may create dedicated numbering instances and an explicit parent continuation.
        // Their marker sequence must stay identical even when the restart representation changes.
        Markers(DocxReader.Read(reopened)).Should().Equal(expectedMarkers);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void StartOverrideStillAppliesWhenTheInstanceAlsoOverridesTheLevel(bool multilevel, int start)
    {
        var levelOverride = $"""
            <w:lvlOverride w:ilvl="0">
              <w:startOverride w:val="{start}"/>
              <w:lvl w:ilvl="0"><w:start w:val="2"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1)"/></w:lvl>
            </w:lvlOverride>
            """;
        var document = Read(5, Item(10, "seven") + Item(10, "eight"), levelOverride, multilevel: multilevel);

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(start, null);

        using var reopened = new MemoryStream(Save(document));
        DocxReader.Read(reopened).Blocks.OfType<Paragraph>()
            .Select(paragraph => paragraph.Formatting.ListStartOverride).Should().Equal(start, null);
    }

    [Fact]
    public void FullLevelOverrideDoesNotReplaceTheAbstractStartWithoutStartOverride()
    {
        // Word ignores w:start inside a full level override (MS-OE376 §2.1.296).
        // Its separate w:startOverride, when present, is the instance's starting-value override.
        const string levelOverride = """
            <w:lvlOverride w:ilvl="0">
              <w:lvl w:ilvl="0"><w:start w:val="9"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1)"/></w:lvl>
            </w:lvlOverride>
            """;
        var document = Read(5, Item(10, "five") + Item(10, "six"), levelOverride);

        document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Formatting.ListStartOverride)
            .Should().Equal(5, null);
    }

    private static string Item(int numId, string text, int level = 0) =>
        $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr></w:pPr>" +
        $"<w:r><w:t>{text}</w:t></w:r></w:p>";

    private static TextDocument Read(
        int? start,
        string body,
        string levelOverride = "",
        string additionalNumbers = "",
        bool multilevel = false,
        string additionalLevels = "")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>");
            Add("word/styles.xml", $"<w:styles xmlns:w=\"{W}\"/>");
            Add("word/numbering.xml", $"""
                <w:numbering xmlns:w="{W}">
                  <w:abstractNum w:abstractNumId="1">
                    <w:multiLevelType w:val="{(multilevel ? "multilevel" : "singleLevel")}"/>
                    <w:lvl w:ilvl="0">{(start is { } value ? $"<w:start w:val=\"{value}\"/>" : "")}<w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl>
                    {additionalLevels}
                  </w:abstractNum>
                  <w:num w:numId="10"><w:abstractNumId w:val="1"/>{levelOverride}</w:num>
                  {additionalNumbers}
                </w:numbering>
                """);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] Save(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static IReadOnlyList<string> Markers(TextDocument document)
    {
        var state = new MultiLevelListMarkerState(document.MultiLevelList.NumberFormats, document.MultiLevelList.LevelTexts);
        return document.Blocks.OfType<Paragraph>()
            .Select(paragraph => state.Advance(paragraph.Formatting.ListLevel, paragraph.Formatting.ListStartOverride))
            .ToList();
    }

    private static XDocument Part(byte[] package, string path)
    {
        using var stream = new MemoryStream(package);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var part = archive.GetEntry(path)!.Open();
        return XDocument.Load(part);
    }
}
