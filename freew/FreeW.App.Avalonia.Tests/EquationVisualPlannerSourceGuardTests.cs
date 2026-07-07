namespace FreeW.App.Avalonia.Tests;

public sealed class EquationVisualPlannerSourceGuardTests
{
    [Fact]
    public void AvaloniaDocumentView_UsesSharedPlannerForEquationDisplayCells()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        source.Should().Contain("DisplayCells(paragraph)");
        source.Should().Contain("EquationVisualPlanner.Build(equation)");
        source.Should().Contain("AddEquationVisualElement");
        source.Should().Contain("ApplyEquationVisualStyle");
        source.Should().Contain("EquationVisualElements");
        source.Should().Contain("LowerLimit");
        source.Should().Contain("UpperLimit");
        source.Should().Contain("run.Equation is null");
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
