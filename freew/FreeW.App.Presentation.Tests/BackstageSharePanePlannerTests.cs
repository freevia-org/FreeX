using FreeW.App.Presentation.Backstage;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageSharePanePlannerTests
{
    [Fact]
    public void Build_WithSavedLocalDocument_OpensContainingFolderFirst()
    {
        var openedPath = "";
        var savedAs = false;
        var groups = BackstageSharePanePlanner.Build(
            @"C:\Docs\Plan.docx",
            path => path == @"C:\Docs\Plan.docx",
            () => savedAs = true,
            path => openedPath = path,
            static () => { },
            static () => { });

        groups.Select(group => group.Heading).Should().Equal("Share", "Send a Copy");
        var primary = groups[0].Actions.Should().ContainSingle().Subject;
        primary.Label.Should().Be("Open Containing Folder");
        primary.Description.Should().Contain("Windows Share is unavailable");
        primary.Description.Should().Contain(@"C:\Docs\Plan.docx");

        primary.Invoke();

        openedPath.Should().Be(@"C:\Docs\Plan.docx");
        savedAs.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Docs\Missing.docx")]
    public void Build_WithoutSavedLocalDocument_RoutesPrimaryActionToSaveAs(string? currentPath)
    {
        var openedPath = "";
        var savedAs = false;
        var groups = BackstageSharePanePlanner.Build(
            currentPath,
            static _ => false,
            () => savedAs = true,
            path => openedPath = path,
            static () => { },
            static () => { });

        var primary = groups[0].Actions.Should().ContainSingle().Subject;
        primary.Label.Should().Be("Save As");
        primary.Description.Should().Contain("Save As is required");
        primary.Description.Should().Contain("document");
        primary.Description.Should().NotContain("workbook");

        primary.Invoke();

        savedAs.Should().BeTrue();
        openedPath.Should().BeEmpty();
    }

    [Fact]
    public void Build_AlwaysOffersCopyAndFixedLayoutActions()
    {
        var savedCopy = false;
        var exportedPdf = false;
        var groups = BackstageSharePanePlanner.Build(
            @"C:\Docs\Plan.docx",
            static _ => true,
            static () => { },
            static _ => { },
            () => savedCopy = true,
            () => exportedPdf = true);

        var copyRows = groups[1].Actions;
        copyRows.Select(row => row.Label).Should().Equal("Save a Copy", "Create PDF/XPS");

        copyRows[0].Invoke();
        copyRows[1].Invoke();

        savedCopy.Should().BeTrue();
        exportedPdf.Should().BeTrue();
    }
}
