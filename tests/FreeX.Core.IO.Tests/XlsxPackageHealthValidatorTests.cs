using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPackageHealthValidatorTests
{
    private const string OfficeDocumentRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string WorksheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

    [Fact]
    public void Validate_AcceptsMinimalWorkbookPackage()
    {
        using var package = CreateMinimalWorkbookPackage();

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsContentTypeOverrideForMissingPart()
    {
        using var package = CreateMinimalWorkbookPackage(
            contentTypeOverrides:
            [
                """<Override PartName="/xl/missing.xml" ContentType="application/xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("references missing package part", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsInvalidRelationshipPartContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            contentTypeOverrides:
            [
                """<Override PartName="/xl/_rels/workbook.xml.rels" ContentType="application/xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("must use relationship content type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipTargetThatEscapesPackageRoot()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "../../../outside.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("target escapes the package root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsInternalRelationshipToMissingPackagePart()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/missing.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("targets missing package part xl/worksheets/missing.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsExternalUriWithoutExternalTargetMode()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "https://example.invalid/sheet.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("external URI without TargetMode=External", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsCaseCollidingPackageEntries()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/media/image1.png", ""),
                ("xl/media/IMAGE1.png", "")
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="image/png" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("collides with package part", StringComparison.OrdinalIgnoreCase));
    }

    private static MemoryStream CreateMinimalWorkbookPackage(
        IReadOnlyList<string>? workbookRelationships = null,
        IReadOnlyList<string>? contentTypeOverrides = null,
        IReadOnlyList<string>? contentTypeDefaults = null,
        IReadOnlyList<(string Path, string Content)>? extraEntries = null)
    {
        var entries = new List<(string Path, string Content)>
        {
            ("[Content_Types].xml", ContentTypesXml(contentTypeOverrides, contentTypeDefaults)),
            ("_rels/.rels", RelationshipsXml(
                Relationship("rIdWorkbook", OfficeDocumentRelationshipType, "xl/workbook.xml"))),
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", RelationshipsXml(
                workbookRelationships?.ToArray() ??
                [
                    Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml")
                ])),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData />
                </worksheet>
                """)
        };

        if (extraEntries is not null)
            entries.AddRange(extraEntries);

        return XlsxPackageTestFixtures.CreatePackage([.. entries]);
    }

    private static string ContentTypesXml(
        IReadOnlyList<string>? overrides,
        IReadOnlyList<string>? defaults)
    {
        var defaultDeclarations = new[]
        {
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />""",
            """<Default Extension="xml" ContentType="application/xml" />"""
        }.Concat(defaults ?? []);

        var overrideDeclarations = new[]
        {
            """<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />""",
            """<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />"""
        }.Concat(overrides ?? []);

        return $"""
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              {string.Join(Environment.NewLine, defaultDeclarations)}
              {string.Join(Environment.NewLine, overrideDeclarations)}
            </Types>
            """;
    }

    private static string RelationshipsXml(params string[] relationships) =>
        $"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {string.Join(Environment.NewLine, relationships)}
        </Relationships>
        """;

    private static string Relationship(string id, string type, string target) =>
        $"""<Relationship Id="{id}" Type="{type}" Target="{target}" />""";
}
