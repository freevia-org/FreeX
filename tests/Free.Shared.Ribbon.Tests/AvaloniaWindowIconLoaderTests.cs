using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaWindowIconLoaderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task TryApply_loads_the_packaged_desktop_icon()
    {
        await Session.Dispatch(() =>
        {
            var window = new Window();
            try
            {
                AvaloniaWindowIconLoader.TryApply(window, "FreeX.ico").Should().BeTrue();
                window.Icon.Should().NotBeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TryApply_ignores_a_missing_icon()
    {
        await Session.Dispatch(() =>
        {
            var window = new Window();
            try
            {
                AvaloniaWindowIconLoader.TryApply(window, "missing.ico").Should().BeFalse();
                window.Icon.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Avalonia_hosts_delegate_resource_loading_to_the_shared_owner()
    {
        var root = FindRepositoryRoot();
        var hosts = new[]
        {
            Read(root, "src", "FreeX.App.Avalonia", "MainWindow.DesktopChrome.cs"),
            Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in hosts)
        {
            source.Should().Contain("AvaloniaWindowIconLoader.TryApply(this, App.ActiveTheme)")
                .And.NotContain("new WindowIcon(")
                .And.NotContain("File.OpenRead(iconPath)");
        }
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
