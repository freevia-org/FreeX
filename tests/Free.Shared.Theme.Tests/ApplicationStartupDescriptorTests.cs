using Free.Shared.AppServices;

namespace Free.Shared.Theme.Tests;

public sealed class ApplicationStartupDescriptorTests
{
    [Fact]
    public void CreatesCanonicalIdentityAndThemeEnvironmentPolicy()
    {
        var descriptor = ApplicationStartupDescriptor<string>.Create(
            productName: "FreeP",
            environmentVariablePrefix: "FREEP",
            defaultTheme: "light",
            alternateTheme: "dark");

        descriptor.ProductIdentity.Should().Be(new AppProductIdentity(
            "FreeP",
            "FREEP_DIAGNOSTICS",
            "FreeP"));
        descriptor.Theme.EnvironmentVariableName.Should().Be("FREEP_THEME");
        descriptor.Theme.AlternateThemeValue.Should().Be("midnight");
        descriptor.Theme.DefaultTheme.Should().Be("light");
        descriptor.Theme.AlternateTheme.Should().Be("dark");
        descriptor.Theme.ResourceKeyPrefix.Should().Be("FreeP");
    }

    [Theory]
    [InlineData("src", "FreeX.App.Services", "FreeXApplicationStartupDescriptor.cs")]
    [InlineData("freew", "FreeW.App.Presentation", "Shell", "FreeWApplicationStartup.cs")]
    [InlineData("freep", "FreeP.App.Presentation", "FreePApplicationStartupDescriptor.cs")]
    public void ProductWrappers_DelegateCanonicalPolicy(params string[] pathParts)
    {
        var source = TestWorkspaceFileLocator.ReadAllText(pathParts);

        source.Should().Contain("ApplicationStartupDescriptor<Theme>.Create(");
        source.Should().NotContain("new AppProductIdentity(");
        source.Should().NotContain("new ApplicationThemeStartupPlan<Theme>(");
    }
}
