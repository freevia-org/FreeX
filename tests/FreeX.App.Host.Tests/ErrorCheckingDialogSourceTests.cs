using System.IO;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ErrorCheckingDialogSourceTests
{
    [Fact]
    public void DialogListAndHeaderUseIssueWordingForMixedAuditResults()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("issue(s) found.");
        source.Should().Contain("Header = \"Issue\"");
        source.Should().NotContain("error(s) found.");
        source.Should().NotContain("Header = \"Error\"");
    }

    [Fact]
    public void ErrorCheckingEmptyResultMessageUsesIssueWording()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"));

        source.Should().Contain("No issues found.");
        source.Should().NotContain("No errors found.");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesOptionsCallbackButton()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));
        var formulaSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        dialogSource.Should().Contain("Action? openOptions");
        dialogSource.Should().Contain("Content = \"_Options...\"");
        dialogSource.Should().Contain("_openOptions?.Invoke()");
        formulaSource.Should().Contain("ShowOptionsDialog");
        backstageSource.Should().Contain("private void ShowOptionsDialog()");
    }

    [Fact]
    public void ErrorCheckingDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        foreach (var content in new[]
        {
            "_Go To",
            "_Previous",
            "_Next",
            "_Ignore Error",
            "_Trace Error",
            "_Options...",
            "_Close"
        })
            source.Should().Contain($"Content = \"{content}\"");

        source.Should().Contain("Content = \"_Close\", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true");
    }

    [Fact]
    public void ErrorCheckingDialogOpenedFromKeyboard_FocusesIssueList()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
        source.Should().Contain("NavigateSelected();");
    }

    [Fact]
    public void ErrorCheckingDialog_LabelsIssueListWithAccessKeyAndAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("new Label { Content = \"_Issues:\", Target = _listView");
        source.Should().Contain("AutomationProperties.SetName(_listView, \"Issues\");");
    }

    [Fact]
    public void ErrorCheckingDialog_UsesExcelLikeErrorHelpAndActionStructure()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "ErrorCheckingDialog.cs"));

        source.Should().Contain("Error help");
        source.Should().Contain("Content = \"_Help on this error\"");
        source.Should().Contain("ShowSelectedIssueHelp");
        source.Should().Contain("Content = \"Show _Calculation Steps\"");
        source.Should().Contain("Content = \"_Ignore Error\"");
        source.Should().Contain("Content = \"_Edit in Formula Bar\"");
        source.Should().NotContain("SystemSounds.Asterisk.Play");
    }

    [Fact]
    public void ErrorCheckingDialog_UpdatesCommandDisabledStatesForSelectionBoundaries()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var issues = new[]
            {
                CreateIssue(sheetId, row: 1),
                CreateIssue(sheetId, row: 2)
            };
            var dialog = new ErrorCheckingDialog(issues, _ => { }, _ => true, _ => { });
            dialog.Show();
            try
            {
                var buttons = FindVisualChildren<Button>(dialog)
                    .Where(button => button.Content is string)
                    .GroupBy(button => (string)button.Content)
                    .ToDictionary(group => group.Key, group => group.ToList());
                var list = FindVisualChildren<ListView>(dialog).Single();

                buttons["_Previous"].Single().IsEnabled.Should().BeFalse();
                buttons["_Next"].Single().IsEnabled.Should().BeTrue();
                buttons["_Go To"].Single().IsEnabled.Should().BeTrue();
                buttons["_Ignore Error"].Should().AllSatisfy(button => button.IsEnabled.Should().BeTrue());
                buttons["Show _Calculation Steps"].Single().IsEnabled.Should().BeTrue();
                buttons["_Help on this error"].Single().IsEnabled.Should().BeTrue();

                list.SelectedIndex = 1;

                buttons["_Previous"].Single().IsEnabled.Should().BeTrue();
                buttons["_Next"].Single().IsEnabled.Should().BeFalse();

                list.SelectedIndex = -1;

                buttons["_Previous"].Single().IsEnabled.Should().BeFalse();
                buttons["_Next"].Single().IsEnabled.Should().BeFalse();
                buttons["_Go To"].Single().IsEnabled.Should().BeFalse();
                buttons["_Ignore Error"].Should().AllSatisfy(button => button.IsEnabled.Should().BeFalse());
                buttons["_Trace Error"].Single().IsEnabled.Should().BeFalse();
                buttons["Show _Calculation Steps"].Single().IsEnabled.Should().BeFalse();
                buttons["_Edit in Formula Bar"].Single().IsEnabled.Should().BeFalse();
                buttons["_Help on this error"].Single().IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static FormulaErrorIssue CreateIssue(SheetId sheetId, uint row) =>
        new(
            sheetId,
            "Sheet1",
            new CellAddress(sheetId, row, 1),
            $"A{row}",
            ErrorValue.Value.Code,
            "=A1",
            "Formula uses an incompatible value.");

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
