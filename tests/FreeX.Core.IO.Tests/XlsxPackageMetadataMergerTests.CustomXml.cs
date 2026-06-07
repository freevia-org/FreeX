using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
{
    [Fact]
    public void NormalizeCustomXmlPackageGraph_RemovesDanglingRootRelationshipAndOrphanProperties()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/customXml/itemProps1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                                Target="xl/workbook.xml"/>
                  <Relationship Id="rIdDanglingCustomXml"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item1.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", "<workbook/>"),
            ("customXml/itemProps1.xml", """
                <ds:datastoreItem ds:itemID="{01234567-89AB-CDEF-0123-456789ABCDEF}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItemProps"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps1.xml"/>
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

        archive.GetEntry("customXml/itemProps1.xml").Should().BeNull();
        archive.GetEntry("customXml/_rels/item1.xml.rels").Should().BeNull();

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels")
            .Root!
            .Elements(relationshipNs + "Relationship")
            .Should()
            .NotContain(relationship =>
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml");

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml")
            .Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .NotContain(element => (string?)element.Attribute("PartName") == "/customXml/itemProps1.xml");
    }

    [Fact]
    public void NormalizeCustomXmlPackageGraph_RebindsItemRelationshipsToPairedPropertiesAndEnsuresContentTypes()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/customXml/itemProps2.xml"
                            ContentType="application/xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                                Target="xl/workbook.xml"/>
                  <Relationship Id="rIdCustomXml1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item1.xml"/>
                  <Relationship Id="rIdCustomXml2"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item2.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", "<workbook/>"),
            ("customXml/item1.xml", "<root/>"),
            ("customXml/item2.xml", "<root/>"),
            ("customXml/itemProps1.xml", """
                <ds:datastoreItem ds:itemID="{01234567-89AB-CDEF-0123-456789ABCDEF}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/itemProps2.xml", """
                <ds:datastoreItem ds:itemID="{11111111-2222-3333-4444-555555555555}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWrongItemProps"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps2.xml"/>
                  <Relationship Id="rIdMissingItemProps"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps99.xml"/>
                </Relationships>
                """),
            ("customXml/_rels/item2.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdSecondItemProps"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps2.xml"/>
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

        AssertCustomXmlPropertiesRelationship(archive, "customXml/_rels/item1.xml.rels", "itemProps1.xml");
        AssertCustomXmlPropertiesRelationship(archive, "customXml/_rels/item2.xml.rels", "itemProps2.xml");

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypes = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        contentTypes.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/customXml/itemProps1.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        contentTypes.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/customXml/itemProps2.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
    }

    [Fact]
    public void NormalizeCustomXmlPackageGraph_ReassignsCollidingPackageRootCustomXmlRelationshipIds()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/customXml/itemProps1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                                Target="xl/workbook.xml"/>
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item1.xml"/>
                  <Relationship Id="rIdDuplicate"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item1.xml"/>
                  <Relationship Id="rIdDanglingCustomXml"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"
                                Target="customXml/item99.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", "<workbook/>"),
            ("customXml/item1.xml", "<root/>"),
            ("customXml/itemProps1.xml", """
                <ds:datastoreItem ds:itemID="{01234567-89AB-CDEF-0123-456789ABCDEF}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItemProps"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps1.xml"/>
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels")
            .Root!
            .Elements(relationshipNs + "Relationship")
            .ToList();

        relationships
            .Select(relationship => (string?)relationship.Attribute("Id"))
            .Should()
            .OnlyHaveUniqueItems();
        relationships
            .Where(relationship =>
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml")
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Id") != "rId1" &&
                (string?)relationship.Attribute("Target") == "customXml/item1.xml");
    }

    private static void AssertCustomXmlPropertiesRelationship(
        ZipArchive archive,
        string relationshipPartPath,
        string expectedTarget)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XlsxPackageTestFixtures.LoadPackageXml(archive, relationshipPartPath)
            .Root!
            .Elements(relationshipNs + "Relationship")
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" &&
                (string?)relationship.Attribute("Target") == expectedTarget);
    }
}
