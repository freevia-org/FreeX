using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFileAdapterFormatTests
{
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
            sourcePackageSource.IndexOf("private static void PreserveSourcePackageParts", StringComparison.Ordinal)..
            sourcePackageSource.IndexOf("private struct SourcePackagePartSummary", StringComparison.Ordinal)];

        adapterSource.Should().NotContain("packageStream.ToArray()");
        saveSource.Should().NotContain("GetUsedCells()");
        saveSource.Should().Contain("GetStyleOnlyRuns");
        savePostProcessingSource.Should().NotContain("GetUsedCells()");
        diagnosticsSource.Should().NotContain("GetUsedCells()");
        adapterSource.Should().Contain("CreateLoadPackageStream(stream)");
        sanitizerSource.Should().NotContain("sourcePackage.ToArray()");
        sanitizerSource.Should().Contain("GetSanitizationRequirements(sourcePackage, removeUnsupportedConditionalFormatting)");
        adapterSource.Should().Contain("OpenClosedXmlWorkbookWithSanitizationFallback(packageStream)");
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
        adapterSource.Should().Contain("XlsxClosedXmlStyleOnlyCellStripper.Create(packageStream)");
        styleOnlyStripperSource.Should().Contain("seenStyleIndexes.Add(styleIndex.Value)");
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
            "var hasSourcePackage = SourcePackages.TryGetValue(workbook, out _);",
            StringComparison.Ordinal);
        var freshSaveReturn = savePostProcessingSource.IndexOf(
            "if (!hasSourcePackage)\n        {\n            SaveSourcePackageIndependentPostProcessingMetadata();\n            return;\n        }",
            StringComparison.Ordinal);
        var sourceReplay = savePostProcessingSource.IndexOf(
            "PreserveSourcePackageParts(workbook, packageStream);",
            StringComparison.Ordinal);

        sourcePackageCheck.Should().BeGreaterThanOrEqualTo(0);
        freshSaveReturn.Should().BeGreaterThan(sourcePackageCheck);
        sourceReplay.Should().BeGreaterThan(
            freshSaveReturn,
            "fresh saves should return before source-package replay work runs");
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
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
