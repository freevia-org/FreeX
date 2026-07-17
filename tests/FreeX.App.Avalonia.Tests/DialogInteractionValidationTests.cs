using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
}
