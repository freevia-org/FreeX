using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class PivotDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    private static readonly string[] DialogIds =
    [
        "dialog.PivotCalculatedFieldDialog",
        "dialog.PivotCalculatedItemDialog",
        "dialog.PivotChartOptionsDialog",
        "dialog.PivotChartTypeDialog",
        "dialog.PivotFieldFilterDialog",
        "dialog.PivotFieldGroupingDialog",
        "dialog.PivotLabelFilterDialog",
        "dialog.PivotSortOptionsDialog",
        "dialog.PivotStyleGalleryDialog",
        "dialog.PivotTableDialog",
        "dialog.PivotTableOptionsDialog",
        "dialog.PivotValueFieldSettingsDialog",
        "dialog.PivotValueFilterDialog",
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedInitialFocus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dialog.PivotCalculatedField"] = "passed:TextBox#PivotCalcFieldNameBox",
            ["dialog.PivotCalculatedItem"] = "passed:TextBox#PivotCalcItemNameBox",
            ["dialog.PivotChartOptions"] = "passed:CheckBox#PivotChartOptionsShowFieldButtons",
            ["dialog.PivotChartType"] = "passed:ListBox#ChangeChartTypeSubtypeGallery",
            ["dialog.PivotFieldFilter"] = "passed:TextBox#PivotItemFilterSearchBox",
            ["dialog.PivotFieldGrouping"] = "passed:ComboBox#PivotGroupFieldBox",
            ["dialog.PivotLabelFilter"] = "passed:ComboBox#PivotLabelFilterKindBox",
            ["dialog.PivotSortOptions"] = "passed:RadioButton#PivotSortOptionsLabelAscending",
            ["dialog.PivotStyleGallery"] = "passed:ListBox#PivotStyleGalleryList",
            ["dialog.PivotTable"] = "passed:TextBox#InsertPivotTableSourceRangeBox",
            ["dialog.PivotTableOptions"] = "passed:ComboBox#PivotOptionsReportLayoutBox",
            ["dialog.PivotValueFieldSettings"] = "passed:TextBox#PivotValueFieldSettingsNameBox",
            ["dialog.PivotValueFilter"] = "passed:ComboBox#PivotValueFilterKindBox",
        };

    private readonly ITestOutputHelper _output;

    public PivotDialogLifecycleRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task PivotDialogLifecycle_DoesNotReassertInitialFocusAfterLateActivation()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            var initial = new TextBox { Text = "Initial" };
            var next = new Button { Content = "Next" };
            var dialog = new Window
            {
                Content = new StackPanel { Children = { initial, next } },
                Width = 280,
                Height = 160,
            };

            try
            {
                owner.Show();
                ConfigurePivotDialogLifecycle(dialog, initial);
                dialog.Show(owner);
                dialog.Activate();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

                initial.IsFocused.Should().BeTrue();
                next.Focus().Should().BeTrue();

                owner.Activate();
                dialog.Activate();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(next);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PivotDialogTabCycle_ListBoxWrapRestoresTheSelectedItemFocus()
    {
        await Session.Dispatch(async () =>
        {
            var listBox = new ListBox
            {
                ItemsSource = new[] { "One", "Two" },
                SelectedIndex = 0,
            };
            var next = new Button { Content = "Next" };
            var root = new StackPanel { Children = { listBox, next } };
            var dialog = new Window { Content = root, Width = 280, Height = 180 };

            try
            {
                ConfigureDialogTabCycle(dialog, root);
                dialog.Show();
                dialog.Activate();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var selectedItem = listBox.GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Single(item => Equals(item.Content, listBox.SelectedItem));
                next.Focus().Should().BeTrue();

                MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.None, out var error)
                    .Should().BeTrue(error);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(selectedItem);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PivotDialogValidation_NormalizesRecreatedListBoxItemsToOneAuthoredStop()
    {
        await Session.Dispatch(async () =>
        {
            var listBox = new ListBox
            {
                ItemsSource = new[] { "One", "Two" },
                SelectedIndex = 1,
            };
            var next = new Button { Content = "Next" };
            var root = new StackPanel { Children = { listBox, next } };
            var dialog = new Window { Content = root, Width = 280, Height = 180 };

            try
            {
                ConfigureDialogTabCycle(dialog, root);
                dialog.Show();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                var selectedItem = listBox.GetVisualDescendants()
                    .OfType<ListBoxItem>()
                    .Single(item => Equals(item.Content, listBox.SelectedItem));
                selectedItem.Focus().Should().BeTrue();

                var replaced = false;
                next.GotFocus += (_, _) =>
                {
                    if (replaced)
                        return;
                    replaced = true;
                    listBox.ItemsSource = new[] { "One", "Two" };
                    listBox.SelectedIndex = 1;
                    dialog.UpdateLayout();
                };

                MainWindow.CountDialogTabStopsForTest(dialog).Should().Be(2);

                var cycle = MainWindow.ExerciseTabCycleForTestAsync(dialog, reverse: false, tabStops: 2);
                (await cycle).Should().Be("passed:full-cycle:steps=2,stops=2");
                replaced.Should().BeTrue();
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PivotDialogs_MatchWpfInitialFocusAndCompleteBothKeyboardCycles()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-pivot-dialog-lifecycle-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = DialogIds.ToHashSet(StringComparer.Ordinal);

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var results = window.BuildDialogInteractionContractResults(selectedIds);
                    foreach (var result in results)
                        _output.WriteLine($"{result.Id}: {result.Status} | {result.Evidence}");

                    results.Should().HaveCount(DialogIds.Length);
                    results.Select(result => result.Id).Should().BeEquivalentTo(DialogIds);
                    results.Should().OnlyContain(
                        result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Status} | {result.Evidence}")));

                    foreach (var (surfaceId, expectedFocus) in ExpectedInitialFocus)
                    {
                        var contract = window.DialogInteractionContracts[surfaceId];
                        contract.InitialFocus.Should().Be(expectedFocus, surfaceId);
                        contract.TabForward.Should().StartWith("passed:full-cycle:", surfaceId);
                        contract.TabBackward.Should().StartWith("passed:full-cycle:", surfaceId);
                        contract.EscapeCancel.Should().Be("passed:closed-by-escape", surfaceId);
                    }
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                    {
                        if (owned.IsVisible)
                            owned.Close();
                    }

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }

    private static void ConfigurePivotDialogLifecycle(Window dialog, Control initialFocus)
    {
        MainWindow.ConfigurePivotDialogLifecycleForTest(dialog, initialFocus);
    }

    private static void ConfigureDialogTabCycle(Window dialog, Control root)
    {
        MainWindow.ConfigureDialogTabCycleForTest(dialog, root);
    }
}
