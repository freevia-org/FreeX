using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R28-io-docprops-theme-deep-1: NormalizeCustomXmlItemPropertyRelationships
/// (XlsxPackageMetadataMerger.cs) used to prefer a same-numbered-filename guess
/// (customXml/item1.xml -&gt; customXml/itemProps1.xml) over an item's own unambiguous, existing
/// customXmlProps relationship whenever a same-numbered itemProps part merely existed in the
/// package - silently cross-wiring/corrupting customXml item&lt;-&gt;itemProps pairings that don't
/// follow the numbering convention (fully valid per OPC, since only the relationship graph is
/// authoritative). The fix: when an item's own rels file has exactly one customXmlProps
/// relationship that targets an existing part, trust it; only fall back to the paired-by-number
/// guess when the item's own relationship graph is missing/ambiguous/dangling.
/// </summary>
public sealed class R28_CustomXmlItemPropertyRelationshipUnambiguousTests
{
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void NormalizeCustomXmlPackageGraph_PreservesUnambiguousSwappedItemPropertiesPairing()
    {
        // item1.xml's ONLY customXmlProps relationship (unambiguous, points to an existing part)
        // targets itemProps2.xml, and item2.xml's ONLY relationship targets itemProps1.xml - a
        // valid, bijective, but non-conventionally-numbered pairing (e.g. from files authored or
        // merged by other tooling). This must survive a FreeX save unchanged.
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/customXml/itemProps1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                  <Override PartName="/customXml/itemProps2.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
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
                <ds:datastoreItem ds:itemID="{11111111-1111-1111-1111-111111111111}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/itemProps2.xml", """
                <ds:datastoreItem ds:itemID="{22222222-2222-2222-2222-222222222222}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItem1Props"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps2.xml"/>
                </Relationships>
                """),
            ("customXml/_rels/item2.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItem2Props"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps1.xml"/>
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

        // The item<->itemProps pairing (and each part's real itemID GUID) must be unchanged: the
        // swap must NOT happen just because same-numbered itemProps parts also exist in the package.
        AssertRelationshipTarget(archive, "customXml/_rels/item1.xml.rels", "itemProps2.xml");
        AssertRelationshipTarget(archive, "customXml/_rels/item2.xml.rels", "itemProps1.xml");
    }

    [Fact]
    public void NormalizeCustomXmlPackageGraph_KeepsConventionalSameNumberedPairingWorking()
    {
        // Sibling already-working case: the ordinary, conventional same-numbered pairing
        // (item1.xml <-> itemProps1.xml, item2.xml <-> itemProps2.xml) must keep working.
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/customXml/itemProps1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
                  <Override PartName="/customXml/itemProps2.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/>
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
                <ds:datastoreItem ds:itemID="{11111111-1111-1111-1111-111111111111}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/itemProps2.xml", """
                <ds:datastoreItem ds:itemID="{22222222-2222-2222-2222-222222222222}"
                                  xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>
                """),
            ("customXml/_rels/item1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItem1Props"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps1.xml"/>
                </Relationships>
                """),
            ("customXml/_rels/item2.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdItem2Props"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"
                                Target="itemProps2.xml"/>
                </Relationships>
                """));
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

        AssertRelationshipTarget(archive, "customXml/_rels/item1.xml.rels", "itemProps1.xml");
        AssertRelationshipTarget(archive, "customXml/_rels/item2.xml.rels", "itemProps2.xml");
    }

    private static void AssertRelationshipTarget(ZipArchive archive, string relationshipPartPath, string expectedTarget)
    {
        XlsxPackageTestFixtures.LoadPackageXml(archive, relationshipPartPath)
            .Root!
            .Elements(RelationshipNs + "Relationship")
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" &&
                (string?)relationship.Attribute("Target") == expectedTarget);
    }
}
