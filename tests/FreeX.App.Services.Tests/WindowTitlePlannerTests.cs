using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WindowTitlePlannerTests
{
    private static readonly ApplicationWindowTitleSpec FreePTitle = new(
        ApplicationName: "FreeP",
        DefaultDocumentDisplayName: "Untitled",
        DirtyMarker: " *",
        Separator: " \u2014 ",
        ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication);

    [Fact]
    public void ApplicationPolicy_ComposesProductDefaultAndDirtyConventions()
    {
        ApplicationWindowTitlePolicy.Compose(FreePTitle, "Quarterly Review", isDirty: true)
            .Should()
            .Be("Quarterly Review * \u2014 FreeP");

        ApplicationWindowTitlePolicy.Compose(FreePTitle, null, isDirty: false)
            .Should()
            .Be("Untitled \u2014 FreeP");
    }

    [Fact]
    public void ApplicationPolicy_CollapsesOnlyCleanDefaultDocumentsWhenConfigured()
    {
        var title = FreePTitle with { CollapseCleanDefaultDocumentTitle = true };

        ApplicationWindowTitlePolicy.Compose(
                title,
                "Untitled",
                isDirty: false,
                isDefaultDocument: true)
            .Should()
            .Be("FreeP");
        ApplicationWindowTitlePolicy.Compose(
                title,
                "Untitled",
                isDirty: false,
                isDefaultDocument: false)
            .Should()
            .Be("Untitled \u2014 FreeP");
    }

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

    [Fact]
    public void NativeTitleSpecs_PreserveConstructorsAndDelegateToSharedPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Wpf",
            "SisterWpfWindowTitleBinder.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaFileCommandWorkflow.cs"));

        wpf.Should().Contain("public sealed record SisterWpfWindowTitleSpec(")
            .And.Contain("string ApplicationName,")
            .And.Contain("string DirtyMarker,")
            .And.Contain("ApplicationWindowTitlePolicy.Compose(")
            .And.NotContain("WindowTitlePlanner.Compose(");
        avalonia.Should().Contain("public sealed record SisterAvaloniaFileTitleSpec(")
            .And.Contain("string ApplicationName,")
            .And.Contain("string UntitledDisplayName = FileCommandSession.DefaultUntitledDisplayName")
            .And.Contain("ApplicationWindowTitlePolicy.Compose(")
            .And.NotContain("WindowTitlePlanner.Compose(");
    }
}
