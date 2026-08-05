using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class SisterDialogTextResourceSourceTests
{
    [Fact]
    public void InsertDialogs_ResolveTextFromPresentationResources()
    {
        var source = ReadAvaloniaSource("InsertDialogs.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("InsertDialogTextResources.Hyperlink");
        source.Should().Contain("InsertDialogTextResources.Bookmark");
        source.Should().Contain("InsertDialogTextResources.QuickPart");
        source.Should().Contain("InsertDialogTextResources.OkButton");
        source.Should().NotContain("PlaceholderText = \"Text to display\"");
        source.Should().NotContain("Title = \"Insert Hyperlink\"");
        source.Should().NotContain("MakeButton(\"Add\"");
        source.Should().NotContain("MakeButton(\"OK\"");
    }

    [Fact]
    public void PageSetupDialog_ResolvesChromeTextFromPlanner()
    {
        var source = ReadAvaloniaSource("PageSetupDialog.cs");

        source.Should().Contain("PageLayoutDialogChrome.Configure(this, PageSetupDialogPlanner.Title");
        source.Should().Contain("PageSetupDialogPlanner.TopMarginLabel");
        source.Should().Contain("PageSetupDialogPlanner.OrientationNames");
        source.Should().Contain("PageLayoutDialogChrome.Actions(");
        source.Should().NotContain("Title = \"Page Setup\"");
        source.Should().NotContain("Content = \"OK\"");
        source.Should().NotContain("SectionLabel(\"Margins (points)\")");
    }

    [Fact]
    public void MainWindow_ResolvesFilePickerAndStatusTextFromSharedResources()
    {
        var source = ReadAvaloniaSource("MainWindow.cs");

        source.Should().Contain("SisterAppFileTextPlanner.Document");
        source.Should().Contain("FileText.OpenPickerTitle");
        source.Should().Contain("FileText.SavePickerTitle");
        source.Should().Contain("FreeWFileTextResources.ExportPdfPickerTitle");
        source.Should().Contain("FreeWFileTextResources.ExportXpsPickerTitle");
        source.Should().Contain("showOverwritePrompt: true");
        source.Should().Contain("ExportAtomicWriter.ReplaceTarget(");
        source.Should().Contain("InsertDialogTextResources.TextFromFilePickerTitle");
        source.Should().Contain("SisterAppFileTextPlanner.FormatUnsupportedFileType(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("Title = \"Open document\"");
        source.Should().NotContain("Title = \"Save document\"");
        source.Should().NotContain("_status.Text = $\"Open failed:");
        source.Should().NotContain("_status.Text = $\"Save failed:");
        source.Should().NotContain("_status.Text = $\"Saved ");
    }

    [Fact]
    public void MainWindow_ResolvesEditorStatusTextFromPresentationPlanner()
    {
        var source = ReadAvaloniaSource("MainWindow.cs");

        source.Should().Contain("using FreeW.App.Presentation.Shell;");
        source.Should().Contain("FreeWEditorStatusPlanner.Build(");
        source.Should().Contain("new FreeWEditorStatusSnapshot(");
        source.Should().Contain("_editor.ComputeStatistics()");
        source.Should().Contain("SelectionText: _editor.SelectedText");
        source.Should().NotContain("text.Split((char[]?)null");
        source.Should().NotContain("SisterAppStatusBarTextPlanner.FormatDocumentSummaryStatus(");
    }

    [Fact]
    public void BackstageView_ResolvesRailAndPaneTextFromPresentationResources()
    {
        var source = ReadAvaloniaSource("Backstage", "BackstageView.cs");

        source.Should().Contain("BackstageViewTextResources.WindowTitle");
        source.Should().Contain("SisterBackstageEntryPlanner.Build(");
        source.Should().Contain("new AvaloniaBackstageFrame(");
        source.Should().Contain("SisterBackstagePalette.FreeW");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildHomePane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildOpenPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSaveAsPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildSharePane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildExportPane(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildPrintPane(");
        source.Should().Contain("SisterBackstageInfoPanePlanner.Build(");
        source.Should().Contain("BackstagePaneSurfacePlanner.BuildAccountPane(");
        source.Should().Contain("surface.DeferredNote");
        source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
        source.Should().Contain("BackstageViewTextResources.EvidenceSection");
        source.Should().Contain("BackstageViewTextResources.ProductName");
        source.Should().NotContain("Content = \"\u2190 Back\"");
        source.Should().NotContain("AddNavEntry(panel, BackstagePane.Home, \"Home\")");
        source.Should().NotContain("BuildPaneHeader(\"Export\"");
        source.Should().NotContain("Create PDF/XPS Document");
        source.Should().NotContain("BackstagePaneSurfacePlanner.BuildOpenActionPane(");
        source.Should().NotContain("BackstageViewTextResources.DirectPrintDeferredNote");
    }

    [Fact]
    public void OptionsDialog_ResolvesTabsAndRulesFromPresentationPlanner()
    {
        var source = ReadAvaloniaSource("OptionsDialog.cs");

        source.Should().Contain("new OptionsDialogSession(");
        source.Should().Contain("_surface = _session.Surface");
        source.Should().Contain("_surface.Tabs[0].Header");
        source.Should().Contain("_surface.AutoCorrect.Header");
        source.Should().Contain("_surface.AutoFormat.Header");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().Contain("_session.PlanEnabledState(");
        source.Should().NotContain("OptionsDialogPlanner.BuildSurface(");
        source.Should().NotContain("OptionsDialogWorkflowPlanner.TryBuildResult(");
        source.Should().NotContain("OptionsDialogWorkflowPlanner.PlanEnabledState(");
        source.Should().NotContain("OptionsDialogPlanner.TryParseRecentFilesCap(");
        source.Should().NotContain("OptionsDialogPlanner.BuildResult(");
        source.Should().NotContain("new AutoCorrectOptions");
        source.Should().NotContain("new AutoFormatOptions");
        source.Should().NotContain("Title = \"FreeW Options\"");
        source.Should().NotContain("AddRow(grid, 0, \"Recent files to keep:\"");
        source.Should().NotContain("new[] { new FormatChoice(");
    }

    private static string ReadAvaloniaSource(params string[] pathParts)
    {
        var path = Path.Combine(new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia" }.Concat(pathParts).ToArray());
        return File.ReadAllText(path);
    }

}
