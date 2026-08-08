using System.Reflection;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Interactions;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class DialogInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task ContractReport_EmitsOneOrderedRowPerCatalogDialog()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var results = window.BuildDialogInteractionContractResults();

            results.Should().HaveCount(MainWindow.InteractiveValidationDialogRouteCount);
            results.Select(result => result.Id).Should().Equal(
                InteractionSurfaceCatalog.Dialogs.Select(dialog => dialog.Id)
                    .Concat(MainWindow.SupplementalInteractionDialogRoutes.Select(route => route.CatalogId)));
            results.Select(result => result.Id).Should().OnlyHaveUniqueItems();
            results.Should().OnlyContain(result =>
                result.Category == "dialog-contract" &&
                result.Status == "failed" &&
                (result.EvidenceLevel == "catalogued-not-exercised" ||
                    result.EvidenceLevel == "production-dialog-not-exercised"));

            var expectedBatchIds = InteractionSurfaceCatalog.Dialogs
                .Skip(20)
                .Take(10)
                .Select(dialog => dialog.Id)
                .ToArray();
            var selectedIds = expectedBatchIds.ToHashSet(StringComparer.Ordinal);
            var batchResults = window.BuildDialogInteractionContractResults(selectedIds);
            batchResults.Should().HaveCount(10);
            batchResults.Select(result => result.Id).Should().Equal(expectedBatchIds);
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void ProductionDialogInventory_IncludesNativeFileProtectionAndTableResizeSurfaces()
    {
        MainWindow.ParityInteractionDialogRoutes.Should().HaveCount(120);
        MainWindow.SupplementalInteractionDialogRoutes.Select(route => route.SurfaceId).Should().Equal(
            "dialog.OpenWorkbook",
            "dialog.SaveAsWorkbook",
            "dialog.ProtectWorkbook",
            "dialog.TableResize");
        MainWindow.InteractiveValidationDialogRouteCount.Should().Be(124);
        MainWindow.InteractiveValidationDialogRoutes.Select(route => route.CatalogId)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AuditedWpfModelessDialogs_KeepOwnerInteractiveAndReturnFromProductionOpeners()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-modeless-dialog-contract-" + Guid.NewGuid().ToString("N"));

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
                        "dialog.AutoFilterDialog",
                        "dialog.CommentListWindow",
                        "dialog.ErrorCheckingDialog",
                        "dialog.FindReplaceDialog",
                        "dialog.WatchWindowDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contracts = window.DialogInteractionContracts.Values
                        .Where(contract => contract.ActualModality == "modeless")
                        .ToList();
                    contracts.Should().HaveCount(5);
                    contracts.Should().OnlyContain(contract =>
                        contract.Ownership == "passed:owned-by-main-window" &&
                        contract.OpenerLifecycle == "passed:modeless-opener-completed-while-open" &&
                        contract.OwnerInteractivity == "passed:modeless-owner-enabled");

                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().OnlyContain(result => result.Status == "passed",
                            string.Join(Environment.NewLine, window.DialogInteractionContracts.Values.Select(contract =>
                                $"{contract.SurfaceId}: {contract.ActualModality}; {contract.OpenerLifecycle}; " +
                                $"{contract.InitialFocus}; {contract.EscapeCancel}")));
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
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
                // Test cleanup must not hide the interaction contract result.
            }
        }
    }

    [Fact]
    public async Task ReusableWpfModelessDialogs_RefreshSwitchModeAndReactivateExistingWindows()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            try
            {
                owner.Show();
                var commentAddress = new FreeX.Core.Model.CellAddress(
                    owner.Session.ActiveSheet.Id,
                    1,
                    1);
                owner.Session.ActiveSheet.ThreadedComments[commentAddress] =
                    new FreeX.Core.Model.ThreadedComment("Modeless contract comment");

                await InvokePrivateTaskAsync(owner, "ShowFindDialogAsync");
                var findReplace = FindOwnedWindow(owner, "FindReplaceDialog");
                await InvokePrivateTaskAsync(owner, "ShowReplaceDialogAsync");
                FindOwnedWindow(owner, "FindReplaceDialog").Should().BeSameAs(findReplace);
                findReplace.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex.Should().Be(1);
                findReplace.GetVisualDescendants().OfType<Button>().Single(button =>
                        AutomationProperties.GetAutomationId(button) == "FindReplaceReplacementChooseFormatFromCellButton")
                    .IsVisible.Should().BeTrue();
                findReplace.Close();

                await InvokePrivateTaskAsync(owner, "ShowWatchWindowDialogAsync");
                var watch = FindOwnedWindow(owner, "WatchWindowDialog");
                await InvokePrivateTaskAsync(owner, "ShowWatchWindowDialogAsync");
                FindOwnedWindow(owner, "WatchWindowDialog").Should().BeSameAs(watch);
                watch.Close();

                await InvokePrivateTaskAsync(owner, "ShowErrorCheckingParityDialogAsync");
                await InvokePrivateTaskAsync(owner, "ShowErrorCheckingParityDialogAsync");
                var errorCheckingWindows = owner.OwnedWindows
                    .Where(window => AutomationProperties.GetAutomationId(window) == "ErrorCheckingDialog")
                    .ToList();
                errorCheckingWindows.Should().HaveCount(2, "WPF creates a fresh modeless error-checking window per command");
                errorCheckingWindows.ForEach(window => window.Close());

                await InvokePrivateTaskAsync(owner, "ShowCommentsListAsync");
                var comments = FindOwnedWindow(owner, "ReviewCommentListWindow");
                await InvokePrivateTaskAsync(owner, "ShowCommentsListAsync");
                FindOwnedWindow(owner, "ReviewCommentListWindow").Should().BeSameAs(comments);

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                owner.Close();
                comments.IsVisible.Should().BeFalse("owned modeless windows must follow the owner lifetime");
            }
            finally
            {
                foreach (var owned in owner.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }

                owner.AllowCloseWithoutDirtyPromptForParityCapture();

                if (owner.IsVisible)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RoutedKeyboardContract_ExercisesTabEscapeAndDefaultEnter()
    {
        await Session.Dispatch(() =>
        {
            Window? owner = null;
            Window? dialog = null;
            try
            {
                owner = new Window { Width = 400, Height = 240 };
                owner.Show();

                var first = new TextBox { Text = "First" };
                var cancel = new Button { Content = "Cancel", IsCancel = true };
                var defaultInvoked = false;
                var accept = new Button { Content = "OK", IsDefault = true };
                accept.Click += (_, _) => defaultInvoked = true;
                dialog = new Window
                {
                    Width = 300,
                    Height = 180,
                    Content = new StackPanel { Children = { first, accept, cancel } },
                };
                dialog.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Escape)
                        dialog.Close();
                };
                dialog.Show(owner);
                first.Focus();

                MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.None, out var tabError)
                    .Should().BeTrue(tabError);
                dialog.FocusManager?.GetFocusedElement().Should().NotBeSameAs(first);

                MainWindow.SendDialogKeyForTest(dialog, Key.Enter, RawInputModifiers.None, out var enterError)
                    .Should().BeTrue(enterError);
                defaultInvoked.Should().BeTrue();

                MainWindow.SendDialogKeyForTest(dialog, Key.Escape, RawInputModifiers.None, out var escapeError)
                    .Should().BeTrue(escapeError);
                dialog.IsVisible.Should().BeFalse();
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (owner?.IsVisible == true)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ModalDialogPolicy_FocusesCyclesEscapesAndRestoresExactOwnerFocus()
    {
        await Session.Dispatch(async () =>
        {
            MainWindow? owner = null;
            Window? dialog = null;
            try
            {
                owner = new MainWindow([]);
                owner.Show();
                owner.UpdateLayout();

                var ownerFocus = owner.GetVisualDescendants()
                    .OfType<Control>()
                    .First(control =>
                        control.Focusable && control.IsVisible && control.IsEffectivelyEnabled);
                ownerFocus.Focus();

                var first = new TextBox { Text = "First" };
                var second = new TextBox { Text = "Second" };
                var defaultInvoked = false;
                var accept = new Button { Content = "OK", IsDefault = true };
                accept.Click += (_, _) => defaultInvoked = true;
                dialog = new Window
                {
                    Width = 300,
                    Height = 180,
                    Content = new StackPanel { Children = { first, second, accept } },
                };

                var dialogTask = dialog.ShowDialog(owner);
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                for (var i = 0; i < 3; i++)
                {
                    MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.None, out var tabError)
                        .Should().BeTrue(tabError);
                }
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(first);

                MainWindow.SendDialogKeyForTest(dialog, Key.Tab, RawInputModifiers.Shift, out var shiftTabError)
                    .Should().BeTrue(shiftTabError);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(accept);

                first.Focus();
                MainWindow.SendDialogKeyForTest(dialog, Key.Enter, RawInputModifiers.None, out var enterError)
                    .Should().BeTrue(enterError);
                defaultInvoked.Should().BeTrue();

                MainWindow.SendDialogKeyForTest(dialog, Key.Escape, RawInputModifiers.None, out var escapeError)
                    .Should().BeTrue(escapeError);
                dialog.IsVisible.Should().BeFalse();
                await dialogTask;
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                owner.FocusManager?.GetFocusedElement().Should().BeSameAs(ownerFocus);
            }
            finally
            {
                if (dialog?.IsVisible == true)
                    dialog.Close();
                if (owner?.IsVisible == true)
                    owner.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ModalDialogPolicy_PreservesExplicitInitialFocusAndEscapeClosesWithoutCancelButton()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new MainWindow([]);
            Window? dialog = null;
            try
            {
                owner.Show();
                var first = new Button { Content = "First" };
                var explicitTarget = new Button { Content = "Explicit" };
                dialog = new Window
                {
                    Width = 260,
                    Height = 140,
                    Content = new StackPanel { Children = { first, explicitTarget } },
                };
                dialog.Opened += (_, _) => explicitTarget.Focus();

                var dialogTask = dialog.ShowDialog(owner);
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(explicitTarget);

                MainWindow.SendDialogKeyForTest(dialog, Key.Escape, RawInputModifiers.None, out var escapeError)
                    .Should().BeTrue(escapeError);
                dialog.IsVisible.Should().BeFalse();
                await dialogTask;
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

    [Fact]
    public async Task CompactDialogChrome_DefaultStylingAlsoSetsDefaultButtonSemantics()
    {
        await Session.Dispatch(() =>
        {
            var button = new Button();
            AvaloniaCompactDialogChrome.ApplyButton(
                button,
                new AvaloniaCompactDialogChromeStyle(FontFamily.Default),
                minWidth: 72,
                isDefault: true);

            button.IsDefault.Should().BeTrue();
        }, CancellationToken.None);
    }

    private static Window FindOwnedWindow(MainWindow owner, string automationId) =>
        owner.OwnedWindows.Single(window =>
            string.Equals(AutomationProperties.GetAutomationId(window), automationId, StringComparison.Ordinal));

    private static Task InvokePrivateTaskAsync(MainWindow owner, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production dialog opener {methodName}.");
        return method.Invoke(owner, null) as Task
            ?? throw new InvalidOperationException($"Production dialog opener {methodName} did not return Task.");
    }
}
