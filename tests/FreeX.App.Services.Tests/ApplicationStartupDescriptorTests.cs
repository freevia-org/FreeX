using FluentAssertions;
using Free.Shared.Theme;

namespace FreeX.App.Services.Tests;

public sealed class ApplicationStartupDescriptorTests
{
    [Fact]
    public void FreeX_descriptor_owns_identity_and_theme_policy()
    {
        var identity = FreeXApplicationStartupDescriptor.ProductIdentity;
        var theme = FreeXApplicationStartupDescriptor.Theme;

        identity.ProductName.Should().Be("FreeX");
        identity.DiagnosticsEnvironmentVariable.Should().Be("FREEX_DIAGNOSTICS");
        identity.ProductDirectoryName.Should().Be("FreeX");
        theme.EnvironmentVariableName.Should().Be("FREEX_THEME");
        theme.AlternateThemeValue.Should().Be("midnight");
        theme.DefaultTheme.Should().BeSameAs(BrandThemes.FreeX);
        theme.AlternateTheme.Should().BeSameAs(BrandThemes.FreeXMidnight);
        theme.ResourceKeyPrefix.Should().Be("FreeX");
    }

    [Fact]
    public void FreeX_hosts_consume_the_shared_descriptor_without_local_identity_or_theme_policy()
    {
        var programSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "Program.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        programSource.Should().Contain("FreeXApplicationStartupDescriptor.ProductIdentity");
        avaloniaSource.Should().Contain("FreeXApplicationStartupDescriptor.ProductIdentity");
        foreach (var source in new[] { programSource, wpfSource, avaloniaSource })
        {
            source.Should().NotContain("new AppProductIdentity(\"FreeX\"");
            source.Should().NotContain("GetEnvironmentVariable(\"FREEX_THEME\"");
        }

        wpfSource.Should().Contain("FreeXApplicationStartupDescriptor.Theme.Apply(");
        avaloniaSource.Should().Contain("FreeXApplicationStartupDescriptor.Theme.Apply(");
        wpfSource.Should().Contain("WpfThemeApplier.Apply(this, theme, resourceKeyPrefix)");
        avaloniaSource.Should().Contain("AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)");
    }
}
