using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class OptionsDialogAdvancedParitySourceTests
{
    [Fact]
    public void AdvancedOptions_UsesSharedMetricsAndWpfRowGeometry()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("OptionsDialogPlanner.CategoryColumnWidth");
        source.Should().Contain("OptionsDialogPlanner.ContentPaddingHorizontal");
        source.Should().Contain("OptionsDialogPlanner.FooterHeight");
        source.Should().Contain("OptionsSectionHeader(OptionsText(\"Options_EditingOptions\"), topMargin: 0)");
        source.Should().Contain("advancedPanel.Spacing = 0;");
        source.Should().Contain("spacing: 0");
        source.Should().Contain("OptionsDialogPlanner.AdvancedDirectionLeftMargin");
        source.Should().Contain("OptionsDialogPlanner.AdvancedObjectsControlWidth");
    }

    [Fact]
    public void AdvancedOptions_PreservesInteractiveStatesAndObjectsSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Options.cs"));

        source.Should().Contain("isChecked: current.EnableAutoCompleteForCellValues");
        source.Should().Contain("isEnabled: true,");
        source.Should().Contain("AutomationProperties.SetAutomationId(objectsDisplayBox, \"OptionsObjectsDisplayComboBox\")");
        source.Should().Contain("objectsDisplay: objectsDisplayBox.SelectedIndex switch");
        source.Should().Contain("AppOptionsObjectDisplay.Placeholders");
        source.Should().Contain("AppOptionsObjectDisplay.Nothing");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
