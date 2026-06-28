using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class EditingPlannerSourceGuardTests
{
    [Fact]
    public void EditingPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Editing");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);

            source.Should().NotContain("System.Windows");
            source.Should().NotContain("Avalonia.");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
            source.Should().NotContain("UiText.Get(");
        }
    }

    [Fact]
    public void WpfHostDoesNotCarryClipboardOrInsertPlannerFacades()
    {
        var hostRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");

        File.Exists(Path.Combine(hostRoot, "ClipboardPastePlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "InsertCopiedCellsPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "KeyboardInsertDeletePlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(hostRoot, "CellShiftDialogPlanner.cs")).Should().BeFalse();
    }
}
