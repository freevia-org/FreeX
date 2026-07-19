using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Interactions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogInteractionValidationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

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
}
