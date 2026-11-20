using System.Reflection;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class PrintPreviewTextInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task FindNavigatesPrintedPagesHighlightsMatchesAndSupportsCopy() =>
        Run(async (window, clipboard) =>
        {
            var workbook = window.Session.Workbook;
            var first = window.Session.ActiveSheet;
            first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("Needle needle"));
            var second = workbook.AddSheet("Second");
            second.SetCell(new CellAddress(second.Id, 1, 1), new TextValue("Second sheet"));
            second.SetCell(new CellAddress(second.Id, 38, 1), new TextValue("lower NEEDLE"));

            var dialog = await OpenPreviewAsync(window);
            Find<ComboBox>(dialog, "PrintPreviewSettingsPrintWhatBox").SelectedIndex = (int)PrintWhat.EntireWorkbook;
            await DrainAsync();
            var query = Find<TextBox>(dialog, "PrintPreviewFindBox");
            var status = Find<TextBlock>(dialog, "PrintPreviewFindStatus");
            AutomationProperties.GetLiveSetting(status).Should().Be(
                OperatingSystem.IsMacOS() ? AutomationLiveSetting.Off : AutomationLiveSetting.Polite);
            var next = Find<Button>(dialog, "PrintPreviewFindNextButton");
            var previous = Find<Button>(dialog, "PrintPreviewFindPreviousButton");
            var copy = Find<Button>(dialog, "PrintPreviewCopyButton");
            next.IsEnabled.Should().BeFalse();
            copy.IsEnabled.Should().BeFalse();

            dialog.KeyPress(Key.F, RawInputModifiers.Meta, PhysicalKey.F, null);
            dialog.KeyRelease(Key.F, RawInputModifiers.Meta, PhysicalKey.F, null);
            query.IsFocused.Should().BeTrue();
            query.Text = "needle";
            await DrainAsync();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            SelectedRun(dialog).SelectedText.Should().Be("Needle");
            Find<TextBox>(dialog, PrintPreviewDialogPlanner.PageNumberBoxAutomationId).Text.Should().Be("1");

            Click(next);
            SelectedRun(dialog).SelectedText.Should().Be("needle");
            Click(next);
            await DrainAsync();
            SelectedRun(dialog).SelectedText.Should().Be("NEEDLE");
            Find<TextBox>(dialog, PrintPreviewDialogPlanner.PageNumberBoxAutomationId).Text.Should().Be("2");
            var pageScroll = Find<Border>(dialog, PrintPreviewDialogPlanner.PageHostAutomationId)
                .Child.Should().BeOfType<ScrollViewer>().Subject;
            pageScroll.Offset.Y.Should().BeGreaterThan(0, "the matching text starts below the visible portion of page two");
            Click(copy);
            clipboard.Text.Should().Be("NEEDLE");

            Click(previous);
            await DrainAsync();
            SelectedRun(dialog).SelectedText.Should().Be("needle");
            Find<TextBox>(dialog, PrintPreviewDialogPlanner.PageNumberBoxAutomationId).Text.Should().Be("1");
            query.Text = "missing text";
            await DrainAsync();
            Click(next);
            status.Text.Should().Be(UiText.Get("MainLoc_NoMatchesFound"));
            copy.IsEnabled.Should().BeFalse();
            query.Text = "needle";
            await DrainAsync();
            status.Text.Should().BeEmpty();
        });

    [Fact]
    public Task PointerSelectionAcrossPrintedCellsCopiesOnlyTheSelectedText() =>
        Run(async (window, clipboard) =>
        {
            var sheet = window.Session.ActiveSheet;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Beta"));
            var dialog = await OpenPreviewAsync(window);
            var runs = dialog.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
            var alpha = runs.Single(run => run.Text == "Alpha");
            var beta = runs.Single(run => run.Text == "Beta");
            var start = PointAt(alpha, 2, dialog);
            var end = PointAt(beta, 2, dialog);

            dialog.MouseMove(start, RawInputModifiers.None);
            dialog.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            dialog.MouseMove(end, RawInputModifiers.LeftMouseButton);
            dialog.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
            alpha.SelectedText.Should().Be("pha");
            beta.SelectedText.Should().Be("Be");

            dialog.KeyPress(Key.C, RawInputModifiers.Meta, PhysicalKey.C, null);
            dialog.KeyRelease(Key.C, RawInputModifiers.Meta, PhysicalKey.C, null);
            clipboard.Text.Should().Be("pha\tBe");
        });

    private static Task Run(Func<MainWindow, TestClipboard, Task> test) =>
        Session.Dispatch(async () =>
        {
            var clipboard = new TestClipboard();
            var window = new MainWindow(
                [], WorkbookShareSheetServiceFactory.Create("Share"), WorkbookFileAccessServiceFactory.Create(),
                new CupsPrintService(isSupportedOverride: false), clipboard);
            try
            {
                window.Show();
                window.ReplaceSession(new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: true));
                await test(window, clipboard);
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray())
                    dialog.Close();

                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);

    private static async Task<Window> OpenPreviewAsync(MainWindow window)
    {
        var show = typeof(MainWindow).GetMethod("ShowPrintPreviewDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = (Task)show.Invoke(window, [null, null, null])!;
        await DrainAsync();
        return window.OwnedWindows.Should().ContainSingle().Which;
    }

    private static async Task DrainAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static SelectableTextBlock SelectedRun(Window dialog) =>
        dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Should()
            .ContainSingle(run => run.SelectionStart != run.SelectionEnd).Which;

    private static Point PointAt(SelectableTextBlock run, int offset, Window dialog)
    {
        var caret = run.TextLayout.HitTestTextPosition(offset);
        var point = new Point(caret.X + 0.25, run.Bounds.Height / 2);
        return run.TranslatePoint(point, dialog)!.Value;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class TestClipboard : IPlatformClipboard
    {
        public string? Text { get; private set; }

        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Empty());

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content, CancellationToken cancellationToken = default)
        {
            Text = content.Text;
            return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
        }

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
