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

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF FormatCellsDialog._lastSelectedTab parity: a default (Number) open reuses the tab last
/// selected in any Format Cells dialog; explicit non-Number requests open the requested tab.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FormatCellsDialogLastSelectedTabTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCells_DefaultOpenRemembersLastTab_ExplicitTabOverrides()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                // Explicit Font tab, user switches to Fill (index 4), then Cancel.
                var task = window.ShowFormatCellsDialogForTestAsync(2);
                await DrainAsync();
                var dialog = Find(window);
                Strip(dialog).SelectedIndex.Should().Be(2, "an explicit tab request opens that tab");
                Strip(dialog).SelectedIndex = 4;
                Cancel(dialog);
                await DrainAsync();
                await task;

                // Default (Number) open reopens on the remembered Fill tab.
                task = window.ShowFormatCellsDialogForTestAsync();
                await DrainAsync();
                dialog = Find(window);
                Strip(dialog).SelectedIndex.Should().Be(4);

                Cancel(dialog);
                await DrainAsync();
                await task;

                // Explicit Alignment (ribbon launcher) wins over the remembered tab and is remembered.
                task = window.ShowFormatCellsDialogForTestAsync(1);
                await DrainAsync();
                dialog = Find(window);
                Strip(dialog).SelectedIndex.Should().Be(1);
                Cancel(dialog);
                await DrainAsync();
                await task;

                task = window.ShowFormatCellsDialogForTestAsync();
                await DrainAsync();
                dialog = Find(window);
                Strip(dialog).SelectedIndex.Should().Be(1);
                Strip(dialog).SelectedIndex = 0;
                Cancel(dialog);
                await DrainAsync();
                await task;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Window Find(MainWindow window) =>
        window.OwnedWindows.OfType<Window>()
            .Single(w => AutomationProperties.GetAutomationId(w) == "FormatCellsCompactDialog");

    private static TabControl Strip(Window dialog) =>
        dialog.GetVisualDescendants().OfType<TabControl>()
            .Single(t => AutomationProperties.GetAutomationId(t) == "FormatCellsTabStrip");

    private static void Cancel(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == "FormatCellsCancelButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task DrainAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
