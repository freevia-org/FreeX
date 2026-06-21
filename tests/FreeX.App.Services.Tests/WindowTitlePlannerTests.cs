using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WindowTitlePlannerTests
{
    [Fact]
    public void Compose_DefaultsToDocumentThenApplication()
    {
        WindowTitlePlanner.Compose(
                displayName: "Book1",
                applicationName: "FreeX",
                isDirty: true,
                dirtyMarker: "*",
                separator: " - ")
            .Should()
            .Be("Book1* - FreeX");
    }

    [Fact]
    public void Compose_CanPlaceApplicationBeforeDocument()
    {
        WindowTitlePlanner.Compose(
                displayName: "Book1",
                applicationName: "FreeX",
                isDirty: true,
                dirtyMarker: " *",
                separator: " - ",
                groupSuffix: " [Group]",
                applicationPlacement: WindowTitleApplicationPlacement.ApplicationThenDocument)
            .Should()
            .Be("FreeX - Book1 [Group] *");
    }

    [Fact]
    public void DisplayNameFromPath_UsesFileNameWithoutExtension()
    {
        WindowTitlePlanner.DisplayNameFromPath(@"C:\Work\Quarterly Budget.xlsx")
            .Should()
            .Be("Quarterly Budget");
    }
}
