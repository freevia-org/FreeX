using System.Reflection;
using FluentAssertions;
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
                window.Session.Workbook.Should().BeSameAs(GetWorkbookMirror(window));
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
    public void MainWindow_SourceKeepsWorkbookSessionAsTheLifecycleOwner()
    {
        var mainWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var lifecycle = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs");
        var backstage = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var multiWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.MultiWindow.cs");
        var editing = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Editing.cs");
        var commandExecution = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs");

        mainWindow.Should().Contain("private WorkbookSession _session;");
        mainWindow.Should().NotContain("private WorkbookDocumentState _documentState;");
        lifecycle.Should().Contain("private void ReplaceWorkbookSession(StartupWorkbookLoadResult source)");
        lifecycle.Should().Contain("_session.MarkDirtyFromHost();");
        lifecycle.Should().Contain("_session.MarkSavedFromHost();");
        backstage.Should().Contain("ReplaceWorkbookSession(new StartupWorkbookLoadResult(");
        backstage.Should().NotContain("_workbook = plan.Workbook;");
        multiWindow.Should().Contain("_session.CreateSiblingView(");
        multiWindow.Should().NotContain("_documentState");
        editing.Should().Contain("_session.CommitCellText(");
        editing.Should().Contain("_session.CommitCellTextAcrossSelection(");
        editing.Should().NotContain("private bool TryCreateCellFromEntryText(");
        editing.Should().NotContain("RegisterFormulaDependencies");
        commandExecution.Should().Contain("_session.UndoLastEdit()");
        commandExecution.Should().Contain("_session.RedoLastEdit()");
        commandExecution.Should().Contain("_session.RepeatLastAction()");
        commandExecution.Should().NotContain("_commandBus.Undo(");
        commandExecution.Should().NotContain("_commandBus.Redo(");
        commandExecution.Should().NotContain("_commandBus.RepeatLast(");
    }

    private static Workbook GetWorkbookMirror(MainWindow window) =>
        (Workbook)typeof(MainWindow)
            .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
}
