using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WpfWorkbookSessionOwnershipTests
{
    [Fact]
    public void MainWindow_SelectionAndDocumentState_DelegateToWorkbookSession()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                window.Session.Workbook.Should().BeSameAs(workbook);

                var sheet = workbook.GetSheetAt(0);
                var selectedCell = new CellAddress(sheet.Id, 7, 4);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", selectedCell);

                window.Session.ActiveSheet.Id.Should().Be(sheet.Id);
                window.Session.ActiveCell.Should().Be(selectedCell);
                window.Session.SelectedRange.Should().Be(new GridRange(selectedCell, selectedCell));

                R49MainWindowTestHarness.Invoke(window, "MarkWorkbookDirty");
                window.Session.IsDirty.Should().BeTrue();
                window.Session.DirtyGeneration.Should().Be(1);

                R49MainWindowTestHarness.Invoke(window, "MarkWorkbookSaved");
                window.Session.IsDirty.Should().BeFalse();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CreateNewWorkbook_ReplacesAndDisposesTheAuthoritativeSession()
    {
        StaTestRunner.Run(() =>
        {
            var (window, originalWorkbook) = R49MainWindowTestHarness.CreateWindow();
            var originalSession = window.Session;
            try
            {
                R49MainWindowTestHarness.Invoke(window, "CreateNewWorkbook");

                window.Session.Should().NotBeSameAs(originalSession);
                window.Session.Workbook.Should().NotBeSameAs(originalWorkbook);
                window.DocumentId.Should().Be(window.Session.Workbook.Id);
                window.Session.IsDirty.Should().BeFalse();
                originalSession.Invoking(session => session.CreateSiblingView(100, 100))
                    .Should().Throw<ObjectDisposedException>();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindow_CellCommitAndHistory_ExecuteThroughWorkbookSession()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var address = new CellAddress(sheet.Id, 3, 2);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", address);
                window.FormulaBar.Text = "=1+1";

                R49MainWindowTestHarness.Invoke(window, "CommitEdit").Should().Be(true);

                sheet.GetCell(address)!.Value.Should().Be(new NumberValue(2));
                window.Session.IsDirty.Should().BeTrue();
                window.Session.CanUndo.Should().BeTrue();

                R49MainWindowTestHarness.Invoke(window, "ExecuteUndo").Should().Be(true);
                sheet.GetCell(address).Should().BeNull();
                window.Session.CanRedo.Should().BeTrue();
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(address, address));

                R49MainWindowTestHarness.Invoke(window, "ExecuteRedo").Should().Be(true);
                sheet.GetCell(address)!.Value.Should().Be(new NumberValue(2));
                window.Session.ActiveCell.Should().Be(address);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindow_GenericCommandExecution_UsesSessionRecalcAndPreservesRendererSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var precedent = new CellAddress(sheet.Id, 1, 1);
                var formula = new CellAddress(sheet.Id, 1, 2);
                var selected = new CellAddress(sheet.Id, 4, 3);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", formula);
                window.FormulaBar.Text = "=A1*2";
                R49MainWindowTestHarness.Invoke(window, "CommitEdit").Should().Be(true);
                R49MainWindowTestHarness.Invoke(window, "MarkWorkbookSaved");
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", selected);

                var execute = typeof(MainWindow)
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Single(method =>
                        method.Name == "TryExecuteCommand" &&
                        method.GetParameters().Length == 2);
                execute.Invoke(
                        window,
                        [EditCellsCommand.ForValue(sheet.Id, precedent, new NumberValue(5)), "Edit Cell"])
                    .Should().Be(true);

                sheet.GetCell(formula)!.Value.Should().Be(new NumberValue(10));
                window.Session.IsDirty.Should().BeTrue();
                window.Session.ActiveCell.Should().Be(selected);
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(selected, selected));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void MainWindow_SourceKeepsWorkbookSessionAsTheLifecycleOwner()
    {
        var mainWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var lifecycle = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs");
        var backstage = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var multiWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.MultiWindow.cs");
        var editing = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var commandExecution = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs");
        var app = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "App.xaml.cs");

        mainWindow.Should().Contain("private WorkbookSession _session;");
        mainWindow.Should().Contain("private WorkbookDocumentContext _documentContext;");
        mainWindow.Should().Contain("private Workbook _workbook => _session.Workbook;");
        mainWindow.Should().NotContain("private Workbook _workbook;");
        mainWindow.Should().NotContain("private WorkbookDocumentState _documentState;");
        mainWindow.Should().NotContain("private ICommandBus _commandBus;");
        mainWindow.Should().NotContain("private ICommandStackChangeNotifier? _commandStackChangeNotifier;");
        mainWindow.Should().NotContain("private WorkbookRef _workbookRef;");
        lifecycle.Should().Contain("private void ReplaceWorkbookSession(StartupWorkbookLoadResult source)");
        lifecycle.Should().Contain("_documentContext.CreateHostOwnedSession(");
        lifecycle.Should().Contain("_session.MarkDirtyFromHost();");
        lifecycle.Should().Contain("_session.MarkSavedFromHost();");
        backstage.Should().Contain("ReplaceWorkbookSession(new StartupWorkbookLoadResult(");
        backstage.Should().NotContain("_workbook = plan.Workbook;");
        multiWindow.Should().Contain("_session.CreateSiblingView(");
        multiWindow.Should().Contain("_documentContext.CreateDetached()");
        multiWindow.Should().NotContain("App.CreateWorkbookCommandBus(");
        multiWindow.Should().NotContain("_documentState");
        app.Should().Contain("WorkbookDocumentContext.Create(workbook)");
        app.Should().NotContain("internal static ICommandBus CreateWorkbookCommandBus(");
        app.Should().NotContain("public sealed class WorkbookRef");
        app.Should().NotContain("internal sealed class WorkbookCommandContext");
        editing.Should().Contain("_session.CommitCellText(");
        editing.Should().Contain("_session.CommitCellTextAcrossSelection(");
        editing.Should().NotContain("private bool TryCreateCellFromEntryText(");
        editing.Should().NotContain("RegisterFormulaDependencies");
        commandExecution.Should().Contain("_session.UndoLastEdit()");
        commandExecution.Should().Contain("_session.RedoLastEdit()");
        commandExecution.Should().Contain("_session.RepeatLastAction()");
        commandExecution.Should().Contain("_session.ExecuteCommandPreservingSelection(command)");
        commandExecution.Should().Contain("_session.ExecuteRepeatableCommandPreservingSelection(commandFactory)");
        commandExecution.Should().NotContain("_commandBus.Execute(");
        commandExecution.Should().NotContain("_commandBus.ExecuteRepeatable(");
        commandExecution.Should().NotContain("RefreshLinkedPicturesAffectedBy");
        commandExecution.Should().NotContain("_commandBus.Undo(");
        commandExecution.Should().NotContain("_commandBus.Redo(");
        commandExecution.Should().NotContain("_commandBus.RepeatLast(");
    }

    [Fact]
    public void MainWindowSources_KeepDirectCommandExecutionBehindWorkbookSession()
    {
        var hostDirectory = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host");
        var sources = Directory.GetFiles(hostDirectory, "MainWindow*.cs")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);

        var directBusExecution = sources
            .SelectMany(pair => Regex.Matches(pair.Value, @"_commandBus\.(?:Execute|ExecuteRepeatable)\(")
                .Select(_ => pair.Key))
            .ToList();
        directBusExecution.Should().BeEmpty();
        sources["MainWindow.DataCommands.cs"].Should().Contain(
            "targetSession.ExecuteCommandPreservingSelection(command)");
        sources["MainWindow.CommandExecution.cs"].Should().Contain(
            "_session.ExecuteCustomViewCommand(command)");
        sources["MainWindow.Selection.cs"].Should().NotContain(
            "The WPF workbook mirror diverged from WorkbookSession.");
        sources.Values.Should().OnlyContain(source => !source.Contains("_commandBus.Retire(", StringComparison.Ordinal));
        sources.Values.Should().OnlyContain(source => !source.Contains("GetField(\"_workbook\"", StringComparison.Ordinal));

        var namedRangeDialog = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "NamedRangeDialog.xaml.cs");
        var customViewsDialog = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "CustomViewsDialog.xaml.cs");
        var findReplaceDialog = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs");
        namedRangeDialog.Should().Contain("Func<IWorkbookCommand, CommandOutcome> _executeCommand");
        customViewsDialog.Should().Contain("Func<IWorkbookCommand, CommandOutcome> _executeCommand");
        findReplaceDialog.Should().Contain("Func<IWorkbookCommand, CommandOutcome> executeCommand");
        namedRangeDialog.Should().NotContain("ICommandBus");
        customViewsDialog.Should().NotContain("ICommandBus");
        findReplaceDialog.Should().NotContain("ICommandBus");
        namedRangeDialog.Should().NotContain("_commandBus");
        customViewsDialog.Should().NotContain("_commandBus");
        findReplaceDialog.Should().NotContain("commandBus.Execute");
        sources["MainWindow.WorkbookUiState.cs"].Should().Contain(
            "ExecuteDialogCommandPreservingSelection");
        sources["MainWindow.WorkbookUiState.cs"].Should().Contain(
            "RefreshAfterFindReplaceSessionEdit");

        foreach (var (fileName, source) in sources.Where(pair => pair.Key != "MainWindow.Backstage.cs"))
        {
            source.Should().NotContain(
                "_recalcEngine.Recalculate",
                $"{fileName} should route workbook recalculation through WorkbookSession");
        }

        sources["MainWindow.Backstage.cs"].Should().Contain("_fileWorkflow.OpenAsync(");
        sources["MainWindow.Backstage.cs"].Should().NotContain("new OpenWorkbookLoader(");
        sources["MainWindow.Backstage.cs"].Should().Contain("_recalcEngine.RebuildFormulaDependencies(_workbook)");
    }

}
