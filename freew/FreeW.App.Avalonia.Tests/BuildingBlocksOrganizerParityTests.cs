using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Presentation.QuickParts;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class BuildingBlocksOrganizerParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Uses_the_Wpf_authority_sizing_labels_preview_and_empty_state()
    {
        await Session.Dispatch(() =>
        {
            var library = QuickPartLibrary.LoadFromPath(null);
            var dialog = new BuildingBlocksOrganizerDialog(library);
            try
            {
                dialog.Width.Should().Be(BuildingBlocksOrganizerPlanner.Width);
                dialog.SizeToContent.Should().Be(SizeToContent.Height);
                dialog.CanResize.Should().BeFalse();

                var labels = dialog.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(text => text.Text)
                    .ToArray();
                labels.Should().Contain(BuildingBlocksOrganizerPlanner.ListLabel);
                labels.Should().Contain(BuildingBlocksOrganizerPlanner.PreviewLabel);
                labels.Should().Contain(BuildingBlocksOrganizerPlanner.EmptyStatus);

                var list = dialog.GetLogicalDescendants().OfType<ListBox>().Single();
                var preview = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
                list.MinWidth.Should().Be(BuildingBlocksOrganizerPlanner.ListMinWidth);
                list.MinHeight.Should().Be(BuildingBlocksOrganizerPlanner.ListMinHeight);
                preview.MinWidth.Should().Be(BuildingBlocksOrganizerPlanner.PreviewMinWidth);
                preview.MinHeight.Should().Be(BuildingBlocksOrganizerPlanner.PreviewMinHeight);
                preview.Text.Should().BeEmpty();

                var buttons = dialog.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.Content is "Insert" or "Delete" or "Close")
                    .ToArray();
                buttons.Select(button => button.Content?.ToString())
                    .Should().Equal("Insert", "Delete", "Close");
                buttons.Single(button => button.Content?.ToString() == "Insert").IsEnabled.Should().BeFalse();
                buttons.Single(button => button.Content?.ToString() == "Delete").IsEnabled.Should().BeFalse();
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Populated_state_uses_shared_list_item_and_Wpf_preview_contract()
    {
        await Session.Dispatch(() =>
        {
            var library = QuickPartLibrary.LoadFromPath(null);
            library.Save(new QuickPart("Greeting", ["Dear Sir or Madam,"], "AutoText", "General", "A formal opener"));
            var dialog = new BuildingBlocksOrganizerDialog(library);
            try
            {
                var list = dialog.GetLogicalDescendants().OfType<ListBox>().Single();
                var preview = dialog.GetLogicalDescendants().OfType<TextBox>().Single();
                list.ItemCount.Should().Be(1);
                list.SelectedItem.Should().BeOfType<BuildingBlockListItem>();
                list.SelectedItem!.ToString().Should().Be("Greeting  (AutoText / General)");
                preview.Text.Should().Be("A formal opener\n\nDear Sir or Madam,");

                dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Content?.ToString() == "Insert").IsEnabled.Should().BeTrue();
                dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(button => button.Content?.ToString() == "Delete").IsEnabled.Should().BeTrue();
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }
}
