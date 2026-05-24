using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxFileAdapterFormatTests
{
    [Fact]
    public void LoadPath_AvoidsFullPackageToArrayCopies()
    {
        var adapterSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.cs"));
        var saveSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.Save.cs"));
        var savePostProcessingSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var diagnosticsSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxWorksheetDiagnosticsMapper.cs"));
        var sanitizerSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxClosedXmlLoadPackageSanitizer.cs"));
        var worksheetMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxWorksheetMetadataPreserver.cs"))
            .ReplaceLineEndings("\n");
        var worksheetCellMetadataSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxWorksheetMetadataPreserver.CellMetadata.cs"));
        var pivotReferencePreserverSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxPivotXmlReferencePreserver.cs"));
        var sheetXmlLayoutSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.SheetXmlLayout.cs"));

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
        pivotReferencePreserverSource.Should().Contain("HasWorksheetPivotTableRelationships(sourceArchive, context)");
        sheetXmlLayoutSource.Should().Contain("XlsxWorksheetDrawingPartReader.ReadParts");
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
}
