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
    public void CreateDefaultAdapters_RegistersEncodingVariantAndTemplateSaveFormats()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(adapter => adapter.Formats).ToList();

        // Excel-parity Save-As types added in the format-expansion work.
        formats.Should().Contain(format =>
            format.Extension == ".csv" && format.FormatName == "CSV UTF-8 (Comma delimited)" && format.CanSave);
        formats.Should().Contain(format =>
            format.Extension == ".txt" && format.FormatName == "Unicode Text" && format.CanSave);
        formats.Should().Contain(format =>
            format.Extension == ".xltx" && format.CanSave && format.OpensAsTemplate);

        // .xltx now resolves to a save-capable adapter (the template writer), not just open-as-template.
        var saveAdapter = FileFormatResolver.FindSaveAdapter(adapters, ".xltx", out var saveFormat);
        saveAdapter.Should().BeOfType<XltxFileAdapter>();
        saveFormat!.CanSave.Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultAdapters_RegistersDbfReadOnlyAndHtmlReadWrite()
    {
        var adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();
        var formats = adapters.SelectMany(adapter => adapter.Formats).ToList();

        // DBF: Excel-parity read-only (opens but does not save).
        formats.Should().Contain(format => format.Extension == ".dbf" && format.CanOpen && !format.CanSave);
        FileFormatResolver.FindOpenAdapter(adapters, ".dbf", out _).Should().BeOfType<DbfFileAdapter>();
        FileFormatResolver.FindSaveAdapter(adapters, ".dbf", out _).Should().BeNull();

        // HTML/HTM: read + write.
        formats.Should().Contain(format => format.Extension == ".html" && format.CanOpen && format.CanSave);
        formats.Should().Contain(format => format.Extension == ".htm" && format.CanOpen && format.CanSave);
        FileFormatResolver.FindOpenAdapter(adapters, ".html", out _).Should().BeOfType<HtmlFileAdapter>();
        FileFormatResolver.FindSaveAdapter(adapters, ".html", out _).Should().BeOfType<HtmlFileAdapter>();
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
