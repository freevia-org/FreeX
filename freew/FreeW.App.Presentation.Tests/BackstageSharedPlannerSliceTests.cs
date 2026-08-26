using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageSharedPlannerSliceTests
{
    [Fact]
    public void RecentActionRows_BuildsFilteredDocumentRowsWithPinnedDescriptions()
    {
        var opened = "";

        var rows = BackstageRecentActionRowsPlanner.BuildDocumentRows(
            [
                new RecentFileEntry { Path = "C:/Docs/Budget.docx", IsPinned = true },
                new RecentFileEntry { Path = "C:/Docs/Notes.docx" },
                new RecentFileEntry { Path = " " },
            ],
            maxRows: 4,
            new BackstageRecentActionRowText(" pinned"),
            path => opened = path,
            filter: "budget");

        rows.Should().ContainSingle();
        rows[0].Label.Should().Be("Budget.docx");
        rows[0].Description.Should().Be("C:/Docs/Budget.docx pinned");

        rows[0].Invoke();

        opened.Should().Be("C:/Docs/Budget.docx");
    }

    [Fact]
    public void RecentActionRows_BuildsDistinctFolderRowsAfterDocumentFiltering()
    {
        var openedFolder = "";

        var rows = BackstageRecentActionRowsPlanner.BuildFolderRows(
            [
                new RecentFileEntry { Path = "C:/Docs/Budget.docx" },
                new RecentFileEntry { Path = "C:/Docs/Budget Copy.docx" },
                new RecentFileEntry { Path = "C:/Reports/Budget Review.docx" },
                new RecentFileEntry { Path = "C:/Notes/Plan.docx" },
            ],
            maxRows: 8,
            path => openedFolder = path,
            filter: "budget");

        rows.Select(row => row.Label).Should().Equal("Docs", "Reports");

        rows[1].Invoke();

        openedFolder.Should().Be("C:/Reports");
    }

    [Fact]
    public void FileTypeActionPlanner_BuildsGroupedRowsAndChoicesFromAppOwnedRows()
    {
        var invoked = "";
        var rows = new[]
        {
            new BackstageFileTypeActionRow<TestFileTypeCategory>(
                TestFileTypeCategory.Native,
                ".docx",
                "Word Document (*.docx)",
                "Save as a Word document."),
            new BackstageFileTypeActionRow<TestFileTypeCategory>(
                TestFileTypeCategory.Other,
                ".txt",
                "Plain Text (*.txt)",
                "Save document text only."),
        };

        var groups = BackstageFileTypeActionPlanner.BuildGroups(
            rows,
            [
                new BackstageFileTypeActionGroupSpec<TestFileTypeCategory>(TestFileTypeCategory.Native, "Documents"),
                new BackstageFileTypeActionGroupSpec<TestFileTypeCategory>(TestFileTypeCategory.Other, "Other Formats"),
            ],
            extension => invoked = extension);
        var changeType = BackstageFileTypeActionPlanner.BuildGroup(
            "Change File Type",
            rows,
            extension => invoked = extension);
        var choices = BackstageFileTypeActionPlanner.BuildChoices(rows);

        groups.Select(group => group.Heading).Should().Equal("Documents", "Other Formats");
        groups[0].Actions.Single().Label.Should().Be("Word Document (*.docx)");
        groups[1].Actions.Single().Invoke();
        invoked.Should().Be(".txt");

        changeType.Heading.Should().Be("Change File Type");
        changeType.Actions.Select(action => action.Label).Should().Equal(
            "Word Document (*.docx)",
            "Plain Text (*.txt)");
        choices.Should().Equal(
            new BackstageFileTypeChoice("Word Document (*.docx)", ".docx"),
            new BackstageFileTypeChoice("Plain Text (*.txt)", ".txt"));
    }

    private enum TestFileTypeCategory
    {
        Native,
        Other
    }
}
