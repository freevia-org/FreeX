using System.Linq;
using Free.Shared.AppServices;
using FreeW.App.Host.Backstage;

namespace FreeW.App.Host.Tests;

public sealed class BackstageHomePanePlannerTests
{
    [Fact]
    public void Build_WithRecentDocuments_PutsRecentRowsBetweenNewAndOpen()
    {
        var opened = "";

        var groups = BackstageHomePanePlanner.Build(
            [
                new RecentFileEntry { Path = @"C:\Docs\Budget.docx" },
                new RecentFileEntry { Path = @"C:\Docs\Plan.rtf", IsPinned = true },
            ],
            static () => { },
            path => opened = path,
            static () => { },
            static () => { });

        groups.Select(group => group.Heading).Should().Equal("New", "Recent Documents", "Open");

        var recent = groups[1].Actions;
        recent.Select(row => row.Label).Should().Equal("Budget.docx", "Plan.rtf");
        recent[0].Description.Should().Be(@"C:\Docs\Budget.docx");
        recent[1].Description.Should().Be(@"C:\Docs\Plan.rtf  (pinned)");

        recent[1].Invoke();

        opened.Should().Be(@"C:\Docs\Plan.rtf");
    }

    [Fact]
    public void Build_WithoutRecentDocuments_KeepsBackedNewAndOpenActions()
    {
        var newCount = 0;
        var browseCount = 0;
        var openMoreCount = 0;

        var groups = BackstageHomePanePlanner.Build(
            [],
            () => newCount++,
            static _ => { },
            () => browseCount++,
            () => openMoreCount++);

        groups.Select(group => group.Heading).Should().Equal("New", "Open");
        groups[0].Actions.Single().Label.Should().Be("Blank document");
        groups[1].Actions.Select(action => action.Label).Should().Equal("Browse", "Open More Documents");

        groups[0].Actions.Single().Invoke();
        groups[1].Actions[0].Invoke();
        groups[1].Actions[1].Invoke();

        newCount.Should().Be(1);
        browseCount.Should().Be(1);
        openMoreCount.Should().Be(1);
    }

    [Fact]
    public void Build_IgnoresBlankRecentPaths()
    {
        var groups = BackstageHomePanePlanner.Build(
            [
                new RecentFileEntry { Path = "" },
                new RecentFileEntry { Path = "   " },
            ],
            static () => { },
            static _ => { },
            static () => { },
            static () => { });

        groups.Should().NotContain(group => group.Heading == "Recent Documents");
    }

    [Fact]
    public void Build_LimitsRecentDocumentsSoOpenActionsStayReachable()
    {
        var groups = BackstageHomePanePlanner.Build(
            Enumerable.Range(1, 10).Select(index => new RecentFileEntry { Path = $@"C:\Docs\File{index}.docx" }),
            static () => { },
            static _ => { },
            static () => { },
            static () => { });

        groups.Single(group => group.Heading == "Recent Documents").Actions
            .Select(action => action.Label)
            .Should().Equal(
                "File1.docx",
                "File2.docx",
                "File3.docx",
                "File4.docx",
                "File5.docx",
                "File6.docx");
    }
}
