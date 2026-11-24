using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class ImportedRichNumberingPolicyTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Theory]
    [InlineData(null, "B.1")]
    [InlineData("0", "B.3")]
    public void ExplicitNeverRestartIsDifferentFromOmittedRestart(string? restart, string last)
    {
        var properties = restart is null ? "<w:isLgl w:val=\"0\"/>" : $"<w:lvlRestart w:val=\"{restart}\"/>";
        var document = Read(Numbering(level1Properties: properties, level1Format: "decimal"), 0, 1, 1, 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.1", "A.2", "B", last);
    }

    [Fact]
    public void CustomRestartParentDoesNotRestartAfterAnInterveningLevel()
    {
        var document = Read(Numbering(level2Properties: "<w:lvlRestart w:val=\"1\"/>"),
            0, 1, 2, 2, 1, 2, 0, 1, 2);

        AssertPreservedMarkersAndRoundTrip(document,
            "A", "A.a", "A.a.1", "A.a.2", "A.b", "A.b.3", "B", "B.a", "B.a.1");
    }

    [Fact]
    public void ANonParentRestartIndexIsIgnoredAndUsesTheDefaultParent()
    {
        var document = Read(Numbering(level2Properties: "<w:lvlRestart w:val=\"99\"/>"), 0, 1, 2, 2, 1, 2);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.a", "A.a.1", "A.a.2", "A.b", "A.b.1");
    }

    [Fact]
    public void LegalNumberingDoesNotInventANumberForAReferencedNoneFormat()
    {
        var document = Read(Numbering(level1Format: "none", level2Properties: "<w:isLgl/>"), 0, 1, 2);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.", "1..1");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    public void LegalNumberingUsesDecimalOnlyForTheLevelThatEnablesIt(string? value, bool legal)
    {
        var property = value is null ? "<w:isLgl/>" : $"<w:isLgl w:val=\"{value}\"/>";
        var document = Read(Numbering(level2Properties: property), 0, 1, 1, 2, 2, 0, 1, 2);

        AssertPreservedMarkersAndRoundTrip(document,
            "A", "A.a", "A.b", legal ? "1.2.1" : "A.b.1", legal ? "1.2.2" : "A.b.2",
            "B", "B.a", legal ? "2.1.1" : "B.a.1");
    }

    [Fact]
    public void AFullInstanceOverrideKeepsItsRestartLegalStyleAndExplicitZeroStart()
    {
        var levelOverride = $"""
            <w:lvlOverride w:ilvl="1"><w:startOverride w:val="0"/>
              {Level(1, "lowerLetter", "%1.%2", "<w:lvlRestart w:val=\"0\"/><w:isLgl/>", "9")}
            </w:lvlOverride>
            """;
        var document = Read(Numbering(instanceOverride: levelOverride), 0, 1, 1, 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "1.0", "1.1", "B", "2.0");
        var saved = SavedNumbering(document);
        var savedOverride = saved.Descendants(W + "lvlOverride").Single();
        savedOverride.Element(W + "startOverride")!.Attribute(W + "val")!.Value.Should().Be("0");
        savedOverride.Element(W + "lvl")!.Element(W + "start")!.Attribute(W + "val")!.Value.Should().Be("9",
            "Word ignores a full override's start but its original XML must remain intact");
        savedOverride.Descendants(W + "lvlRestart").Single().Attribute(W + "val")!.Value.Should().Be("0");
        savedOverride.Descendants(W + "isLgl").Should().ContainSingle();
    }

    [Fact]
    public void AFullInstanceOverrideCannotChangeTheAbstractNeverRestartPolicy()
    {
        var levelOverride = $"""
            <w:lvlOverride w:ilvl="1"><w:startOverride w:val="0"/>
              {Level(1, "decimal", "%1.%2", "<w:lvlRestart w:val=\"1\"/>")}
            </w:lvlOverride>
            """;
        var document = Read(Numbering(level1Properties: "<w:lvlRestart w:val=\"0\"/>", instanceOverride: levelOverride),
            0, 1, 1, 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.0", "A.1", "B", "B.2");
    }

    [Fact]
    public void RichInstanceOverridesKeepTheirIndentationInTheLayoutProjection()
    {
        var level = Level(1, "decimal", "%1.%2", "<w:isLgl/>").Replace(
            "</w:lvl>", "<w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl>",
            StringComparison.Ordinal);
        var document = Read(Numbering(instanceOverride: $"<w:lvlOverride w:ilvl=\"1\">{level}</w:lvlOverride>"), 1);
        var formatting = document.Paragraphs.Single().Formatting;

        formatting.ListLevelIndentLeftPt.Should().Be(72);
        formatting.ListLevelFirstLineIndentPt.Should().Be(-18);
        AssertPreservedMarkersAndRoundTrip(document, "1.1");

        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        saved.Position = 0;
        var reopened = DocxReader.Read(saved).Paragraphs.Single().Formatting;
        reopened.ListLevelIndentLeftPt.Should().Be(72);
        reopened.ListLevelFirstLineIndentPt.Should().Be(-18);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2")]
    public void SkippingTheConfiguredParentStillRestartsAfterAHigherAncestor(string? restart)
    {
        var property = restart is null ? "<w:isLgl w:val=\"0\"/>" : $"<w:lvlRestart w:val=\"{restart}\"/>";
        var document = Read(Numbering(level2Properties: property), 0, 1, 2, 2, 0, 2);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.a", "A.a.1", "A.a.2", "B", "B.a.1");
    }

    [Fact]
    public void BulletLevelTextKeepsLiteralPercentSequencesEvenWithHigherLevelNumbers()
    {
        var document = Read(Numbering(level1Properties: "<w:isLgl w:val=\"0\"/>",
            level1Format: "bullet", level1Text: "literal %1 %9"), 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "literal %1 %9");
    }

    [Fact]
    public void AReferenceToADeeperLevelSuppressesTheEntireNumberingText()
    {
        var document = Read(Numbering(level1Properties: "<w:isLgl w:val=\"0\"/>",
            level1Text: "prefix %1 and %3"), 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "");
        PreservedNumberingMarkerPlanner.BuildByParagraph(document).Should().NotContainKey(document.Paragraphs.Last());
    }

    [Theory]
    [InlineData("0")]
    [InlineData(null)]
    public void ZeroStartingCountersContinueRatherThanLookingUninitialized(string? start)
    {
        var document = Read(Numbering(
            level1Properties: "<w:lvlRestart w:val=\"0\"/>", level1Format: "decimal", level1Start: start),
            0, 1, 1, 0, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.0", "A.1", "B", "B.2");
    }

    [Fact]
    public void FullOverrideStartIsIgnoredWithoutItsSeparateStartOverride()
    {
        var levelOverride = $"""
            <w:lvlOverride w:ilvl="1">
              {Level(1, "decimal", "%1.%2", "<w:lvlRestart w:val=\"0\"/>", "9")}
            </w:lvlOverride>
            """;
        var document = Read(Numbering(level1Start: "0", instanceOverride: levelOverride), 0, 1, 1);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.0", "A.1");
    }

    [Fact]
    public void OnlyTheRichInstanceUsesPreservedNumberingWhileOrdinaryListsStayNative()
    {
        var overrideXml = $"""
            <w:num w:numId="20"><w:abstractNumId w:val="1"/>
              <w:lvlOverride w:ilvl="1">{Level(1, "decimal", "%1.%2", "<w:isLgl/>")}</w:lvlOverride>
            </w:num>
            """;
        var document = ReadItems(Numbering(additionalNumbers: overrideXml), [(10, 0), (20, 0), (20, 1), (10, 0)]);
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[3].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.None);
        paragraphs[2].Formatting.ListKind.Should().Be(ListKind.None);
        paragraphs[1].PreservedNumbering.Should().Be(new PreservedNumbering(20, 0));
        paragraphs[2].PreservedNumbering.Should().Be(new PreservedNumbering(20, 1));
        var markers = PreservedNumberingMarkerPlanner.BuildByParagraph(document);
        markers[paragraphs[1]].Text.Should().Be("A");
        markers[paragraphs[2]].Text.Should().Be("1.1");
    }

    [Fact]
    public void IndependentRichInstancesKeepTheirOwnNeverRestartCounters()
    {
        var document = ReadItems(Numbering(
            level1Properties: "<w:lvlRestart w:val=\"0\"/>", level1Format: "decimal", level1Start: "0",
            additionalNumbers: "<w:num w:numId=\"20\"><w:abstractNumId w:val=\"1\"/></w:num>"),
            [(10, 0), (10, 1), (20, 0), (20, 1), (10, 1)]);

        AssertPreservedMarkersAndRoundTrip(document, "A", "A.0", "A", "A.0", "A.1");
    }

    private static void AssertPreservedMarkersAndRoundTrip(TextDocument document, params string[] expected)
    {
        document.Blocks.OfType<Paragraph>().Should().OnlyContain(paragraph =>
            paragraph.Formatting.ListKind == ListKind.None && paragraph.PreservedNumbering.HasValue);
        var originalNumbering = document.Preserved.OriginalNumbering!.ToString(SaveOptions.DisableFormatting);
        Markers(document).Should().Equal(expected);

        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        document.Preserved.OriginalNumbering.ToString(SaveOptions.DisableFormatting).Should().Be(originalNumbering);
        saved.Position = 0;
        Markers(DocxReader.Read(saved)).Should().Equal(expected);

        var before = document.Preserved.OriginalNumbering.Descendants(W + "lvl")
            .Select(level => level.ToString(SaveOptions.DisableFormatting)).ToList();
        var after = SavedNumbering(document).Descendants(W + "lvl")
            .Select(level => level.ToString(SaveOptions.DisableFormatting)).ToList();
        after.Should().BeEquivalentTo(before, "preserved levels and their overrides must survive unchanged");
    }

    private static IReadOnlyList<string> Markers(TextDocument document)
    {
        var markers = PreservedNumberingMarkerPlanner.BuildByParagraph(document);
        return document.Blocks.OfType<Paragraph>()
            .Select(paragraph => markers.TryGetValue(paragraph, out var marker) ? marker.Text : "")
            .ToList();
    }

    private static XElement SavedNumbering(TextDocument document)
    {
        using var saved = new MemoryStream();
        DocxWriter.Write(document, saved);
        using var archive = new ZipArchive(new MemoryStream(saved.ToArray()), ZipArchiveMode.Read);
        using var part = archive.GetEntry("word/numbering.xml")!.Open();
        return XElement.Load(part);
    }

    private static TextDocument Read(string numbering, params int[] levels) =>
        ReadItems(numbering, levels.Select(level => (10, level)).ToArray());

    private static TextDocument ReadItems(string numbering, IReadOnlyList<(int NumId, int Level)> items)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            var paragraphs = items.Select((item, index) => $"""
                <w:p><w:pPr><w:numPr><w:ilvl w:val="{item.Level}"/><w:numId w:val="{item.NumId}"/></w:numPr></w:pPr>
                  <w:r><w:t>Item {index}</w:t></w:r></w:p>
                """);
            Add("word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body>{string.Concat(paragraphs)}</w:body></w:document>");
            Add("word/styles.xml", $"<w:styles xmlns:w=\"{W}\"/>");
            Add("word/numbering.xml", numbering);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static string Numbering(
        string level1Properties = "",
        string level2Properties = "",
        string level1Format = "lowerLetter",
        string? level1Start = "1",
        string instanceOverride = "",
        string additionalNumbers = "",
        string level1Text = "%1.%2") => $"""
        <w:numbering xmlns:w="{W}">
          <w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="multilevel"/>
            {Level(0, "upperLetter", "%1")}
            {Level(1, level1Format, level1Text, level1Properties, level1Start)}
            {Level(2, "decimal", "%1.%2.%3", level2Properties)}
          </w:abstractNum>
          <w:num w:numId="10"><w:abstractNumId w:val="1"/>{instanceOverride}</w:num>
          {additionalNumbers}
        </w:numbering>
        """;

    private static string Level(int level, string format, string text, string properties = "", string? start = "1") =>
        $"<w:lvl w:ilvl=\"{level}\">{(start is null ? "" : $"<w:start w:val=\"{start}\"/>")}" +
        $"<w:numFmt w:val=\"{format}\"/>{properties}<w:lvlText w:val=\"{text}\"/></w:lvl>";
}
