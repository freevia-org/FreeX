using System.Xml.Linq;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidenceBuildContractTests
{
    [Fact]
    public void Fidelity_renderer_friend_access_is_available_to_normal_release_builds()
    {
        var project = XDocument.Parse(TestWorkspaceFileLocator.ReadAllText(
            "freew",
            "FreeW.App.Host",
            "FreeW.App.Host.csproj"));

        var friend = project
            .Descendants("InternalsVisibleTo")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                "FreeW.FidelityRender",
                StringComparison.Ordinal));

        friend.Parent.Should().NotBeNull();
        friend.Parent!.Attribute("Condition").Should().BeNull(
            "the normal FidelityRender Release build consumes internal pagination and render primitives");
    }
}
