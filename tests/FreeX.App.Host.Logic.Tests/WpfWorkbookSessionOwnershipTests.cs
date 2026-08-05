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
    public void MainWindow_SourceKeepsWorkbookSessionAsTheLifecycleOwner()
    {
        var mainWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.xaml.cs");
        var lifecycle = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.WorkbookLifecycle.cs");
        var backstage = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var multiWindow = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.MultiWindow.cs");

        mainWindow.Should().Contain("private WorkbookSession _session;");
        mainWindow.Should().NotContain("private WorkbookDocumentState _documentState;");
        lifecycle.Should().Contain("private void ReplaceWorkbookSession(StartupWorkbookLoadResult source)");
        lifecycle.Should().Contain("_session.MarkDirtyFromHost();");
        lifecycle.Should().Contain("_session.MarkSavedFromHost();");
        backstage.Should().Contain("ReplaceWorkbookSession(new StartupWorkbookLoadResult(");
        backstage.Should().NotContain("_workbook = plan.Workbook;");
        multiWindow.Should().Contain("_session.CreateSiblingView(");
        multiWindow.Should().NotContain("_documentState");
    }

    private static Workbook GetWorkbookMirror(MainWindow window) =>
        (Workbook)typeof(MainWindow)
            .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
}
