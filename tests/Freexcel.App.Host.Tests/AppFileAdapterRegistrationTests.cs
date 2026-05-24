using System.Reflection;
using FluentAssertions;
using Freexcel.Core.IO;
using Microsoft.Extensions.DependencyInjection;

namespace Freexcel.App.Host.Tests;

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
        formats.Should().Contain(format => format.Extension == ".txt" && format.CanOpen && !format.CanSave);
        formats.Should().Contain(format => format.Extension == ".tsv" && format.CanOpen && !format.CanSave);
        formats.Should().Contain(format => format.Extension == ".tab" && format.CanOpen && !format.CanSave);
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
