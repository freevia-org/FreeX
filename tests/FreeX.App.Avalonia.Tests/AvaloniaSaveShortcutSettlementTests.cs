using System.Threading;

using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;

using FreeX.App.Presentation.Backstage;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaSaveShortcutSettlementTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(Key.S, KeyModifiers.Control, PhysicalKey.None)]
    [InlineData(Key.F12, KeyModifiers.Shift, PhysicalKey.F12)]
    [InlineData(Key.F24, KeyModifiers.Shift, PhysicalKey.F12)]
    public async Task SaveShortcut_AwaitsSaveAsThenReusesSettledPath(
        Key key,
        KeyModifiers modifiers,
        PhysicalKey physicalKey)
    {
        await Session.Dispatch(async () =>
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                $"freex-save-shortcut-test-{Guid.NewGuid():N}.fxl");
            var window = new MainWindow([]);
            var pickerCalls = 0;
            try
            {
                var firstAddress = window.Session.ActiveCell;
                var sheetName = window.Session.ActiveSheet.Name;
                Edit(window, firstAddress, "first-save");
                window.WorkbookSaveAsPickerOverrideForTest = _ =>
                {
                    pickerCalls++;
                    return Task.FromResult<MainWindow.WorkbookSaveAsPickerSelection?>(
                        window.CreateTransientWorkbookSaveAsSelection(path));
                };

                await PressHandled(window, key, modifiers, physicalKey);

                pickerCalls.Should().Be(1);
                window.Session.IsDirty.Should().BeFalse();
                Path.GetFullPath(window.Session.CurrentFilePath!).Should().Be(Path.GetFullPath(path));
                ReadValue(path, sheetName, firstAddress.Row, firstAddress.Col)
                    .Should().Be(new TextValue("first-save"));

                var secondAddress = new CellAddress(firstAddress.Sheet, firstAddress.Row + 1, firstAddress.Col);
                Edit(window, secondAddress, "current-path-save");
                window.WorkbookSaveAsPickerOverrideForTest = _ =>
                    throw new InvalidOperationException("A reusable save path must not reopen Save As.");

                await PressHandled(window, key, modifiers, physicalKey);

                pickerCalls.Should().Be(1);
                window.Session.IsDirty.Should().BeFalse();
                ReadValue(path, sheetName, secondAddress.Row, secondAddress.Col)
                    .Should().Be(new TextValue("current-path-save"));
            }
            finally
            {
                window.WorkbookSaveAsPickerOverrideForTest = null;
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                File.Delete(path);
            }

            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(Key.S, KeyModifiers.Control, PhysicalKey.None)]
    [InlineData(Key.F12, KeyModifiers.Shift, PhysicalKey.F12)]
    [InlineData(Key.F24, KeyModifiers.Shift, PhysicalKey.F12)]
    [InlineData(Key.F12, KeyModifiers.None, PhysicalKey.F12)]
    [InlineData(Key.F24, KeyModifiers.None, PhysicalKey.F12)]
    public async Task SaveShortcut_AwaitsCanceledSaveAsAndRemainsAvailableForAnotherPicker(
        Key key,
        KeyModifiers modifiers,
        PhysicalKey physicalKey)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var pickerCalls = 0;
            try
            {
                Edit(window, window.Session.ActiveCell, "unsaved-after-cancel");
                window.WorkbookSaveAsPickerOverrideForTest = _ =>
                {
                    pickerCalls++;
                    return Task.FromResult<MainWindow.WorkbookSaveAsPickerSelection?>(null);
                };

                await PressHandled(window, key, modifiers, physicalKey);

                pickerCalls.Should().Be(1);
                window.Session.IsDirty.Should().BeTrue();
                window.Session.CurrentFilePath.Should().BeNull();

                // A native chooser cancellation must release the in-flight file operation. This
                // is what lets the next file shortcut open its own picker instead of inheriting a
                // stale Save As operation.
                await PressHandled(window, key, modifiers, physicalKey);
                pickerCalls.Should().Be(2);
                window.Session.IsDirty.Should().BeTrue();
                window.Session.CurrentFilePath.Should().BeNull();
            }
            finally
            {
                window.WorkbookSaveAsPickerOverrideForTest = null;
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlainF12_AlwaysRoutesThroughSaveAsAndSettlesTheSelectedPath()
    {
        await Session.Dispatch(async () =>
        {
            var firstPath = Path.Combine(
                Path.GetTempPath(),
                $"freex-f12-first-{Guid.NewGuid():N}.fxl");
            var saveAsPath = Path.Combine(
                Path.GetTempPath(),
                $"freex-f12-save-as-{Guid.NewGuid():N}.fxl");
            var window = new MainWindow([]);
            try
            {
                Edit(window, window.Session.ActiveCell, "first-path");
                window.WorkbookSaveAsPickerOverrideForTest = _ =>
                    Task.FromResult<MainWindow.WorkbookSaveAsPickerSelection?>(
                        window.CreateTransientWorkbookSaveAsSelection(firstPath));
                await PressHandled(window, Key.S, KeyModifiers.Control);

                var saveAsAddress = new CellAddress(
                    window.Session.ActiveCell.Sheet,
                    window.Session.ActiveCell.Row + 1,
                    window.Session.ActiveCell.Col);
                Edit(window, saveAsAddress, "plain-f12-save-as");
                var pickerCalls = 0;
                window.WorkbookSaveAsPickerOverrideForTest = _ =>
                {
                    pickerCalls++;
                    return Task.FromResult<MainWindow.WorkbookSaveAsPickerSelection?>(
                        window.CreateTransientWorkbookSaveAsSelection(saveAsPath));
                };

                await PressHandled(window, Key.F24, KeyModifiers.None, PhysicalKey.F12);

                pickerCalls.Should().Be(1);
                window.Session.IsDirty.Should().BeFalse();
                Path.GetFullPath(window.Session.CurrentFilePath!).Should().Be(Path.GetFullPath(saveAsPath));
                File.Exists(firstPath).Should().BeTrue();
                ReadValue(
                        saveAsPath,
                        window.Session.ActiveSheet.Name,
                        saveAsAddress.Row,
                        saveAsAddress.Col)
                    .Should().Be(new TextValue("plain-f12-save-as"));
            }
            finally
            {
                window.WorkbookSaveAsPickerOverrideForTest = null;
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
                File.Delete(firstPath);
                File.Delete(saveAsPath);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TransformedControlF12_OpensTheDirtyWorkbookGateBeforeNativeOpenPicker()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                Edit(window, window.Session.ActiveCell, "unsaved-before-open");
                var args = new KeyEventArgs
                {
                    Key = Key.F24,
                    PhysicalKey = PhysicalKey.F12,
                    KeyModifiers = KeyModifiers.Control,
                };

                var dispatch = window.RaiseKeyDownForTest(args);
                global::Avalonia.Controls.Window? dialog = null;
                for (var attempt = 0; dialog is null && !dispatch.IsCompleted && attempt < 300; attempt++)
                {
                    dialog = window.OwnedWindows.FirstOrDefault(
                        candidate => candidate.Title == "Open Workbook");
                    if (dialog is null)
                        await Task.Delay(10);
                }

                if (dialog is null && dispatch.IsCompleted)
                    await dispatch;

                dialog.Should().NotBeNull("Ctrl+physical-F12 must reach the Open Workbook workflow before the native picker opens");
                dialog!.Close();
                await dispatch;
                args.Handled.Should().BeTrue();
                window.Session.IsDirty.Should().BeTrue();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                    owned.Close();
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TransformedControlShiftF12_EntersAndSettlesBackstagePrintPane()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var args = new KeyEventArgs
                {
                    Key = Key.F24,
                    PhysicalKey = PhysicalKey.F12,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                };
                await window.RaiseKeyDownForTest(args);

                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
                window.ActiveBackstagePaneForTest.Should().Be(FreeXBackstagePaneId.Print);
                args.Handled.Should().BeTrue();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                    owned.Close();
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TransformedControlShiftF12_AfterCanceledOpenDirtyGate_EntersBackstagePrintPane()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                Edit(window, window.Session.ActiveCell, "dirty-before-open");

                var openArgs = new KeyEventArgs
                {
                    Key = Key.F12,
                    PhysicalKey = PhysicalKey.F12,
                    KeyModifiers = KeyModifiers.Control,
                };
                var openDispatch = window.RaiseKeyDownForTest(openArgs);
                var dirtyGate = await WaitForOwnedWindowAsync(window, candidate => candidate.Title == "Open Workbook");
                dirtyGate.Should().NotBeNull("Ctrl+physical-F12 should open the dirty Open confirmation");
                dirtyGate!.Close();

                var printArgs = new KeyEventArgs
                {
                    Key = Key.F24,
                    PhysicalKey = PhysicalKey.F12,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                };
                var printDispatch = window.RaiseKeyDownForTest(printArgs);
                await Task.WhenAll(openDispatch, printDispatch);
                openArgs.Handled.Should().BeTrue();
                printArgs.Handled.Should().BeTrue();
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();
                window.ActiveBackstagePaneForTest.Should().Be(FreeXBackstagePaneId.Print);
                window.OwnedWindows.Should().BeEmpty();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                    owned.Close();
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void Edit(MainWindow window, CellAddress address, string text)
    {
        var result = window.Session.ExecuteReviewCommand(
            EditCellsCommand.ForValue(address.Sheet, address, new TextValue(text)),
            address);
        result.Success.Should().BeTrue(result.ErrorMessage);
        window.Session.IsDirty.Should().BeTrue();
    }

    private static ScalarValue ReadValue(string path, string sheetName, uint row, uint col)
    {
        using var stream = File.OpenRead(path);
        var workbook = new NativeJsonAdapter().Load(stream);
        var sheet = workbook.GetSheet(sheetName)!;
        return sheet.GetValue(new CellAddress(sheet.Id, row, col));
    }

    private static async Task PressHandled(
        MainWindow window,
        Key key,
        KeyModifiers modifiers,
        PhysicalKey physicalKey = PhysicalKey.None)
    {
        var args = new KeyEventArgs
        {
            Key = key,
            KeyModifiers = modifiers,
            PhysicalKey = physicalKey,
        };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue();
    }

    private static async Task<global::Avalonia.Controls.Window?> WaitForOwnedWindowAsync(
        MainWindow window,
        Func<global::Avalonia.Controls.Window, bool> predicate)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var candidate = window.OwnedWindows.FirstOrDefault(predicate);
            if (candidate is not null)
                return candidate;

            await Task.Delay(10);
        }

        return null;
    }
}
