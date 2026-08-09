using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave150PrintPreviewSelectionParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(2, 2, 2, 2, "Selected single")]
    [InlineData(2, 2, 3, 3, "Selected multi")]
    public void SelectionPaginationUsesTheExplicitRangeForSingleAndMultiCellScopes(
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn,
        string expectedText)
    {
        var workbook = new Workbook("Selection preview");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Outside"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Selected single"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Selected multi"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 8));

        var selection = new GridRange(
            new CellAddress(sheet.Id, startRow, startColumn),
            new CellAddress(sheet.Id, endRow, endColumn));

        AvaloniaPrintPreviewPaginationContext.TryCreate(
            workbook,
            sheet,
            new DeterministicTextMeasurer(),
            selection,
            out var context).Should().BeTrue();

        var layout = context.BuildPage(0);
        layout.Should().NotBeNull();
        layout!.Cells.Where(cell => !string.IsNullOrEmpty(cell.Text))
            .Select(cell => cell.Text)
            .Should().Contain(expectedText)
            .And.NotContain("Outside");
    }

    [Fact]
    public async Task PrintSelectionRepaginatesAndReturnsToActiveSheetScope()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedWorkbook(window);
                var sheet = window.Session.ActiveSheet;
                window.Session.SelectRange(new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 3)));

                var preview = await OpenPrintPreviewDialogAsync(window);
                var printWhat = FindComboBox(preview, "PrintPreviewSettingsPrintWhatBox");

                printWhat.SelectedIndex = (int)PrintWhat.Selection;
                await DrainInputAsync();

                CanvasText(preview).Should().Contain("Selected B2").And.NotContain("Outside A1");

                printWhat.SelectedIndex = (int)PrintWhat.ActiveSheets;
                await DrainInputAsync();

                CanvasText(preview).Should().Contain("Selected B2").And.Contain("Outside A1");
            }
            finally
            {
                CloseOwnedWindows(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NestedPageSetupRefreshKeepsSelectionScopeAndRepaginates()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedWorkbook(window);
                var sheet = window.Session.ActiveSheet;
                window.Session.SelectRange(new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 3)));

                var preview = await OpenPrintPreviewDialogAsync(window);
                var printWhat = FindComboBox(preview, "PrintPreviewSettingsPrintWhatBox");
                printWhat.SelectedIndex = (int)PrintWhat.Selection;
                await DrainInputAsync();

                var scaling = FindComboBox(preview, "PrintPreviewSettingsScalingBox");
                scaling.SelectedIndex = PrintPreviewSettingsPanelPlanner.CustomScalingOptionIndex;
                await DrainInputAsync();
                await DrainInputAsync();

                var setup = window.OwnedWindows.Single(window =>
                    AutomationProperties.GetAutomationId(window) == PageSetupDialogPlanner.DialogAutomationId);
                FindComboBox(setup, PageSetupDialogPlanner.OrientationBoxAutomationId).SelectedIndex = 1;
                FindButton(setup, PageSetupDialogPlanner.OkButtonAutomationId).RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await DrainInputAsync();

                sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
                CanvasText(preview).Should().Contain("Selected B2").And.NotContain("Outside A1");
                FindPageCanvas(preview).Width.Should().BeGreaterThan(FindPageCanvas(preview).Height);
            }
            finally
            {
                CloseOwnedWindows(window);
            }

            return true;
        }, CancellationToken.None);
    }

    private static void SeedWorkbook(MainWindow window)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Outside A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Selected B2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Selected C3"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("Outside D4"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 4));
    }

    private static async Task<Window> OpenPrintPreviewDialogAsync(MainWindow window)
    {
        var showMethod = typeof(MainWindow).GetMethod(
            "ShowPrintPreviewDialogAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)showMethod.Invoke(window, [null, null])!;
        await DrainInputAsync();
        await DrainInputAsync();

        window.OwnedWindows.Should().ContainSingle();
        _ = task;
        return window.OwnedWindows.Single();
    }

    private static Canvas FindPageCanvas(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Canvas>()
            .Single(canvas => AutomationProperties.GetAutomationId(canvas) == PrintPreviewDialogPlanner.PageCanvasAutomationId);

    private static ComboBox FindComboBox(Window dialog, string automationId) =>
        dialog.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == automationId);

    private static Button FindButton(Window dialog, string automationId) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == automationId);

    private static IReadOnlyList<string> CanvasText(Window dialog) =>
        FindPageCanvas(dialog).GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? "")
            .ToArray();

    private static void CloseOwnedWindows(MainWindow window)
    {
        foreach (var owned in window.OwnedWindows.ToList())
            owned.Close();

        window.AllowCloseWithoutDirtyPromptForParityCapture();

        window.Close();
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private sealed class DeterministicTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text)
                ? TextSize.Empty
                : new(Math.Max(1, text.Length * 5), Math.Max(1, fontSize));
    }
}
