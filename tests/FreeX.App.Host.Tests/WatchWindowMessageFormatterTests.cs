using FluentAssertions;
using FreeX.App.Services;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed class WatchWindowDialogTests
{
    [Fact]
    public void WatchWindowDialog_ExposesKeyboardAccessKeysForCommandButtons()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("Content = UiText.Get(\"WatchWindow_AddWatch\")");
        source.Should().Contain("IsEnabled = _addWatch is not null");
        source.Should().Contain("AutomationProperties.SetAutomationId(add, \"WatchWindowAddButton\");");
        source.Should().Contain("AddWatchDialog");
        source.Should().Contain("Content = UiText.Get(\"WatchWindow_Refresh\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(refresh, \"WatchWindowRefreshButton\");");
        source.Should().Contain("Content = UiText.Get(\"WatchWindow_DeleteWatch\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(_deleteButton, \"WatchWindowDeleteButton\");");
        source.Should().Contain("Content = UiText.Get(\"WatchWindow_Close\")");
        source.Should().Contain("AutomationProperties.SetAutomationId(close, \"WatchWindowCloseButton\");");
        source.Should().Contain("Content = UiText.Get(\"WatchWindow_Close\"), Width = 80, Height = 26, Margin = new Thickness(4, 0, 0, 0), IsCancel = true");
    }

    [Fact]
    public void WatchWindowDialog_DeleteKeyAndSelectionStateMirrorDeleteWatchButton()
    {
        var source = ReadWatchWindowSource();

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
        var dialogSource = ReadWatchWindowSource();
        var mainWindowSource = ReadMainWindowFormulaCommandsSource();

        dialogSource.Should().Contain("Action? addWatch");
        dialogSource.Should().Contain("Func<string>? getSelectionText");
        mainWindowSource.Should().Contain("AddWatchFromSelection(showMessage: false)");
        mainWindowSource.Should().Contain("AddWatchFromSelection(showMessage: true)");
        mainWindowSource.Should().Contain("FormatRangeReference(range.Start, range.End)");
    }

    [Fact]
    public void AddWatchDialog_ExposesSelectedRangePreview()
    {
        var watchWindowSource = ReadWatchWindowSource();
        var source = ReadAddWatchSource();

        watchWindowSource.Should().NotContain("public sealed class AddWatchDialog");
        source.Should().Contain("public sealed class AddWatchDialog");
        source.Should().Contain("Title = UiText.Get(AddWatchDialogPlanner.TitleKey)");
        source.Should().Contain("Content = UiText.Get(AddWatchDialogPlanner.SelectedRangeLabelKey)");
        source.Should().Contain("Target = _rangeBox");
        source.Should().Contain("Content = UiText.Get(AddWatchDialogPlanner.AddButtonKey)");
        source.Should().Contain("Width = AddWatchDialogPlanner.ButtonWidth");
        source.Should().Contain("Width = AddWatchDialogPlanner.Width");
        source.Should().Contain("Height = AddWatchDialogPlanner.Height");
        source.Should().Contain("new Thickness(AddWatchDialogPlanner.RootMargin)");
        UiText.Get("AddWatch_Title").Should().Be("Add Watch");
        UiText.Get("AddWatch_SelectedRangeLabel").Should().Be("Selected _range:");
    }

    [Fact]
    public void AddWatchDialog_SelectedRangePreviewExposesAutomationName()
    {
        var source = ReadAddWatchSource();

        source.Should().Contain("AutomationProperties.SetName(_rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeAutomationNameKey));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_rangeBox, AddWatchDialogPlanner.SelectedRangeAutomationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(_rangeBox, UiText.Get(AddWatchDialogPlanner.SelectedRangeHelpTextKey));");
        UiText.Get("AddWatch_SelectedRangeAutomationName").Should().Be("Selected range");
    }

    [Fact]
    public void AddWatchDialog_CommandButtonsExposeExcelStyleAutomationMetadata()
    {
        var source = ReadAddWatchSource();

        source.Should().Contain("AutomationProperties.SetName(add, UiText.Get(AddWatchDialogPlanner.AddAutomationNameKey));");
        source.Should().Contain("AutomationProperties.SetAutomationId(add, AddWatchDialogPlanner.AddButtonAutomationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(add, UiText.Get(AddWatchDialogPlanner.AddHelpTextKey));");
        source.Should().Contain("Content = UiText.Get(AddWatchDialogPlanner.CancelButtonKey)");
        source.Should().Contain("new Thickness(0, AddWatchDialogPlanner.ActionRowTopMargin, 0, 0)");
        source.Should().Contain("AutomationProperties.SetName(cancel, UiText.Get(AddWatchDialogPlanner.CancelAutomationNameKey));");
        source.Should().Contain("AutomationProperties.SetAutomationId(cancel, AddWatchDialogPlanner.CancelButtonAutomationId);");
        source.Should().Contain("AutomationProperties.SetHelpText(cancel, UiText.Get(AddWatchDialogPlanner.CancelHelpTextKey));");
    }

    [Fact]
    public void AddWatchDialog_UsesPlannerForSharedWpfGeometryAndMetadata()
    {
        var source = ReadAddWatchSource();

        source.Should().NotContain("Width = 360");
        source.Should().NotContain("Height = 170");
        source.Should().NotContain("new Thickness(12)");
        source.Should().NotContain("Width = 76");
        source.Should().NotContain("\"AddWatchAddButton\"");
        source.Should().NotContain("\"AddWatchCancelButton\"");
        source.Should().Contain("AddWatchDialogPlanner.RangeBottomMargin");
        source.Should().Contain("AddWatchDialogPlanner.ActionRowTopMargin");
        UiText.Get("AddWatch_AddAutomationName").Should().Be("Add");
    }

    [Fact]
    public void AddWatchDialog_RuntimeControlsExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AddWatchDialog("Sheet1!$A$1:$B$2");
            try
            {
                var rangeBox = WpfTestTree.FindLogicalDescendants<TextBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "AddWatchSelectedRangeBox");
                var buttons = WpfTestTree.FindLogicalDescendants<Button>(dialog)
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
        var source = ReadAddWatchSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_rangeBox);");
    }

    [Fact]
    public void WatchWindowDialog_ExposesExcelLikeWatchColumns()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("Width = WatchWindowDialogPlanner.Width;");
        source.Should().Contain("MinWidth = WatchWindowDialogPlanner.MinWidth;");
        source.Should().Contain("Width = WatchWindowDialogPlanner.BookColumnWidth");
        source.Should().Contain("Width = WatchWindowDialogPlanner.FormulaColumnWidth");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Book\")");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Sheet\")");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Name\")");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Cell\")");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Value\")");
        source.Should().Contain("Header = UiText.Get(\"WatchWindow_Formula\")");
    }

    [Fact]
    public void WatchWindowDialog_BindsSharedRowPlans()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("ObservableCollection<WatchWindowRowPlan>");
        source.Should().Contain("WatchWindowDialogPlanner.CreateRows(");
        source.Should().Contain("nameof(WatchWindowRowPlan.Book)");
        source.Should().Contain("nameof(WatchWindowRowPlan.Formula)");
        source.Should().NotContain("record WatchWindowRow(");
    }

    [Fact]
    public void WatchWindowDialog_InitialSizeFitsAllWatchColumns()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new WatchWindowDialog(
                () => [],
                addWatch: null,
                getSelectionText: () => "Sheet1!A1",
                navigateTo: _ => { },
                removeWatch: _ => { });
            try
            {
                var list = WpfTestTree.FindLogicalDescendants<ListView>(dialog)
                    .Single(listView => AutomationProperties.GetAutomationId(listView) == "WatchWindowList");
                var grid = (GridView)list.View;
                var columnWidth = grid.Columns.Sum(column => column.Width);

                dialog.Width.Should().BeGreaterThan(columnWidth);
                dialog.MinWidth.Should().BeGreaterThan(columnWidth);
                (dialog.Width - columnWidth).Should().BeGreaterThanOrEqualTo(100,
                    "initial chrome and padding should leave the last Formula column fully visible");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void WatchWindowDialogOpenedFromKeyboard_FocusesWatchList()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_listView.SelectedIndex = 0;");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
    }

    [Fact]
    public void WatchWindowDialog_LabelsWatchListWithAccessKeyAndAutomationName()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("new Label { Content = UiText.Get(\"WatchWindow_Watches\"), Target = _listView");
        source.Should().Contain("AutomationProperties.SetName(_listView, UiText.Get(\"WatchWindow_Watches2\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_listView, \"WatchWindowList\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_listView, UiText.Get(\"WatchWindow_ListsWatchedCellsWithTheirWorkbookSheetAddressValueAndFormula\"));");
    }

    [Fact]
    public void WatchWindowDialog_RefreshPreservesSelectedWatchRows()
    {
        var source = ReadWatchWindowSource();

        source.Should().Contain("_listView.SelectedItems");
        source.Should().Contain(".Select(row => row.Address)");
        source.Should().Contain("RestoreSelection(selectedAddresses);");
        source.Should().Contain("private void RestoreSelection(IReadOnlySet<CellAddress> selectedAddresses)");
        source.Should().Contain("_listView.SelectedItems.Add(row);");
    }

    [Fact]
    public void WatchWindowDialog_DoubleClickNavigateHandlesMouseEvent()
    {
        var source = ReadWatchWindowSource();
        var doubleClick = source[
            source.IndexOf("private void ListView_MouseDoubleClick", StringComparison.Ordinal)..
            source.IndexOf("private void ListView_KeyDown", StringComparison.Ordinal)];

        doubleClick.Should().Contain("_navigateTo(row.Address);");
        doubleClick.Should().Contain("e.Handled = true;");
        doubleClick.IndexOf("_navigateTo(row.Address);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(doubleClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    private static string ReadWatchWindowSource() =>
        DialogSourceTestSupport.ReadHostSources("WatchWindowDialog.cs");

    private static string ReadAddWatchSource() =>
        DialogSourceTestSupport.ReadHostSources("AddWatchDialog.cs");

    private static string ReadMainWindowFormulaCommandsSource() =>
        DialogSourceTestSupport.ReadHostSources("MainWindow.FormulaCommands.cs");
}
