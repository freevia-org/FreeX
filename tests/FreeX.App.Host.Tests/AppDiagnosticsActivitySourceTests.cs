using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class AppDiagnosticsActivitySourceTests
{
    [Fact]
    public void MainWindow_RecordsSafeWorkbookAndExportActivityEvents()
    {
        var mainWindowSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        var exportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PrintExport.cs");

        mainWindowSource.Should().Contain("IAppDiagnostics? diagnostics = null");
        mainWindowSource.Should().Contain("RecordDiagnosticEvent");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_new\")");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_opened\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_open_failed\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_saved\"");
        backstageSource.Should().Contain("RecordDiagnosticEvent(\"workbook_save_failed\"");
        backstageSource.Should().Contain("[\"fileType\"] = FileFormatResolver.SafeFileTypeFromExtension(ext)");
        dataSource.Should().Contain("RecordDiagnosticEvent(\"import_completed\"");
        dataSource.Should().Contain("RecordDiagnosticEvent(\"import_failed\"");
        dataSource.Should().Contain("BuildImportDiagnosticProperties(ext");
        exportSource.Should().Contain("RecordDiagnosticEvent(\"export_completed\"");
        exportSource.Should().Contain("RecordDiagnosticEvent(\"export_failed\"");
        exportSource.Should().Contain("[\"fileType\"] = \"pdf\"");
        exportSource.Should().Contain("[\"fileType\"] = \"xps\"");
    }

    [Fact]
    public void MainWindow_RecordsCentralCommandAndDialogUsageEvents()
    {
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CommandExecution.cs");
        var editingSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        commandSource.Should().Contain("RecordDiagnosticEvent(\"command_invoked\"");
        commandSource.Should().Contain("[\"command\"] = title");
        commandSource.Should().Contain("[\"status\"] = outcome.Success ? \"succeeded\" : \"failed\"");
        editingSource.Should().Contain("RecordDiagnosticEvent(\"dialog_opened\"");
        editingSource.Should().Contain("[\"dialog\"] = dialog.GetType().Name");
    }

    [Fact]
    public void MainWindow_RecordsManualUpdateCheckUsageEvent()
    {
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        reviewSource.Should().Contain("private async void CheckForUpdatesBtn_Click(");
        reviewSource.Should().Contain("RecordDiagnosticEvent(\"update_check_opened\"");
        reviewSource.Should().Contain("[\"source\"] = \"help\"");
        reviewSource.Should().Contain("OpenExternalHelpLink(updates.ReleasesPageUrl, UiText.Get(\"MainWindowMessage_CheckForUpdatesTitle\"))");

        // The ribbon "Check for Updates" command is owned by the declarative model and routes through
        // the generated typed handler binding.
        var ribbonDefinition = DialogSourceTestSupport.ReadRibbonDefinitionSource("FreeXRibbonDefinition.cs");
        ribbonDefinition.Should().Contain("FreeXRibbonCommandIds.HelpCheckForUpdates");
    }
}
