using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class FileDialogFilterBuilderTests
{
    [Fact]
    public void BuildOpenFilter_IncludesAllOpenExtensionsGroupedByFormat()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xltx", "Excel Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".csv", "CSV (Comma-separated values)", CanOpen: true, CanSave: true)
            ])
        };

        var filter = FileDialogFilterBuilder.BuildOpenFilter(adapters);

        filter.Should().Be(
            "All supported files (*.xlsx;*.xlsm;*.xltx;*.csv)|*.xlsx;*.xlsm;*.xltx;*.csv|" +
            "Excel Workbook (*.xlsx)|*.xlsx|" +
            "Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm|" +
            "Excel Template (*.xltx)|*.xltx|" +
            "CSV (Comma-separated values) (*.csv)|*.csv|" +
            "All files (*.*)|*.*");
    }

    [Fact]
    public void BuildOpenFilter_DeduplicatesExtensionsInAllSupportedFilter()
    {
        var adapters = new IFileAdapter[]
        {
            new FakeAdapter([
                new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".XLSX", "Excel Workbook Alias", CanOpen: true, CanSave: false),
                new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
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
                new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
                new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
            ]),
            new FakeAdapter([
                new FileFormatDescriptor(".xls", "Excel 97-2003 Workbook", CanOpen: true, CanSave: false)
            ])
        };

        FileDialogFilterBuilder.BuildSaveFilter(adapters)
            .Should().Be("Excel Workbook (*.xlsx)|*.xlsx");
    }

    [Fact]
    public void BuildOpenFilter_RealAdaptersExposeExcelOpenAliases()
    {
        var filter = FileDialogFilterBuilder.BuildOpenFilter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter()]);

        filter.Should().Contain("*.xlsx;*.xlsm;*.xltx;*.xltm;*.xls;*.xlsb;*.xlt;*.csv");
        filter.Should().Contain("Excel Binary Workbook (*.xlsb)|*.xlsb");
        filter.Should().Contain("Excel 97-2003 Template (*.xlt)|*.xlt");
        filter.Should().Contain("Excel Macro-Enabled Template (*.xltm)|*.xltm");
    }

    [Fact]
    public void BuildSaveFilter_RealAdaptersExcludeOpenOnlyExcelFormats()
    {
        var filter = FileDialogFilterBuilder.BuildSaveFilter(
            [new XlsxFileAdapter(), new LegacyXlsFileAdapter(), new CsvFileAdapter()]);

        filter.Should().Be("Excel Workbook (*.xlsx)|*.xlsx|CSV (Comma-separated values) (*.csv)|*.csv");
        filter.Should().NotContain("Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm");
        filter.Should().NotContain("Excel Template (*.xltx)|*.xltx");
        filter.Should().NotContain("Excel Macro-Enabled Template (*.xltm)|*.xltm");
        filter.Should().NotContain("Excel 97-2003 Workbook (*.xls)|*.xls");
        filter.Should().NotContain("Excel Binary Workbook (*.xlsb)|*.xlsb");
        filter.Should().NotContain("Excel 97-2003 Template (*.xlt)|*.xlt");
    }

    [Fact]
    public void FindOpenAdapter_ResolvesAliasesCaseInsensitively()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var result = FileDialogFilterBuilder.FindOpenAdapter([adapter], " XLSM ", out var format);

        result.Should().BeSameAs(adapter);
        format.Should().NotBeNull();
        format!.Extension.Should().Be(".xlsm");
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
