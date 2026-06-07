using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

internal static class WorkbookWindowRegistryTestSupport
{
    public static (WorkbookWindowRegistry Registry, TestWorkbookWindow[] Windows) RegisterWindows(int count)
    {
        var registry = new WorkbookWindowRegistry();
        var windows = new TestWorkbookWindow[count];
        for (var i = 0; i < count; i++)
        {
            windows[i] = new TestWorkbookWindow();
            registry.Register(windows[i]);
        }

        return (registry, windows);
    }
}
