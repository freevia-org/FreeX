using FluentAssertions;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed class WatchWindowMessageFormatterTests
{
    [Theory]
    [InlineData(1, "A1", "1 cell added to Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells added to Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is already watched.")]
    public void FormatAddResult_HandlesSingularPluralAndNoOp(int added, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatAddResult(added, rangeText).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "A1", "1 cell removed from Watch Window.")]
    [InlineData(2, "A1:B1", "2 cells removed from Watch Window.")]
    [InlineData(0, "A1:B1", "A1:B1 is not watched.")]
    public void FormatRemoveResult_HandlesSingularPluralAndNoOp(int removed, string rangeText, string expected)
    {
        WatchWindowMessageFormatter.FormatRemoveResult(removed, rangeText).Should().Be(expected);
    }

    [Fact]
    public void WatchWindowDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("Content = \"_Add Watch\"");
        source.Should().Contain("IsEnabled = _addWatch is not null");
        source.Should().Contain("AutomationProperties.SetAutomationId(add, \"WatchWindowAddButton\");");
        source.Should().Contain("AddWatchDialog");
        source.Should().Contain("Content = \"_Refresh\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(refresh, \"WatchWindowRefreshButton\");");
        source.Should().Contain("Content = \"_Delete Watch\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(_deleteButton, \"WatchWindowDeleteButton\");");
        source.Should().Contain("Content = \"_Close\"");
        source.Should().Contain("AutomationProperties.SetAutomationId(close, \"WatchWindowCloseButton\");");
        source.Should().Contain("Content = \"_Close\", Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true");
    }

    [Fact]
    public void WatchWindowDialog_DeleteKeyAndSelectionStateMirrorDeleteWatchButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("private readonly Button _deleteButton");
        source.Should().Contain("_listView.SelectionChanged += (_, _) => UpdateDeleteButtonState();");
        source.Should().Contain("_listView.KeyDown += ListView_KeyDown;");
        source.Should().Contain("private void ListView_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key == Key.Delete)");
        source.Should().Contain("DeleteSelectedWatch();");
        source.Should().Contain("private void UpdateDeleteButtonState()");
        source.Should().Contain("_deleteButton.IsEnabled = _listView.SelectedItems.Count > 0;");
    }

    [Fact]
    public void WatchWindowDialog_WiresAddWatchToCurrentSelectionWorkflow()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));
        var mainWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs"));

        dialogSource.Should().Contain("Action? addWatch");
        dialogSource.Should().Contain("Func<string>? getSelectionText");
        mainWindowSource.Should().Contain("AddWatchFromSelection(showMessage: false)");
        mainWindowSource.Should().Contain("AddWatchFromSelection(showMessage: true)");
        mainWindowSource.Should().Contain("FormatRangeReference(range.Start, range.End)");
    }

    [Fact]
    public void AddWatchDialog_ExposesSelectedRangePreview()
    {
        var watchWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AddWatchDialog.cs"));

        watchWindowSource.Should().NotContain("public sealed class AddWatchDialog");
        source.Should().Contain("public sealed class AddWatchDialog");
        source.Should().Contain("Title = \"Add Watch\"");
        source.Should().Contain("Content = \"Selected _range:\"");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("Content = \"_Add\"");
    }

    [Fact]
    public void AddWatchDialog_SelectedRangePreviewExposesAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AddWatchDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, \"Selected range\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rangeBox, \"AddWatchSelectedRangeBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, \"Shows the selected worksheet cells that will be watched.\");");
    }

    [Fact]
    public void AddWatchDialog_CommandButtonsExposeExcelStyleAutomationMetadata()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AddWatchDialog.cs"));

        source.Should().Contain("AutomationProperties.SetName(add, \"Add\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(add, \"AddWatchAddButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(add, \"Add the selected cells to the Watch Window.\");");
        source.Should().Contain("var cancel = new Button { Content = \"_Cancel\", Width = 76, IsCancel = true };");
        source.Should().Contain("AutomationProperties.SetName(cancel, \"Cancel\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancel, \"AddWatchCancelButton\");");
        source.Should().Contain("AutomationProperties.SetHelpText(cancel, \"Close the Add Watch dialog without adding cells.\");");
    }

    [Fact]
    public void AddWatchDialog_RuntimeControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AddWatchDialog("Sheet1!$A$1:$B$2");
            try
            {
                var rangeBox = FindLogicalDescendants<TextBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "AddWatchSelectedRangeBox");
                var buttons = FindLogicalDescendants<Button>(dialog)
                    .ToDictionary(button => AutomationProperties.GetAutomationId(button));

                rangeBox.Text.Should().Be("Sheet1!$A$1:$B$2");
                AutomationProperties.GetName(rangeBox).Should().Be("Selected range");
                AutomationProperties.GetHelpText(rangeBox).Should().Be("Shows the selected worksheet cells that will be watched.");

                buttons["AddWatchAddButton"].IsDefault.Should().BeTrue();
                AutomationProperties.GetName(buttons["AddWatchAddButton"]).Should().Be("Add");
                AutomationProperties.GetHelpText(buttons["AddWatchAddButton"]).Should().Be("Add the selected cells to the Watch Window.");

                buttons["AddWatchCancelButton"].IsCancel.Should().BeTrue();
                AutomationProperties.GetName(buttons["AddWatchCancelButton"]).Should().Be("Cancel");
                AutomationProperties.GetHelpText(buttons["AddWatchCancelButton"]).Should().Be("Close the Add Watch dialog without adding cells.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AddWatchDialogOpenedFromKeyboard_FocusesSelectedRangePreview()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AddWatchDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void WatchWindowDialog_ExposesExcelLikeWatchColumns()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("Header = \"Book\"");
        source.Should().Contain("Header = \"Sheet\"");
        source.Should().Contain("Header = \"Name\"");
        source.Should().Contain("Header = \"Cell\"");
        source.Should().Contain("Header = \"Value\"");
        source.Should().Contain("Header = \"Formula\"");
    }

    [Fact]
    public void WatchWindowDialogOpenedFromKeyboard_FocusesWatchList()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_listView.SelectedIndex = 0;");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
    }

    [Fact]
    public void WatchWindowDialog_LabelsWatchListWithAccessKeyAndAutomationName()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("new Label { Content = \"_Watches:\", Target = _listView");
        source.Should().Contain("AutomationProperties.SetName(_listView, \"Watches\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(_listView, \"WatchWindowList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_listView, \"Lists watched cells with their workbook, sheet, address, value, and formula.\");");
    }

    [Fact]
    public void WatchWindowDialog_RefreshPreservesSelectedWatchRows()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "WatchWindowDialog.cs"));

        source.Should().Contain("_listView.SelectedItems");
        source.Should().Contain(".Select(row => row.Address)");
        source.Should().Contain("RestoreSelection(selectedAddresses);");
        source.Should().Contain("private void RestoreSelection(IReadOnlySet<CellAddress> selectedAddresses)");
        source.Should().Contain("_listView.SelectedItems.Add(row);");
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }
}
