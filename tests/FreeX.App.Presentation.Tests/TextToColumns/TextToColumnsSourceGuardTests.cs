using FluentAssertions;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsSourceGuardTests
{
    [Fact]
    public void FixedWidthPlanners_AreSingleSharedPresentationImplementations()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostRoot = Path.Combine(repoRoot, "src", "FreeX.App.Host");

        File.Exists(Path.Combine(presentationRoot, "TextToColumns", "TextToColumnsFixedWidthBreakPlanner.cs"))
            .Should()
            .BeTrue("fixed-width break parsing and mutation should be shared by renderers");
        File.Exists(Path.Combine(presentationRoot, "TextToColumns", "TextToColumnsFixedWidthRulerPlanner.cs"))
            .Should()
            .BeTrue("fixed-width ruler coordinate planning should be shared by renderers");
        File.Exists(Path.Combine(hostRoot, "TextToColumnsFixedWidthBreakPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared fixed-width break planner instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(hostRoot, "TextToColumnsFixedWidthRulerPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared fixed-width ruler planner instead of carrying a renderer-local facade");
        File.Exists(Path.Combine(hostRoot, "TextToColumnsPlanner.cs"))
            .Should()
            .BeFalse("WPF host should call the shared Text-to-Columns apply planner directly instead of carrying a pure facade");
        File.Exists(Path.Combine(hostRoot, "TextToColumnsWizardPlanner.cs"))
            .Should()
            .BeFalse("WPF host should localize the shared wizard surface plan at the dialog edge instead of carrying a duplicate planner");

        var commandPlannerPath = Path.Combine(presentationRoot, "TextToColumns", "TextToColumnsCommandPlanner.cs");
        File.Exists(commandPlannerPath)
            .Should()
            .BeTrue("Text-to-Columns command creation should be shared by renderers");
        File.Exists(Path.Combine(hostRoot, "TextToColumnsCommandPlanner.cs"))
            .Should()
            .BeFalse("WPF host should call the shared Text-to-Columns command planner instead of carrying a pure facade");

        var commandPlannerSource = File.ReadAllText(commandPlannerPath);
        commandPlannerSource.Should().Contain("TextToColumnsApplyPlanner.BuildSheetPlans(");
        commandPlannerSource.Should().NotContain("FindOverwriteTargets(");
        commandPlannerSource.Should().NotContain(
            "TextToColumnsSheetApplyPlan> BuildSheetPlans(",
            "sheet planning should stay in the shared Presentation apply planner");
    }
}
