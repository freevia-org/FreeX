using System.Linq;
using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public class RibbonNativeRegistryTests
{
    [Fact]
    public void EveryGeneratedHandler_ResolvesToAMainWindowMethod()
    {
        var type = typeof(MainWindow);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        var missing = FreeXRibbonHandlerMap.Handlers
            .Where(kv => type.GetMethod(kv.Value, flags) is null)
            .Select(kv => $"{kv.Key} -> {kv.Value}")
            .OrderBy(x => x)
            .ToList();

        missing.Should().BeEmpty("every generated ribbon handler must bind to a real MainWindow method");
    }

    [Fact]
    public void HandlerMap_CoversCoreHomeCommands()
    {
        FreeXRibbonHandlerMap.Handlers.Keys.Should().Contain(new[]
        {
            "Paste", "Cut", "Copy", "Bold", "Italic", "Underline"
        });
    }
}
