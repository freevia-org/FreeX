using System.Reflection;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorksheetReadyTextInputTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ReadyWorksheetText_StartsInlineEditAndKeepsRawAndFocusedPackets(bool showFormulaBar) =>
        Run(showFormulaBar, async window =>
        {
            var address = window.Session.ActiveCell;
            var sheet = window.Session.ActiveSheet;
            sheet.SetCell(address, new NumberValue(42));

            RaiseWorksheetText(window, "New").Handled.Should().BeTrue();
            var editor = FocusedInlineEditor(window);
            window.Session.FormulaEditAddress.Should().Be(address);
            window.InlineCellEditorTextForTest.Should().Be("New");
            sheet.GetValue(address).Should().Be(new NumberValue(42));

            // The next packet can still carry the old worksheet source while native focus is
            // handing off. It must append once, rather than restart the edit or lose the packet.
            RaiseWorksheetText(window, " value").Handled.Should().BeTrue();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(editor);
            window.InlineCellEditorTextForTest.Should().Be("New value");

            window.KeyTextInput("!");
            await DrainInputAsync();
            window.InlineCellEditorTextForTest.Should().Be("New value!");
            window.FormulaBoxTextForTest.Should().Be("New value!");
            sheet.GetValue(address).Should().Be(new NumberValue(42), "typing stays pending until a commit key");
            window.Session.IsDirty.Should().BeFalse();
        });

    [Theory]
    [InlineData(true, Key.Enter, PhysicalKey.Enter, 2u, 1u)]
    [InlineData(false, Key.Enter, PhysicalKey.Enter, 2u, 1u)]
    [InlineData(true, Key.Tab, PhysicalKey.Tab, 1u, 2u)]
    [InlineData(false, Key.Tab, PhysicalKey.Tab, 1u, 2u)]
    public Task ReadyTyping_CommitKeepsEveryPacketAndMovesToTheNextCell(
        bool showFormulaBar, Key commitKey, PhysicalKey physicalKey, uint nextRow, uint nextColumn) =>
        Run(showFormulaBar, async window =>
        {
            var sheet = window.Session.ActiveSheet;
            var address = window.Session.ActiveCell;
            var next = new CellAddress(sheet.Id, nextRow, nextColumn);
            var previousNextValue = sheet.GetValue(next);
            sheet.SetCell(address, new NumberValue(42));
            RaiseWorksheetText(window, "Com");
            FocusedInlineEditor(window);
            window.KeyTextInput("mitted");
            await DrainInputAsync();

            Press(window, commitKey, physicalKey);
            await DrainInputAsync();

            window.Session.FormulaEditAddress.Should().BeNull();
            sheet.GetValue(address).Should().Be(new TextValue("Committed"));
            sheet.GetValue(next).Should().Be(previousNextValue);
            window.Session.ActiveCell.Should().Be(next);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(window.ActiveCellBorderForTest);

            // Typing immediately after Enter/Tab must start a fresh edit at the new active cell.
            window.KeyTextInput("Next");
            await DrainInputAsync();
            window.Session.FormulaEditAddress.Should().Be(next);
            window.InlineCellEditorTextForTest.Should().Be("Next");
            FocusedInlineEditor(window);
            sheet.GetValue(address).Should().Be(new TextValue("Committed"));
            Press(window, Key.Escape, PhysicalKey.Escape);
            await DrainInputAsync();
            sheet.GetValue(next).Should().Be(previousNextValue);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ReadyTyping_EscapeRestoresTheCellAndTheNextEditStartsFresh(bool showFormulaBar) =>
        Run(showFormulaBar, async window =>
        {
            var address = window.Session.ActiveCell;
            var sheet = window.Session.ActiveSheet;
            sheet.SetCell(address, new NumberValue(42));
            RaiseWorksheetText(window, "Dis");
            window.KeyTextInput("card me");
            await DrainInputAsync();
            window.InlineCellEditorTextForTest.Should().Be("Discard me");

            Press(window, Key.Escape, PhysicalKey.Escape);
            await DrainInputAsync();

            window.Session.FormulaEditAddress.Should().BeNull();
            window.Session.ActiveCell.Should().Be(address);
            sheet.GetValue(address).Should().Be(new NumberValue(42));
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(window.ActiveCellBorderForTest);
            window.Session.IsDirty.Should().BeFalse();

            window.KeyTextInput("Fresh");
            await DrainInputAsync();
            window.Session.FormulaEditAddress.Should().Be(address);
            window.InlineCellEditorTextForTest.Should().Be("Fresh");
            FocusedInlineEditor(window);
            Press(window, Key.Escape, PhysicalKey.Escape);
            await DrainInputAsync();
            sheet.GetValue(address).Should().Be(new NumberValue(42));
        });

    private static TextBox FocusedInlineEditor(MainWindow window)
    {
        var editor = window.FocusManager!.GetFocusedElement().Should().BeAssignableTo<TextBox>().Subject;
        var formulaEditor = typeof(MainWindow)
            .GetField("_formulaBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        editor.Should().NotBeSameAs(formulaEditor, "ordinary ready-cell typing must stay in the worksheet");
        editor.IsEffectivelyVisible.Should().BeTrue();
        editor.IsFocused.Should().BeTrue();
        return editor;
    }

    private static TextInputEventArgs RaiseWorksheetText(MainWindow window, string text)
    {
        var args = new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = window.SheetGridHostForTest,
            Text = text,
        };
        window.SheetGridHostForTest.RaiseEvent(args);
        return args;
    }

    private static void Press(MainWindow window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static Task Run(bool showFormulaBar, Func<MainWindow, Task> test) =>
        Session.Dispatch(async () =>
        {
            var options = new AppOptions { ShowFormulaBar = showFormulaBar };
            var optionsSession = new FreeXOptionsRuntimeSession(
                initialOptions: options,
                load: () => options.Clone(),
                save: _ => true);
            var window = new MainWindow([], null!, optionsSession);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("Ready text input");
                window.Session.SelectSheet(sheet.Id);
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                await test(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
}
