using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaBarPlannerSourceGuardTests
{
    [Fact]
    public void FormulaBarPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "FormulaBar");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);

            source.Should().NotContain("System.Windows");
            source.Should().NotContain("Avalonia.");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
        }
    }

    [Fact]
    public void Hosts_AdaptPlatformInputBeforeCallingFormulaBarPlanners()
    {
        var hostEditing = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        var avaloniaMain = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        hostEditing.Should().Contain("FormulaBarWpfInputAdapter.ToFormulaEditorKey");
        hostEditing.Should().Contain("FormulaBarWpfInputAdapter.ToFormulaEditorModifiers");
        hostEditing.Should().Contain("FormulaEditInteractionPlanner.EditModeStatusBarResourceKey");
        hostEditing.Should().Contain("FormulaEditInteractionPlanner.EnterModeStatusBarResourceKey");
        avaloniaMain.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorKey");
        avaloniaMain.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorModifiers");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(relativeParts));
    }
}
