using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    [Fact]
    public void DialogList_DoubleClickShowsSelectedView()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Custom views");
            workbook.AddSheet("Sheet1");
            workbook.CustomViews.Add(new WorkbookCustomView(
                "Quarter Close",
                [new WorksheetCustomViewState("Sheet1", WorksheetViewMode.Normal, 0, 0, null, null)]));
            var commandBus = new CapturingCommandBus();
            var dialog = new CustomViewsDialog(
                workbook,
                command => commandBus.Execute(workbook.Id, command));
            var viewsList = (ListView)dialog.FindName("ViewsList");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                viewsList.SelectedIndex = 0;
                var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();
                viewsList.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (!dialog.ViewApplied)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog();

            dialog.ViewApplied.Should().BeTrue();
            commandBus.LastCommand.Should().BeOfType<ApplyCustomViewCommand>();
        });
    }

    [Fact]
    public void DialogList_DoubleClickWithoutSelectionDoesNotShowView()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Custom views");
            workbook.AddSheet("Sheet1");
            var commandBus = new CapturingCommandBus();
            var dialog = new CustomViewsDialog(
                workbook,
                command => commandBus.Execute(workbook.Id, command));
            var viewsList = (ListView)dialog.FindName("ViewsList");

            viewsList.SelectedItem = null;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            viewsList.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.ViewApplied.Should().BeFalse();
            commandBus.LastCommand.Should().BeNull();
        });
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesViewsList()
    {
        var dialogSource = ReadCustomViewsDialogSource();

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("ViewsList.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(ViewsList);");
    }

    [Fact]
    public void DialogCommandFailure_FocusesViewsList()
    {
        var dialogSource = ReadCustomViewsDialogSource();

        dialogSource.Should().Contain("FocusViewsList();");
        dialogSource.Should().Contain("private void FocusViewsList()");
        dialogSource.Split("FocusViewsList();").Length.Should().BeGreaterThanOrEqualTo(5);
        dialogSource.Should().Contain("ViewsList.Focus();");
        dialogSource.Should().Contain("Keyboard.Focus(ViewsList);");
    }

    [Fact]
    public void DialogCommandFailure_UsesOwnedMessageBoxes()
    {
        var dialogSource = ReadCustomViewsDialogSource();

        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"CustomViews_ApplyFailedMessage\"),");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"CustomViews_SaveFailedMessage\"),");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? UiText.Get(\"CustomViews_DeleteFailedMessage\"),");
    }

    [Fact]
    public void DialogSelectionGuards_FocusViewsListWhenNoViewIsSelected()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewsDialog.xaml.cs");

        source.Should().Contain("if (ViewsList.SelectedItem is not CustomViewViewModel vm) { FocusViewsList(); return; }");
    }

    [Fact]
    public void CustomViewsDialog_ThreadsAddViewIncludeOptionsIntoCommandAndIndicators()
    {
        var source = DialogSourceTestSupport.ReadHostSources(
            "CustomViewsDialog.xaml.cs",
            "CustomViewsDialog.Planning.cs");

        source.Should().Contain("dialog.Result.IncludePrintSettings");
        source.Should().Contain("dialog.Result.IncludeHiddenRowsColumnsAndFilterSettings");
        source.Should().Contain("CustomViewsPlanner.BuildSaveCommand(");
        source.Should().Contain("CustomViewsPlanner.BuildDialogRows(");
        source.Should().Contain("UiText.Get(\"CustomViews_Included\")");
        source.Should().Contain("UiText.Get(\"CustomViews_NotIncluded\")");
    }
}

file sealed class CapturingCommandBus : ICommandBus
{
    public IWorkbookCommand? LastCommand { get; private set; }

    public CommandOutcome Execute(WorkbookId workbookId, IWorkbookCommand command)
    {
        LastCommand = command;
        return new CommandOutcome(true);
    }

    public CommandOutcome ExecuteRepeatable(WorkbookId workbookId, Func<IWorkbookCommand> commandFactory) => Execute(workbookId, commandFactory());
    public CommandOutcome Undo(WorkbookId workbookId) => new(false, "Undo is not available.");
    public CommandOutcome Redo(WorkbookId workbookId) => new(false, "Redo is not available.");
    public bool CanUndo(WorkbookId workbookId) => false;
    public bool CanRedo(WorkbookId workbookId) => false;
    public CommandOutcome RepeatLast(WorkbookId workbookId) => new(false, "Repeat is not available.");
    public bool CanRepeat(WorkbookId workbookId) => false;
    public int GetUndoStackDepth(WorkbookId workbookId) => 0;
}
