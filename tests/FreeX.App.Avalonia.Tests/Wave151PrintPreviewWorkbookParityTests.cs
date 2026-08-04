using System.Reflection;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave151PrintPreviewWorkbookParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void EmptyAdapterHasZeroPagesAndBuildsNoPage()
    {
        var context = AvaloniaPrintPreviewPaginationContext.Empty();

        context.PageCount.Should().Be(0);
        context.BuildPage(0).Should().BeNull();
        context.BuildPainting(0).Should().BeNull();
        PrintPreviewNavigationState.Create(1, context.PageCount).StatusText.Should().Be("Page 1 of 1");
    }

    [Fact]
    public async Task EntireWorkbookOptionIsEnabledAndNextNavigatesToAnotherSheet()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var first = window.Session.ActiveSheet;
                first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("First sheet"));
                first.PrintArea = new GridRange(
                    new CellAddress(first.Id, 1, 1),
                    new CellAddress(first.Id, 1, 1));

                var second = window.Session.Workbook.AddSheet("Second");
                second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("Second sheet"));
                second.PrintArea = new GridRange(
                    new CellAddress(second.Id, 1, 1),
                    new CellAddress(second.Id, 1, 1));

                var preview = await OpenPrintPreviewDialogAsync(window);
                var printWhat = FindComboBox(preview, "PrintPreviewSettingsPrintWhatBox");
                printWhat.Items[1].Should().BeOfType<ComboBoxItem>()
                    .Which.IsEnabled.Should().BeTrue();

                printWhat.SelectedIndex = 1;
                await DrainInputAsync();
                CanvasText(preview).Should().Contain("First sheet");

                FindButton(preview, PrintPreviewDialogPlanner.NextPageButtonAutomationId).RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                CanvasText(preview).Should().Contain("Second sheet");

                printWhat.SelectedIndex = 0;
                await DrainInputAsync();
                CanvasText(preview).Should().Contain("First sheet");
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToList())
                    owned.Close();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task<Window> OpenPrintPreviewDialogAsync(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "ShowPrintPreviewDialogAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = (Task)method.Invoke(window, [null, null])!;
        await DrainInputAsync();
        await DrainInputAsync();
        return window.OwnedWindows.Single();
    }

    private static ComboBox FindComboBox(Window dialog, string automationId) =>
        dialog.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == automationId);

    private static Button FindButton(Window dialog, string automationId) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == automationId);

    private static IReadOnlyList<string> CanvasText(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Canvas>()
            .Single(canvas => AutomationProperties.GetAutomationId(canvas) == PrintPreviewDialogPlanner.PageCanvasAutomationId)
            .GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? "")
            .ToArray();

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
