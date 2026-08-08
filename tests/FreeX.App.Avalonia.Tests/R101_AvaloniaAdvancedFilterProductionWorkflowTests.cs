using System.Reflection;
using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R101_AvaloniaAdvancedFilterProductionWorkflowTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AdvancedFilterProductionDialog_RangePickersCopyUndoRedoAndReapplyMatchWpfAuthority()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            Window? dialog = null;
            Task? opener = null;
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                SeedFixture(sheet);

                var listRange = Range(sheet, 1, 1, 5, 2);
                var criteriaRange = Range(sheet, 1, 4, 2, 4);
                var copyToRange = Range(sheet, 1, 7, 1, 8);
                window.Session.SelectRange(listRange);

                opener = InvokeAdvancedFilterOpener(window);
                dialog = await WaitForOwnedDialogAsync(window);
                var controls = GetControls(dialog);

                await ExerciseRangePickerAsync(
                    window,
                    dialog,
                    "AdvancedFilterSelectListRangeButton",
                    "AdvancedFilterListRangeBox",
                    listRange,
                    "A1:B5");
                await ExerciseRangePickerAsync(
                    window,
                    dialog,
                    "AdvancedFilterSelectCriteriaRangeButton",
                    "AdvancedFilterCriteriaRangeBox",
                    criteriaRange,
                    "D1:D2");

                controls.CopyToAnotherLocation.IsChecked = true;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                await ExerciseRangePickerAsync(
                    window,
                    dialog,
                    "AdvancedFilterSelectCopyToButton",
                    "AdvancedFilterCopyToBox",
                    copyToRange,
                    "G1:H1");

                controls.UniqueRecordsOnly.IsChecked = true;
                controls.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, controls.OkButton));
                await AwaitClosedAsync(opener);
                opener = null;
                dialog = null;

                sheet.GetValue(new CellAddress(sheet.Id, 1, 7)).Should().Be(new TextValue("Region"));
                sheet.GetValue(new CellAddress(sheet.Id, 1, 8)).Should().Be(new TextValue("Amount"));
                sheet.GetValue(new CellAddress(sheet.Id, 2, 7)).Should().Be(new TextValue("West"));
                sheet.GetValue(new CellAddress(sheet.Id, 2, 8)).Should().Be(new NumberValue(10));
                sheet.GetValue(new CellAddress(sheet.Id, 3, 7)).Should().Be(BlankValue.Instance);
                sheet.FilterHiddenRows.Should().BeEmpty("copy mode must leave the source list visible");
                window.Session.SelectedRange.Should().Be(copyToRange);

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.GetValue(new CellAddress(sheet.Id, 1, 7)).Should().Be(BlankValue.Instance);
                window.Session.SelectedRange.Should().Be(copyToRange);
                window.Session.RedoLastEdit().Success.Should().BeTrue();
                sheet.GetValue(new CellAddress(sheet.Id, 2, 7)).Should().Be(new TextValue("West"));

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                window.Session.SelectRange(listRange);
                opener = InvokeAdvancedFilterOpener(window);
                dialog = await WaitForOwnedDialogAsync(window);
                controls = GetControls(dialog);
                controls.ListRange.Text = "A1:B5";
                controls.CriteriaRange.Text = "D1:D2";
                controls.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, controls.OkButton));
                await AwaitClosedAsync(opener);
                opener = null;
                dialog = null;

                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
                window.Session.SelectedRange.Should().Be(listRange);

                sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
                InvokeReapply(window);
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u]);
                sheet.FilterHiddenRows.Should().NotContain(2u);

                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
                window.Session.RedoLastEdit().Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u]);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (opener is not null)
                    await AwaitClosedAsync(opener);

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reapply_CombinesTwoAutoFilterColumnsAndInPlaceAdvancedFilter_AsOneUndoRedoUnit()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            Window? dialog = null;
            Task? opener = null;
            try
            {
                window.Show();
                var sheet = window.Session.ActiveSheet;
                SeedCombinedReapplyFixture(sheet);

                var listRange = Range(sheet, 1, 1, 6, 3);
                var criteriaRange = Range(sheet, 1, 5, 2, 5);
                sheet.AutoFilter = new WorksheetAutoFilterModel(listRange.ToString(), null);
                window.Session.SelectRange(listRange);

                // These are the production AutoFilter command routes, applied to two columns.
                window.RunAutoFilterForTest(listRange, 0, ["West"]);
                window.RunAutoFilterForTest(listRange, 1, ["200"]);

                opener = InvokeAdvancedFilterOpener(window);
                dialog = await WaitForOwnedDialogAsync(window);
                var controls = GetControls(dialog);
                controls.ListRange.Text = "A1:C6";
                controls.CriteriaRange.Text = "E1:E2";
                controls.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, controls.OkButton));
                await AwaitClosedAsync(opener);
                opener = null;
                dialog = null;

                // Advanced Filter's initial application is intentionally followed by Reapply; the
                // latter is the WPF-authoritative operation that combines all active definitions.
                sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u]);

                // Row 4 now passes both AutoFilter criteria. Row 5 now passes Advanced Filter, while
                // row 3 still fails Amount and row 6 still fails both mechanisms.
                sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("Keep"));
                InvokeReapply(window);

                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 6u]);
                window.Session.SelectedRange.Should().Be(listRange);

                // A single undo restores the visibility before Reapply, proving the composite was
                // recorded as one history item instead of one item per filter definition.
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u]);
                window.Session.RedoLastEdit().Success.Should().BeTrue();
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 6u]);

                // Corrupt the remembered criteria after the successful operation. Reapply must fail
                // without disturbing the current visibility state.
                sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Missing"));
                InvokeReapply(window);
                sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 6u]);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (opener is not null)
                    await AwaitClosedAsync(opener);

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    private static void SeedFixture(Sheet sheet)
    {
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Amount");
        Set(sheet, 2, 1, "West");
        Set(sheet, 2, 2, 10);
        Set(sheet, 3, 1, "East");
        Set(sheet, 3, 2, 20);
        Set(sheet, 4, 1, "West");
        Set(sheet, 4, 2, 10);
        Set(sheet, 5, 1, "North");
        Set(sheet, 5, 2, 30);
        Set(sheet, 1, 4, "Region");
        Set(sheet, 2, 4, "West");
    }

    private static void SeedCombinedReapplyFixture(Sheet sheet)
    {
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Amount");
        Set(sheet, 1, 3, "Status");
        Set(sheet, 2, 1, "West");
        Set(sheet, 2, 2, 200);
        Set(sheet, 2, 3, "Keep");
        Set(sheet, 3, 1, "West");
        Set(sheet, 3, 2, 100);
        Set(sheet, 3, 3, "Keep");
        Set(sheet, 4, 1, "East");
        Set(sheet, 4, 2, 200);
        Set(sheet, 4, 3, "Keep");
        Set(sheet, 5, 1, "West");
        Set(sheet, 5, 2, 200);
        Set(sheet, 5, 3, "Drop");
        Set(sheet, 6, 1, "East");
        Set(sheet, 6, 2, 100);
        Set(sheet, 6, 3, "Drop");
        Set(sheet, 1, 5, "Status");
        Set(sheet, 2, 5, "Keep");
    }

    private static void Set(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static void Set(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));

    private static Task InvokeAdvancedFilterOpener(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("ShowAdvancedFilterInputDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, null) as Task
        ?? throw new InvalidOperationException("Missing production Advanced Filter dialog opener.");

    private static void InvokeReapply(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("ReapplyCurrentFilterSort", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, null);

    private static AdvancedFilterControls GetControls(Window dialog)
    {
        var controls = dialog.GetVisualDescendants().OfType<Control>().ToArray();
        return new AdvancedFilterControls(
            Find<TextBox>(controls, "AdvancedFilterListRangeBox"),
            Find<TextBox>(controls, "AdvancedFilterCriteriaRangeBox"),
            Find<RadioButton>(controls, "AdvancedFilterCopyToAnotherLocationButton"),
            Find<CheckBox>(controls, "AdvancedFilterUniqueRecordsOnlyBox"),
            Find<Button>(controls, "AdvancedFilterOkButton"));
    }

    private static T Find<T>(IEnumerable<Control> controls, string automationId)
        where T : Control =>
        controls.OfType<T>().Single(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static async Task ExerciseRangePickerAsync(
        MainWindow window,
        Window dialog,
        string pickerAutomationId,
        string textBoxAutomationId,
        GridRange selectedRange,
        string expectedText)
    {
        var controls = dialog.GetVisualDescendants().OfType<Control>();
        var picker = Find<Button>(controls, pickerAutomationId);
        var target = Find<TextBox>(controls, textBoxAutomationId);

        picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, picker));
        window.Session.SelectRange(selectedRange);
        SendRangeKey(window, Key.Enter);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        target.Text.Should().Be(expectedText);

        picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, picker));
        window.Session.SelectRange(new GridRange(selectedRange.Start, selectedRange.Start));
        SendRangeKey(window, Key.Escape);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        target.Text.Should().Be(expectedText);
        await Task.CompletedTask;
    }

    private static void SendRangeKey(MainWindow window, Key key) =>
        typeof(MainWindow)
            .GetMethod("RaiseDialogRangeValidationKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key]);

    private static async Task<Window> WaitForOwnedDialogAsync(MainWindow owner)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.FirstOrDefault(window =>
                window.IsVisible &&
                AutomationProperties.GetAutomationId(window) == "AdvancedFilterCompactDialog");
            if (dialog is not null)
                return dialog;

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Advanced Filter dialog did not open within 5 seconds.");
    }

    private static async Task AwaitClosedAsync(Task opener)
    {
        var completed = await Task.WhenAny(opener, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().BeSameAs(opener, "the modal opener must complete after the dialog closes");
        await opener;
    }

    private sealed record AdvancedFilterControls(
        TextBox ListRange,
        TextBox CriteriaRange,
        RadioButton CopyToAnotherLocation,
        CheckBox UniqueRecordsOnly,
        Button OkButton);
}
