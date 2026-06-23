using System.Linq;
using Free.Shared.AppServices;
using FreeW.App.Host.Backstage;

namespace FreeW.App.Host.Tests;

public sealed class BackstageOpenPanePlannerTests
{
    [Fact]
    public void Build_WithRecentDocuments_PutsRecentBeforePlacesAndOpensPath()
    {
        var opened = "";

        var groups = BackstageOpenPanePlanner.Build(
            [
                new RecentFileEntry { Path = @"C:\Docs\Budget.docx", IsPinned = false },
                new RecentFileEntry { Path = @"C:\Docs\Plan.rtf", IsPinned = true },
            ],
            path => opened = path,
            static () => { },
            static () => { });

        groups.Select(group => group.Heading).Should().Equal("Recent Documents", "Places", "Recovery");

        var recent = groups[0].Actions;
        recent.Select(row => row.Label).Should().Equal("Budget.docx", "Plan.rtf");
        recent[0].Description.Should().Be(@"C:\Docs\Budget.docx");
        recent[1].Description.Should().Be(@"C:\Docs\Plan.rtf  (pinned)");

        recent[1].Invoke();

        opened.Should().Be(@"C:\Docs\Plan.rtf");
    }

    [Fact]
    public void Build_WithoutRecentDocuments_OmitsRecentGroupButKeepsBackedOpenActions()
    {
        var browseCount = 0;
        var recoverCount = 0;

        var groups = BackstageOpenPanePlanner.Build(
            [],
            static _ => { },
            () => browseCount++,
            () => recoverCount++);

        groups.Select(group => group.Heading).Should().Equal("Places", "Recovery");

        groups[0].Actions.Select(row => row.Label).Should().Equal("This PC", "Browse");
        groups[1].Actions.Single().Label.Should().Be("Recover Unsaved Documents");

        groups[0].Actions[0].Invoke();
        groups[0].Actions[1].Invoke();
        groups[1].Actions.Single().Invoke();

        browseCount.Should().Be(2);
        recoverCount.Should().Be(1);
    }

    [Fact]
    public void Build_IgnoresBlankRecentPaths()
    {
        var groups = BackstageOpenPanePlanner.Build(
            [
                new RecentFileEntry { Path = "" },
                new RecentFileEntry { Path = "   " },
            ],
            static _ => { },
            static () => { },
            static () => { });

        groups.Should().NotContain(group => group.Heading == "Recent Documents");
    }

    [Fact]
    public void Build_LimitsRecentDocumentsSoPlacesStayReachable()
    {
        var groups = BackstageOpenPanePlanner.Build(
            Enumerable.Range(1, 12).Select(index => new RecentFileEntry { Path = $@"C:\Docs\File{index}.docx" }),
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
                "File6.docx",
                "File7.docx",
                "File8.docx");
    }

    [Fact]
    public void BuildPlan_FiltersRecentDocumentsAndFolders()
    {
        var opened = "";
        var openedFolder = "";

        var plan = BackstageOpenPanePlanner.BuildPlan(
            [
                new RecentFileEntry { Path = @"C:\Docs\Budget.docx" },
                new RecentFileEntry { Path = @"C:\Docs\Notes.rtf" },
                new RecentFileEntry { Path = @"C:\Reports\Budget Review.docx" },
            ],
            "budget",
            path => opened = path,
            folder => openedFolder = folder,
            static () => { },
            static () => { });

        plan.DocumentRows.Select(row => row.Label).Should().Equal("Budget.docx", "Budget Review.docx");
        plan.FolderRows.Select(row => row.Label).Should().Equal("Docs", "Reports");
        plan.PlaceRows.Select(row => row.Label).Should().Equal("This PC", "Browse");
        plan.RecoveryRows.Single().Label.Should().Be("Recover Unsaved Documents");

        plan.DocumentRows[1].Invoke();
        plan.FolderRows[0].Invoke();

        opened.Should().Be(@"C:\Reports\Budget Review.docx");
        openedFolder.Should().Be(@"C:\Docs");
    }

    [Fact]
    public void BuildPlan_DeduplicatesFoldersAndLimitsTheList()
    {
        var plan = BackstageOpenPanePlanner.BuildPlan(
            Enumerable.Range(1, 12).Select(index => new RecentFileEntry
            {
                Path = $@"C:\Projects\Folder{index}\Doc{index}.docx"
            }).Concat(
            [
                new RecentFileEntry { Path = @"C:\Projects\Folder1\Second.docx" },
            ]),
            filter: null,
            static _ => { },
            static _ => { },
            static () => { },
            static () => { });

        plan.FolderRows.Select(row => row.Label)
            .Should().Equal(
                "Folder1",
                "Folder2",
                "Folder3",
                "Folder4",
                "Folder5",
                "Folder6",
                "Folder7",
                "Folder8");
    }
}
