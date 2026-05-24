using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookDropPlannerTests
{
    [Fact]
    public void SelectOpenableFile_ReturnsFirstSupportedWorkbookPath()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter(".xlsx", "Excel Workbook"),
            new FakeAdapter(".csv", "CSV")
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
            [@"C:\Temp\notes.pdf"],
            [new FakeAdapter(".xlsx", "Excel Workbook")]);

        selected.Should().BeNull();
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

    private sealed class FakeAdapter(string extension, string formatName) : IFileAdapter
    {
        public string Extension => extension;
        public string FormatName => formatName;
        public Workbook Load(Stream stream) => throw new NotSupportedException();
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }
}
