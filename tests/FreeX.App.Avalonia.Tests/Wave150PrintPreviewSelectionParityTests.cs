using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.App.Services;
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
    public async Task ToolbarStartsAsWpfsSingleIconRowAndTheChevronRevealsPageNavigationAndZoom()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var preview = await OpenPrintPreviewDialogAsync(window);

                // WPF default: one icon row only (print, copy, zoom in/out, actual size, page width,
                // whole page); page navigation, page box, zoom combo, Margins and Page Setup are
                // not shown until the overflow chevron is toggled.
                var toggle = PrintPreviewToolbarTestSupport.FindOverflowToggle(preview);
                toggle.IsChecked.Should().NotBe(true);
                FindButton(preview, "PrintPreviewZoomInButton").IsEffectivelyVisible.Should().BeTrue();
                FindButton(preview, "PrintPreviewFitWidthButton").IsEffectivelyVisible.Should().BeTrue();
                preview.GetVisualDescendants().OfType<ComboBox>().Where(combo =>
                        AutomationProperties.GetAutomationId(combo) == PrintPreviewDialogPlanner.ZoomBoxAutomationId)
                    .Should().BeEmpty();
                preview.GetVisualDescendants().OfType<Button>().Where(button =>
                        AutomationProperties.GetAutomationId(button) == PrintPreviewDialogPlanner.NextPageButtonAutomationId)
                    .Should().BeEmpty();

                await PrintPreviewToolbarTestSupport.ExpandPageRowAsync(preview);
                FindComboBox(preview, PrintPreviewDialogPlanner.ZoomBoxAutomationId).IsEffectivelyVisible.Should().BeTrue();
                FindButton(preview, PrintPreviewDialogPlanner.NextPageButtonAutomationId).IsEffectivelyVisible.Should().BeTrue();
            }
            finally
            {
                CloseOwnedWindows(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BlankWorksheetPreviewsAndContributesToEntireWorkbookPageCount()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.ReplaceSession(new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: true));
                var blankSheet = window.Session.ActiveSheet;
                blankSheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
                var dataSheet = window.Session.Workbook.AddSheet("Data");
                dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Data sheet"));
                dataSheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");

                var preview = await OpenPrintPreviewDialogAsync(window);
                await PrintPreviewToolbarTestSupport.ExpandPageRowAsync(preview);
                CanvasText(preview).Should().Contain("Page 1 of 1");

                FindComboBox(preview, "PrintPreviewSettingsPrintWhatBox").SelectedIndex = (int)PrintWhat.EntireWorkbook;
                await DrainInputAsync();
                CanvasText(preview).Should().Contain("Page 1 of 2");

                var lastPage = FindButton(preview, PrintPreviewDialogPlanner.LastPageButtonAutomationId);
                lastPage.IsEffectivelyVisible.Should().BeTrue();
                lastPage.RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                CanvasText(preview).Should().Contain("Data sheet").And.Contain("Page 2 of 2");
            }
            finally
            {
                CloseOwnedWindows(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VisibleDocumentZoomControlsChangeThePageScale()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var preview = await OpenPrintPreviewDialogAsync(window);
                await PrintPreviewToolbarTestSupport.ExpandPageRowAsync(preview);
                var zoom = FindComboBox(preview, PrintPreviewDialogPlanner.ZoomBoxAutomationId);
                zoom.IsEffectivelyVisible.Should().BeTrue();
                double PageScale() => FindPageCanvas(preview).TransformToVisual(preview)!.Value.M11;

                zoom.SelectedIndex = 0;
                await DrainInputAsync();
                PageScale().Should().Be(0.5);

                FindButton(preview, "PrintPreviewZoomInButton").RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                PageScale().Should().Be(0.75);

                FindButton(preview, "PrintPreviewFitWidthButton").RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                var fitWidthScale = PageScale();
                fitWidthScale.Should().BeGreaterThan(0.1);

                FindButton(preview, "PrintPreviewFitPageButton").RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                PageScale().Should().BeLessThan(fitWidthScale);
            }
            finally
            {
                CloseOwnedWindows(window);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CommentsAtEndRemainNavigableAndFollowTheSelectedScope()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedWorkbook(window);
                var sheet = window.Session.ActiveSheet;
                var selected = new CellAddress(sheet.Id, 2, 2);
                sheet.PrintComments = WorksheetPrintComments.AtEnd;
                sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "Outside selection";
                sheet.Comments[selected] = "Selected note";
                sheet.Comments[new CellAddress(sheet.Id, 8, 8)] = "Outside print area";
                window.Session.SelectRange(new GridRange(selected, selected));

                var preview = await OpenPrintPreviewDialogAsync(window);
                await PrintPreviewToolbarTestSupport.ExpandPageRowAsync(preview);
                FindButton(preview, PrintPreviewDialogPlanner.LastPageButtonAutomationId).RaiseEvent(
                    new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();

                CanvasText(preview).Should().Contain("Comments")
                    .And.Contain("A1: Outside selection")
                    .And.Contain("B2: Selected note")
                    .And.NotContain("H8: Outside print area");

                var printWhat = FindComboBox(preview, "PrintPreviewSettingsPrintWhatBox");
                printWhat.SelectedIndex = (int)PrintWhat.Selection;
                await DrainInputAsync();

                CanvasText(preview).Should().Contain("Comments")
                    .And.Contain("B2: Selected note")
                    .And.NotContain("A1: Outside selection");
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
                setup.GetVisualDescendants().OfType<RadioButton>()
                    .Single(radio => AutomationProperties.GetAutomationId(radio) == PageSetupDialogPlanner.LandscapeRadioAutomationId)
                    .IsChecked = true;
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
        var task = (Task)showMethod.Invoke(window, [null, null, null])!;
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
