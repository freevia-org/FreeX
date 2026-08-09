using Free.Shared.Theme;

using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ApplicationStartupDescriptorTests
{
    [Fact]
    public void FreeP_descriptor_owns_identity_and_theme_policy()
    {
        var identity = FreePApplicationStartupDescriptor.ProductIdentity;
        var theme = FreePApplicationStartupDescriptor.Theme;

        identity.ProductName.Should().Be("FreeP");
        identity.DiagnosticsEnvironmentVariable.Should().Be("FREEP_DIAGNOSTICS");
        identity.ProductDirectoryName.Should().Be("FreeP");
        theme.EnvironmentVariableName.Should().Be("FREEP_THEME");
        theme.AlternateThemeValue.Should().Be("midnight");
        theme.DefaultTheme.Should().BeSameAs(BrandThemes.FreeP);
        theme.AlternateTheme.Should().BeSameAs(BrandThemes.FreeXMidnight);
        theme.ResourceKeyPrefix.Should().Be("FreeP");
    }

    [Fact]
    public void FreeP_hosts_consume_the_shared_descriptor_without_local_identity_or_theme_policy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfProgram = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "Program.cs"));
        var avaloniaProgram = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "Program.cs"));
        var avaloniaApp = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "App.cs"));

        wpfProgram.Should().Contain("FreePApplicationStartupDescriptor.ProductIdentity");
        avaloniaProgram.Should().Contain("FreePApplicationStartupDescriptor.ProductIdentity");
        wpfProgram.Should().Contain("Plan: FreePApplicationStartupDescriptor.Theme");
        avaloniaApp.Should().Contain("FreePApplicationStartupDescriptor.Theme.Apply(");

        foreach (var source in new[] { wpfProgram, avaloniaProgram, avaloniaApp })
        {
            source.Should().NotContain("new AppProductIdentity(\"FreeP\"");
            source.Should().NotContain("GetEnvironmentVariable(\"FREEP_THEME\"");
        }
    }
}
