using System.Reflection;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFileAdapterFormatTests
{
    [Fact]
    public void Load_MapsBuiltInNumberFormatIdsToModelFormatCodes()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Formats");
            sheet.Cell("A1").Value = 1234.56;
            sheet.Cell("A1").Style.NumberFormat.NumberFormatId = 4;
            sheet.Cell("A2").Value = 0.875;
            sheet.Cell("A2").Style.NumberFormat.NumberFormatId = 10;
            sheet.Cell("A3").Value = 1.5;
            sheet.Cell("A3").Style.NumberFormat.NumberFormatId = 13;
            sheet.Cell("A4").Value = "Text value";
            sheet.Cell("A4").Style.NumberFormat.NumberFormatId = 49;
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream);
        var sheetModel = loaded.GetSheetAt(0);

        loaded.GetStyle(sheetModel.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be("#,##0.00");
        loaded.GetStyle(sheetModel.GetCell(2, 1)!.StyleId).NumberFormat.Should().Be("0.00%");
        loaded.GetStyle(sheetModel.GetCell(3, 1)!.StyleId).NumberFormat.Should().Be("# ??/??");
        loaded.GetStyle(sheetModel.GetCell(4, 1)!.StyleId).NumberFormat.Should().Be("@");
    }

    [Fact]
    public void Save_RoundTripsStyleOnlyCellsWithoutMaterializingBlanks()
    {
        var workbook = new Workbook("Style-only xlsx");
        var sheet = workbook.AddSheet("Styled blanks");
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(221, 235, 247),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213))
        });

        sheet.SetStyleOnly(1, 3, styleId);
        sheet.SetStyleOnly(1, 4, styleId);
        sheet.SetStyleOnly(1, 5, styleId);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(42));

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.Sheets[0];

        loadedSheet.GetCell(1, 3).Should().BeNull();
        loadedSheet.GetCell(1, 5).Should().BeNull();
        loadedSheet.GetCell(1, 4)!.Value.Should().Be(new NumberValue(42));
        loadedSheet.GetStyleOnly(1, 3).Should().NotBeNull();
        loadedSheet.GetStyleOnly(1, 5).Should().NotBeNull();
        loaded.GetStyle(loadedSheet.GetStyleOnly(1, 3)!.Value).FillColor.Should().Be(new CellColor(221, 235, 247));
        loaded.GetStyle(loadedSheet.GetStyleOnly(1, 5)!.Value).BorderBottom.Style.Should().Be(BorderStyle.Thin);
    }

    [Fact]
    public void Load_AppliesNativeCellXfBordersToPopulatedCells()
    {
        using var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/styles.xml", document =>
        {
            XNamespace ns = document.Root!.Name.Namespace;
            document.Root.Element(ns + "borders")!.ReplaceWith(
                new XElement(ns + "borders",
                    new XAttribute("count", "2"),
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top"),
                        new XElement(ns + "bottom"),
                        new XElement(ns + "diagonal")),
                    new XElement(ns + "border",
                        new XElement(ns + "left", new XAttribute("style", "medium")),
                        new XElement(ns + "right", new XAttribute("style", "medium")),
                        new XElement(ns + "top", new XAttribute("style", "medium")),
                        new XElement(ns + "bottom", new XAttribute("style", "medium")),
                        new XElement(ns + "diagonal"))));
            document.Root.Element(ns + "cellXfs")!.ReplaceWith(
                new XElement(ns + "cellXfs",
                    new XAttribute("count", "2"),
                    new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                    new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyBorder", "1"))));
        });
        XlsxPackageTestHelper.PatchWorksheetXml(package, document =>
        {
            XNamespace ns = document.Root!.Name.Namespace;
            document.Root
                .Element(ns + "sheetData")!
                .Element(ns + "row")!
                .Element(ns + "c")!
                .SetAttributeValue("s", "1");
        });

        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        var style = loaded.GetStyle(loadedSheet.GetCell(1, 1)!.StyleId);

        style.BorderTop.Style.Should().Be(BorderStyle.Medium);
        style.BorderRight.Style.Should().Be(BorderStyle.Medium);
        style.BorderBottom.Style.Should().Be(BorderStyle.Medium);
        style.BorderLeft.Style.Should().Be(BorderStyle.Medium);
    }

    [Fact]
    public void CellBorderStyleReader_ReadsCellXfBorderTable()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = new XDocument(
            new XElement(ns + "styleSheet",
                new XElement(ns + "borders",
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top"),
                        new XElement(ns + "bottom")),
                    new XElement(ns + "border",
                        new XElement(ns + "left", new XAttribute("style", "thin")),
                        new XElement(ns + "right", new XAttribute("style", "dashed")),
                        new XElement(ns + "top", new XAttribute("style", "medium"),
                            new XElement(ns + "color", new XAttribute("rgb", "FF1F4E79"))),
                        new XElement(ns + "bottom", new XAttribute("style", "double")))),
                new XElement(ns + "cellXfs",
                    new XElement(ns + "xf", new XAttribute("borderId", "0")),
                    new XElement(ns + "xf", new XAttribute("borderId", "1")))));

        var table = XlsxCellBorderStyleReader.Read(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        table.TryGetVisibleBorders(1, out var borders).Should().BeTrue();
        borders.Top.Should().Be(new CellBorder(BorderStyle.Medium, CellColor.FromArgb(0x1F, 0x4E, 0x79)));
        borders.Right.Style.Should().Be(BorderStyle.Dashed);
        borders.Bottom.Style.Should().Be(BorderStyle.Double);
        borders.Left.Style.Should().Be(BorderStyle.Thin);
    }

    [Fact]
    public void Load_UsesWorkbookDefaultStyleFromXlsxStyleZero()
    {
        using var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/styles.xml", document =>
        {
            XNamespace ns = document.Root!.Name.Namespace;
            var font = document.Root.Element(ns + "fonts")!.Elements(ns + "font").First();
            font.Element(ns + "name")!.SetAttributeValue("val", "Arial");
            font.Element(ns + "sz")!.SetAttributeValue("val", "10");
        });

        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        var defaultStyle = loaded.GetStyle(StyleId.Default);

        defaultStyle.FontName.Should().Be("Arial");
        defaultStyle.FontSize.Should().Be(10);
        loadedSheet.GetCell(1, 1)!.StyleId.Should().Be(StyleId.Default);
    }

    [Fact]
    public void Load_UsesAptosNarrowStandardRowHeightWhenSheetDefaultIsUncustomized()
    {
        using var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/styles.xml", document =>
        {
            XNamespace ns = document.Root!.Name.Namespace;
            foreach (var font in document.Root.Element(ns + "fonts")!.Elements(ns + "font"))
            {
                font.Element(ns + "name")!.SetAttributeValue("val", "Aptos Narrow");
                font.Element(ns + "sz")!.SetAttributeValue("val", "11");
            }
        });
        XlsxPackageTestHelper.PatchWorksheetXml(package, document =>
        {
            XNamespace ns = document.Root!.Name.Namespace;
            var sheetFormat = document.Root!.Element(ns + "sheetFormatPr");
            if (sheetFormat is null)
            {
                sheetFormat = new XElement(ns + "sheetFormatPr");
                document.Root.AddFirst(sheetFormat);
            }

            sheetFormat.SetAttributeValue("defaultRowHeight", "15");
            sheetFormat.SetAttributeValue("customHeight", null);
        });

        var loaded = new XlsxFileAdapter().Load(package);

        loaded.GetSheetAt(0).DefaultRowHeight.Should().Be(19);
    }

    [Fact]
    public void Save_WritesWorkbookDefaultStyleToXlsxStyleZero()
    {
        var workbook = new Workbook("Default style", new CellStyle
        {
            FontName = "Arial",
            FontSize = 10
        });
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("default font"));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var loaded = new XLWorkbook(stream);
        loaded.Worksheet(1).Cell("A1").Style.Font.FontName.Should().Be("Arial");
        loaded.Worksheet(1).Cell("A1").Style.Font.FontSize.Should().Be(10);
    }

    [Fact]
    public void Save_LoadedWorkbookDropsMalformedDuplicateCorePropertiesRelationship()
    {
        const string validCorePropertiesType =
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
        const string malformedCorePropertiesType =
            "http://schemas.openxmlformats.org/package/2006/relationships/meatadata/core-properties";
        using var package = XlsxPackageTestHelper.CreateSingleCellWorkbookPackage();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("docProps/core.xml")?.Delete();
            var corePropertiesEntry = archive.CreateEntry("docProps/core.xml");
            using var writer = new StreamWriter(corePropertiesEntry.Open());
            writer.Write(
                """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                                   xmlns:dcterms="http://purl.org/dc/terms/"
                                   xmlns:dcmitype="http://purl.org/dc/dcmitype/"
                                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <dc:creator>FreeX</dc:creator>
                </cp:coreProperties>
                """);
        }

        XlsxPackageTestHelper.PatchPackageXml(package, "[Content_Types].xml", document =>
        {
            XNamespace contentTypesNs = document.Root!.Name.Namespace;
            document.Root.Add(new XElement(
                contentTypesNs + "Override",
                new XAttribute("PartName", "/docProps/core.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
        });
        XlsxPackageTestHelper.PatchPackageXml(package, "_rels/.rels", document =>
        {
            XNamespace relNs = document.Root!.Name.Namespace;
            document.Root
                .Elements(relNs + "Relationship")
                .Where(relationship =>
                    string.Equals(
                        relationship.Attribute("Type")?.Value,
                        validCorePropertiesType,
                        StringComparison.OrdinalIgnoreCase))
                .Remove();
            document.Root.Add(new XElement(
                relNs + "Relationship",
                new XAttribute("Id", "rIdMalformedCoreProps"),
                new XAttribute("Type", malformedCorePropertiesType),
                new XAttribute("Target", "/docProps/core.xml")));
        });

        var adapter = new XlsxFileAdapter();
        package.Position = 0;
        var workbook = adapter.Load(package);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("forces full save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels");
            var corePropertyRelationships = relsXml.Root!
                .Elements(relNs + "Relationship")
                .Where(relationship =>
                    relationship.Attribute("Target")?.Value.Trim().TrimStart('/') is "docProps/core.xml")
                .ToArray();

            corePropertyRelationships.Should().ContainSingle();
            corePropertyRelationships[0].Attribute("Type")!.Value.Should().Be(validCorePropertiesType);
        }

        saved.Position = 0;
        using var document = SpreadsheetDocument.Open(saved, isEditable: false);
        document.WorkbookPart.Should().NotBeNull();
    }

    [Fact]
    public void Save_WritesNonFiniteNumbersAsTextCells()
    {
        var workbook = new Workbook("NonFinite xlsx");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(double.NegativeInfinity));

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_WritesNonFiniteDateTimesAsTextCells()
    {
        var workbook = new Workbook("NonFinite dates xlsx");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(double.NegativeInfinity));

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_WritesOutOfRangeDateTimesAsTextCells()
    {
        var workbook = new Workbook("OutOfRange dates xlsx");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.MaxValue));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.MinValue));

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue(double.MaxValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue(double.MinValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void LoadPath_AvoidsFullPackageToArrayCopies()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.cs");
        var saveSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.Save.cs");
        var savePostProcessingSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var diagnosticsSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetDiagnosticsMapper.cs");
        var sanitizerSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxClosedXmlLoadPackageSanitizer.cs");
        var worksheetMetadataSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetMetadataPreserver.cs")
            .ReplaceLineEndings("\n");
        var worksheetCellMetadataSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetMetadataPreserver.CellMetadata.cs");
        var worksheetMergeHelpersSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetMetadataPreserver.MergeHelpers.cs");
        var drawingPartMergerSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetDrawingPartMerger.cs");
        var pivotReferencePreserverSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxPivotXmlReferencePreserver.cs");
        var tableReferencePreserverSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxStructuredTableReferencePreserver.cs");
        var styleOnlyStripperSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxClosedXmlStyleOnlyCellStripper.cs");
        var sheetXmlLayoutSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SheetXmlLayout.cs");
        var sourcePackageSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SourcePackage.cs")
            .ReplaceLineEndings("\n");
        var preserveSourcePackageParts = sourcePackageSource[
            sourcePackageSource.IndexOf("private static SourcePackagePartSummary PreserveSourcePackageParts", StringComparison.Ordinal)..
            sourcePackageSource.IndexOf("private struct SourcePackagePartSummary", StringComparison.Ordinal)];
        var legacyDrawingHfDependencies = sourcePackageSource[
            sourcePackageSource.IndexOf("private static IEnumerable<string> GetLegacyDrawingHfDependencyPaths", StringComparison.Ordinal)..
            sourcePackageSource.IndexOf("private static IEnumerable<string> GetRelationshipDependencyPaths", StringComparison.Ordinal)];

        adapterSource.Should().NotContain("packageStream.ToArray()");
        saveSource.Should().NotContain("GetUsedCells()");
        saveSource.Should().Contain("ApplyStyleOnlySeedCells");
        saveSource.Should().Contain("XlsxStyleOnlyCellWriter.GetSeedCells(sheet)");
        savePostProcessingSource.Should().Contain("XlsxStyleOnlyCellWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        saveSource.Should().NotContain(".GroupBy(entry => entry.Key.Row)");
        saveSource.Should().NotContain(".OrderBy(entry => entry.Key.Col)");
        savePostProcessingSource.Should().NotContain("GetUsedCells()");
        diagnosticsSource.Should().NotContain("GetUsedCells()");
        adapterSource.Should().Contain("CreateLoadPackageStream(stream)");
        sanitizerSource.Should().NotContain("sourcePackage.ToArray()");
        sanitizerSource.Should().Contain("GetSanitizationRequirements(");
        sanitizerSource.Should().Contain("XlsxClosedXmlLoadSanitizationHints? hints = null");
        sanitizerSource.Should().Contain("bool mutateSourcePackage = false");
        sanitizerSource.Should().Contain("IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip");
        sanitizerSource.Should().Contain("CreateFusedTransientPackage(");
        sanitizerSource.Should().Contain("RemoveDrawingPackageParts(archive)");
        sanitizerSource.Should().Contain("RemoveSheetDrawingReferences(archive)");
        sanitizerSource.Should().Contain("RemoveSheetDrawingRelationships(archive, removedParts)");
        adapterSource.Should().Contain("var sheetXmlLayoutWarningCount = warnings.Count;");
        adapterSource.Should().Contain("sheetXmlLayout = LoadSheetXmlLayout(");
        adapterSource.Should().Contain("packageParts.HasStructuredTables,");
        adapterSource.Should().Contain("warnings.Count != sheetXmlLayoutWarningCount");
        adapterSource.Should().Contain("OpenClosedXmlWorkbookWithSanitizationFallback(");
        adapterSource.Should().Contain("styleOnlyWorksheetPathsToStrip");
        adapterSource.Should().Contain("sanitizationHints");
        sanitizerSource.Should().Contain("removeUnsupportedConditionalFormatting");
        sanitizerSource.Should().Contain("return sourcePackage;");
        worksheetMetadataSource.Should().NotContain(".Descendants(workbookNs + \"c\")\n                .Where(cell => !string.IsNullOrWhiteSpace(cell.Attribute(\"r\")?.Value))\n                .ToList();");
        worksheetMetadataSource.Should().Contain("MergeWorksheetCellNativeMetadata(sourceSheetData, GetTargetCellsByAddress, targetArchive, workbookNs)");
        worksheetCellMetadataSource.Should().Contain("private static bool MergeWorksheetCellNativeMetadata");
        worksheetCellMetadataSource.Should().Contain("GetSourceCellNativeMetadata(sourceCell, workbookNs)");
        worksheetMergeHelpersSource.Should().Contain(".Where(shouldRetain)");
        drawingPartMergerSource.Should().Contain("ReadWorksheetDrawingRelId(worksheetEntry, worksheetNs, relNs)");
        drawingPartMergerSource.Should().Contain("XmlReader.Create");
        pivotReferencePreserverSource.Should().Contain("GetWorksheetPathsWithPivotTableRelationships(context)");
        pivotReferencePreserverSource.Should().Contain("PreserveWorksheetPivotTableDefinitions(context, pivotWorksheetPaths)");
        tableReferencePreserverSource.Should().Contain("GetWorksheetPathsWithTableRelationships(sourceArchive, context)");
        adapterSource.Should().Contain("XlsxClosedXmlLoadPackageSanitizer.Create(");
        sanitizerSource.Should().Contain("styleOnlyWorksheetPathsToStrip is not { Count: 0 }");
        adapterSource.Should().NotContain("XlsxClosedXmlStyleOnlyCellStripper.Create(packageStream, styleOnlyWorksheetPathsToStrip)");
        adapterSource.Should().NotContain("mutateSourcePackage: canMutateStyleOptimizedPackage");
        styleOnlyStripperSource.Should().Contain("XmlWriter.Create(outputStream, writerSettings)");
        styleOnlyStripperSource.Should().Contain("reader.IsEmptyElement");
        styleOnlyStripperSource.Should().Contain("XNode.ReadFrom(reader)");
        styleOnlyStripperSource.Should().Contain("seenStyleIndexes.Add(styleIndex)");
        styleOnlyStripperSource.Should().NotContain("byte[]? StripRedundantStyleOnlyCells");
        styleOnlyStripperSource.Should().NotContain("output.ToArray()");
        styleOnlyStripperSource.Should().NotContain("XDocument.Load(sourceStream)");
        styleOnlyStripperSource.Should().NotContain("worksheetXml.Descendants(worksheetNs + \"c\").ToList()");
        sheetXmlLayoutSource.Should().Contain("XlsxWorksheetDrawingPartReader.ReadParts");
        preserveSourcePackageParts.Should().Contain("var sourceParts = InspectSourcePackageParts(sourceArchive)");
        preserveSourcePackageParts.Should().Contain("sourceParts.HasPivotPackageParts");
        preserveSourcePackageParts.Should().Contain("sourceParts.HasStructuredTables");
        preserveSourcePackageParts.Should().Contain("sourceParts.HasExternalLinks");
        preserveSourcePackageParts.Should().Contain("sourceParts.HasDrawings");
        preserveSourcePackageParts.Should().Contain("XlsxWorksheetSinglePassNormalizer.NormalizeWorksheets(generatedArchive);");
        preserveSourcePackageParts.Should().NotContain("XlsxWorksheetDataConsolidationNormalizer.NormalizeWorksheets(generatedArchive);",
            "worksheet normalizers are fused into a single pass — individual NormalizeWorksheets calls must not appear at the save-pipeline level");
        preserveSourcePackageParts.Should().NotContain("XlsxWorksheetDataValidationNormalizer.NormalizeWorksheets(generatedArchive);",
            "worksheet normalizers are fused into a single pass — individual NormalizeWorksheets calls must not appear at the save-pipeline level");
        preserveSourcePackageParts.Should().NotContain("XlsxWorksheetExtensionListNormalizer.NormalizeWorksheets(generatedArchive);",
            "worksheet normalizers are fused into a single pass — individual NormalizeWorksheets calls must not appear at the save-pipeline level");
        preserveSourcePackageParts.Should().NotContain(
            "HasSourcePackagePart(sourceArchive",
            "loaded-workbook save replay should avoid rescanning all ZIP entries for each optional source package part");
        preserveSourcePackageParts.Should().NotContain(
            "HasAnySourcePackagePart(sourceArchive",
            "loaded-workbook save replay should classify source package parts in a single entry pass");
        preserveSourcePackageParts.Should().NotContain(
            "HasUnsupportedSheetPackagePart(sourceArchive",
            "unsupported sheet package part detection should reuse the single source package summary");
        sourcePackageSource.Should().Contain("foreach (var entry in archive.Entries)");
        legacyDrawingHfDependencies.Should().Contain("var legacyDrawingRelId =");
        legacyDrawingHfDependencies.Should().Contain("foreach (var relationship in relationshipsXml.Root?.Elements");
        legacyDrawingHfDependencies.Should().Contain("IsLegacyDrawingHfRelationship(relationship, legacyDrawingRelId)");
        legacyDrawingHfDependencies.Should().NotContain(
            "new HashSet<string>([relId]",
            "legacy header/footer drawing cleanup only needs a single relationship id comparison");
        legacyDrawingHfDependencies.Should().NotContain(
            ".ToList()",
            "legacy header/footer drawing cleanup should stream relationship targets instead of allocating a target list");
        adapterSource.Should().Contain("IsClosedXmlRelationshipLookupFailure(ex)",
            "ClosedXML's .First() on pivot/table relationships throws InvalidOperationException when LibreOffice-authored files have unexpected part layouts — the guard must strip pivot metadata and retry");
        adapterSource.Should().Contain("OpenPivotStripped()",
            "the pivot-stripped fallback local function must exist and be reachable from the sanitization fallback path");
        adapterSource.Should().Contain("IsClosedXmlRelationshipLookupFailure",
            "the detection method for the pivot relationship lookup failure must exist in the adapter source");
    }

    [Fact]
    public void Formats_IncludeModernExcelOpenVariants()
    {
        var adapter = new XlsxFileAdapter();

        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsx" &&
            format.CanOpen &&
            format.CanSave &&
            !format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsm" &&
            format.CanOpen &&
            !format.CanSave &&
            !format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xltx" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xltm" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
    }

    [Fact]
    public void Save_TruncatesSeekableOutputStreamBeforeWritingPackage()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("saved"));
        using var stream = new MemoryStream(new byte[1024 * 1024], writable: true);

        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position.Should().Be(stream.Length);
        stream.Length.Should().BeLessThan(1024 * 1024);
        stream.Position = 0;
        using var loaded = new ClosedXML.Excel.XLWorkbook(stream);
        loaded.Worksheet("Sheet1").Cell("A1").GetString().Should().Be("saved");
    }

    [Fact]
    public void Save_TruncatesWriteOnlySeekableOutputStreamAfterWritingPackage()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("saved"));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");

        try
        {
            File.WriteAllBytes(path, new byte[1024 * 1024]);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                new XlsxFileAdapter().Save(workbook, stream);
            }

            new FileInfo(path).Length.Should().BeLessThan(1024 * 1024);
            using var readStream = File.OpenRead(path);
            using var loaded = new ClosedXML.Excel.XLWorkbook(readStream);
            loaded.Worksheet("Sheet1").Cell("A1").GetString().Should().Be("saved");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void SavePostProcessing_UsesSourcePackageReplayOnlyForLoadedWorkbooks()
    {
        var savePostProcessingSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SavePostProcessing.cs")
            .ReplaceLineEndings("\n");
        var adapter = new XlsxFileAdapter();
        var freshWorkbook = CreateSimpleWorkbook("fresh");

        using var freshSave = new MemoryStream();
        adapter.Save(freshWorkbook, freshSave);

        HasSourcePackage(freshWorkbook).Should().BeFalse();

        freshSave.Position = 0;
        var loadedWorkbook = adapter.Load(freshSave);
        HasSourcePackage(loadedWorkbook).Should().BeTrue();

        using var loadedSave = new MemoryStream();
        adapter.Save(loadedWorkbook, loadedSave);

        HasSourcePackage(loadedWorkbook).Should().BeTrue();

        var sourcePackageCheck = savePostProcessingSource.IndexOf(
            "var hasSourcePackage = SourcePackages.TryGetValue(workbook, out var sourcePackage);",
            StringComparison.Ordinal);
        // R96-io-external-link-writer-1: the fresh-workbook early-return block now also carries a
        // conditional XlsxExternalLinkAuthoringWriter.Save call (a freshly typed bracketed
        // external-workbook reference has no source package for that writer's OTHER call site,
        // inside PreserveSourcePackageParts, to ever run against) -- anchor on the still-unique
        // opening/closing fragments of the block instead of its exact full text, so this contract
        // test keeps checking "fresh saves return before source-package replay work runs" without
        // re-breaking on every future addition to that block.
        var freshSaveBlockStart = savePostProcessingSource.IndexOf(
            "if (!hasSourcePackage)\n        {\n            SaveSourcePackageIndependentPostProcessingMetadata();",
            StringComparison.Ordinal);
        var freshSaveReturn = freshSaveBlockStart < 0
            ? -1
            : savePostProcessingSource.IndexOf(
                "NormalizeWorkbookForSchema();\n            return;\n        }",
                freshSaveBlockStart,
                StringComparison.Ordinal);
        var sourceReplay = savePostProcessingSource.IndexOf(
            "var sourceParts = PreserveSourcePackageParts(workbook, packageStream, preserveVbaProject);",
            StringComparison.Ordinal);

        sourcePackageCheck.Should().BeGreaterThanOrEqualTo(0);
        freshSaveReturn.Should().BeGreaterThan(sourcePackageCheck);
        sourceReplay.Should().BeGreaterThan(
            freshSaveReturn,
            "fresh saves should return before source-package replay work runs");
    }

    [Fact]
    public void ForgetLoadedPackageSnapshot_RemovesLoadedWorkbookSourcePackage()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateSimpleWorkbook("loaded");

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedWorkbook = adapter.Load(stream);
        HasSourcePackage(loadedWorkbook).Should().BeTrue();

        XlsxFileAdapter.ForgetLoadedPackageSnapshot(loadedWorkbook);

        HasSourcePackage(loadedWorkbook).Should().BeFalse();
    }

    [Fact]
    public void SavePostProcessing_CapturesRefreshedSourcePackageWithoutIntermediateStreamCopy()
    {
        var savePostProcessingSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        savePostProcessingSource.Should().Contain("currentModelFingerprint,");
        savePostProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata");
        savePostProcessingSource.Should().NotContain("refreshedPackageStream");
        savePostProcessingSource.Should().NotContain("packageStream.CopyTo(refreshedPackageStream)");
        snapshotSource.Should().Contain("public static XlsxSourcePackage Capture(Stream stream, Workbook workbook)");
    }

    private static Workbook CreateSimpleWorkbook(string value)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(value));
        return workbook;
    }

    private static bool HasSourcePackage(Workbook workbook)
    {
        var sourcePackagesField = typeof(XlsxFileAdapter).GetField(
            "SourcePackages",
            BindingFlags.NonPublic | BindingFlags.Static);
        sourcePackagesField.Should().NotBeNull();
        var sourcePackages = sourcePackagesField!.GetValue(null);
        sourcePackages.Should().NotBeNull();

        var tryGetValue = sourcePackages!.GetType().GetMethod("TryGetValue");
        tryGetValue.Should().NotBeNull();
        var arguments = new object?[] { workbook, null };
        return (bool)tryGetValue!.Invoke(sourcePackages, arguments)!;
    }
}
