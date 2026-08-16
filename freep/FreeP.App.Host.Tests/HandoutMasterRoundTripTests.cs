using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R137 follow-up: the reader imports ppt/handoutMasters/handoutMaster1.xml (placeholders +
/// p:hf) for the handout PDF/print exporter, so the writer must round-trip the authored part,
/// its rels, its content-type override, the presentation.xml.rels relationship AND the
/// p:handoutMasterIdLst entry that binds it — otherwise a save silently drops the user's
/// handout header/footer authoring. Mirrors <see cref="NotesMasterRoundTripTests"/>.
/// </summary>
public sealed class HandoutMasterRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.HandoutMasterTests-");
    private string _tempDir => _temporaryDirectory.Path;

    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";

    private const string HandoutMasterRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/handoutMaster";
    private const string ThemeRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string HandoutMasterCT =
        "application/vnd.openxmlformats-officedocument.presentationml.handoutMaster+xml";
    private const string HandoutMasterPath = "ppt/handoutMasters/handoutMaster1.xml";
    private const string HandoutMasterRelsPath = "ppt/handoutMasters/_rels/handoutMaster1.xml.rels";

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void AuthoredHandoutMaster_SurvivesReadWriteRoundTrip()
    {
        var source = Path.Combine(_tempDir, "handout-source.pptx");
        PptxPackageWriter.Write(Presentation.CreateEmpty(), source);
        InjectHandoutMaster(source);

        var original = PptxPackageReader.Read(source);
        original.HandoutMasterXml.Should().NotBeNullOrEmpty();
        original.HandoutMasterRelsXml.Should().NotBeNullOrEmpty();
        original.HandoutHfVisibility.Should().NotBeNull();

        var output = Path.Combine(_tempDir, "handout-roundtrip.pptx");
        PptxPackageWriter.Write(original, output);
        var reloaded = PptxPackageReader.Read(output);

        // Model level: raw bytes + the parsed projections the handout exporter consumes.
        reloaded.HandoutMasterXml.Should().Equal(original.HandoutMasterXml!);
        reloaded.HandoutMasterRelsXml.Should().Equal(original.HandoutMasterRelsXml!);
        reloaded.HandoutHfVisibility!.ShowFooter.Should().BeFalse();
        reloaded.HandoutHfVisibility.ShowDate.Should().BeTrue();
        reloaded.HandoutMasterPlaceholders
            .Single(shape => shape.Placeholder?.Type == PlaceholderType.Footer)
            .TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Contain("Handout Footer Text");

        // Package level: part, rels, content type, presentation relationship, id list.
        using var archive = ZipFile.OpenRead(output);
        archive.Entries.Count(entry =>
            string.Equals(entry.FullName, HandoutMasterPath, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "a duplicate zip entry would be malformed OPC");
        archive.GetEntry(HandoutMasterRelsPath).Should().NotBeNull();

        LoadXml(archive, "[Content_Types].xml").Root!.Elements(CT + "Override")
            .Should().Contain(el =>
                (string)el.Attribute("PartName")! == "/" + HandoutMasterPath &&
                (string)el.Attribute("ContentType")! == HandoutMasterCT);

        var handoutRelId = LoadXml(archive, "ppt/_rels/presentation.xml.rels").Root!
            .Elements(Rel + "Relationship")
            .Where(el => (string?)el.Attribute("Type") == HandoutMasterRelType)
            .Select(el => (string?)el.Attribute("Id"))
            .Should().ContainSingle().Subject;
        handoutRelId.Should().NotBeNullOrWhiteSpace();

        LoadXml(archive, "ppt/presentation.xml").Root!
            .Element(P + "handoutMasterIdLst")!.Elements(P + "handoutMasterId")
            .Should().ContainSingle(el => (string?)el.Attribute(R + "id") == handoutRelId);
    }

    [Fact]
    public void PresentationWithoutHandoutMaster_GetsNoSynthesizedPart()
    {
        var output = Path.Combine(_tempDir, "no-handout.pptx");
        PptxPackageWriter.Write(Presentation.CreateEmpty(), output);

        using var archive = ZipFile.OpenRead(output);
        archive.Entries.Should().NotContain(entry =>
            entry.FullName.StartsWith("ppt/handoutMasters/", StringComparison.OrdinalIgnoreCase));
        LoadXml(archive, "ppt/presentation.xml").Root!
            .Element(P + "handoutMasterIdLst").Should().BeNull();
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"expected {entryName} in the package");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>
    /// Builds a package that looks like one PowerPoint writes after the user edits the handout
    /// master: the part itself, its rels (theme), the content-type override, the presentation
    /// relationship and the p:handoutMasterIdLst entry.
    /// </summary>
    private static void InjectHandoutMaster(string pptxPath)
    {
        var handoutMasterXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "handoutMaster",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr"),
                        new XElement(P + "sp",
                            new XElement(P + "nvSpPr",
                                new XElement(P + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "Footer Placeholder 1")),
                                new XElement(P + "cNvSpPr"),
                                new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "ftr")))),
                            new XElement(P + "spPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", "100000"), new XAttribute("y", "200000")),
                                    new XElement(A + "ext", new XAttribute("cx", "300000"), new XAttribute("cy", "50000")))),
                            new XElement(P + "txBody",
                                new XElement(A + "bodyPr"),
                                new XElement(A + "p",
                                    new XElement(A + "r",
                                        new XElement(A + "t", "Handout Footer Text"))))))),
                new XElement(P + "clrMap",
                    new XAttribute("bg1", "lt1"), new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"), new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink")),
                new XElement(P + "hf",
                    new XAttribute("ftr", "0"),
                    new XAttribute("dt", "1"),
                    new XAttribute("sldNum", "1"),
                    new XAttribute("hdr", "0"))));

        var handoutMasterRelsXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", ThemeRelType),
                    new XAttribute("Target", "../theme/theme1.xml"))));

        using var archive = ZipFile.Open(pptxPath, ZipArchiveMode.Update);
        WriteEntry(archive, HandoutMasterPath, handoutMasterXml);
        WriteEntry(archive, HandoutMasterRelsPath, handoutMasterRelsXml);

        var contentTypes = ReadEntry(archive, "[Content_Types].xml");
        contentTypes.Root!.Add(new XElement(CT + "Override",
            new XAttribute("PartName", "/" + HandoutMasterPath),
            new XAttribute("ContentType", HandoutMasterCT)));
        WriteEntry(archive, "[Content_Types].xml", contentTypes);

        const string presRelsName = "ppt/_rels/presentation.xml.rels";
        var relsDoc = ReadEntry(archive, presRelsName);
        var usedIds = relsDoc.Root!.Elements(Rel + "Relationship")
            .Select(rel => (string?)rel.Attribute("Id"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var handoutRelId = "rId1";
        for (int i = 1; usedIds.Contains(handoutRelId); i++)
            handoutRelId = $"rId{i + 1}";
        relsDoc.Root!.Add(new XElement(Rel + "Relationship",
            new XAttribute("Id", handoutRelId),
            new XAttribute("Type", HandoutMasterRelType),
            new XAttribute("Target", "handoutMasters/handoutMaster1.xml")));
        WriteEntry(archive, presRelsName, relsDoc);

        var presentation = ReadEntry(archive, "ppt/presentation.xml");
        var handoutIdLst = new XElement(P + "handoutMasterIdLst",
            new XElement(P + "handoutMasterId", new XAttribute(R + "id", handoutRelId)));
        var sldIdLst = presentation.Root!.Element(P + "sldIdLst");
        if (sldIdLst is not null)
            sldIdLst.AddBeforeSelf(handoutIdLst);
        else
            presentation.Root!.Add(handoutIdLst);
        WriteEntry(archive, "ppt/presentation.xml", presentation);
    }

    private static XDocument ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"expected {entryName} to already exist");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }
}
