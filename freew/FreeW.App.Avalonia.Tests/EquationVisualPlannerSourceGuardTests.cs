namespace FreeW.App.Avalonia.Tests;

public sealed class EquationVisualPlannerSourceGuardTests
{
    [Fact]
    public void AvaloniaDocumentView_UsesSharedPlannerForEquationDisplayCells()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        // Pins the display-cell path, not its exact arity: the method gained a block index and a
        // flow-break flag, which does not change what this guard is protecting.
        source.Should().Contain("DisplayCells(blockIndex, paragraph");
        source.Should().Contain("EquationVisualPlanner.Build(equation)");
        source.Should().Contain("AddEquationVisualElement");
        source.Should().Contain("ApplyEquationVisualStyle");
        source.Should().Contain("EquationElement: element");
        source.Should().Contain("MeasureEquationVisualElement");
        source.Should().Contain("DrawEquationVisualElement");
        source.Should().Contain("EquationVisualElements");
        source.Should().Contain("LowerLimit");
        source.Should().Contain("UpperLimit");
        source.Should().Contain("MatrixRows");
        source.Should().Contain("BaseText");
        source.Should().Contain("OpenDelimiter");
        source.Should().Contain("GroupCharacterPosition");
        source.Should().Contain("FunctionName");
        source.Should().Contain("FunctionArgument");
        source.Should().Contain("run.Equation is { } equation");
        source.Should().NotContain("hasInlineEquation");
    }

    private static string RepositoryFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FreeW.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("tests run from inside the repository tree");
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
