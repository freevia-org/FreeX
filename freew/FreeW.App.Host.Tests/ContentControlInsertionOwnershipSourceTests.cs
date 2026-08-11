using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ContentControlInsertionOwnershipSourceTests
{
    [Fact]
    public void WpfInsertion_DelegatesDefaultsAndFormattingToSharedPlanner()
    {
        var source = File.ReadAllText(TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx",
            "freew",
            "FreeW.App.Host",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("ContentControlInteractionPlanner.PromptText(selected)");
        source.Should().Contain("ContentControlInteractionPlanner.DateFormatOrDefault(dateFormat)");
        source.Should().Contain("ContentControlInteractionPlanner.FormatDate(fmt, System.DateTime.Today)");
        source.Should().Contain("ContentControlInteractionPlanner.ListItemsOrDefault(items)");
        source.Should().NotContain("private static readonly IReadOnlyList<ContentControlListItem> DefaultListItems");
        source.Should().NotContain("string.IsNullOrEmpty(selected) ? \"Click to enter text\"");
    }
}
