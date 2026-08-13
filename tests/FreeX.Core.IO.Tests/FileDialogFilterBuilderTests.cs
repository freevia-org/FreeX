using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class FileDialogFilterBuilderTests
{
    [Fact]
    public void BuildOpenFilter_WithNoOpenFormats_ReturnsAllFilesFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: false, CanSave: false)
            ])
        };

        BuildOpenFilter(adapters)
            .Should().Be("All files (*.*)|*.*");
    }

    [Fact]
    public void BuildSaveFilter_WithNoSaveFormats_ReturnsEmptyFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ])
        };

        BuildSaveFilter(adapters)
            .Should().BeEmpty();
    }

    [Fact]
    public void SharedBuildPerFormatFilter_BuildsSimpleFormatRowsWithoutAllSupportedGroup()
    {
        var formats = new[]
        {
            new Free.Shared.IO.FileDialogFormatDescriptor("fxp", "FreeP presentations")
        };

        Free.Shared.IO.FileDialogFilterBuilder.BuildPerFormatFilter(formats)
            .Should().Be("FreeP presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        Free.Shared.IO.FileDialogFilterBuilder.BuildPerFormatFilter(formats, includeAllFiles: false)
            .Should().Be("FreeP presentations (*.fxp)|*.fxp");
    }

    [Fact]
    public void SharedGetDefaultExtension_UsesFirstFormatOrEmpty()
    {
        Free.Shared.IO.FileDialogFilterBuilder.GetDefaultExtension([
                new Free.Shared.IO.FileDialogFormatDescriptor("fxp", "FreeP presentations")
            ])
            .Should().Be(".fxp");

        Free.Shared.IO.FileDialogFilterBuilder.GetDefaultExtension([])
            .Should().Be("");
    }

    [Fact]
    public void BuildOpenFilter_IncludesAllOpenExtensionsGroupedByFormat()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xltx", "XLTX Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        var filter = BuildOpenFilter(adapters);

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
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".XLSX", "XLSX Workbook Alias", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ])
        };

        var filter = BuildOpenFilter(adapters);

        filter.Should().StartWith("All supported files (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|");
    }

    [Fact]
    public void BuildOpenPickerTypes_IncludesAllSupportedDescriptorAndFormatDescriptors()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".XLSX", "XLSX Alias", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        var descriptors = BuildOpenPickerTypes(
            adapters,
            allSupportedName: "All supported workbooks");

        descriptors.Select(descriptor => descriptor.DisplayName)
            .Should()
            .Equal(
                "All supported workbooks",
                "XLSX Workbook",
                "XLSX Alias",
                "XLSM Macro-Enabled Workbook",
                "CSV (Comma-separated values)");
        descriptors[0].Patterns.Should().Equal("*.xlsx", "*.xlsm", "*.csv");
        descriptors[1].Patterns.Should().Equal("*.xlsx");
    }

    [Fact]
    public void BuildSavePickerTypes_PromotesPreferredExtensionForNativeSaveChoice()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true)
            ])
        };

        var descriptors = BuildSavePickerTypes(
            adapters,
            preferredFirstExtension: ".fxl");

        descriptors.Select(descriptor => descriptor.DisplayName)
            .Should()
            .Equal("FreeX Workbook", "XLSX Workbook", "CSV (Comma-separated values)");
        descriptors[0].Patterns.Should().Equal("*.fxl");
        descriptors.SelectMany(descriptor => descriptor.Patterns)
            .Should()
            .NotContain("*.xlsm");
    }

    [Fact]
    public void BuildSaveFilter_IncludesOnlySaveCapableFormats()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".xls", "XLS 97-2003 Workbook", CanOpen: true, CanSave: false)
            ])
        };

        BuildSaveFilter(adapters)
            .Should().Be("XLSX Workbook (*.xlsx)|*.xlsx");
    }

    [Fact]
    public void BuildFilters_NormalizeIndividualFormatExtensions()
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor("csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        BuildOpenFilter(adapters)
            .Should().Contain("CSV (Comma-separated values) (*.csv)|*.csv");
        BuildSaveFilter(adapters)
            .Should().Be("CSV (Comma-separated values) (*.csv)|*.csv");
    }

    [Fact]
    public void BuildOpenFilter_RealAdaptersExposeExcelOpenAliases()
    {
        var filter = BuildOpenFilter(
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
        var filter = BuildSaveFilter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter()]);

        filter.Should().Be("XLSX Workbook (*.xlsx)|*.xlsx|CSV (Comma-separated values) (*.csv)|*.csv|XML Spreadsheet 2003 (*.xml)|*.xml|FreeX Workbook (*.fxl)|*.fxl");
        filter.Should().NotContain("XLSM Macro-Enabled Workbook (*.xlsm)|*.xlsm");
        filter.Should().NotContain("XLTX Template (*.xltx)|*.xltx");
        filter.Should().NotContain("XLTM Macro-Enabled Template (*.xltm)|*.xltm");
        filter.Should().NotContain("XLS 97-2003 Workbook (*.xls)|*.xls");
        filter.Should().NotContain("XLSB Binary Workbook (*.xlsb)|*.xlsb");
        filter.Should().NotContain("XLT 97-2003 Template (*.xlt)|*.xlt");
    }

    [Theory]
    [InlineData(".xlsx", 1)]
    [InlineData("csv", 2)]
    [InlineData(" .FXL ", 3)]
    [InlineData(".xlsm", 1)]
    [InlineData("", 1)]
    public void FindSaveFilterIndex_ReturnsOneBasedSaveFormatIndexOrDefault(string extension, int expected)
    {
        var adapters = new IFileAdapter[]
        {
            new TestFileAdapter([
                new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new TestFileAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true)
            ])
        };

        FindSaveFilterIndex(adapters, extension).Should().Be(expected);
    }

    [Fact]
    public void FindSaveFilterIndex_RealAdaptersSelectsFreexWorkbookFilter()
    {
        var index = FindSaveFilterIndex(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter()],
            ".fxl");

        index.Should().Be(4);
    }

    [Fact]
    public void FindOpenAdapter_ResolvesAliasesCaseInsensitively()
    {
        var adapter = new TestFileAdapter([
            new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "XLSM Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var result = FileFormatResolver.FindOpenAdapter([adapter], " XLSM ", out var format);

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

        var result = FileFormatResolver.FindOpenAdapter(adapters, extension, out var format);

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

        var result = FileFormatResolver.FindSaveAdapter(adapters, extension, out var format);

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

        var result = FileFormatResolver.FindSaveAdapter(adapters, extension, out var format);

        result.Should().BeNull();
        format.Should().BeNull();
    }

    [Fact]
    public void FindAdapters_RealAdaptersRejectOdsUntilAdapterIsImplemented()
    {
        var adapters = new IFileAdapter[] { new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter(), new SpreadsheetXmlFileAdapter(), new NativeJsonAdapter() };

        FileFormatResolver.FindOpenAdapter(adapters, ".ods", out var openFormat).Should().BeNull();
        openFormat.Should().BeNull();
        FileFormatResolver.FindSaveAdapter(adapters, ".ods", out var saveFormat).Should().BeNull();
        saveFormat.Should().BeNull();
    }

    private static string BuildOpenFilter(IEnumerable<IFileAdapter> adapters) =>
        Free.Shared.IO.FileDialogFilterBuilder.BuildOpenFilter(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)));

    private static string BuildSaveFilter(IEnumerable<IFileAdapter> adapters) =>
        Free.Shared.IO.FileDialogFilterBuilder.BuildSaveFilter(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)));

    private static IReadOnlyList<Free.Shared.IO.FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string allSupportedName) =>
        Free.Shared.IO.FileDialogFilterBuilder.BuildOpenPickerTypes(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToOpenDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)),
            allSupportedName);

    private static IReadOnlyList<Free.Shared.IO.FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<IFileAdapter> adapters,
        string? preferredFirstExtension = null) =>
        Free.Shared.IO.FileDialogFilterBuilder.BuildSavePickerTypes(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)),
            preferredFirstExtension);

    private static int FindSaveFilterIndex(IEnumerable<IFileAdapter> adapters, string extension) =>
        Free.Shared.IO.FileDialogFilterBuilder.FindSaveFilterIndex(
            Free.Shared.IO.FileFormatDialogDescriptorAdapter.ToSaveDialogDescriptors(
                adapters.SelectMany(adapter => adapter.Formats)),
            extension);

}
