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

    [Fact]
    public async Task AllowEditRangeDialog_UsesWpfRangeBoxAsInitialFocusAndTabOrigin()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-allow-edit-range-focus-" + Guid.NewGuid().ToString("N"));

        try
        {
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

                    var contract = window.DialogInteractionContracts["dialog.AllowEditRangeDialog"];
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

                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Temp cleanup must not hide the dialog focus regression.
            }
        }
    }

    [Fact]
    public async Task ConfirmPasswordDialog_UsesSharedProtectionInputFocusAndEscapeLifecycle()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-confirm-password-focus-" + Guid.NewGuid().ToString("N"));

        try
        {
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

                    var contract = window.DialogInteractionContracts["dialog.ConfirmPasswordDialog"];
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

                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Temp cleanup must not hide the protection focus regression.
            }
        }
    }

    [Fact]
    public async Task AdvancedFilterOwnedModal_ClosesThroughEscapeContract()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-advanced-filter-escape-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: new HashSet<string>(StringComparer.Ordinal)
                        {
                            "dialog.AdvancedFilter",
                        });

                    var contract = window.DialogInteractionContracts["dialog.AdvancedFilterDialog"];
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
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide interaction lifecycle regressions.
            }
        }
    }

    [Fact]
    public async Task AssignedProductionDialogs_PassOwnedFocusTabAndEscapeContracts()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-owned-dialog-lifecycle-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    AssignedDialogIds.Should().HaveCount(39).And.OnlyHaveUniqueItems();
                    var selectedIds = AssignedDialogIds.ToHashSet(StringComparer.Ordinal);

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var results = window.BuildDialogInteractionContractResults(selectedIds);
                    results.Should().HaveCount(AssignedDialogIds.Length);
                    results.Select(result => result.Id).Should().BeEquivalentTo(AssignedDialogIds);
                    results.Should().OnlyContain(
                        result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Evidence}")));

                    var contracts = window.DialogInteractionContracts;
                    foreach (var id in MissingFocusTabAndEscapeIds.Append("dialog.AutoFilterDialog"))
                    {
                        contracts[id].InitialFocus.Should().StartWith("passed:", id);
                        contracts[id].TabForward.Should().StartWith("passed:", id);
                        contracts[id].TabBackward.Should().StartWith("passed:", id);
                        contracts[id].EscapeCancel.Should().StartWith("passed:", id);
                    }

                    foreach (var id in MissingInitialFocusIds)
                        contracts[id].InitialFocus.Should().StartWith("passed:", id);

                    contracts["dialog.AdvancedFilterDialog"].EscapeCancel
                        .Should().StartWith("passed:");
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                    {
                        if (owned.IsVisible)
                            owned.Close();
                    }
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide interaction lifecycle regressions.
            }
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
                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }
}
