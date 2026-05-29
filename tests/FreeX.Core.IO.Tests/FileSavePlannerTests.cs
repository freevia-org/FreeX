using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

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
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("  C:\\Temp\\Book.XLSX  ", [adapter], out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeSameAs(adapter);
        target.Path.Should().Be("C:\\Temp\\Book.XLSX");
    }

    [Fact]
    public void TryResolveExistingPath_UsesSharedFileFormatResolver()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", "FileSavePlanner.cs"));

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

    private sealed class FakeAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IFileAdapter
    {
        public string Extension => formats[0].Extension;
        public string FormatName => formats[0].FormatName;
        public IReadOnlyList<FileFormatDescriptor> Formats => formats;
        public Workbook Load(Stream stream) => throw new NotSupportedException();
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
