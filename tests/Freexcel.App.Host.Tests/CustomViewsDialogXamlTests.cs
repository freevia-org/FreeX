using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class CustomViewsDialogXamlTests
{
    [Fact]
    public void DialogList_ExposesAccessibleName()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));

        xaml.Should().Contain("AutomationProperties.Name=\"Custom views\"");
        xaml.Should().Contain("AutomationProperties.HelpText=\"Shows saved workbook views");
    }

    [Fact]
    public void ShowButton_IsDefaultDialogAction()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "ShowButton");

        showButton.Attribute("IsDefault")?.Value.Should().Be("True");
    }

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
            var dialog = new CustomViewsDialog(workbook, commandBus);
            var viewsList = (ListView)dialog.FindName("ViewsList");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                viewsList.SelectedIndex = 0;
                viewsList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                });

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
    public void Dialog_ExposesKeyboardAccessKeys()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GroupBox")
            .Single()
            .Attribute("Header")?.Value.Should().Be("_Views");

        document.Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Show", "_Add...", "_Delete", "_Close"]);
    }

    [Fact]
    public void DialogList_ShowsPrintAndFilterSettingIndicators()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document.Descendants(presentation + "GridViewColumn")
            .Select(element => element.Attribute("Header")?.Value)
            .Should()
            .Contain(["Print settings", "Hidden rows, columns and filter settings"]);
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

        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? \"Could not apply custom view.\",");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? \"Could not save custom view.\",");
        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, outcome.ErrorMessage ?? \"Could not delete custom view.\",");
    }

    [Fact]
    public void DialogSelectionGuards_FocusViewsListWhenNoViewIsSelected()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));

        source.Should().Contain("if (ViewsList.SelectedItem is not CustomViewViewModel vm) { FocusViewsList(); return; }");
    }

    [Fact]
    public void CustomViewNameDialog_ExposesKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewNameDialog.cs"));

        source.Should().Contain("new Label { Content = \"_Name:\"");
        source.Should().Contain("Target = _nameBox");
        source.Should().Contain("Content = \"_Print settings\"");
        source.Should().Contain("Content = \"_Hidden rows, columns and filter settings\"");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void CustomViewNameDialog_CreateResult_TrimsViewName()
    {
        CustomViewNameDialog.CreateResult("  Quarter Close  ", includePrintSettings: false, includeHiddenRowsColumnsAndFilterSettings: true)
            .Should()
            .Be(new CustomViewNameDialogResult("Quarter Close", IncludePrintSettings: false, IncludeHiddenRowsColumnsAndFilterSettings: true));
    }

    [Fact]
    public void CustomViewNameDialogOpenedFromKeyboard_FocusesNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewNameDialog.cs"));
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        dialogSource.Should().Contain("private void FocusInitialKeyboardTarget()");
        dialogSource.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }

    [Fact]
    public void CustomViewNameDialogBlankName_WarnsAndFocusesNameBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewNameDialog.cs"));
        var dialogSource = source[source.IndexOf("public sealed class CustomViewNameDialog", StringComparison.Ordinal)..];

        dialogSource.Should().Contain("DialogMessageHelper.ShowWarning(this, \"Enter a view name.\", Title);");
        dialogSource.Should().Contain("FocusNameInput();");
        dialogSource.Should().Contain("private void FocusNameInput()");
        dialogSource.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }

    [Fact]
    public void CustomViewsDialog_ThreadsAddViewIncludeOptionsIntoCommandAndIndicators()
    {
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.Planning.cs")));

        source.Should().Contain("dialog.Result.IncludePrintSettings");
        source.Should().Contain("dialog.Result.IncludeHiddenRowsColumnsAndFilterSettings");
        source.Should().Contain("new SaveCustomViewCommand(");
        source.Should().Contain("GetIncludedIndicator(view.IncludePrintSettings)");
        source.Should().Contain("GetIncludedIndicator(view.IncludeHiddenRowsColumnsAndFilterSettings)");
    }

    [Fact]
    public void MainWindow_CustomViewsApplyRefreshesViewportStatusAndWorksheetFocus()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.ViewCommands.cs"));
        var methodStart = source.IndexOf("private void CustomViewsBtn_Click(", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var nextMethodStart = source.IndexOf("private void ArrangeAllPickerBtn_Click(", methodStart, StringComparison.Ordinal);
        nextMethodStart.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..nextMethodStart];

        method.Should().Contain("new CustomViewsDialog(_workbook, _commandBus) { Owner = this }");
        method.Should().Contain("dialog.ShowDialog();");
        method.Should().Contain("if (dialog.ViewApplied)");
        method.Should().Contain("UpdateViewport();");
        method.Should().Contain("RefreshStatusBar();");
        method.Should().Contain("FocusSheetGridIfNeeded();");
    }

    private static string ReadCustomViewsDialogSource()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CustomViewsDialog.xaml.cs"));
        var start = source.IndexOf("public sealed partial class CustomViewsDialog", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return source[start..];
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
}
