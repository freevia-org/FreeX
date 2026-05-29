using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;
using Microsoft.Extensions.DependencyInjection;

namespace FreeX.App.Host.Tests;

public sealed class AppFileAdapterRegistrationTests
{
    [Fact]
    public void ConfigureServices_RegistersExcelAndTextOpenAdapters()
    {
        using var provider = BuildAppServices();

        var formats = provider
            .GetServices<IFileAdapter>()
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
    }

    [Fact]
    public void ConfigureServices_XmlSpreadsheetAppearsInOpenAndSaveFilters()
    {
        using var provider = BuildAppServices();
        var adapters = provider.GetServices<IFileAdapter>().ToList();

        var openFilter = FileDialogFilterBuilder.BuildOpenFilter(adapters);
        var saveFilter = FileDialogFilterBuilder.BuildSaveFilter(adapters);

        openFilter.Should().Contain("XML Spreadsheet 2003 (*.xml)|*.xml");
        saveFilter.Should().Contain("XML Spreadsheet 2003 (*.xml)|*.xml");
    }

    [Fact]
    public void ConfigureServices_XmlSpreadsheetResolvesOpenAndSaveAdapters()
    {
        using var provider = BuildAppServices();
        var adapters = provider.GetServices<IFileAdapter>().ToList();

        var openAdapter = FileDialogFilterBuilder.FindOpenAdapter(adapters, ".xml", out var openFormat);
        var saveAdapter = FileDialogFilterBuilder.FindSaveAdapter(adapters, ".xml", out var saveFormat);

        openAdapter.Should().BeOfType<SpreadsheetXmlFileAdapter>();
        openFormat.Should().NotBeNull();
        openFormat!.Extension.Should().Be(".xml");
        openFormat.CanOpen.Should().BeTrue();

        saveAdapter.Should().BeSameAs(openAdapter);
        saveFormat.Should().NotBeNull();
        saveFormat!.Extension.Should().Be(".xml");
        saveFormat.CanSave.Should().BeTrue();
    }

    private static ServiceProvider BuildAppServices()
    {
        var services = new ServiceCollection();
        var configureServices = typeof(App).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        configureServices.Should().NotBeNull();

        configureServices!.Invoke(null, [services]);
        return services.BuildServiceProvider();
    }
}
