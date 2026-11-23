using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsOptionTextInputTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(KeyModifiers.Alt, "é", true)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift, "É", true)]
    [InlineData(KeyModifiers.Alt, "´", true)]
    [InlineData(KeyModifiers.Alt, "£", true)]
    [InlineData(KeyModifiers.Alt, "\u00a0", true)]
    [InlineData(KeyModifiers.Alt, null, false)]
    [InlineData(KeyModifiers.Alt, "\t", false)]
    [InlineData(KeyModifiers.Alt, "\uf702", false)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Meta, "é", false)]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Control, "é", false)]
    [InlineData(KeyModifiers.None, "é", false)]
    public void OptionTextPolicy_PreservesCompositionAndNavigation(
        KeyModifiers modifiers, string? symbol, bool expected)
    {
        MainWindow.ShouldPassOptionTextToInput(true, modifiers, symbol, false).Should().Be(expected);
        MainWindow.ShouldPassOptionTextToInput(false, modifiers, symbol, false).Should().BeFalse(
            "Windows and Linux retain their Alt accelerators");
        MainWindow.ShouldPassOptionTextToInput(true, modifiers, symbol, true).Should().BeFalse(
            "an explicitly entered keytip mode retains its routing");
    }

    [Theory]
    [InlineData(Key.E, "´")]
    [InlineData(Key.D3, "£")]
    [InlineData(Key.H, "˙")]
    [InlineData(Key.OemPlus, "≠")]
    public Task NativeOptionText_DoesNotEnterRibbonOrQuickAccessKeyTips(Key key, string symbol) =>
        Run(async window =>
        {
            await Press(window, Key.LeftAlt, KeyModifiers.Alt);
            var option = await Press(window, key, KeyModifiers.Alt, symbol);
            option.Handled.Should().BeFalse("the native input method must receive the Option key chord");
            window.RibbonKeyTipInputForTest.Should().BeEmpty();
            window.QuickAccessKeyTipInputForTest.Should().BeEmpty();
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            window.OwnedWindows.Should().BeEmpty();
            window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse(
                "typing an Option chord cancels the standalone Option tap candidate");
        });

    [Fact]
    public Task OptionDeadKeyComposition_CanStartAWorksheetEdit() =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            window.Session.ActiveSheet.SetCell(address, new NumberValue(42));
            (await Press(window, Key.E, KeyModifiers.Alt, "´")).Handled.Should().BeFalse();

            // The native input method must find a real editor before processing the dead key,
            // rather than have the first text packet move focus out of the ready worksheet.
            window.Session.FormulaEditAddress.Should().Be(address);
            window.FormulaBoxTextForTest.Should().BeEmpty();
            var editor = window.FocusManager!.GetFocusedElement().Should().BeAssignableTo<TextBox>().Subject;
            editor.Should().NotBeSameAs(FormulaEditor(window), "ready-cell typing belongs to the inline worksheet editor");
            editor.IsEffectivelyVisible.Should().BeTrue();
            editor.IsFocused.Should().BeTrue();
            window.InlineCellEditorTextForTest.Should().BeEmpty();
            window.Session.ActiveSheet.GetValue(address).Should().Be(new NumberValue(42));
            window.Session.IsDirty.Should().BeFalse();
            (await Press(window, Key.E, KeyModifiers.None, "e")).Handled.Should().BeFalse();

            // AppKit owns dead-key composition; send its composed result through the focused
            // editor's real TextInput route, not a synthetic packet on the worksheet host.
            window.KeyTextInput("é");

            window.Session.FormulaEditAddress.Should().Be(address);
            window.FormulaBoxTextForTest.Should().Be("é");
        });

    [Fact]
    public Task OptionDeadKey_InExistingFormulaEdit_PreservesTextAndSelection() =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            window.BeginFormulaEditForTest(address, "Existing text");
            var editor = FormulaEditor(window);
            editor.Focus().Should().BeTrue();
            editor.SelectionStart = 1;
            editor.SelectionEnd = 4;

            var option = await Press(window, Key.E, KeyModifiers.Alt, "´", editor);

            option.Handled.Should().BeFalse();
            window.Session.FormulaEditAddress.Should().Be(address);
            editor.Text.Should().Be("Existing text");
            editor.SelectionStart.Should().Be(1);
            editor.SelectionEnd.Should().Be(4);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(editor);
        });

    [Fact]
    public Task OptionDeadKey_InNameBox_DoesNotStartOrOverwriteWorksheetEditing() =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            var editor = (TextBox)typeof(MainWindow)
                .GetField("_cellAddressText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            editor.Focus().Should().BeTrue();
            editor.Text = "C3";
            editor.SelectionStart = 0;
            editor.SelectionEnd = 1;

            var option = await Press(window, Key.E, KeyModifiers.Alt, "´", editor);

            option.Handled.Should().BeFalse();
            window.Session.FormulaEditAddress.Should().BeNull();
            window.Session.ActiveCell.Should().Be(address);
            editor.Text.Should().Be("C3");
            editor.SelectionStart.Should().Be(0);
            editor.SelectionEnd.Should().Be(1);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(editor);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task OptionDeadKey_FocusesInlineEditorAndSupportsCancelAndCommit(bool showFormulaBar) =>
        Run(async window =>
        {
            var address = window.Session.ActiveCell;
            var sheet = window.Session.ActiveSheet;
            sheet.SetCell(address, new NumberValue(42));

            (await Press(window, Key.E, KeyModifiers.Alt, "´")).Handled.Should().BeFalse();

            window.Session.FormulaEditAddress.Should().Be(address);
            window.InlineCellEditorTextForTest.Should().BeEmpty();
            var editor = window.FocusManager!.GetFocusedElement().Should().BeAssignableTo<TextBox>().Subject;
            editor.Should().NotBeSameAs(FormulaEditor(window), "ready-cell input uses the inline editor in both visibility modes");
            editor.IsEffectivelyVisible.Should().BeTrue();
            editor.IsFocused.Should().BeTrue("AppKit must have its visible editor before TextInput");
            window.KeyTextInput("é");
            window.InlineCellEditorTextForTest.Should().Be("é");
            sheet.GetValue(address).Should().Be(new NumberValue(42), "the composed value is still pending");

            // A second dead key must not recreate the editor or erase its pending text/caret.
            editor.SelectionStart = editor.SelectionEnd = 1;
            (await Press(window, Key.E, KeyModifiers.Alt, "´", editor)).Handled.Should().BeFalse();
            window.InlineCellEditorTextForTest.Should().Be("é");
            window.FocusManager.GetFocusedElement().Should().BeSameAs(editor);
            editor.SelectionStart.Should().Be(1);
            editor.SelectionEnd.Should().Be(1);
            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
            window.Session.FormulaEditAddress.Should().BeNull();
            sheet.GetValue(address).Should().Be(new NumberValue(42));

            (await Press(window, Key.E, KeyModifiers.Alt, "´")).Handled.Should().BeFalse();
            window.KeyTextInput("é");
            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Enter });
            window.Session.FormulaEditAddress.Should().BeNull();
            sheet.GetValue(address).Should().Be(new TextValue("é"));
        }, showFormulaBar);

    [Fact]
    public Task F10AndStandaloneOption_StillEnterKeyTipMode() =>
        Run(async window =>
        {
            (await Press(window, Key.F10, KeyModifiers.None)).Handled.Should().BeTrue();
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
            (await Press(window, Key.H, KeyModifiers.None, "h")).Handled.Should().BeTrue();
            window.RibbonKeyTipInputForTest.Should().Be("H");
            await Press(window, Key.Escape, KeyModifiers.None);

            await Press(window, Key.LeftAlt, KeyModifiers.Alt);
            window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
            (await Press(window, Key.H, KeyModifiers.Alt, "˙")).Handled.Should().BeTrue();
            window.RibbonKeyTipInputForTest.Should().Be("H");
        });

    private static async Task<KeyEventArgs> Press(
        MainWindow window, Key key, KeyModifiers modifiers, string? symbol = null, Control? source = null)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers, KeySymbol = symbol, Source = source };
        await window.RaiseKeyDownForTest(args);
        return args;
    }

    private static TextBox FormulaEditor(MainWindow window) =>
        (TextBox)typeof(MainWindow).GetField("_formulaBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static Task Run(Func<MainWindow, Task> action, bool showFormulaBar = true)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.CompletedTask;

        return Session.Dispatch(async () =>
        {
            var options = new AppOptions { ShowFormulaBar = showFormulaBar };
            var optionsSession = new FreeXOptionsRuntimeSession(
                initialOptions: options,
                load: () => options.Clone(),
                save: _ => true);
            var window = new MainWindow([], null!, optionsSession);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("Option input");
                window.Session.SelectSheet(sheet.Id);
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                await action(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
