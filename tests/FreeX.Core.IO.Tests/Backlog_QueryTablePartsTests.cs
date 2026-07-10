using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item "queryTableParts". The deferred ask was: make
/// <see cref="XlsxConnectionQueryTableSchemaNormalizer"/> strip only DANGLING worksheet
/// &lt;queryTableParts&gt;&lt;queryTablePart r:id="..."/&gt;&gt; markers (an r:id with no matching
/// worksheet relationship of type .../relationships/queryTable) while preserving valid ones.
///
/// That ask turned out to rest on a false premise: &lt;queryTableParts&gt; is NOT a member of
/// CT_Worksheet's content model in the real ECMA-376/ISO-29500 schema. The
/// RealOpenXmlValidator_FlagsWorksheetQueryTablePartsAsInvalid_RegardlessOfRelationshipValidity test below
/// proves this empirically with the actual DocumentFormat.OpenXml validator this project's own
/// SchemaErrors-style tests rely on: a fully-valid queryTablePart (matching worksheet relationship,
/// matching xl/queryTables/*.xml part, matching connection) is STILL reported as an invalid child
/// element of &lt;worksheet&gt;. There is no "dangling vs. valid" distinction to make for this
/// element -- every occurrence must be stripped to keep the package schema-valid, which is exactly
/// what the (unchanged) normalizer already does. These tests pin that correct behavior down and
/// document why "preserve valid parts" is not a safe fix.
/// </summary>
public sealed class Backlog_queryTableParts_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private const string WorksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";

    [Fact]
    public void NormalizePackage_RemovesQueryTablePartsMarker_EvenWhenItsRelationshipIsValid()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (WorksheetPath, $"""
                <worksheet xmlns="{WorksheetNs}" xmlns:r="{RelNs}">
                  <queryTableParts count="1">
                    <queryTablePart r:id="rIdValidQueryTable"/>
                  </queryTableParts>
                </worksheet>
                """),
            (WorksheetRelsPath, XlsxPackageTestFixtures.RelationshipsXml(
                XlsxPackageTestFixtures.Relationship(
                    "rIdValidQueryTable",
                    QueryTableRelationshipType,
                    "../queryTables/queryTable1.xml"))),
            ("xl/queryTables/queryTable1.xml", $"""
                <queryTable xmlns="{WorksheetNs}" name="FreeXQueryTable" connectionId="1"/>
                """));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxConnectionQueryTableSchemaNormalizer.NormalizePackage(archive);
        }

        package.Position = 0;
        using var verifyArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, WorksheetPath);
        worksheetXml.Root!.Element(WorksheetNs + "queryTableParts").Should().BeNull(
            "queryTableParts is not a valid CT_Worksheet child per the real schema, so it must always be stripped");

        // The normalizer only touches the inert worksheet marker element -- the actual relationship
        // graph that carries the real range<->queryTable binding is left completely untouched.
        verifyArchive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull();
        var worksheetRelationships = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, WorksheetRelsPath);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        worksheetRelationships.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdValidQueryTable" &&
                relationship.Attribute("Type")?.Value == QueryTableRelationshipType)
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void NormalizePackage_RemovesDanglingQueryTablePartsMarker_WithNoMatchingRelationship()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (WorksheetPath, $"""
                <worksheet xmlns="{WorksheetNs}" xmlns:r="{RelNs}">
                  <queryTableParts count="1">
                    <queryTablePart r:id="rIdDanglingQueryTable"/>
                  </queryTableParts>
                </worksheet>
                """),
            (WorksheetRelsPath, XlsxPackageTestFixtures.RelationshipsXml()));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxConnectionQueryTableSchemaNormalizer.NormalizePackage(archive);
        }

        package.Position = 0;
        using var verifyArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(verifyArchive, WorksheetPath);
        worksheetXml.Root!.Element(WorksheetNs + "queryTableParts").Should().BeNull();
    }

    [Fact]
    public void RealOpenXmlValidator_FlagsWorksheetQueryTablePartsAsInvalid_RegardlessOfRelationshipValidity()
    {
        using var package = BuildMinimalPackageWithValidQueryTablePart();

        package.Position = 0;
        using (var document = SpreadsheetDocument.Open(package, false))
        {
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            var schemaErrors = validator.Validate(document)
                .Where(error => error.ErrorType == ValidationErrorType.Schema)
                .ToList();

            schemaErrors.Should().Contain(
                error => error.Description != null && error.Description.Contains("queryTableParts"),
                "the real Open XML schema does not model <queryTableParts> as a worksheet child, " +
                "even though this queryTablePart's r:id resolves to a fully valid relationship, " +
                "xl/queryTables/queryTable1.xml part, and connection");
        }

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxConnectionQueryTableSchemaNormalizer.NormalizePackage(archive);
        }

        package.Position = 0;
        using var normalizedDocument = SpreadsheetDocument.Open(package, false);
        var normalizedValidator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        var remainingQueryTablePartsErrors = normalizedValidator.Validate(normalizedDocument)
            .Where(error =>
                error.ErrorType == ValidationErrorType.Schema &&
                error.Description != null &&
                error.Description.Contains("queryTableParts"))
            .ToList();

        remainingQueryTablePartsErrors.Should().BeEmpty();
    }

    private static MemoryStream BuildMinimalPackageWithValidQueryTablePart()
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var stream = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", $"""
                <Types xmlns="{contentTypeNs}">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/queryTables/queryTable1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml"/>
                  <Override PartName="/xl/connections.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml"/>
                </Types>
                """),
            ("_rels/.rels", $"""
                <Relationships xmlns="{packageRelNs}">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml", $"""
                <workbook xmlns="{WorksheetNs}" xmlns:r="{RelNs}">
                  <sheets><sheet name="Data" sheetId="1" r:id="rIdSheet1"/></sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", $"""
                <Relationships xmlns="{packageRelNs}">
                  <Relationship Id="rIdSheet1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rIdConnections" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/connections" Target="connections.xml"/>
                </Relationships>
                """),
            (WorksheetPath, $"""
                <worksheet xmlns="{WorksheetNs}" xmlns:r="{RelNs}">
                  <dimension ref="A1"/>
                  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
                  <sheetFormatPr defaultRowHeight="15"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="str"><v>Value</v></c></row>
                  </sheetData>
                  <queryTableParts count="1">
                    <queryTablePart r:id="rIdFreeXQueryTable"/>
                  </queryTableParts>
                </worksheet>
                """),
            (WorksheetRelsPath, XlsxPackageTestFixtures.RelationshipsXml(
                XlsxPackageTestFixtures.Relationship(
                    "rIdFreeXQueryTable",
                    QueryTableRelationshipType,
                    "../queryTables/queryTable1.xml"))),
            ("xl/queryTables/queryTable1.xml", $"""
                <queryTable xmlns="{WorksheetNs}" name="FreeXQueryTable" connectionId="1"/>
                """),
            ("xl/connections.xml", $"""
                <connections xmlns="{WorksheetNs}">
                  <connection id="1" name="FreeXConnection" refreshedVersion="0"/>
                </connections>
                """));

        return stream;
    }
}
