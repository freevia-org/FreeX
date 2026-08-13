using System.IO;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class ApplicationLocalizationSeamInstallerTests : IDisposable
{
    private readonly IShellStrings _originalShellStrings = ShellStrings.Current;
    private readonly IBackstageStrings _originalBackstageStrings = BackstageStrings.Current;

    public void Dispose()
    {
        ShellStrings.Current = _originalShellStrings;
        BackstageStrings.Current = _originalBackstageStrings;
    }

    [Fact]
    public void Install_RoutesShellAndBackstageTextThroughProvidedResources()
    {
        ApplicationLocalizationSeamInstaller.Install(
            key => "get:" + key,
            (key, arguments) => $"format:{key}:{arguments.Length}",
            text => "automation:" + text);

        ShellStrings.Current.Ok.Should().Be("get:Common_Ok");
        ShellStrings.Current.CreateAutomationName("_Open").Should().Be("automation:_Open");
        BackstageStrings.Current.Get("Greeting").Should().Be("get:Greeting");
        BackstageStrings.Current.Format("Recent", "Roadmap.xlsx").Should().Be("format:Recent:1");
    }

    [Fact]
    public void PlatformBootstraps_DelegateResourceAdapterOwnershipToPortableShell()
    {
        var avalonia = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Shell.Avalonia", "AvaloniaAppLocalizationBootstrap.cs"));
        var wpf = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "shared", "Free.Shared.Shell.Wpf", "WpfAppLocalizationBootstrap.cs"));

        foreach (var source in new[] { avalonia, wpf })
        {
            source.Should().Contain("ApplicationLocalizationSeamInstaller.Install(");
            source.Should().NotContain("new ResourceShellStrings(");
            source.Should().NotContain("new ResourceBackstageStrings(");
        }
    }
}
