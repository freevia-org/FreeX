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
    public void Hosts_AdaptPlatformInputAndDelegateFormulaEditingPolicyToSession()
    {
        var hostEditing = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        var hostFormulaReferenceEditing = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.FormulaReferenceEditing.cs"));
        var hostSelection = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var avaloniaMain = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        hostEditing.Should().Contain("FormulaBarWpfInputAdapter.ToFormulaEditorKey");
        hostEditing.Should().Contain("FormulaBarWpfInputAdapter.ToFormulaEditorModifiers");
        hostEditing.Should().Contain("_formulaRangeEditingSession.TogglePointMode(");
        hostEditing.Should().Contain("_formulaRangeEditingSession.BuildEditStatusBarPlan(");
        hostEditing.Should().Contain("ApplyFormulaEditStatusBarPlan");
        hostFormulaReferenceEditing.Should().Contain("_formulaRangeEditingSession.ApplyTextChanged(");
        hostSelection.Should().Contain("_formulaRangeEditingSession.ApplyTypedEntry(");
        avaloniaMain.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorKey");
        avaloniaMain.Should().Contain("FormulaBarAvaloniaInputAdapter.ToFormulaEditorModifiers");
        avaloniaMain.Should().Contain("_formulaRangeEditingSession.TogglePointMode(");

        var rendererSources = string.Join(
            Environment.NewLine,
            hostEditing,
            hostFormulaReferenceEditing,
            hostSelection,
            avaloniaMain);
        rendererSources.Should().NotContain("FormulaEditInteractionPlanner.BuildPointModeTogglePlan");
        rendererSources.Should().NotContain("FormulaEditInteractionPlanner.BuildTextChangePlan");
        rendererSources.Should().NotContain("FormulaEditInteractionPlanner.BuildTypedEntryPlan");
    }

    [Fact]
    public void WpfFormulaEditing_ConsumesPortableStatusPlansInsteadOfSharedModeKeys()
    {
        var hostFiles = new[]
        {
            FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Editing.cs"),
            FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.FormulaReferenceEditing.cs"),
            FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.Selection.cs"),
            FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"),
            FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.TextBoxInlineEditing.cs")
        };

        foreach (var hostFile in hostFiles)
        {
            var source = File.ReadAllText(hostFile);

            source.Should().NotContain("StatusBarTextResourceKeys.EnterMode");
            source.Should().NotContain("StatusBarTextResourceKeys.EditMode");
            source.Should().NotContain("StatusBarTextResourceKeys.PointMode");
            source.Should().NotContain("FormulaEditInteractionPlanner.EnterModeStatusBarResourceKey");
            source.Should().NotContain("FormulaEditInteractionPlanner.EditModeStatusBarResourceKey");
            source.Should().NotContain("UiText.Get(FormulaEditInteractionPlanner");
        }
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(relativeParts);
}
