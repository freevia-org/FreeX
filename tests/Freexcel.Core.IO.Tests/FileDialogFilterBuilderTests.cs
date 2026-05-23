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
            "Excel Workbook (*.xlsx)|*.xlsx|" +
            "Excel Macro-Enabled Workbook (*.xlsm)|*.xlsm|" +
            "Excel Template (*.xltx)|*.xltx|" +
            "CSV (Comma-separated values) (*.csv)|*.csv|" +
            "All supported files (*.xlsx;*.xlsm;*.xltx;*.csv)|*.xlsx;*.xlsm;*.xltx;*.csv|" +
            "All files (*.*)|*.*");
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
    public void FindOpenAdapter_ResolvesAliasesCaseInsensitively()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var result = FileDialogFilterBuilder.FindOpenAdapter([adapter], ".XLSM", out var format);

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
