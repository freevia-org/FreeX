using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class AppDiagnosticsActivitySourceTests
{
    [Fact]
    public void MainWindow_RecordsSafeWorkbookAndExportActivityEvents()
    {
        var mainWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Backstage.cs"));
        var exportSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        mainWindowSource.Should().Contain("IAppDiagnostics? diagnostics = null");
        mainWindowSource.Should().Contain("RecordDiagnosticEvent");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_new\")");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_opened\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_open_failed\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_saved\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_save_failed\"");
        backstageSource.Should().Contain("[\"fileType\"] = FileDialogFilterBuilder.SafeFileTypeFromExtension(ext)");
        exportSource.Should().Contain("RecordDiagnosticEvent(\"export_completed\"");
        exportSource.Should().Contain("RecordDiagnosticEvent(\"export_failed\"");
        exportSource.Should().Contain("[\"fileType\"] = \"pdf\"");
        exportSource.Should().Contain("[\"fileType\"] = \"xps\"");
    }

    [Fact]
    public void MainWindow_RecordsCentralCommandAndDialogUsageEvents()
    {
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.CommandExecution.cs"));
        var editingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Editing.cs"));

        commandSource.Should().Contain("RecordDiagnosticEvent(\"command_invoked\"");
        commandSource.Should().Contain("[\"command\"] = title");
        commandSource.Should().Contain("[\"status\"] = outcome.Success ? \"succeeded\" : \"failed\"");
        editingSource.Should().Contain("RecordDiagnosticEvent(\"dialog_opened\"");
        editingSource.Should().Contain("[\"dialog\"] = dialog.GetType().Name");
    }
}
