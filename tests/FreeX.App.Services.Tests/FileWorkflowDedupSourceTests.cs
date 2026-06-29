using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FileWorkflowDedupSourceTests
{
    [Fact]
    public void RecentFileRegistrationDecision_StaysInSharedService()
    {
        var serviceSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "RecentFileRegistrationService.cs"));
        var sessionSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "FileCommandSession.cs"));
        var wpfWorkbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.Backstage.cs"));
        var avaloniaWorkbookSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var freewSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Host",
            "FileCommands.cs"));
        var freepSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));

        serviceSource.Should().Contain("FileLifecyclePlanner.PlanRecentRegistration(");
        sessionSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        wpfWorkbookSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        avaloniaWorkbookSource.Should().Contain("RecentFileRegistrationService.RegisterIfNeeded(");
        avaloniaWorkbookSource.Should().Contain("FileAccessIdentity: fileAccessIdentity ?? target.FileAccessIdentity");

        sessionSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        wpfWorkbookSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        avaloniaWorkbookSource.Should().NotContain("FileLifecyclePlanner.PlanRecentRegistration(");
        wpfWorkbookSource.Should().NotContain("_recentFiles.AddOrUpdate(");
        avaloniaWorkbookSource.Should().NotContain("_recentFiles.AddOrUpdate(");
        freewSource.Should().Contain("FileCommandWorkflow");
        freepSource.Should().Contain("FileCommandWorkflow");
    }

    [Fact]
    public void AvaloniaPortablePdfExportTargetDecision_StaysInExportPickerPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "ExportFilePickerPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var printSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.Print.cs"));

        plannerSource.Should().Contain("BuildPortablePdfSaveTargetPlan(");
        plannerSource.Should().Contain("ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf)");
        plannerSource.Should().Contain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, pathPlan, pathExists)");

        avaloniaSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists)");
        printSource.Should().Contain("ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists)");
        avaloniaSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)");
        printSource.Should().NotContain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)");
    }

    [Fact]
    public void GetDataPickerPolicy_StaysInSharedPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "ImportDataFilePickerPlanner.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.DataCommands.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.GetData.cs"));

        plannerSource.Should().Contain("AdapterImportExtensions");
        plannerSource.Should().Contain("\".csv\",");
        plannerSource.Should().Contain("\".xml\"");
        plannerSource.Should().Contain("TextImportPatterns");
        plannerSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(importAdapters)");

        wpfSource.Should().Contain("ImportDataFilePickerPlanner.BuildAdapterOpenDialogPlan(_fileAdapters)");
        wpfSource.Should().Contain("var adapters = plan.Adapters;");
        wpfSource.Should().Contain("checkFileExists: plan.CheckFileExists");
        wpfSource.Should().Contain("multiselect: plan.Multiselect");
        wpfSource.Should().NotContain("string[] dataExtensions");
        wpfSource.Should().NotContain("FileDialogFilterBuilder.BuildOpenFilter(adapters)");

        avaloniaSource.Should().Contain("ImportDataFilePickerPlanner.BuildTextOpenPickerPlan(UiText.Get(\"GetData_FileTypeName\"))");
        avaloniaSource.Should().Contain("FileTypeFilter = CreateFilePickerFileTypes(pickerPlan.FileTypes)");
        avaloniaSource.Should().NotContain("Patterns = [\"*.csv\", \"*.tsv\", \"*.tab\", \"*.txt\"]");
    }
}
