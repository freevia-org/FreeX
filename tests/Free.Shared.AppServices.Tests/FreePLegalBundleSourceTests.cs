namespace Free.Shared.AppServices.Tests;

public sealed class FreePLegalBundleSourceTests
{
    [Theory]
    [InlineData("freep/FreeP.App.Host/FreeP.App.Host.csproj")]
    [InlineData("freep/FreeP.App.Avalonia/FreeP.App.Avalonia.csproj")]
    public void FreeP_renderer_packages_the_complete_legal_bundle(string projectPath)
    {
        var project = TestWorkspaceFileLocator.ReadAllText(projectPath.Split('/'));
        var bundle = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.Shell", "FamilyLegalBundle.props");

        project.Should().Contain("FamilyLegalBundle.props");
        project.Should().NotContain("Link=\"Legal\\LICENSE.txt\"");
        bundle.Should().Contain("LICENSE.txt");
        bundle.Should().Contain("legal-notices.md");
        bundle.Should().Contain("privacy.md");
        bundle.Should().Contain("THIRD_PARTY_NOTICES.md");
        bundle.Should().Contain("THIRD_PARTY_LICENSES.md");
        bundle.Should().Contain("CopyToPublishDirectory=\"PreserveNewest\"");
    }
}
