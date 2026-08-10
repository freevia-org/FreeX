namespace FreeW.App.Presentation.Tests;

public sealed class FreeWCommandRoutingOwnershipSourceGuardTests
{
    [Fact]
    public void EditorsDelegateComplexFieldTransitionsToThePortableEditingSession()
    {
        var wpf = ReadSource("FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ReferenceEdits.ToggleComplexFieldCodes(");
            source.Should().Contain("ReferenceEdits.SetComplexFieldsLocked(");
            source.Should().Contain("ReferenceEdits.UpdateComplexFields(");
            source.Should().Contain("ReferenceEdits.UnlinkComplexFields(");
        }

        wpf.Should().NotContain("private void MutateComplexFields(");
        avalonia.Should().NotContain("fieldRun.ComplexField = fieldRun.ComplexField! with");
        avalonia.Should().NotContain("fieldRun.ComplexField = null;");
    }

    [Fact]
    public void RenderersDelegateFinishEmailAndShortcutRoutingToPresentation()
    {
        var wpfRibbon = ReadSource("FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avaloniaMail = ReadSource("FreeW.App.Avalonia", "Ribbon", "MailMergeEngine.cs");
        var wpfShell = ReadSource("FreeW.App.Host", "MainWindow.cs");
        var avaloniaShell = ReadSource("FreeW.App.Avalonia", "MainWindow.cs");

        wpfRibbon.Should().Contain("workflow.RouteFinish(");
        wpfRibbon.Should().Contain("workflow.ExecuteEmailDrafts(");
        avaloniaMail.Should().Contain("_workflow.RouteFinish(");
        avaloniaMail.Should().Contain("_workflow.ExecuteEmailDrafts(");
        avaloniaMail.Should().NotContain("finishPlan.Destination !=");
        wpfShell.Should().Contain("_applicationCommands.Shortcuts");
        avaloniaShell.Should().Contain("_applicationCommands.TryExecute(");
        wpfShell.Should().NotContain("FreeWKeyboardShortcutCatalog.All");
        avaloniaShell.Should().NotContain("FreeWKeyboardShortcutCatalog.TryDispatch(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, "freew", .. parts]));
    }
}
