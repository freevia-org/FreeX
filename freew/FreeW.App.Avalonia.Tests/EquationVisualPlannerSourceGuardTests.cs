namespace FreeW.App.Avalonia.Tests;

public sealed class EquationVisualPlannerSourceGuardTests
{
    [Fact]
    public void AvaloniaDocumentView_UsesSharedPlannerForEquationDisplayCells()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        var projection = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.RunDisplayProjection.cs"));
        var keyboard = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.KeyboardNavigation.cs"));
        var spacing = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.CharacterSpacing.cs"));

        // Pin the shared planner and its actual body, table, keyboard and intrinsic-width consumers.
        source.Should().Contain("DisplayCells(blockIndex, paragraph");
        projection.Should().Contain("EquationVisualPlanner.Build(equation)");
        source.Should().Contain("ResolveRunDisplayProjection(paragraph, run");
        source.Should().Contain("BuildCellDisplayItems(blockIndex, paragraph");
        keyboard.Should().Contain("ResolveRunDisplayProjection(paragraph.Model, run");
        spacing.Should().Contain("BuildCellDisplayItems(blockIndex, paragraph)");
        source.Should().Contain("ApplyEquationVisualStyle");
        source.Should().Contain("EquationElement: token.EquationElement");
        source.Should().Contain("MeasureEquationVisualElement");
        source.Should().Contain("DrawEquationVisualElement");
        source.Should().Contain("_equationVisualElements");
        source.Should().Contain("LowerLimit");
        source.Should().Contain("UpperLimit");
        source.Should().Contain("MatrixRows");
        source.Should().Contain("BaseText");
        source.Should().Contain("OpenDelimiter");
        source.Should().Contain("GroupCharacterPosition");
        source.Should().Contain("FunctionName");
        source.Should().Contain("FunctionArgument");
        projection.Should().Contain("source.Equation is { } equation");
        source.Should().NotContain("hasInlineEquation");
    }

    private static string RepositoryFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeW.slnx", parts);
}
