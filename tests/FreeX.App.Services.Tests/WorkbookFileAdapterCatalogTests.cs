using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileAdapterCatalogTests
{
    [Fact]
    public void CreateDefaultAdapters_IncludesCurrentOpenAndSaveFormats()
    {
        var formats = WorkbookFileAdapterCatalog
            .CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .ToList();

        formats.Should().Contain(format => format.Extension == ".xlsx" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".xlsm" && format.CanOpen && !format.CanSave);
        formats.Should().Contain(format => format.Extension == ".xltx" && format.CanOpen && !format.CanSave && format.OpensAsTemplate);
        formats.Should().Contain(format => format.Extension == ".xltm" && format.CanOpen && !format.CanSave && format.OpensAsTemplate);
        formats.Should().Contain(format => format.Extension == ".xls" && format.CanOpen && !format.CanSave);
        formats.Should().Contain(format => format.Extension == ".xlsb" && format.CanOpen && !format.CanSave);
        formats.Should().Contain(format => format.Extension == ".xlt" && format.CanOpen && !format.CanSave && format.OpensAsTemplate);
        formats.Should().Contain(format => format.Extension == ".csv" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".txt" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".tsv" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".tab" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".xml" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".fxl" && format.CanOpen && format.CanSave);
    }

    [Fact]
    public void CreateDefaultAdapters_ResolvesNativeWorkbookSaveAdapter()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

        var adapter = FileFormatResolver.FindSaveAdapter(adapters, ".fxl", out var format);

        adapter.Should().BeOfType<NativeJsonAdapter>();
        format.Should().NotBeNull();
        format!.CanSave.Should().BeTrue();
    }
}
