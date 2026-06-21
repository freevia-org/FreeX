using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookOpenIngressPlannerTests
{
    [Fact]
    public void SelectOpenableFile_ReturnsFirstSupportedWorkbookPath()
    {
        var notesPath = TempPath("notes.pdf");
        var bookPath = TempPath("Book.xlsx");
        var otherPath = TempPath("Other.csv");
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook"),
            new TestFileAdapter(extension: ".csv", formatName: "CSV")
        };

        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                notesPath,
                bookPath,
                otherPath
            ],
            adapters);

        selected.Should().Be(bookPath);
    }

    [Fact]
    public void SelectOpenableFile_ReturnsNullWhenNoDroppedPathCanOpen()
    {
        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [TempPath("README"), TempPath("notes.pdf")],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().BeNull();
    }

    [Fact]
    public void SelectOpenableFile_SkipsPathsWithoutExtensions()
    {
        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                TempPath("README"),
                TempPath("Book.xlsx")
            ],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().Be(TempPath("Book.xlsx"));
    }

    [Fact]
    public void SelectOpenableFile_SkipsMalformedPathCandidates()
    {
        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                "bad\0path.xlsx",
                TempPath("Book.xlsx")
            ],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        selected.Should().Be(TempPath("Book.xlsx"));
    }

    [Fact]
    public void SelectOpenableFile_UsesAdapterFormatAliases()
    {
        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                TempPath("notes.pdf"),
                TempPath("Template.XLT"),
                TempPath("Book.xls")
            ],
            [new LegacyXlsFileAdapter()]);

        selected.Should().Be(TempPath("Template.XLT"));
    }

    [Fact]
    public void SelectOpenableFile_UsesRealExcelAdapterAliasesInDropOrder()
    {
        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                TempPath("notes.pdf"),
                TempPath("MacroBook.XLSM"),
                TempPath("BinaryBook.xlsb")
            ],
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter()]);

        selected.Should().Be(TempPath("MacroBook.XLSM"));
    }

    [Fact]
    public void SelectOpenableFile_SkipsSupportedExtensionDirectories()
    {
        using var temp = new TestTemporaryDirectory();
        var tempDirectory = Path.Combine(temp.Path, "dropped.xlsx");
        Directory.CreateDirectory(tempDirectory);

        var selected = WorkbookOpenIngressPlanner.SelectOpenableFile(
            [
                tempDirectory,
                TempPath("Book.xlsb")
            ],
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter()]);

        selected.Should().Be(TempPath("Book.xlsb"));
    }

    [Fact]
    public void SelectOpenableExistingLocalFile_RequiresExistingFileAndReturnsCandidateIndex()
    {
        using var temp = new TestTemporaryDirectory();
        var directory = Path.Combine(temp.Path, "Folder.xlsx");
        var missing = Path.Combine(temp.Path, "Missing.xlsx");
        var unsupported = Path.Combine(temp.Path, "Notes.pdf");
        var supported = Path.Combine(temp.Path, "Book.xlsx");
        Directory.CreateDirectory(directory);
        File.WriteAllText(unsupported, "pdf");
        File.WriteAllText(supported, "xlsx");

        var plan = WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
            [directory, missing, unsupported, supported],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        plan.Success.Should().BeTrue();
        plan.CandidateIndex.Should().Be(3);
        plan.Path.Should().Be(supported);
        plan.Message.Should().BeEmpty();
    }

    [Fact]
    public void SelectOpenableExistingLocalFile_ReturnsUnsupportedMessageForExistingUnsupportedFile()
    {
        using var temp = new TestTemporaryDirectory();
        var unsupported = Path.Combine(temp.Path, "Notes.pdf");
        File.WriteAllText(unsupported, "pdf");

        var plan = WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
            [unsupported],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        plan.Success.Should().BeFalse();
        plan.Message.Should().Be("Unsupported file type: .pdf.");
    }

    [Fact]
    public void SelectOpenableExistingLocalFile_ReturnsLocalPathMessageWhenNoCandidateHasLocalPath()
    {
        var plan = WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
            ["", "https://example.test/Book.xlsx", "bad\0path.xlsx"],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        plan.Success.Should().BeFalse();
        plan.Message.Should().Be(WorkbookOpenIngressPlanner.LocalPathRequiredMessage);
    }

    [Fact]
    public void SelectOpenableExistingLocalFile_ReturnsDropMessageWhenLocalPathsAreNotFiles()
    {
        using var temp = new TestTemporaryDirectory();
        var directory = Path.Combine(temp.Path, "Folder.xlsx");
        var missing = Path.Combine(temp.Path, "Missing.xlsx");
        Directory.CreateDirectory(directory);

        var plan = WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
            [directory, missing],
            [new TestFileAdapter(extension: ".xlsx", formatName: "XLSX Workbook")]);

        plan.Success.Should().BeFalse();
        plan.Message.Should().Be(WorkbookOpenIngressPlanner.UnsupportedWorkbookFileMessage);
    }

    [Fact]
    public void SelectOpenableExistingLocalFile_CanUseSessionResolverCallback()
    {
        using var temp = new TestTemporaryDirectory();
        var supported = Path.Combine(temp.Path, "Book.xlsx");
        File.WriteAllText(supported, "xlsx");

        var plan = WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
            [supported],
            path => WorkbookOpenIngressResolution.Resolved(path.ToUpperInvariant()));

        plan.Success.Should().BeTrue();
        plan.Path.Should().Be(supported.ToUpperInvariant());
    }

    private static string TempPath(string fileName) =>
        Path.Combine(Path.GetTempPath(), fileName);
}
