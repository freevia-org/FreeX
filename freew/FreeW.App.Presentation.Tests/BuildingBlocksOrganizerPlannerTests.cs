using FreeW.App.Presentation.QuickParts;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class BuildingBlocksOrganizerPlannerTests
{
    [Fact]
    public void Shared_contract_formats_metadata_and_description_like_the_Wpf_authority()
    {
        var part = new QuickPart(
            "Greeting",
            ["Dear Sir or Madam,"],
            "AutoText",
            "General",
            "A formal opener");

        BuildingBlocksOrganizerPlanner.FormatListItem(part)
            .Should().Be("Greeting  (AutoText / General)");
        BuildingBlocksOrganizerPlanner.FormatPreview(part)
            .Should().Be("A formal opener\n\nDear Sir or Madam,");
        new BuildingBlockListItem(part).ToString()
            .Should().Be("Greeting  (AutoText / General)");
    }

    [Fact]
    public void Shared_contract_preserves_empty_preview_and_status_text()
    {
        BuildingBlocksOrganizerPlanner.FormatPreview(null).Should().BeEmpty();
        BuildingBlocksOrganizerPlanner.EmptyStatus.Should().Contain("No building blocks saved yet.");
        BuildingBlocksOrganizerPlanner.FormatRemovedStatus("Greeting")
            .Should().Be("Removed \"Greeting\".");
        BuildingBlocksOrganizerPlanner.Width.Should().Be(660);
        BuildingBlocksOrganizerPlanner.ListMinWidth.Should().Be(300);
        BuildingBlocksOrganizerPlanner.PreviewMinWidth.Should().Be(300);
    }
}
