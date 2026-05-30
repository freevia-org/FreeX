using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class FileDialogFilterBuilderTests
{
    [Fact]
    public void BuildOpenFilter_WithNoOpenFormats_ReturnsAllFilesFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: false, CanSave: false)
            ])
        };

        FileDialogFilterBuilder.BuildOpenFilter(adapters)
            .Should().Be("All files (*.*)|*.*");
    }

    [Fact]
    public void BuildSaveFilter_WithNoSaveFormats_ReturnsEmptyFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ])
        };

        FileDialogFilterBuilder.BuildSaveFilter(adapters)
            .Should().BeEmpty();
    }

    [Fact]
    public void BuildOpenFilter_IncludesAllOpenExtensionsGroupedByFormat()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xltx", "XLTX Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        var filter = FileDialogFilterBuilder.BuildOpenFilter(adapters);

        filter.Should().Be(
            "All supported files (*.xlsx;*.xlsm;*.xltx;*.csv)|*.xlsx;*.xlsm;*.xltx;*.csv|" +
            "XLSX Workbook (*.xlsx)|*.xlsx|" +
            "XLSM Macro-Enabled Workbook (*.xlsm)|*.xlsm|" +
            "XLTX Template (*.xltx)|*.xltx|" +
            "CSV (Comma-separated values) (*.csv)|*.csv|" +
            "All files (*.*)|*.*");
    }

    [Fact]
    public void BuildOpenFilter_DeduplicatesExtensionsInAllSupportedFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".XLSX", "XLSX Workbook Alias", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ])
        };

        var filter = FileDialogFilterBuilder.BuildOpenFilter(adapters);

        filter.Should().StartWith("All supported files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|");
    }

    [Fact]
    public void BuildSaveFilter_IncludesOnlySaveCapableFormats()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".xls", "XLS 97-2003 Workbook", CanOpen: true, CanSave: false)
            ])
        };

        FileDialogFilterBuilder.BuildSaveFilter(adapters)
            .Should().Be("XLSX Workbook (*.xlsx)|*.xlsx");
    }

    [Fact]
    public void BuildFilters_NormalizeIndividualFormatExtensions()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor("csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        FileDialogFilterBuilder.BuildOpenFilter(adapters)
            .Should().Contain("CSV (Comma-separated values) (*.csv)|*.csv");
        FileDialogFilterBuilder.BuildSaveFilter(adapters)
            .Should().Be("CSV (Comma-separated values) (*.csv)|*.csv");
    }

    [Fact]
    public void BuildOpenFilter_RealAdaptersExposeExcelOpenAliases()
    {
        var filter = FileDialogFilterBuilder.BuildOpenFilter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter()]);

        filter.Should().Contain("*.xlsx;*.xlsm;*.xltx;*.xltm;*.xls;*.xlsb;*.xlt;*.csv;*.xml;*.fxl");
        filter.Should().Contain("XLSB Binary Workbook (*.xlsb)|*.xlsb");
        filter.Should().Contain("XLT 97-2003 Template (*.xlt)|*.xlt");
        filter.Should().Contain("XLTM Macro-Enabled Template (*.xltm)|*.xltm");
        filter.Should().Contain("XML Spreadsheet 2003 (*.xml)|*.xml");
        filter.Should().Contain("FreeX Workbook (*.fxl)|*.fxl");
    }

    [Fact]
    public void BuildSaveFilter_RealAdaptersExcludeOpenOnlyExcelFormats()
    {
        var filter = FileDialogFilterBuilder.BuildSaveFilter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter()]);

        filter.Should().Be("XLSX Workbook (*.xlsx)|*.xlsx|CSV (Comma-separated values) (*.csv)|*.csv|XML Spreadsheet 2003 (*.xml)|*.xml|FreeX Workbook (*.fxl)|*.fxl");
        filter.Should().NotContain("XLSM Macro-Enabled Workbook (*.xlsm)|*.xlsm");
        filter.Should().NotContain("XLTX Template (*.xltx)|*.xltx");
        filter.Should().NotContain("XLTM Macro-Enabled Template (*.xltm)|*.xltm");
        filter.Should().NotContain("XLS 97-2003 Workbook (*.xls)|*.xls");
        filter.Should().NotContain("XLSB Binary Workbook (*.xlsb)|*.xlsb");
        filter.Should().NotContain("XLT 97-2003 Template (*.xlt)|*.xlt");
    }

    [Fact]
    public void FindOpenAdapter_ResolvesAliasesCaseInsensitively()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var result = FileDialogFilterBuilder.FindOpenAdapter([adapter], " XLSM ", out var format);

        result.Should().BeSameAs(adapter);
        format.Should().NotBeNull();
        format!.Extension.Should().Be(".xlsm");
    }

    [Theory]
    [InlineData("xlsx", ".xlsx")]
    [InlineData(" .CSV ", ".CSV")]
    [InlineData("*.XLSX", ".XLSX")]
    [InlineData(" *.csv ", ".csv")]
    [InlineData(".fxl", ".fxl")]
    [InlineData("   ", "")]
    public void FileFormatResolver_NormalizesExtensionsForFilterAndAdapterMatching(string extension, string expected)
    {
        FileFormatResolver.NormalizeExtension(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".XLSX", "xlsx")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("*.*", "unknown")]
    [InlineData(".tar.gz", "unknown")]
    public void FileFormatResolver_CreatesSafeFileTypeTokens(string extension, string expected)
    {
        FileFormatResolver.SafeFileTypeFromExtension(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData("XLSX", typeof(XlsxFileAdapter), ".xlsx", false)]
    [InlineData(".xlsm", typeof(XlsxFileAdapter), ".xlsm", false)]
    [InlineData("XLTX", typeof(XlsxFileAdapter), ".xltx", true)]
    [InlineData(".xltm", typeof(XlsxFileAdapter), ".xltm", true)]
    [InlineData("XLS", typeof(LegacyXlsFileAdapter), ".xls", false)]
    [InlineData(".xlsb", typeof(LegacyXlsFileAdapter), ".xlsb", false)]
    [InlineData("XLT", typeof(LegacyXlsFileAdapter), ".xlt", true)]
    [InlineData(".csv", typeof(CsvFileAdapter), ".csv", false)]
    [InlineData(".xml", typeof(SpreadsheetXmlFileAdapter), ".xml", false)]
    [InlineData(".fxl", typeof(NativeJsonAdapter), ".fxl", false)]
    public void FindOpenAdapter_RealAdaptersResolveSupportedFormats(
        string extension,
        Type expectedAdapterType,
        string expectedExtension,
        bool opensAsTemplate)
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter() };

        var result = FileDialogFilterBuilder.FindOpenAdapter(adapters, extension, out var format);

        result.Should().BeOfType(expectedAdapterType);
        format.Should().NotBeNull();
        format!.Extension.Should().Be(expectedExtension);
        format.CanOpen.Should().BeTrue();
        format.OpensAsTemplate.Should().Be(opensAsTemplate);
    }

    [Theory]
    [InlineData("xlsx", typeof(XlsxFileAdapter), ".xlsx")]
    [InlineData("*.CSV", typeof(CsvFileAdapter), ".csv")]
    [InlineData(".xml", typeof(SpreadsheetXmlFileAdapter), ".xml")]
    [InlineData(".fxl", typeof(NativeJsonAdapter), ".fxl")]
    public void FindSaveAdapter_RealAdaptersResolveOnlySaveCapableFormats(
        string extension,
        Type expectedAdapterType,
        string expectedExtension)
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter() };

        var result = FileDialogFilterBuilder.FindSaveAdapter(adapters, extension, out var format);

        result.Should().BeOfType(expectedAdapterType);
        format.Should().NotBeNull();
        format!.Extension.Should().Be(expectedExtension);
        format.CanSave.Should().BeTrue();
    }

    [Theory]
    [InlineData(".xlsm")]
    [InlineData(".xltx")]
    [InlineData(".xltm")]
    [InlineData(".xls")]
    [InlineData(".xlsb")]
    [InlineData(".xlt")]
    public void FindSaveAdapter_RealAdaptersRejectOpenOnlyExcelFormats(string extension)
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new NativeJsonAdapter() };

        var result = FileDialogFilterBuilder.FindSaveAdapter(adapters, extension, out var format);

        result.Should().BeNull();
        format.Should().BeNull();
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
