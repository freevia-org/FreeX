using System.Reflection;
using ClosedXML.Excel;
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
        var adapterSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));
        var saveSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));
        var savePostProcessingSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var diagnosticsSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetDiagnosticsMapper.cs"));
        var sanitizerSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxClosedXmlLoadPackageSanitizer.cs"));
        var worksheetMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.cs"))
            .ReplaceLineEndings("\n");
        var worksheetCellMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.CellMetadata.cs"));
        var worksheetMergeHelpersSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.MergeHelpers.cs"));
        var drawingPartMergerSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxWorksheetDrawingPartMerger.cs"));
        var pivotReferencePreserverSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxPivotXmlReferencePreserver.cs"));
        var tableReferencePreserverSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxStructuredTableReferencePreserver.cs"));
        var styleOnlyStripperSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxClosedXmlStyleOnlyCellStripper.cs"));
        var sheetXmlLayoutSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SheetXmlLayout.cs"));
        var sourcePackageSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackage.cs"))
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
        adapterSource.Should().Contain("var sheetXmlLayoutWarningCount = warnings.Count;");
        adapterSource.Should().Contain("var sheetXmlLayout = LoadSheetXmlLayout(packageStream, stylesXml, workbookTheme, indexedColors, warnings);");
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
        pivotReferencePreserverSource.Should().Contain("GetWorksheetPathsWithPivotTableRelationships(sourceArchive, context)");
        pivotReferencePreserverSource.Should().Contain("PreserveWorksheetPivotTableDefinitions(sourceArchive, targetArchive, context, pivotWorksheetPaths)");
        tableReferencePreserverSource.Should().Contain("GetWorksheetPathsWithTableRelationships(sourceArchive, context)");
        adapterSource.Should().Contain("XlsxClosedXmlStyleOnlyCellStripper.Create(packageStream, styleOnlyWorksheetPathsToStrip)");
        adapterSource.Should().Contain("styleOnlyWorksheetPathsToStrip is not { Count: 0 }");
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
    public void SavePostProcessing_UsesSourcePackageReplayOnlyForLoadedWorkbooks()
    {
        var savePostProcessingSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"))
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
        var freshSaveReturn = savePostProcessingSource.IndexOf(
            "if (!hasSourcePackage)\n        {\n            SaveSourcePackageIndependentPostProcessingMetadata();\n            NormalizeStylesheetForSchema();\n            NormalizeWorkbookForSchema();\n            return;\n        }",
            StringComparison.Ordinal);
        var sourceReplay = savePostProcessingSource.IndexOf(
            "var sourceParts = PreserveSourcePackageParts(workbook, packageStream);",
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
        var savePostProcessingSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var snapshotSource = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        savePostProcessingSource.Should().Contain("currentModelFingerprint,");
        savePostProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata");
        savePostProcessingSource.Should().NotContain("refreshedPackageStream");
        savePostProcessingSource.Should().NotContain("packageStream.CopyTo(refreshedPackageStream)");
        snapshotSource.Should().Contain("public static XlsxSourcePackage Capture(Stream stream, Workbook workbook)");
    }

    private static string FindWorkspaceFile(params string[] relativeParts) => TestWorkspaceFiles.FindWorkspaceFile(relativeParts);

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
