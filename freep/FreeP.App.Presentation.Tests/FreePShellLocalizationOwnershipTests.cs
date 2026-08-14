namespace FreeP.App.Compositor.Tests;

public sealed class FreePShellLocalizationOwnershipTests
{
    [Fact]
    public void RendererOperationNamesAndOptionsSurfaceResolveThroughFreePResources()
    {
        var avalonia = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var wpf = Read("freep", "FreeP.App.Host", "MainWindow.cs");
        var options = Read(
            "freep",
            "FreeP.App.Presentation",
            "Options",
            "OptionsDialogPlanner.cs");

        avalonia.Should().Contain("UiText.Get(\"Shell_Command_SlideZoom\")")
            .And.Contain("UiText.Get(\"Shell_Command_SectionZoom\")")
            .And.Contain("UiText.Get(\"Shell_Command_SummaryZoom\")")
            .And.Contain("UiText.Get(\"Shell_Command_ZoomTarget\")")
            .And.Contain("UiText.Get(\"Shell_Command_SummaryZoomTargets\")")
            .And.Contain("UiText.Get(\"Shell_Command_SlidePane\")")
            .And.Contain("UiText.Get(\"Shell_Command_CustomShow\")")
            .And.NotContain("RunGuarded(OpenSlideZoomDialogAsync, \"Slide Zoom\")")
            .And.NotContain("FormatCommandFailed(FileText, \"Slide Pane\"");

        wpf.Should().Contain("FileTabHeader:  UiText.Get(\"Ribbon_Group_File_Label\")")
            .And.Contain("OptionsDialogPlanner.Title")
            .And.NotContain("FileTabHeader:  \"File\"")
            .And.NotContain("_optionsStore.LastError, \"FreeP Options\"");

        options.Should().Contain("Loc.Get(\"Options_Title\")")
            .And.Contain("Loc.Get(\"Options_RecentFilesLabel\")")
            .And.Contain("Loc.Get(\"Options_UiLanguageCurrentHint\")");
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
