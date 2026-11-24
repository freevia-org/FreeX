using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Free.Shared.Shell.Avalonia.Tests.ShellHeadlessApp))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class ShellHeadlessApp : HeadlessFluentApplication
{
    public static AppBuilder BuildAvaloniaApp() => ConfigureHeadless<ShellHeadlessApp>();
}
