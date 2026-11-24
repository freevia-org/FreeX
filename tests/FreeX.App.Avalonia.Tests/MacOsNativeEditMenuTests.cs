using System.Reflection;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsNativeEditMenuTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task FormulaEditor_CopyCutAndPasteUseSelectedTextWithoutCommittingTheCell() =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            var oldValue = window.Session.ActiveSheet.GetValue(address);
            window.BeginFormulaEditForTest(address, "Pending");
            var editor = GetField<TextBox>(window, "_formulaBox");
            editor.Focus().Should().BeTrue();
            editor.SelectionStart = 1;
            editor.SelectionEnd = 4;
            var clipboard = new AvaloniaPlatformClipboard(() => window.Clipboard,
                new AvaloniaPlatformClipboardOptions(FlushAfterWrite: false));

            await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Copy);
            await DrainInputAsync();
            (await clipboard.ReadTextAsync()).Value.Should().Be("end");
            editor.Text.Should().Be("Pending");
            window.Session.ActiveSheet.GetValue(address).Should().Be(oldValue);
            window.Session.FormulaEditAddress.Should().Be(address);

            await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Cut);
            await DrainInputAsync();
            editor.Text.Should().Be("Ping");
            (await clipboard.ReadTextAsync()).Value.Should().Be("end");

            (await clipboard.WriteAsync(new PlatformClipboardContent(Text: "aste"))).IsSuccess.Should().BeTrue();
            editor.CaretIndex = 1;
            editor.SelectionStart = editor.SelectionEnd = 1;
            await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Paste);
            await DrainInputAsync();
            editor.Text.Should().Be("Pasteing");
            window.Session.ActiveSheet.GetValue(address).Should().Be(oldValue);
            window.Session.FormulaEditAddress.Should().Be(address);
            window.Session.IsDirty.Should().BeFalse("native text edits must stay pending until a cell commit");
        });

    [Fact]
    public Task FormulaEditor_SelectAllKeepsTheWorksheetSelectionAndPendingEdit() =>
        Run(async window =>
        {
            var range = window.Session.SelectedRange;
            window.BeginFormulaEditForTest(window.Session.ActiveCell, "Pending formula");
            var editor = GetField<TextBox>(window, "_formulaBox");
            editor.Focus().Should().BeTrue();
            editor.SelectionStart = editor.SelectionEnd = 2;

            await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.SelectAll);

            editor.SelectedText.Should().Be("Pending formula");
            window.Session.SelectedRange.Should().Be(range);
            window.Session.FormulaEditAddress.Should().NotBeNull();
        });

    [Fact]
    public Task OwnedModelessEditor_UndoRedoAndSelectAllStayInsideTheDialog() =>
        Run(async window =>
        {
            var editor = new TextBox { Text = "original", IsUndoEnabled = true };
            var dialog = new Window { Content = editor, Width = 320, Height = 120 };
            try
            {
                dialog.Show(window);
                editor.Focus().Should().BeTrue();
                editor.SelectAll();
                dialog.KeyTextInput("changed");
                await DrainInputAsync();
                editor.Text.Should().Be("changed");
                editor.CanUndo.Should().BeTrue();

                window.RefreshNativeTextEditMenuAvailability();
                GetField<NativeMenuItem>(window, "_undoMenuItem").IsEnabled.Should().BeTrue(
                    "the dialog has text undo even though the workbook has no undo history");
                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Undo);
                editor.Text.Should().Be("original");
                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Redo);
                editor.Text.Should().Be("changed");
                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.SelectAll);
                editor.SelectedText.Should().Be("changed");
                window.Session.IsDirty.Should().BeFalse();
                window.Session.CanUndo.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });

    [Fact]
    public Task ReadOnlyEditor_DisablesMutationsAndNeverFallsThroughToWorksheetCommands() =>
        Run(async window =>
        {
            var editor = new TextBox { Text = "read only", IsReadOnly = true };
            var dialog = new Window { Content = editor, Width = 320, Height = 120 };
            try
            {
                dialog.Show(window);
                editor.Focus().Should().BeTrue();
                editor.SelectAll();
                window.RefreshNativeTextEditMenuAvailability();

                GetField<NativeMenuItem>(window, "_copyMenuItem").IsEnabled.Should().BeTrue();
                GetField<NativeMenuItem>(window, "_selectAllMenuItem").IsEnabled.Should().BeTrue();
                foreach (var field in new[] { "_cutMenuItem", "_pasteMenuItem", "_undoMenuItem", "_redoMenuItem" })
                    GetField<NativeMenuItem>(window, field).IsEnabled.Should().BeFalse();

                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Cut);
                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.Paste);
                editor.Text.Should().Be("read only");
                window.Session.IsDirty.Should().BeFalse();
                window.Session.FormulaEditAddress.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });

    [Fact]
    public Task OwnedDialogButtonFocus_DisablesEditCommandsAndKeepsWorksheetSelection() =>
        Run(async window =>
        {
            var button = new Button { Content = "Find Next" };
            var dialog = new Window { Content = button, Width = 320, Height = 120 };
            var range = window.Session.SelectedRange;
            try
            {
                dialog.Show(window);
                button.Focus().Should().BeTrue();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(button);
                window.OwnedWindows.Should().Contain(dialog);
                window.RefreshNativeTextEditMenuAvailability();
                GetField<NativeMenuItem>(window, "_selectAllMenuItem").IsEnabled.Should().BeFalse();
                GetField<NativeMenuItem>(window, "_pasteMenuItem").IsEnabled.Should().BeFalse();

                await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.SelectAll);
                window.Session.SelectedRange.Should().Be(range);
            }
            finally
            {
                dialog.Close();
            }
        });

    [Fact]
    public Task WorksheetFocus_SelectAllStillUsesTheWorkbookCommand() =>
        Run(async window =>
        {
            window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
            await window.ExecuteNativeEditMenuCommandAsync(NativeMenuItemId.SelectAll);
            window.Session.SelectedRange.CellCount.Should().BeGreaterThan(1);
        });

    private static Task Run(Func<MainWindow, Task> test) =>
        Session.Dispatch(async () =>
        {
            // Never inherit the developer's persisted formula-bar visibility or a preceding
            // test's options. The focused formula editor must exist in this fixture.
            var options = new AppOptions { ShowFormulaBar = true };
            var optionsSession = new FreeXOptionsRuntimeSession(
                initialOptions: options,
                load: () => options.Clone(),
                save: _ => true);
            var window = new MainWindow([], null!, optionsSession);
            try
            {
                window.Show();
                await DrainInputAsync();
                await test(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);

    private static T GetField<T>(MainWindow window, string name) where T : class =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
