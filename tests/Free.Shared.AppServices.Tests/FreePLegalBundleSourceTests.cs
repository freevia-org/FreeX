namespace Free.Shared.AppServices.Tests;

public sealed class FreePLegalBundleSourceTests
{
    [Theory]
    [InlineData("freep/FreeP.App.Host/FreeP.App.Host.csproj")]
    [InlineData("freep/FreeP.App.Avalonia/FreeP.App.Avalonia.csproj")]
    public void FreeP_renderer_packages_the_complete_legal_bundle(string projectPath)
    {
        var source = TestWorkspaceFileLocator.ReadAllText(projectPath.Split('/'));

        source.Should().Contain("LICENSE.txt");
        source.Should().Contain("legal-notices.md");
        source.Should().Contain("privacy.md");
        source.Should().Contain("THIRD_PARTY_NOTICES.md");
        source.Should().Contain("THIRD_PARTY_LICENSES.md");
        source.Should().Contain("CopyToPublishDirectory=\"PreserveNewest\"");
    }

}
