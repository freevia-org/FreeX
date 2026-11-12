using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Drives the production Avalonia dialogs end to end for the WPF features they gained for macOS
/// parity: the Data Validation use-selection buttons, the Insert/Delete entire row and column
/// choices, the Page Break dialog, the editable Sort Options first-key order, the Insert Function
/// search and help buttons, and the Insert Hyperlink browse button with its hint line.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class MacOsParityDataHomeInsertFormulaDialogFeatureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task DataValidation_UseSelection_WritesTheSelectedRangeIntoTheFormulaBox()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                var start = new CellAddress(sheet.Id, 2, 2);
                var end = new CellAddress(sheet.Id, 4, 2);
                window.Session.SelectCell(start);
                window.Session.SelectRange(new GridRange(start, end));

                var task = window.ShowDataValidationDialogForTestAsync();
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var formula1Box = Find<TextBox>(dialog, "DataValidationFormula1Box");
                var useSelection = Find<Button>(dialog, "DataValidationUseSelectionButton");
                useSelection.IsVisible.Should().BeTrue();
                dialog.GetVisualDescendants().OfType<Button>()
                    .Count(button => AutomationProperties.GetAutomationId(button) == "DataValidationSourcePickerButton")
                    .Should().Be(1, "the range-picker wiring must reuse the dialog's own picker button");

                useSelection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                formula1Box.Text.Should().Be("=$B$2:$B$4");

                var useSelection2 = Find<Button>(dialog, "DataValidationUseSelection2Button");
                var formula2Box = Find<TextBox>(dialog, "DataValidationFormula2Box");
                useSelection2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                formula2Box.Text.Should().Be("=$B$2:$B$4");

                Find<Button>(dialog, "DataValidationCancelButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    // C3 is selected; a marker in A5 moves with a row edit and a marker in E3 with a column edit, so
    // each case observes the worksheet itself rather than transient status text.
    [Theory]
    [InlineData(false, "CellShiftEntireRowOption", 6u, 1u, "row-marker")]
    [InlineData(false, "CellShiftEntireColumnOption", 3u, 6u, "column-marker")]
    [InlineData(true, "CellShiftEntireRowOption", 4u, 1u, "row-marker")]
    [InlineData(true, "CellShiftEntireColumnOption", 3u, 4u, "column-marker")]
    public async Task InsertDeleteCells_EntireRowAndColumnChoices_RouteToTheWholeBandOperations(
        bool delete,
        string optionAutomationId,
        uint expectedMarkerRow,
        uint expectedMarkerColumn,
        string expectedMarker)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new TextValue("row-marker")));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 5), Cell.FromValue(new TextValue("column-marker")));
                var cell = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(cell);
                window.Session.SelectRange(new GridRange(cell, cell));

                var task = delete
                    ? window.ShowDeleteCellsDialogForTestAsync()
                    : window.ShowInsertCellsDialogForTestAsync();
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var options = dialog.GetVisualDescendants().OfType<RadioButton>().ToArray();
                options.Should().HaveCount(4, "WPF lists both shift directions plus entire row and entire column");
                options[0].IsChecked.Should().BeTrue();

                Find<RadioButton>(dialog, optionAutomationId).IsChecked = true;
                dialog.GetVisualDescendants().OfType<Button>().Single(button => button.IsDefault)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);

                sheet.GetValue(expectedMarkerRow, expectedMarkerColumn)
                    .Should().Be(new TextValue(expectedMarker));
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PageBreakDialog_SeedsTheSelectedRow_EnablesOnlyTheCheckedEntry_AndInsertsTheBreak()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                var cell = new CellAddress(sheet.Id, 12, 4);
                window.Session.SelectCell(cell);
                window.Session.SelectRange(new GridRange(cell, cell));

                var task = window.ShowPageBreakDialogForTestAsync();
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(dialog).Should().Be("PageBreakDialog");

                var rowBox = Find<TextBox>(dialog, "PageBreakRowBreakBox");
                var columnBox = Find<TextBox>(dialog, "PageBreakColumnBreakBox");
                var rowRadio = Find<RadioButton>(dialog, "PageBreakInsertRowButton");
                var columnRadio = Find<RadioButton>(dialog, "PageBreakInsertColumnButton");
                var resetRadio = Find<RadioButton>(dialog, "PageBreakResetAllButton");

                rowRadio.IsChecked.Should().BeTrue();
                rowBox.Text.Should().Be("12");
                rowBox.IsEnabled.Should().BeTrue();
                columnBox.IsEnabled.Should().BeFalse();

                columnRadio.IsChecked = true;
                rowBox.IsEnabled.Should().BeFalse();
                columnBox.IsEnabled.Should().BeTrue();

                resetRadio.IsChecked = true;
                rowBox.IsEnabled.Should().BeFalse();
                columnBox.IsEnabled.Should().BeFalse();

                rowRadio.IsChecked = true;
                rowBox.Text = "7";
                Find<Button>(dialog, "PageBreakOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);

                sheet.RowPageBreaks.Should().Contain(7u);
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SortOptions_FirstKeySortOrder_IsEditableAndRoundTripsATypedCustomList()
    {
        SortDialogOptions? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.ShowSortOptionsDialogForTestAsync(
                    new SortDialogOptions(FirstKeySortOrder: "Low, Medium, High"));
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var firstKeyBox = Find<ComboBox>(dialog, "SortOptionsFirstKeySortOrderBox");
                firstKeyBox.IsEditable.Should().BeTrue();
                firstKeyBox.Text.Should().Be(
                    "Low, Medium, High",
                    "a previously authored custom list must be shown instead of reverting to the normal order");

                firstKeyBox.Text = "Small, Large";
                Find<Button>(dialog, "SortOptionsOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);
                result = await task;
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FirstKeySortOrder.Should().Be("Small, Large");
    }

    // Structural only: the help button opens a second modal box owned by this dialog, and a nested
    // modal is not driven here (the help text itself is covered by FunctionHelpMessages below).
    [Fact]
    public async Task InsertFunction_HasSearchAndHelpButtons()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.ShowInsertFunctionPickerDialogForTestAsync();
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var goButton = Find<Button>(dialog, "InsertFunctionGoButton");
                var helpButton = Find<Button>(dialog, "InsertFunctionHelpButton");
                var okButton = Find<Button>(dialog, "InsertFunctionOkButton");
                goButton.IsVisible.Should().BeTrue();
                helpButton.IsVisible.Should().BeTrue();
                helpButton.Width.Should().Be(146);
                goButton.Width.Should().Be(64);

                // WPF button row: help first, then OK and Cancel.
                var row = (Panel)helpButton.Parent!;
                row.Children.IndexOf(helpButton).Should().BeLessThan(row.Children.IndexOf(okButton));

                Find<Button>(dialog, "InsertFunctionCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void FunctionHelpMessages_FollowTheWpfHelpText()
    {
        MainWindow.BuildInsertFunctionHelpMessage(null)
            .Should().Be(UiText.Get("InsertFunction_SelectFunctionForHelpMessage"));

        var arguments = FunctionArgumentCatalog.GetArgumentSpecs("IF");
        arguments.Should().NotBeEmpty();
        var message = MainWindow.BuildFunctionArgumentsHelpMessage("IF", arguments);
        message.Should().StartWith("IF()");
        foreach (var argument in arguments)
            message.Should().Contain($"{argument.Name}: {argument.Description}");
    }

    [Fact]
    public async Task InsertHyperlink_HasBrowseButtonAndHintLine_AndABrowsedPathBecomesTheAddress()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var task = window.ShowInsertHyperlinkInputDialogForTestAsync();
                await DrainInputAsync();
                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var browse = Find<Button>(dialog, "HyperlinkBrowseButton");
                browse.IsVisible.Should().BeTrue();
                AutomationProperties.GetName(browse).Should().Be(UiText.Get("Hyperlink_BrowseForAFile"));

                var context = Find<ListBox>(dialog, "HyperlinkCurrentFolderContext");
                context.Items.Cast<object?>().Last()
                    .Should().Be(UiText.Get("Hyperlink_ChooseALocalFileForThisHyperlink"));

                var targetBox = Find<TextBox>(dialog, "HyperlinkTargetTextBox");
                MainWindow.ApplyHyperlinkBrowseResult(targetBox, "/tmp/linked-file.txt");
                targetBox.Text.Should().Be("/tmp/linked-file.txt");

                Find<Button>(dialog, "HyperlinkCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await CompleteAsync(task);
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    private static T Find<T>(Window dialog, string automationId)
        where T : Control =>
        dialog.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static void CloseAll(MainWindow window)
    {
        foreach (var owned in window.OwnedWindows.ToArray())
        {
            foreach (var nested in owned.OwnedWindows.ToArray())
            {
                if (nested.IsVisible)
                    nested.Close();
            }

            if (owned.IsVisible)
                owned.Close();
        }

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        if (window.IsVisible)
            window.Close();
    }

    /// <summary>
    /// Waits for a dialog flow to finish, but never indefinitely: a flow that is still open after
    /// the bound fails the test (the caller's finally block then closes whatever is left).
    /// </summary>
    private static async Task CompleteAsync(Task flow)
    {
        await DrainInputAsync();
        var finished = await Task.WhenAny(flow, Task.Delay(5000));
        ReferenceEquals(finished, flow).Should().BeTrue("the dialog flow must complete once its dialog is closed");
        await flow;
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
