using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class OwnedDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    private static readonly string[] MissingFocusTabAndEscapeIds =
    [
        "dialog.ChangeChartTypeDialog",
        "dialog.ChartAreaLegendDialog",
        "dialog.ChartAxisFormatDialog",
        "dialog.ChartBarFormatDialog",
        "dialog.ChartBubbleFormatDialog",
        "dialog.ChartDataLabelsDialog",
        "dialog.ChartErrorBarsDialog",
        "dialog.ChartPieFormatDialog",
        "dialog.ChartSeriesFormatDialog",
        "dialog.ChartStockFormatDialog",
        "dialog.ChartTitlesDialog",
        "dialog.ChartTrendlineOptionsDialog",
        "dialog.FormatPictureDialog",
        "dialog.HeaderFooterDialog",
        "dialog.MoveChartDialog",
        "dialog.ObjectSizeDialog",
        "dialog.PictureCropDialog",
        "dialog.PivotCalculatedFieldDialog",
        "dialog.PivotCalculatedItemDialog",
        "dialog.PivotChartOptionsDialog",
        "dialog.PivotChartTypeDialog",
        "dialog.PivotFieldGroupingDialog",
        "dialog.PivotLabelFilterDialog",
        "dialog.PivotSortOptionsDialog",
        "dialog.PivotStyleGalleryDialog",
        "dialog.PivotTableDialog",
        "dialog.PivotTableNameDialog",
        "dialog.PivotTableOptionsDialog",
        "dialog.PivotValueFilterDialog",
        "dialog.RotationDialog",
        "dialog.SelectionPaneDialog",
        "dialog.ShapeGradientDialog",
        "dialog.TextToColumnsDialog",
    ];

    private static readonly string[] MissingInitialFocusIds =
    [
        "dialog.AllowEditRangeDialog",
        "dialog.ManageConditionalFormatsDialog",
        "dialog.PivotTableDataSourceDialog",
        "dialog.ScenarioManagerDialog",
    ];

    private static readonly string[] AssignedDialogIds =
    [
        .. MissingFocusTabAndEscapeIds,
        .. MissingInitialFocusIds,
        "dialog.AutoFilterDialog",
        "dialog.AdvancedFilterDialog",
    ];

    // The 39 assigned dialogs used to be exercised by one test that opened every production dialog
    // sequentially in a single MainWindow/process. Headless Avalonia windows retain sizeable
    // visual/native graphs until a full GC, and concentrating dozens of open/close cycles in one test
    // was the leading suspect for the CaptureTests project's observed 22-24 GB working-set blowups
    // under contention. Splitting into Batch2-6-sized chunks (mirrors the sibling *.Batch2-6.csproj
    // projects, which stay small and never balloon) bounds how many dialogs any single test/process
    // concentrates, without dropping coverage of any of the 39 dialogs. Each dialog still gets its own
    // full open/interact/close/GC cycle; the split just spreads them across more test-method boundaries.
    private static readonly string[] AssignedDialogsBatch1 = [.. MissingFocusTabAndEscapeIds[..10]];
    private static readonly string[] AssignedDialogsBatch2 = [.. MissingFocusTabAndEscapeIds[10..20]];
    private static readonly string[] AssignedDialogsBatch3 = [.. MissingFocusTabAndEscapeIds[20..30]];
    private static readonly string[] AssignedDialogsBatch4 =
    [
        .. MissingFocusTabAndEscapeIds[30..],
        .. MissingInitialFocusIds,
        "dialog.AutoFilterDialog",
        "dialog.AdvancedFilterDialog",
    ];

    [Fact]
    public async Task AllowEditRangeDialog_UsesWpfRangeBoxAsInitialFocusAndTabOrigin()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-allow-edit-range-focus-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.AllowEditRangeDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.AllowEditRanges"];
                    contract.InitialFocus.Should().Be("passed:TextBox#AllowEditRangeBox");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(
                            result => result.Status == "passed",
                            "AllowEditRange contract should pass: {0}; {1}; {2}",
                            contract.InitialFocus,
                            contract.TabForward,
                            contract.TabBackward);
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

    [Fact]
    public async Task ConfirmPasswordDialog_UsesSharedProtectionInputFocusAndEscapeLifecycle()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-confirm-password-focus-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.ConfirmPasswordDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.ProtectSheet"];
                    contract.InitialFocus.Should().Be("passed:TextBox#ProtectSheetPasswordBox");
                    contract.TabForward.Should().StartWith("passed:");
                    contract.TabBackward.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().StartWith("passed:");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(
                            result => result.Status == "passed",
                            "ConfirmPassword contract should pass: {0}; {1}; {2}; {3}",
                            contract.InitialFocus,
                            contract.TabForward,
                            contract.TabBackward,
                            contract.EscapeCancel);
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

    [Fact]
    public async Task AdvancedFilterOwnedModal_ClosesThroughEscapeContract()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-advanced-filter-escape-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        // The filter matches ParityInteractionDialogRoute.CatalogId, which keeps the
                        // "Dialog" suffix, while contracts are keyed by SurfaceId, which drops it.
                        // Passing the surface id here selected no route, so nothing was captured and
                        // the lookup below could never find its key.
                        interactionDialogCatalogIds: new HashSet<string>(StringComparer.Ordinal)
                        {
                            "dialog.AdvancedFilterDialog",
                        });

                    var contract = window.DialogInteractionContracts["dialog.AdvancedFilter"];
                    contract.ActualModality.Should().Be("modal");
                    contract.Ownership.Should().StartWith("passed:");
                    contract.OpenerLifecycle.Should().StartWith("passed:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    contract.OwnerFocusRestore.Should().StartWith("passed:");
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

    [Fact]
    public void AssignedDialogsBatches_CoverAllThirtyNineAssignedDialogsExactlyOnce()
    {
        // Guards the split below: every dialog in AssignedDialogIds must land in exactly one batch,
        // and no batch may introduce an id that isn't assigned. Prevents the chunking from silently
        // dropping (or duplicating) coverage as dialogs are added/removed over time.
        var batched = AssignedDialogsBatch1
            .Concat(AssignedDialogsBatch2)
            .Concat(AssignedDialogsBatch3)
            .Concat(AssignedDialogsBatch4)
            .ToArray();

        batched.Should().OnlyHaveUniqueItems();
        batched.Should().BeEquivalentTo(AssignedDialogIds);
        AssignedDialogIds.Should().HaveCount(39);
    }

    [Fact]
    public Task AssignedProductionDialogsBatch1_PassOwnedFocusTabAndEscapeContracts() =>
        RunAssignedDialogsBatchAsync(AssignedDialogsBatch1, "batch1");

    [Fact]
    public Task AssignedProductionDialogsBatch2_PassOwnedFocusTabAndEscapeContracts() =>
        RunAssignedDialogsBatchAsync(AssignedDialogsBatch2, "batch2");

    [Fact]
    public Task AssignedProductionDialogsBatch3_PassOwnedFocusTabAndEscapeContracts() =>
        RunAssignedDialogsBatchAsync(AssignedDialogsBatch3, "batch3");

    [Fact]
    public Task AssignedProductionDialogsBatch4_PassOwnedFocusTabAndEscapeContracts() =>
        RunAssignedDialogsBatchAsync(AssignedDialogsBatch4, "batch4");

    /// <summary>
    /// Opens a fresh <see cref="MainWindow"/>, drives only <paramref name="batchIds"/> through the owned
    /// dialog focus/tab/escape contract, and asserts the same per-dialog contracts the original single
    /// 39-dialog test asserted -- just scoped to this batch's ids.
    /// </summary>
    private static async Task RunAssignedDialogsBatchAsync(string[] batchIds, string batchTag)
    {
        using (var temporaryDirectory = new TestTemporaryDirectory($"freex-owned-dialog-lifecycle-{batchTag}-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = batchIds.ToHashSet(StringComparer.Ordinal);

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var results = window.BuildDialogInteractionContractResults(selectedIds);
                    results.Should().HaveCount(batchIds.Length);
                    results.Select(result => result.Id).Should().BeEquivalentTo(batchIds);
                    results.Should().OnlyContain(
                        result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Evidence}")));

                    var contracts = window.DialogInteractionContracts;
                    foreach (var id in batchIds)
                    {
                        if (MissingFocusTabAndEscapeIds.Contains(id) || id == "dialog.AutoFilterDialog")
                        {
                            contracts[id].InitialFocus.Should().StartWith("passed:", id);
                            contracts[id].TabForward.Should().StartWith("passed:", id);
                            contracts[id].TabBackward.Should().StartWith("passed:", id);
                            contracts[id].EscapeCancel.Should().StartWith("passed:", id);
                        }

                        if (MissingInitialFocusIds.Contains(id))
                            contracts[id].InitialFocus.Should().StartWith("passed:", id);

                        if (id == "dialog.AdvancedFilterDialog")
                            contracts[id].EscapeCancel.Should().StartWith("passed:");
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
}

[Collection(AvaloniaHeadlessCollectionOrderer.PostCaptureCollectionName)]
public sealed class PostCaptureOwnedDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task DirectlyOwnedModelessWindow_ReceivesFocusTabCycleAndEscapeLifecycle()
    {
        await Session.Dispatch(() =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            try
            {
                owner.Show();
                var first = new TextBox { Text = "First" };
                var second = new TextBox { Text = "Second" };
                dialog = new Window
                {
                    Width = 280,
                    Height = 160,
                    Content = new StackPanel { Children = { first, second } },
                };

                dialog.Show(owner);
                dialog.Activate();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

                first.Focus(NavigationMethod.Tab).Should().BeTrue();
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);
                MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.None, out var tabError)
                    .Should().BeTrue(tabError);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(second);
                MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.None, out tabError)
                    .Should().BeTrue(tabError);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                MainWindow.SendDialogKeyForTest(dialog, Key.Escape, RawInputModifiers.None, out var escapeError)
                    .Should().BeTrue(escapeError);
                dialog.IsVisible.Should().BeFalse();
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }
}
