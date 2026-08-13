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

    [Fact]
    public void Session_owns_sorted_items_selection_preview_and_insert_acceptance()
    {
        var library = QuickPartLibrary.LoadFromPath(null);
        library.Save(new QuickPart("Signature", ["Regards,"], "AutoText", "General", null));
        library.Save(new QuickPart("Greeting", ["Hello"], "Quick Parts", "General", "Opening"));

        var session = BuildingBlocksOrganizerPlanner.CreateSession(library);

        session.Current.Items.Select(item => item.Part.Name).Should().Equal("Greeting", "Signature");
        session.Current.SelectedIndex.Should().Be(0);
        session.Current.PreviewText.Should().Be("Opening\n\nHello");
        session.Current.CanInsert.Should().BeTrue();
        session.Current.CanDelete.Should().BeTrue();

        session.SelectIndex(1);
        session.AcceptSelection().Should().Be(new BuildingBlocksOrganizerAction(
            BuildingBlocksOrganizerActionKind.Insert,
            "Signature",
            "Regards,"));
    }

    [Fact]
    public void Session_filters_metadata_and_content_without_renderer_policy()
    {
        var library = QuickPartLibrary.LoadFromPath(null);
        library.Save(new QuickPart("Greeting", ["Hello"], "AutoText", "Openers", "Formal"));
        library.Save(new QuickPart("Signature", ["Regards"], "Quick Parts", "Closers", null));
        var session = BuildingBlocksOrganizerPlanner.CreateSession(library);

        session.SetFilter("closers").Items.Select(item => item.Part.Name).Should().Equal("Signature");

        var empty = session.SetFilter("not present");
        empty.Items.Should().BeEmpty();
        empty.SelectedIndex.Should().Be(-1);
        empty.StatusText.Should().Be(BuildingBlocksOrganizerPlanner.NoFilterMatchesStatus);
        empty.CanInsert.Should().BeFalse();
        session.AcceptSelection().Should().BeNull();
    }

    [Fact]
    public void Session_deletes_selected_item_and_restores_first_surviving_selection()
    {
        var library = QuickPartLibrary.LoadFromPath(null);
        library.Save(new QuickPart("Greeting", ["Hello"]));
        library.Save(new QuickPart("Signature", ["Regards"]));
        var session = BuildingBlocksOrganizerPlanner.CreateSession(library);
        session.SelectIndex(1);

        var state = session.DeleteSelection();

        library.Get("Signature").Should().BeNull();
        state.Items.Select(item => item.Part.Name).Should().Equal("Greeting");
        state.SelectedIndex.Should().Be(0);
        state.StatusText.Should().Be("Removed \"Signature\".");
        state.PreviewText.Should().Be("Hello");
    }
}
