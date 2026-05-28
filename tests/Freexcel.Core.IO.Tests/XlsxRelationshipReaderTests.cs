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
    public void ReadTargets_SkipsAbsoluteUriTargetsEvenWhenTargetModeIsMissing()
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
                new XAttribute("Id", "rIdAbsolute"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                new XAttribute("Target", "https://example.com/workbook.xlsx"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target));

        targets.Should().ContainKey("rIdSheet").WhoseValue.Should().Be("xl/worksheets/sheet1.xml");
        targets.Should().NotContainKey("rIdAbsolute");
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

    [Fact]
    public void ReadTargets_KeepsEncodedPathSeparatorsAsLiteralPathText()
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = new XDocument(new XElement(
            relationshipNs + "Relationships",
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdForwardSlash"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "../media/image%2F1.png")),
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdBackSlash"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "../media/image%5C1.png"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/drawings/drawing1.xml", target));

        targets.Should().HaveCount(2);
        targets.Should().ContainKey("rIdForwardSlash").WhoseValue.Should().Be("xl/media/image%2F1.png");
        targets.Should().ContainKey("rIdBackSlash").WhoseValue.Should().Be("xl/media/image%5C1.png");
    }

    [Fact]
    public void ReadTargets_KeepsEncodedDotSegmentsAsLiteralPathText()
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = new XDocument(new XElement(
            relationshipNs + "Relationships",
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdDot"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "%2E/media/image.png")),
            new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdDotDot"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "%2E%2E/media/image.png"))));

        var targets = XlsxRelationshipReader.ReadTargets(
            relationshipsXml,
            relationshipNs,
            target => XlsxPackagePath.ResolveRelationshipTarget("xl/drawings/drawing1.xml", target));

        targets.Should().HaveCount(2);
        targets.Should().ContainKey("rIdDot").WhoseValue.Should().Be("xl/drawings/%2E/media/image.png");
        targets.Should().ContainKey("rIdDotDot").WhoseValue.Should().Be("xl/drawings/%2E%2E/media/image.png");
    }
}
