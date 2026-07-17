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

            results.Should().HaveCount(120);
            results.Select(result => result.Id).Should().Equal(
                InteractionSurfaceCatalog.Dialogs.Select(dialog => dialog.Id));
            results.Select(result => result.Id).Should().OnlyHaveUniqueItems();
            results.Should().OnlyContain(result =>
                result.Category == "dialog-contract" &&
                result.Status == "failed" &&
                result.EvidenceLevel == "catalogued-not-exercised");

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
    public async Task HeadlessKeyboardDevice_ExercisesTabEscapeAndDefaultEnter()
    {
        await Session.Dispatch(() =>
        {
            Window? owner = null;
            Window? dialog = null;
            try
            {
                MainWindow.DialogKeySenderOverride = SendHeadlessDialogKey;
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
                MainWindow.DialogKeySenderOverride = null;
            }
        }, CancellationToken.None);
    }

    private static string? SendHeadlessDialogKey(
        global::Avalonia.Controls.Window dialog,
        Key key,
        RawInputModifiers modifiers)
    {
        var physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            _ => PhysicalKey.None,
        };
        dialog.KeyPress(key, modifiers, physicalKey, keySymbol: null);
        if (dialog.IsVisible)
            dialog.KeyRelease(key, modifiers, physicalKey, keySymbol: null);
        return null;
    }
}
