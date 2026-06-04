using FluentAssertions;
using FreeX.Core.IO;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookDropPlannerTests
{
    [Fact]
    public void SelectOpenableFile_ReturnsFirstSupportedWorkbookPath()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook"),
            new TestFileAdapter(extension: ".csv", formatName: "CSV")
        };

        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [
                @"C:\Temp\notes.pdf",
                @"C:\Temp\Book.xlsx",
                @"C:\Temp\Other.csv"
            ],
            adapters);

        selected.Should().Be(@"C:\Temp\Book.xlsx");
    }

    [Fact]
    public void SelectOpenableFile_ReturnsNullWhenNoDroppedPathCanOpen()
    {
        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [@"C:\Temp\README", @"C:\Temp\notes.pdf"],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().BeNull();
    }

    [Fact]
    public void SelectOpenableFile_SkipsPathsWithoutExtensions()
    {
        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [
                @"C:\Temp\README",
                @"C:\Temp\Book.xlsx"
            ],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().Be(@"C:\Temp\Book.xlsx");
    }

    [Fact]
    public void SelectOpenableFile_SkipsMalformedPathCandidates()
    {
        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [
                "bad\0path.xlsx",
                @"C:\Temp\Book.xlsx"
            ],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().Be(@"C:\Temp\Book.xlsx");
    }

    [Fact]
    public void SelectOpenableFile_UsesAdapterFormatAliases()
    {
        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [
                @"C:\Temp\notes.pdf",
                @"C:\Temp\Template.XLT",
                @"C:\Temp\Book.xls"
            ],
            [new LegacyXlsFileAdapter()]);

        selected.Should().Be(@"C:\Temp\Template.XLT");
    }

    [Fact]
    public void SelectOpenableFile_UsesRealExcelAdapterAliasesInDropOrder()
    {
        var selected = WorkbookDropPlanner.SelectOpenableFile(
            [
                @"C:\Temp\notes.pdf",
                @"C:\Temp\MacroBook.XLSM",
                @"C:\Temp\BinaryBook.xlsb"
            ],
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter()]);

        selected.Should().Be(@"C:\Temp\MacroBook.XLSM");
    }

    [Fact]
    public void SelectOpenableFile_SkipsSupportedExtensionDirectories()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var selected = WorkbookDropPlanner.SelectOpenableFile(
                [
                    tempDirectory,
                    @"C:\Temp\Book.xlsb"
                ],
                [new XlsxFileAdapter(), new LegacyXlsFileAdapter()]);

            selected.Should().Be(@"C:\Temp\Book.xlsb");
        }
        finally
        {
            Directory.Delete(tempDirectory);
        }
    }

}
