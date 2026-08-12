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
    private const string SharedStringsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    private const string StylesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string ExternalLinkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string VbaProjectRelationshipType =
        "http://schemas.microsoft.com/office/2006/relationships/vbaProject";
    private const string PivotCacheDefinitionRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotCacheRecordsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
    private const string PivotTableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string DrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string ChartRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string TableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string StylesContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private const string ExternalLinkContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";
    private const string VbaProjectContentType =
        "application/vnd.ms-office.vbaProject";
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MacroEnabledWorkbookContentType =
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string PivotCacheDefinitionContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml";
    private const string PivotCacheRecordsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml";
    private const string PivotTableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml";
    private const string DrawingContentType =
        "application/vnd.openxmlformats-officedocument.drawing+xml";
    private const string ChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string TableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml";

    [Fact]
    public void Validator_IsOwnedByDiagnosticsAssembly()
    {
        typeof(XlsxPackageHealthValidator).Assembly.GetName().Name
            .Should().Be("FreeX.XlsxPackageDiagnostics");
        typeof(XlsxFileAdapter).Assembly
            .GetType("FreeX.Core.IO.XlsxPackageHealthValidator")
            .Should().BeNull();
    }

    [Fact]
    public void Validate_AcceptsMinimalWorkbookPackage()
    {
        using var package = CreateMinimalWorkbookPackage();

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithSharedStringTable()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: SharedStringWorksheetXml("0"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdSharedStrings", SharedStringsRelationshipType, "sharedStrings.xml")
            ],
            extraEntries:
            [
                ("xl/sharedStrings.xml", SharedStringsXml("<si><t>Hello</t></si>"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/sharedStrings.xml" ContentType="{SharedStringsContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsXmlPartWithProhibitedDtd()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/sharedStrings.xml", """
                    <!DOCTYPE sst [
                      <!ELEMENT sst ANY>
                    ]>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />
                    """)
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/sharedStrings.xml" ContentType="{SharedStringsContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/sharedStrings.xml is not parseable XML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsMissingSharedStringTableForSharedStringCell()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: SharedStringWorksheetXml("0"));

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("missing xl/sharedStrings.xml for shared-string cells", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsSharedStringTableWithoutWorkbookRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: SharedStringWorksheetXml("0"),
            extraEntries:
            [
                ("xl/sharedStrings.xml", SharedStringsXml("<si><t>Hello</t></si>"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/sharedStrings.xml" ContentType="{SharedStringsContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("has no workbook relationship to xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsSharedStringCellIndexOutsideTable()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: SharedStringWorksheetXml("2"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdSharedStrings", SharedStringsRelationshipType, "sharedStrings.xml")
            ],
            extraEntries:
            [
                ("xl/sharedStrings.xml", SharedStringsXml("<si><t>Hello</t></si>"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/sharedStrings.xml" ContentType="{SharedStringsContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("references shared-string index 2, but xl/sharedStrings.xml contains 1 entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithStylesPackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: StyledWorksheetXml("1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdStyles", StylesRelationshipType, "styles.xml")
            ],
            extraEntries:
            [
                ("xl/styles.xml", StylesXml(cellFormatCount: 2))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/styles.xml" ContentType="{StylesContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsMissingStylesPackageForStyleReference()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: StyledWorksheetXml("1"));

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("missing xl/styles.xml for style references", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsStylesPackageWithoutWorkbookRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: StyledWorksheetXml("1"),
            extraEntries:
            [
                ("xl/styles.xml", StylesXml(cellFormatCount: 2))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/styles.xml" ContentType="{StylesContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("has no workbook relationship to xl/styles.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsStyleReferenceOutsideCellFormats()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: StyledWorksheetXml("3"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdStyles", StylesRelationshipType, "styles.xml")
            ],
            extraEntries:
            [
                ("xl/styles.xml", StylesXml(cellFormatCount: 2))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/styles.xml" ContentType="{StylesContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("references style index 3, but xl/styles.xml cellXfs contains 2 entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsStylesCellXfsCountMismatch()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: StyledWorksheetXml("1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdStyles", StylesRelationshipType, "styles.xml")
            ],
            extraEntries:
            [
                ("xl/styles.xml", StylesXml(cellFormatCount: 2, declaredCellFormatCount: 3))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/styles.xml" ContentType="{StylesContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/styles.xml cellXfs count is 3, but contains 2 child entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithExternalLinkPackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithExternalReferenceXml("rIdExternalLink1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdExternalLink1", ExternalLinkRelationshipType, "externalLinks/externalLink1.xml")
            ],
            extraEntries:
            [
                ("xl/externalLinks/externalLink1.xml", ExternalLinkXml("rIdExternalBook1")),
                ("xl/externalLinks/_rels/externalLink1.xml.rels", RelationshipsXml(
                    Relationship("rIdExternalBook1", ExternalLinkPathRelationshipType, "ExternalWorkbook.xlsx", "External")))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/externalLinks/externalLink1.xml" ContentType="{ExternalLinkContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsWorkbookExternalReferenceMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithExternalReferenceXml("rIdMissing"));

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("workbook externalReference #1 targets missing relationship rIdMissing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsExternalLinkPartWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithExternalReferenceXml("rIdExternalLink1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdExternalLink1", ExternalLinkRelationshipType, "externalLinks/externalLink1.xml")
            ],
            extraEntries:
            [
                ("xl/externalLinks/externalLink1.xml", ExternalLinkXml("rIdExternalBook1")),
                ("xl/externalLinks/_rels/externalLink1.xml.rels", RelationshipsXml(
                    Relationship("rIdExternalBook1", ExternalLinkPathRelationshipType, "ExternalWorkbook.xlsx", "External")))
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/externalLinks/externalLink1.xml has content type application/xml; expected {ExternalLinkContentType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsExternalBookRelationshipThatIsNotExternal()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithExternalReferenceXml("rIdExternalLink1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdExternalLink1", ExternalLinkRelationshipType, "externalLinks/externalLink1.xml")
            ],
            extraEntries:
            [
                ("xl/externalLinks/externalLink1.xml", ExternalLinkXml("rIdExternalBook1")),
                ("xl/externalLinks/_rels/externalLink1.xml.rels", RelationshipsXml(
                    Relationship("rIdExternalBook1", ExternalLinkPathRelationshipType, "ExternalWorkbook.xlsx")))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/externalLinks/externalLink1.xml" ContentType="{ExternalLinkContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/externalLinks/externalLink1.xml externalBook #1 relationship rIdExternalBook1 is not external", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithVbaProjectPackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookContentType: MacroEnabledWorkbookContentType,
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdVbaProject", VbaProjectRelationshipType, "vbaProject.bin")
            ],
            extraEntries:
            [
                ("xl/vbaProject.bin", "macro")
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/vbaProject.bin" ContentType="{VbaProjectContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsVbaProjectWithoutWorkbookRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookContentType: MacroEnabledWorkbookContentType,
            extraEntries:
            [
                ("xl/vbaProject.bin", "macro")
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/vbaProject.bin" ContentType="{VbaProjectContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("has no workbook relationship to xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsVbaProjectWithoutVbaContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookContentType: MacroEnabledWorkbookContentType,
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdVbaProject", VbaProjectRelationshipType, "vbaProject.bin")
            ],
            extraEntries:
            [
                ("xl/vbaProject.bin", "macro")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/vbaProject.bin has content type (none); expected {VbaProjectContentType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsVbaProjectInNonMacroWorkbook()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdVbaProject", VbaProjectRelationshipType, "vbaProject.bin")
            ],
            extraEntries:
            [
                ("xl/vbaProject.bin", "macro")
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/vbaProject.bin" ContentType="{VbaProjectContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/workbook.xml has content type {WorkbookContentType} but contains xl/vbaProject.bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithPivotCachePackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml("rIdPivotCacheRecords1")),
                ("xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotCacheRecords1", PivotCacheRecordsRelationshipType, "pivotCacheRecords1.xml"))),
                ("xl/pivotCache/pivotCacheRecords1.xml", PivotCacheRecordsXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotCache/pivotCacheRecords1.xml" ContentType="{PivotCacheRecordsContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsDuplicateWorkbookPivotCacheId()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""
                <pivotCache cacheId="0" r:id="rIdPivotCache1" />
                <pivotCache cacheId="0" r:id="rIdPivotCache2" />
                """),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml"),
                Relationship("rIdPivotCache2", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition2.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/pivotCache/pivotCacheDefinition2.xml", PivotCacheDefinitionXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition2.xml" ContentType="{PivotCacheDefinitionContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("workbook pivotCache #2 duplicates cacheId 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookPivotCacheRelationshipWithWrongType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", StylesRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"workbook pivotCache #1 relationship rIdPivotCache1 has Type={StylesRelationshipType}; expected {PivotCacheDefinitionRelationshipType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPivotCacheRecordsMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml("rIdMissingRecords")),
                ("xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels", RelationshipsXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("pivot cache records reference rIdMissingRecords targets missing relationship", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithPivotTablePackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotTable1", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
                ("xl/pivotTables/pivotTable1.xml", PivotTableXml("0")),
                ("xl/pivotTables/_rels/pivotTable1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotCacheDefinition1", PivotCacheDefinitionRelationshipType, "../pivotCache/pivotCacheDefinition1.xml")))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotTables/pivotTable1.xml" ContentType="{PivotTableContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsPivotTableWithoutCacheDefinitionRelationshipPart()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotTable1", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
                ("xl/pivotTables/pivotTable1.xml", PivotTableXml("0"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotTables/pivotTable1.xml" ContentType="{PivotTableContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/pivotTables/pivotTable1.xml has no relationship part for pivot cache definition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPivotTableCacheDefinitionRelationshipMismatch()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/pivotCache/pivotCacheDefinition2.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotTable1", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
                ("xl/pivotTables/pivotTable1.xml", PivotTableXml("0")),
                ("xl/pivotTables/_rels/pivotTable1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotCacheDefinition2", PivotCacheDefinitionRelationshipType, "../pivotCache/pivotCacheDefinition2.xml")))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition2.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotTables/pivotTable1.xml" ContentType="{PivotTableContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/pivotTables/pivotTable1.xml pivot cache definition relationship rIdPivotCacheDefinition2 targets xl/pivotCache/pivotCacheDefinition2.xml, but workbook cacheId 0 targets xl/pivotCache/pivotCacheDefinition1.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorksheetPivotTableMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/worksheets/sheet1.xml pivotTableDefinition #1 references missing relationship rIdPivotTable1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPivotTableWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotTable1", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
                ("xl/pivotTables/pivotTable1.xml", PivotTableXml("0"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/pivotTables/pivotTable1.xml has content type application/xml; expected {PivotTableContentType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPivotTableCacheIdWithoutWorkbookPivotCache()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: WorkbookWithPivotCachesXml("""<pivotCache cacheId="0" r:id="rIdPivotCache1" />"""),
            worksheetXml: WorksheetWithPivotTableXml("rIdPivotTable1"),
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rIdPivotCache1", PivotCacheDefinitionRelationshipType, "pivotCache/pivotCacheDefinition1.xml")
            ],
            extraEntries:
            [
                ("xl/pivotCache/pivotCacheDefinition1.xml", PivotCacheDefinitionXml()),
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdPivotTable1", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
                ("xl/pivotTables/pivotTable1.xml", PivotTableXml("9"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="{PivotCacheDefinitionContentType}" />""",
                $"""<Override PartName="/xl/pivotTables/pivotTable1.xml" ContentType="{PivotTableContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/pivotTables/pivotTable1.xml references cacheId 9, but workbook has no matching pivotCache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithWorksheetDrawingPackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithDrawingXml("rIdDrawing1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdDrawing1", DrawingRelationshipType, "../drawings/drawing1.xml"))),
                ("xl/drawings/drawing1.xml", DrawingWithChartAndImageXml("rIdChart1", "rIdImage1")),
                ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml(
                    Relationship("rIdChart1", ChartRelationshipType, "../charts/chart1.xml"),
                    Relationship("rIdImage1", ImageRelationshipType, "../media/image1.png"))),
                ("xl/charts/chart1.xml", ChartXml()),
                ("xl/media/image1.png", "png")
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/drawings/drawing1.xml" ContentType="{DrawingContentType}" />""",
                $"""<Override PartName="/xl/charts/chart1.xml" ContentType="{ChartContentType}" />"""
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="image/png" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsWorksheetDrawingMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithDrawingXml("rIdDrawing1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml())
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/worksheets/sheet1.xml drawing #1 references missing relationship rIdDrawing1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsDrawingChartWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithDrawingXml("rIdDrawing1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdDrawing1", DrawingRelationshipType, "../drawings/drawing1.xml"))),
                ("xl/drawings/drawing1.xml", DrawingWithChartXml("rIdChart1")),
                ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml(
                    Relationship("rIdChart1", ChartRelationshipType, "../charts/chart1.xml"))),
                ("xl/charts/chart1.xml", ChartXml())
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/drawings/drawing1.xml" ContentType="{DrawingContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/charts/chart1.xml has content type application/xml; expected {ChartContentType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsDrawingImageWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithDrawingXml("rIdDrawing1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdDrawing1", DrawingRelationshipType, "../drawings/drawing1.xml"))),
                ("xl/drawings/drawing1.xml", DrawingWithImageXml("rIdImage1")),
                ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml(
                    Relationship("rIdImage1", ImageRelationshipType, "../media/image1.png"))),
                ("xl/media/image1.png", "png")
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/drawings/drawing1.xml" ContentType="{DrawingContentType}" />"""
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="application/octet-stream" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/media/image1.png has content type application/octet-stream; expected an image/* content type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithWorksheetBackgroundPicture()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithBackgroundPictureXml("rIdBackgroundPicture1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdBackgroundPicture1", ImageRelationshipType, "../media/background1.png"))),
                ("xl/media/background1.png", "png")
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="image/png" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsWorksheetBackgroundPictureMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithBackgroundPictureXml("rIdBackgroundPicture1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml())
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/worksheets/sheet1.xml background picture #1 reference rIdBackgroundPicture1: targets missing relationship rIdBackgroundPicture1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorksheetBackgroundPictureWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithBackgroundPictureXml("rIdBackgroundPicture1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdBackgroundPicture1", ImageRelationshipType, "../media/background1.png"))),
                ("xl/media/background1.png", "png")
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="application/octet-stream" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/media/background1.png has content type application/octet-stream; expected an image/* content type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AcceptsWorkbookWithWorksheetTablePackage()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithTablePartsXml("rIdTable1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdTable1", TableRelationshipType, "../tables/table1.xml"))),
                ("xl/tables/table1.xml", TableXml("1", "Table1", "A1:B2", """
                    <tableColumn id="1" name="Column1" />
                    <tableColumn id="2" name="Column2" />
                    """))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/tables/table1.xml" ContentType="{TableContentType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FlagsWorksheetTableMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithTablePartsXml("rIdTable1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml())
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/worksheets/sheet1.xml tablePart #1 references missing relationship rIdTable1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorksheetTableWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithTablePartsXml("rIdTable1"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdTable1", TableRelationshipType, "../tables/table1.xml"))),
                ("xl/tables/table1.xml", TableXml("1", "Table1", "A1:B2", """
                    <tableColumn id="1" name="Column1" />
                    <tableColumn id="2" name="Column2" />
                    """))
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains($"xl/tables/table1.xml has content type application/xml; expected {TableContentType}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorksheetTableMetadataCorruption()
    {
        using var package = CreateMinimalWorkbookPackage(
            worksheetXml: WorksheetWithTablePartsXml("rIdTable1", count: "2"),
            extraEntries:
            [
                ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                    Relationship("rIdTable1", TableRelationshipType, "../tables/table1.xml"))),
                ("xl/tables/table1.xml", TableXml("0", "", "", """
                    <tableColumn id="1" name="Column1" />
                    <tableColumn id="1" name="Column1" />
                    """, columnCount: "3"))
            ],
            contentTypeOverrides:
            [
                $"""<Override PartName="/xl/tables/table1.xml" ContentType="{TableContentType}" />"""
            ]);

        var issues = XlsxPackageHealthValidator.Validate(package);

        issues.Should().Contain(issue => issue.Contains("xl/worksheets/sheet1.xml tableParts count is 2, but contains 1 tablePart entries", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml table has invalid id '0'", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml table has no ref", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml table has no displayName", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml tableColumns count is 3, but contains 2 tableColumn entries", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml tableColumns has duplicate tableColumn id 1", StringComparison.OrdinalIgnoreCase));
        issues.Should().Contain(issue => issue.Contains("xl/tables/table1.xml tableColumns has duplicate tableColumn name 'Column1'", StringComparison.OrdinalIgnoreCase));
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
    public void Validate_FlagsDuplicateDefaultContentTypeDeclaration()
    {
        using var package = CreateMinimalWorkbookPackage(
            contentTypeDefaults:
            [
                """<Default Extension="xml" ContentType="application/xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("duplicate Default extension 'xml'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsDuplicateOverrideContentTypeDeclaration()
    {
        using var package = CreateMinimalWorkbookPackage(
            contentTypeOverrides:
            [
                """<Override PartName="/xl/workbook.xml" ContentType="application/xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("duplicate Override PartName '/xl/workbook.xml'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPackagePartWithoutEffectiveContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/customPayload/item1.bin", "")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/customPayload/item1.bin has no effective package content type", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/xl\\workbook.xml", "must use forward slashes")]
    [InlineData("/xl/workbook.xml?part=1", "must not include query or fragment text")]
    [InlineData("/xl/workbook.xml#fragment", "must not include query or fragment text")]
    public void Validate_FlagsInvalidOverridePartName(string partName, string expectedWarning)
    {
        using var package = CreateMinimalWorkbookPackage(
            contentTypeOverrides:
            [
                $"""<Override PartName="{partName}" ContentType="application/xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains(expectedWarning, StringComparison.OrdinalIgnoreCase));
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
    public void Validate_FlagsNonRelationshipRelsExtension()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/not-a-relationship.rels", "")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains(".rels extension outside a valid relationship part location", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipPartWithoutOwningPackagePart()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/drawings/_rels/drawing1.xml.rels", RelationshipsXml(
                    Relationship(
                        "rId1",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                        "../media/image1.png"))),
                ("xl/media/image1.png", "")
            ],
            contentTypeDefaults:
            [
                """<Default Extension="png" ContentType="image/png" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/drawings/_rels/drawing1.xml.rels has no owning package part xl/drawings/drawing1.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsMissingPackageRootRelationshipsPart()
    {
        using var package = CreateMinimalWorkbookPackage(
            omitRootRelationships: true);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("missing package root relationships part _rels/.rels", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPackageRootRelationshipsWithoutOfficeDocument()
    {
        using var package = CreateMinimalWorkbookPackage(
            rootRelationships:
            [
                Relationship("rIdMetadata", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")
            ],
            extraEntries:
            [
                ("docProps/core.xml", "<coreProperties />")
            ],
            contentTypeOverrides:
            [
                """<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("has no http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument relationship", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsDuplicatePackageRootOfficeDocumentRelationships()
    {
        using var package = CreateMinimalWorkbookPackage(
            rootRelationships:
            [
                Relationship("rIdWorkbook1", OfficeDocumentRelationshipType, "xl/workbook.xml"),
                Relationship("rIdWorkbook2", OfficeDocumentRelationshipType, "xl/workbook.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("has multiple http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument relationships", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsExternalPackageRootOfficeDocumentRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            rootRelationships:
            [
                $"""<Relationship Id="rIdWorkbook" Type="{OfficeDocumentRelationshipType}" Target="https://example.invalid/workbook.xml" TargetMode="External" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("Relationship rIdWorkbook must target the workbook package part internally", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsPackageRootOfficeDocumentTargetWithNonWorkbookContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            rootRelationships:
            [
                Relationship("rIdWorkbook", OfficeDocumentRelationshipType, "xl/not-a-workbook.xml")
            ],
            extraEntries:
            [
                ("xl/not-a-workbook.xml", "<worksheet />")
            ],
            contentTypeOverrides:
            [
                """<Override PartName="/xl/not-a-workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("targets xl/not-a-workbook.xml with non-workbook content type application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookSheetWithoutRelationshipId()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" />
                  </sheets>
                </workbook>
                """);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/workbook.xml sheet Sheet1 has no relationship id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookSheetMissingRelationship()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookXml: """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rIdMissing" />
                  </sheets>
                </workbook>
                """);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/workbook.xml sheet Sheet1 references missing workbook relationship rIdMissing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookSheetRelationshipWithNonSheetType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml")
            ],
            extraEntries:
            [
                ("xl/styles.xml", "<styleSheet />")
            ],
            contentTypeOverrides:
            [
                """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/workbook.xml sheet Sheet1 relationship rId1 has non-sheet Type http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookSheetRelationshipWithExternalTarget()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                $"""<Relationship Id="rId1" Type="{WorksheetRelationshipType}" Target="https://example.invalid/sheet.xml" TargetMode="External" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/workbook.xml sheet Sheet1 relationship rId1 must target a sheet package part internally", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsWorkbookSheetRelationshipTargetWithWrongContentType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "styles.xml")
            ],
            extraEntries:
            [
                ("xl/styles.xml", "<styleSheet />")
            ],
            contentTypeOverrides:
            [
                """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("xl/workbook.xml sheet Sheet1 relationship rId1 targets xl/styles.xml with content type application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml; expected application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipContentTypeOnNonRelationshipPart()
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                ("xl/customPayload/item1.bin", "")
            ],
            contentTypeDefaults:
            [
                """<Default Extension="bin" ContentType="application/vnd.openxmlformats-package.relationships+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("uses relationship content type but is not a valid relationship part", StringComparison.OrdinalIgnoreCase));
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
    public void Validate_FlagsRelationshipTargetWithBackslashes()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, @"worksheets\sheet1.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("Target uses backslashes instead of package URI separators", StringComparison.OrdinalIgnoreCase));
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
    public void Validate_PreservesEncodedPathSeparatorsWhenResolvingRelationshipTargets()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet%2F1.xml")
            ],
            extraEntries:
            [
                ("xl/worksheets/sheet%2F1.xml", """
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <sheetData />
                    </worksheet>
                    """)
            ],
            contentTypeOverrides:
            [
                """<Override PartName="/xl/worksheets/sheet%2F1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package).Should().BeEmpty();
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

    [Theory]
    [InlineData(@"xl\workbook.xml", "uses a backslash in the package part name")]
    [InlineData("/xl/workbook.xml", "starts with '/'")]
    [InlineData("xl//workbook.xml", "has an empty path segment")]
    [InlineData("xl/../workbook.xml", "has a relative path segment")]
    public void Validate_FlagsInvalidPackageEntryNames(string entryName, string expectedWarning)
    {
        using var package = CreateMinimalWorkbookPackage(
            extraEntries:
            [
                (entryName, "")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains(expectedWarning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsInvalidRelationshipTargetMode()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                $"""<Relationship Id="rId1" Type="{WorksheetRelationshipType}" Target="worksheets/sheet1.xml" TargetMode="Embed" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("invalid TargetMode Embed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsUnexpectedRelationshipAttribute()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                $"""<Relationship Id="rId1" Type="{WorksheetRelationshipType}" Target="worksheets/sheet1.xml" Extra="1" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("unexpected attribute 'Extra'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipWithChildElement()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                $"""
                <Relationship Id="rId1" Type="{WorksheetRelationshipType}" Target="worksheets/sheet1.xml">
                  <Unexpected />
                </Relationship>
                """
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("must not contain child elements", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsDuplicateRelationshipId()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml"),
                Relationship("rId1", WorksheetRelationshipType, "worksheets/sheet1.xml")
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("duplicate Relationship Id rId1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipWithoutType()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                """<Relationship Id="rId1" Target="worksheets/sheet1.xml" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("Relationship rId1 has no Type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FlagsRelationshipWithoutTarget()
    {
        using var package = CreateMinimalWorkbookPackage(
            workbookRelationships:
            [
                $"""<Relationship Id="rId1" Type="{WorksheetRelationshipType}" />"""
            ]);

        XlsxPackageHealthValidator.Validate(package)
            .Should()
            .Contain(issue => issue.Contains("Relationship rId1 has no Target", StringComparison.OrdinalIgnoreCase));
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
        string? workbookXml = null,
        string? worksheetXml = null,
        IReadOnlyList<string>? rootRelationships = null,
        IReadOnlyList<string>? workbookRelationships = null,
        IReadOnlyList<string>? contentTypeOverrides = null,
        IReadOnlyList<string>? contentTypeDefaults = null,
        IReadOnlyList<(string Path, string Content)>? extraEntries = null,
        string workbookContentType = WorkbookContentType,
        bool omitRootRelationships = false)
    {
        var entries = new List<(string Path, string Content)>
        {
            ("[Content_Types].xml", ContentTypesXml(contentTypeOverrides, contentTypeDefaults, workbookContentType)),
            ("xl/workbook.xml", workbookXml ?? """
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
            ("xl/worksheets/sheet1.xml", worksheetXml ?? """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData />
                </worksheet>
                """)
        };

        if (!omitRootRelationships)
        {
            entries.Add(("_rels/.rels", RelationshipsXml(
                rootRelationships?.ToArray() ??
                [
                    Relationship("rIdWorkbook", OfficeDocumentRelationshipType, "xl/workbook.xml")
                ])));
        }

        if (extraEntries is not null)
            entries.AddRange(extraEntries);

        return XlsxPackageTestFixtures.CreatePackage([.. entries]);
    }

    private static string ContentTypesXml(
        IReadOnlyList<string>? overrides,
        IReadOnlyList<string>? defaults,
        string workbookContentType)
    {
        var defaultDeclarations = new[]
        {
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />""",
            """<Default Extension="xml" ContentType="application/xml" />"""
        }.Concat(defaults ?? []);

        var overrideDeclarations = new[]
        {
            $"""<Override PartName="/xl/workbook.xml" ContentType="{workbookContentType}" />""",
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

    private static string Relationship(string id, string type, string target, string targetMode) =>
        $"""<Relationship Id="{id}" Type="{type}" Target="{target}" TargetMode="{targetMode}" />""";

    private static string WorkbookWithExternalReferenceXml(string relationshipId) =>
        $"""
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
          </sheets>
          <externalReferences>
            <externalReference r:id="{relationshipId}" />
          </externalReferences>
        </workbook>
        """;

    private static string ExternalLinkXml(string relationshipId) =>
        $"""
        <externalLink xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <externalBook r:id="{relationshipId}" />
        </externalLink>
        """;

    private static string WorkbookWithPivotCachesXml(string pivotCacheElements) =>
        $"""
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Sheet1" sheetId="1" r:id="rId1" />
          </sheets>
          <pivotCaches>
            {pivotCacheElements}
          </pivotCaches>
        </workbook>
        """;

    private static string PivotCacheDefinitionXml(string? recordsRelationshipId = null) =>
        string.IsNullOrWhiteSpace(recordsRelationshipId)
            ? """<pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""
            : $"""<pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{recordsRelationshipId}" />""";

    private static string PivotCacheRecordsXml() =>
        """<pivotCacheRecords xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" />""";

    private static string WorksheetWithPivotTableXml(string relationshipId) =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData />
          <pivotTableDefinition r:id="{relationshipId}" />
        </worksheet>
        """;

    private static string PivotTableXml(string cacheId) =>
        $"""<pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="PivotTable1" cacheId="{cacheId}" />""";

    private static string WorksheetWithDrawingXml(string relationshipId) =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData />
          <drawing r:id="{relationshipId}" />
        </worksheet>
        """;

    private static string DrawingWithChartAndImageXml(string chartRelationshipId, string imageRelationshipId) =>
        DrawingXml($"""
            <xdr:twoCellAnchor>
              <xdr:graphicFrame>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                    <c:chart r:id="{chartRelationshipId}" />
                  </a:graphicData>
                </a:graphic>
              </xdr:graphicFrame>
            </xdr:twoCellAnchor>
            <xdr:oneCellAnchor>
              <xdr:pic>
                <xdr:blipFill>
                  <a:blip r:embed="{imageRelationshipId}" />
                </xdr:blipFill>
              </xdr:pic>
            </xdr:oneCellAnchor>
            """);

    private static string DrawingWithChartXml(string relationshipId) =>
        DrawingXml($"""
            <xdr:twoCellAnchor>
              <xdr:graphicFrame>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                    <c:chart r:id="{relationshipId}" />
                  </a:graphicData>
                </a:graphic>
              </xdr:graphicFrame>
            </xdr:twoCellAnchor>
            """);

    private static string DrawingWithImageXml(string relationshipId) =>
        DrawingXml($"""
            <xdr:oneCellAnchor>
              <xdr:pic>
                <xdr:blipFill>
                  <a:blip r:embed="{relationshipId}" />
                </xdr:blipFill>
              </xdr:pic>
            </xdr:oneCellAnchor>
            """);

    private static string DrawingXml(string body) =>
        $"""
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          {body}
        </xdr:wsDr>
        """;

    private static string ChartXml() =>
        """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <c:chart />
        </c:chartSpace>
        """;

    private static string WorksheetWithBackgroundPictureXml(string relationshipId) =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData />
          <picture r:id="{relationshipId}" />
        </worksheet>
        """;

    private static string WorksheetWithTablePartsXml(string relationshipId, string count = "1") =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheetData />
          <tableParts count="{count}">
            <tablePart r:id="{relationshipId}" />
          </tableParts>
        </worksheet>
        """;

    private static string TableXml(
        string id,
        string displayName,
        string reference,
        string tableColumns,
        string columnCount = "2") =>
        $"""
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="{id}"
               name="{displayName}"
               displayName="{displayName}"
               ref="{reference}">
          <autoFilter ref="{reference}" />
          <tableColumns count="{columnCount}">
            {tableColumns}
          </tableColumns>
        </table>
        """;

    private static string SharedStringWorksheetXml(string sharedStringIndex) =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" t="s"><v>{sharedStringIndex}</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private static string SharedStringsXml(string sharedStringItems) =>
        $"""
        <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          {sharedStringItems}
        </sst>
        """;

    private static string StyledWorksheetXml(string styleIndex) =>
        $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cols>
            <col min="1" max="1" style="0" />
          </cols>
          <sheetData>
            <row r="1" s="0" customFormat="1">
              <c r="A1" s="{styleIndex}"><v>42</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private static string StylesXml(int cellFormatCount, int? declaredCellFormatCount = null)
    {
        var cellFormats = string.Join(Environment.NewLine, Enumerable.Repeat("""<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" />""", cellFormatCount));
        return $"""
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="1"><font /></fonts>
          <fills count="1"><fill /></fills>
          <borders count="1"><border /></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" /></cellStyleXfs>
          <cellXfs count="{declaredCellFormatCount ?? cellFormatCount}">
            {cellFormats}
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0" /></cellStyles>
        </styleSheet>
        """;
    }
}
