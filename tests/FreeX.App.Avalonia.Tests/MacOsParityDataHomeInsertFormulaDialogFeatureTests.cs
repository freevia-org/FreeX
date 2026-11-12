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
                await DrainInputAsync();
                await task;
            }
            finally
            {
                CloseAll(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, "CellShiftEntireRowOption", "Inserted rows")]
    [InlineData(false, "CellShiftEntireColumnOption", "Inserted columns")]
    [InlineData(true, "CellShiftEntireRowOption", "Deleted rows")]
    [InlineData(true, "CellShiftEntireColumnOption", "Deleted columns")]
    public async Task InsertDeleteCells_EntireRowAndColumnChoices_RouteToTheWholeBandOperations(
        bool delete,
        string optionAutomationId,
        string expectedStatus)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
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
                await DrainInputAsync();
                await task;

                window.StatusTextForTest.Text.Should().Be(expectedStatus);
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
                await DrainInputAsync();
                await task;

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
                await DrainInputAsync();
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

    [Fact]
    public async Task InsertFunction_HasSearchAndHelpButtons_AndHelpShowsTheSelectedFunction()
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
                var searchBox = Find<TextBox>(dialog, "InsertFunctionSearchBox");
                var listBox = Find<ListBox>(dialog, "InsertFunctionListBox");

                searchBox.Text = "VLOOKUP";
                goButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                listBox.Items.Cast<object?>().Should().Contain("VLOOKUP");
                listBox.SelectedItem = "VLOOKUP";
                await DrainInputAsync();

                helpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                var help = dialog.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(help).Should().Be("FunctionHelpDialog");
                Find<TextBlock>(help, "FunctionHelpMessageText").Text.Should().StartWith("VLOOKUP()");
                Find<Button>(help, "FunctionHelpOkButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();

                Find<Button>(dialog, "InsertFunctionCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await task;
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
                await DrainInputAsync();
                await task;
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

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
