using System.IO;
using System.Text.RegularExpressions;

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

        source.Should().Contain("var surface = PageSetupDialogPlanner.Surface;");
        source.Should().Contain("BuildTab(tabSpec)");
        source.Should().Contain("ControlFor(row.Kind)");
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
        var outputWorkflow = ReadPresentationSource("Shell", "FreeWOutputWorkflow.cs");
        var fragmentWorkflow = ReadPresentationSource(
            "DocumentFragments", "FreeWDocumentFragmentImportWorkflow.cs");

        source.Should().Contain("FreeWFileTextResources.Document");
        source.Should().Contain("FileText.OpenPickerTitle");
        source.Should().Contain("FileText.SavePickerTitle");
        source.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        outputWorkflow.Should().Contain("FreeWFileTextResources.ExportPdfPickerTitle");
        outputWorkflow.Should().Contain("FreeWFileTextResources.ExportXpsPickerTitle");
        source.Should().Contain("showOverwritePrompt: true");
        source.Should().Contain("FreeWExportWorkflow.ExecuteAsync(");
        outputWorkflow.Should().Contain("ExportAtomicWriter.ReplaceTarget(");
        fragmentWorkflow.Should().Contain("InsertDialogTextResources.TextFromFilePickerTitle");
        fragmentWorkflow.Should().Contain("SisterAppFileTextPlanner.FormatUnsupportedFileType(");
        fragmentWorkflow.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("SisterAppFileTextPlanner.Document");
        source.Should().NotContain("SisterAppFileTextPlanner.OpenCommand");
        source.Should().NotContain("SisterAppFileTextPlanner.SaveCommand");
        source.Should().NotContain("SisterAppFileTextPlanner.InsertPictureCommand");
        source.Should().NotContain("SisterAppFileTextPlanner.InsertPicturePickerTitle");
        var formatterCalls = Regex.Matches(
            source,
            @"SisterAppFileTextPlanner\.Format(?:CommandUnavailable|SelectedFileNotLocalPath|UnsupportedFileType|UnsupportedExtension|CommandFailed|Opened|Saved|Inserted|SaveAsTitle)\(\s*(?<first>[A-Za-z_][A-Za-z0-9_.]*)");
        formatterCalls.Should().NotBeEmpty();
        formatterCalls.Cast<Match>()
            .Should().OnlyContain(match => match.Groups["first"].Value == "FileText");
        source.Should().NotContain("Title = \"Open document\"");
        source.Should().NotContain("Title = \"Save document\"");
        source.Should().NotContain("_status.Text = $\"Open failed:");
        source.Should().NotContain("_status.Text = $\"Save failed:");
        source.Should().NotContain("_status.Text = $\"Saved ");
    }

    private static string ReadPresentationSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, "freew", "FreeW.App.Presentation", .. parts]));
    }

    [Fact]
    public void MainWindow_ResolvesEditorStatusTextFromPresentationPlanner()
    {
        var source = ReadAvaloniaSource("MainWindow.cs");
        var statusStart = source.IndexOf("private void UpdateStatus()", StringComparison.Ordinal);
        var statusEnd = source.IndexOf("// ", statusStart, StringComparison.Ordinal);
        var statusSource = source[statusStart..statusEnd];

        source.Should().Contain("using FreeW.App.Presentation.Shell;");
        statusSource.Should().Contain("_editorInteraction.BuildStatus(");
        statusSource.Should().NotContain("FreeWEditorStatusPlanner.Build(");
        statusSource.Should().Contain("new FreeWEditorStatusContext(");
        statusSource.Should().NotContain("new FreeWEditorStatusSnapshot(");
        statusSource.Should().NotContain("_editor.ComputeStatistics()");
        statusSource.Should().Contain("SelectionText: _editor.SelectedText");
        statusSource.Should().NotContain("text.Split((char[]?)null");
        statusSource.Should().NotContain("SisterAppStatusBarTextPlanner.FormatDocumentSummaryStatus(");
        source.Should().Contain("ZoomLevels.FormatPercent(");
        source.Should().NotContain("$\"{ZoomLevels.ToPercent(");
    }

    [Fact]
    public void BackstageView_ResolvesRailAndPaneTextFromPresentationResources()
    {
        var source = ReadAvaloniaSource("Backstage", "BackstageView.cs");

        source.Should().Contain("BackstageViewTextResources.WindowTitle");
        source.Should().Contain("SisterBackstageEntryPlanner.Build(");
        source.Should().Contain("new AvaloniaBackstageFrame(");
        source.Should().Contain("AvaloniaSisterBackstageTheme.FreeW");
        source.Should().Contain("BackstageTheme.Accent");
        source.Should().Contain("new SolidColorBrush(BackstageTheme.LinkColor)");
        source.Should().Contain("Width = BackstageTheme.TileWidth");
        source.Should().Contain("Height = BackstageTheme.TileHeight");
        source.Should().NotContain("SisterBackstagePalette.FreeW");
        source.Should().NotContain("ToColor(BackstageRgb");
        source.Should().Contain("FreeWBackstagePaneTextCatalog.BuildTextSpec(BackstageStrings.Current.Get)");
        source.Should().Contain("BackstageActionBinder.DismissBefore(Dismiss),");
        source.Should().Contain("BackstageStrings.Current.Get);");
        source.Should().Contain("new FreeWBackstageSession(");
        source.Should().Contain("_session.BuildHomePane(");
        source.Should().Contain("_session.BuildOpenPane(");
        source.Should().Contain("_session.BuildSaveAsPane(");
        source.Should().Contain("_session.BuildSharePane(");
        source.Should().Contain("_session.BuildExportPane(");
        source.Should().Contain("_session.BuildPrintPane(");
        source.Should().Contain("_session.BuildInfoPane(");
        source.Should().Contain("_session.BuildAccountPane(");
        source.Should().NotContain("BackstagePaneSurfacePlanner.Build");
        source.Should().Contain("surface.DeferredNote");
        source.Should().Contain("BuildPrintEvidenceSection(surface.Evidence)");
        source.Should().Contain("BackstageViewTextResources.EvidenceSection");
        source.Should().NotContain("Content = \"\u2190 Back\"");
        source.Should().NotContain("AddNavEntry(panel, BackstagePane.Home, \"Home\")");
        source.Should().NotContain("BuildPaneHeader(\"Export\"");
        source.Should().NotContain("Create PDF/XPS Document");
        source.Should().NotContain("BackstagePaneSurfacePlanner.BuildOpenActionPane(");
        source.Should().NotContain("BackstageViewTextResources.DirectPrintDeferredNote");
    }

    [Fact]
    public void Help_commands_use_the_shared_desktop_external_uri_adapter()
    {
        var source = ReadAvaloniaSource("MainWindow.HelpCommands.cs");
        var planner = ReadPresentationSource(Path.Combine("Shell", "FreeWSupportCommandFeedbackPlanner.cs"));

        source.Should().Contain("DesktopExternalUriLauncher.Open(target)");
        source.Should().Contain("FreeWSupportCommandFeedbackPlanner.PlanExternalUriLaunch(result, title, url)");
        source.Should().Contain("FreeWSupportCommandFeedbackPlanner.PlanDiagnosticsCopy(write)");
        planner.Should().Contain("FreeWApplicationFrameTextCatalog.FormatExternalLinkFailure(title, url)");
        planner.Should().Contain("FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle");
        source.Should().NotContain("ExternalUriLauncher.OpenAsync(target, launchAsync)");
        source.Should().NotContain("launcher.LaunchUriAsync(uri)");
        source.Should().NotContain("AvaloniaExternalUriLauncher.OpenAsync(");
        source.Should().NotContain("$\"FreeW could not open {title}");
    }

    [Fact]
    public void OptionsDialog_ResolvesTabsAndRulesFromPresentationPlanner()
    {
        var source = ReadAvaloniaSource("OptionsDialog.cs");

        source.Should().Contain("new OptionsDialogSession(");
        source.Should().Contain("_surface = _session.Surface");
        source.Should().Contain("_surface.Tabs[index].Header");
        source.Should().Contain("_surface.General.Fields");
        source.Should().Contain("_surface.AutoCorrect.ReplacementColumns");
        source.Should().Contain("_surface.AutoFormat.RuleToggles");
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
