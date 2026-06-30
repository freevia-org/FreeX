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
    public void FreeXWorkbookOpenSavePickerReadiness_StaysInSharedPlanner()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "WorkbookFileCommandPlanner.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var wpfLifecycleSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "MainWindow.WorkbookLifecycle.cs"));

        plannerSource.Should().Contain("PlanOpenPicker(");
        plannerSource.Should().Contain("PlanSaveAsPicker(");
        plannerSource.Should().Contain("OpenUnavailableMessage");
        plannerSource.Should().Contain("NoSaveFormatsMessage");

        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanOpenPicker(StorageProvider.CanOpen, _session.OpenFormats)");
        avaloniaSource.Should().Contain("WorkbookFileCommandPlanner.PlanSaveAsPicker(");
        avaloniaSource.Should().Contain("StorageProvider.CanSave,");
        avaloniaSource.Should().NotContain("\"Open unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No open formats are available.\"");
        avaloniaSource.Should().NotContain("\"Save As unavailable on this platform.\"");
        avaloniaSource.Should().NotContain("\"No save formats are available.\"");

        wpfLifecycleSource.Should().Contain("WorkbookFileLifecycleCoordinator.SaveResolvedAsync(");
        wpfLifecycleSource.Should().Contain("WorkbookFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(");
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
        avaloniaSource.Should().Contain("FileTypeFilter = AvaloniaFilePickerTypeAdapter.ToFileTypes(pickerPlan.FileTypes)");
        avaloniaSource.Should().NotContain("Patterns = [\"*.csv\", \"*.tsv\", \"*.tab\", \"*.txt\"]");
    }

    [Fact]
    public void SisterAppFileNamePolicy_StaysInSharedWorkflow()
    {
        var workflowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "FileCommandWorkflow.cs"));
        var freewWpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Host",
            "FileCommands.cs"));
        var freewAvaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs"));
        var freepWpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Host",
            "FileCommands.cs"));
        var freepAvaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));

        workflowSource.Should().Contain("CurrentFileName");
        workflowSource.Should().Contain("CurrentFileNameWithoutExtensionOr");
        freewWpfSource.Should().Contain("_workflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileName");
        freewAvaloniaSource.Should().Contain("SisterAppFileTextPlanner.Document");
        freewAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName)");
        freepWpfSource.Should().Contain("_workflow.CurrentFileName");
        freepAvaloniaSource.Should().Contain("_fileWorkflow.CurrentFileName");

        freewWpfSource.Should().NotContain("Path.GetFileName(_workflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileName(_fileWorkflow.CurrentPath)");
        freewAvaloniaSource.Should().NotContain("Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentPath)");
        freepWpfSource.Should().NotContain("SourceFileName(_workflow.CurrentPath)");
        freepAvaloniaSource.Should().NotContain("SourceFileName(_fileWorkflow.CurrentPath)");
    }
}
