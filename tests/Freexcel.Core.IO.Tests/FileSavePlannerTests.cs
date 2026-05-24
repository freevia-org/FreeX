using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class FileSavePlannerTests
{
    [Fact]
    public void TryResolveExistingPath_UsesOnlySaveCapableFormats()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.xlsm", [adapter], out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_ResolvesSaveCapableAlias()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".fxjson", "Freexcel Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.FXJSON", [adapter], out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeSameAs(adapter);
        target.Path.Should().Be("Book.FXJSON");
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
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter()],
            out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_RealExcelAdaptersResolveXlsxAndCsv()
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter() };

        FileSavePlanner.TryResolveExistingPath("Book.xlsx", adapters, out var xlsxTarget).Should().BeTrue();
        xlsxTarget.Should().NotBeNull();
        xlsxTarget!.Adapter.Should().BeOfType<XlsxFileAdapter>();

        FileSavePlanner.TryResolveExistingPath("Data.csv", adapters, out var csvTarget).Should().BeTrue();
        csvTarget.Should().NotBeNull();
        csvTarget!.Adapter.Should().BeOfType<CsvFileAdapter>();
    }

    private sealed class FakeAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IFileAdapter
    {
        public string Extension => formats[0].Extension;
        public string FormatName => formats[0].FormatName;
        public IReadOnlyList<FileFormatDescriptor> Formats => formats;
        public Workbook Load(Stream stream) => throw new NotSupportedException();
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }
}
