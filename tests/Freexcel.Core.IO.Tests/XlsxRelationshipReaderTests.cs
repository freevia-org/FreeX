using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxRelationshipReaderTests
{
    [Fact]
    public void ReadTargets_SkipsExternalRelationshipsWhenBuildingInternalPartMap()
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = new XDocument(new XElement(
            relationshipNs + "Relationships",
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdInternal"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", "worksheets/sheet%201.xml")),
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdExternal"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                new XAttribute("Target", "https://example.com/workbook.xlsx"),
                new XAttribute("TargetMode", "External"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));

        targets.Should().ContainSingle();
        targets.Should().ContainKey("rIdInternal").WhoseValue.Should().Be("xl/worksheets/sheet 1.xml");
        targets.Should().NotContainKey("rIdExternal");
    }

    [Fact]
    public void ReadTargets_IgnoresDuplicateRelationshipIdsWhenBuildingInternalPartMap()
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = new XDocument(new XElement(
            relationshipNs + "Relationships",
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdSheet"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdSheet"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", "worksheets/sheet2.xml"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));

        targets.Should().ContainSingle();
        targets.Should().ContainKey("rIdSheet").WhoseValue.Should().Be("xl/worksheets/sheet1.xml");
    }

    [Fact]
    public void ReadTargets_KeepsMalformedPercentEscapesAsLiteralPathText()
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = new XDocument(new XElement(
            relationshipNs + "Relationships",
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdImage"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "../media/image%E0%A4%A.png"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/drawings/drawing1.xml", target));

        targets.Should().ContainSingle();
        targets.Should().ContainKey("rIdImage").WhoseValue.Should().Be("xl/media/image%E0%A4%A.png");
    }
}
