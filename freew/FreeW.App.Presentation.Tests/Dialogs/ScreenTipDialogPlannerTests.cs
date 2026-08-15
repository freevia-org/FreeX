using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class ScreenTipDialogPlannerTests
{
    [Fact]
    public void Build_projects_shared_text_and_initial_value()
    {
        var presentation = ScreenTipDialogPlanner.Build("Current tip");

        presentation.Title.Should().Be("Set ScreenTip");
        presentation.Label.Should().NotBeNullOrWhiteSpace();
        presentation.Placeholder.Should().NotBeNullOrWhiteSpace();
        presentation.InitialScreenTip.Should().Be("Current tip");
    }

    [Theory]
    [InlineData("  Open documentation  ", "Open documentation")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void PlanAcceptance_trims_and_preserves_blank_as_clear(string? input, string expected)
    {
        ScreenTipDialogPlanner.PlanAcceptance(input).Should().Be(expected);
    }

    [Fact]
    public void Both_renderers_project_the_shared_contract_and_the_route_is_paired()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpfDialog = Read(root, "freew", "FreeW.App.Host", "ScreenTipDialog.cs");
        var wpfCommands = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaDialog = Read(root, "freew", "FreeW.App.Avalonia", "InsertDialogs.cs");
        var catalog = Read(root, "freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        wpfDialog.Should().Contain(": Free.Shared.Ribbon.Wpf.DialogWindow")
            .And.Contain("ScreenTipDialogPlanner.Build(")
            .And.Contain("ScreenTipDialogPlanner.PlanAcceptance(")
            .And.Contain("DialogButtonRowFactory.Create(");
        avaloniaDialog.Should().Contain("class ScreenTipDialog : FreeWDialogWindow")
            .And.Contain("ScreenTipDialogPlanner.Build(")
            .And.Contain("ScreenTipDialogPlanner.PlanAcceptance(");
        wpfCommands.Should().Contain("ScreenTipDialog.Ask(Window.GetWindow(editor), seed)")
            .And.NotContain("private static class HyperlinkPrompt");
        catalog.Should().Contain("Pair(\"screen-tip\", \"ScreenTipDialog\")")
            .And.NotContain("AvaloniaOnly(\"screen-tip\"");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine([root, .. relativeParts]));
}
