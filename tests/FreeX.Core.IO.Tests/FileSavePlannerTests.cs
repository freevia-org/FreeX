using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class FileSavePlannerTests
{
    [Fact]
    public void CanSkipCleanSave_ReturnsTrueForCleanWorkbookAndSameTargetPath()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Book.xlsx");

        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: false,
            currentFilePath: $"  {path}  ",
            targetPath: path);

        skip.Should().BeTrue();
    }

    [Fact]
    public void CanSkipCleanSave_ReturnsFalseForDirtyWorkbook()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "Book.xlsx");

        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: true,
            currentFilePath: path,
            targetPath: path);

        skip.Should().BeFalse();
    }

    [Fact]
    public void CanSkipCleanSave_ReturnsFalseForSaveAsTarget()
    {
        using var temp = new TestTemporaryDirectory();

        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: false,
            currentFilePath: Path.Combine(temp.Path, "Book.xlsx"),
            targetPath: Path.Combine(temp.Path, "Copy.xlsx"));

        skip.Should().BeFalse();
    }

    [Fact]
    public void CanSkipCleanSave_ReturnsFalseForMalformedPaths()
    {
        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: false,
            currentFilePath: "bad\0Book.xlsx",
            targetPath: "bad\0Book.xlsx");

        skip.Should().BeFalse();
    }

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, true)]
    [InlineData(StringComparison.Ordinal, false)]
    public void CanSkipCleanSave_UsesPathComparisonForCaseOnlyTargetDifferences(
        StringComparison pathComparison,
        bool expected)
    {
        using var temp = new TestTemporaryDirectory();

        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: false,
            currentFilePath: Path.Combine(temp.Path, "Book.xlsx"),
            targetPath: Path.Combine(temp.Path, "book.xlsx"),
            pathComparison);

        skip.Should().Be(expected);
    }

    [Theory]
    [InlineData("bad\0Book.xlsx", "Book.xlsx", StringComparison.Ordinal)]
    [InlineData("Book.xlsx", "bad\0Book.xlsx", StringComparison.OrdinalIgnoreCase)]
    public void CanSkipCleanSave_PathComparisonOverloadReturnsFalseForMalformedPaths(
        string currentFilePath,
        string targetPath,
        StringComparison pathComparison)
    {
        var skip = FileSavePlanner.CanSkipCleanSave(
            workbookDirty: false,
            currentFilePath,
            targetPath,
            pathComparison);

        skip.Should().BeFalse();
    }

    [Fact]
    public void TryResolveExistingPath_UsesOnlySaveCapableFormats()
    {
        var adapter = new TestFileAdapter([
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.xlsm", [adapter], out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_ResolvesSaveCapableAlias()
    {
        var adapter = new TestFileAdapter([
            new FileFormatDescriptor(".fxjson", "FreeX Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.FXJSON", [adapter], out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeSameAs(adapter);
        target.Path.Should().Be("Book.FXJSON");
    }

    [Fact]
    public void TryResolveExistingPath_TrimsCurrentPathBeforeReturningTarget()
    {
        var adapter = new TestFileAdapter([
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("  C:\\Temp\\Book.XLSX  ", [adapter], out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeSameAs(adapter);
        target.Path.Should().Be("C:\\Temp\\Book.XLSX");
    }

    [Fact]
    public void TryResolveExistingPath_ReturnsFalseForMalformedCurrentPath()
    {
        var adapter = new TestFileAdapter([
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("bad\0Book.xlsx", [adapter], out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_UsesSharedFileFormatResolver()
    {
        var source = TestWorkspaceFiles.ReadCoreIoSource("FileSavePlanner.cs");

        source.Should().Contain("FileFormatResolver.FindSaveAdapter(adapters, extension, out _)");
    }

    [Theory]
    [InlineData("Book.xlsm")]
    [InlineData("Template.xltx")]
    [InlineData("Template.xltm")]
    [InlineData("Legacy.xls")]
    [InlineData("Binary.xlsb")]
    [InlineData("LegacyTemplate.xlt")]
    public void TryResolveExistingPath_RealExcelAdaptersRejectOpenOnlyFormats(string currentFilePath)
    {
        var resolved = FileSavePlanner.TryResolveExistingPath(
            currentFilePath,
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter()],
            out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_RealExcelAdaptersResolveXlsxCsvAndXml()
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter() };

        FileSavePlanner.TryResolveExistingPath("Book.xlsx", adapters, out var xlsxTarget).Should().BeTrue();
        xlsxTarget.Should().NotBeNull();
        xlsxTarget!.Adapter.Should().BeOfType<XlsxFileAdapter>();

        FileSavePlanner.TryResolveExistingPath("Data.csv", adapters, out var csvTarget).Should().BeTrue();
        csvTarget.Should().NotBeNull();
        csvTarget!.Adapter.Should().BeOfType<CsvFileAdapter>();

        FileSavePlanner.TryResolveExistingPath("Data.xml", adapters, out var xmlTarget).Should().BeTrue();
        xmlTarget.Should().NotBeNull();
        xmlTarget!.Adapter.Should().BeOfType<SpreadsheetXmlFileAdapter>();
    }
}
