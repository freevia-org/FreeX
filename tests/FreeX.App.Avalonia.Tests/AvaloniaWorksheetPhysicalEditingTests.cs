using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetPhysicalEditingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RawF2PerCharacterTextInputAndEnter_KeepInlineEditorAttachedAndCommitCompleteText()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var address = window.Session.ActiveCell;
                window.ActiveCellBorderForTest.Should().NotBeNull();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                editor.IsFocused.Should().BeTrue("F2 must finish the physical focus handoff before X11 sends the first character");

                var rawTarget = window.ActiveCellBorderForTest!;
                foreach (var character in "X11InlineCommit")
                {
                    rawTarget.Focus().Should().BeTrue();
                    RaiseRawTextInput(rawTarget, character.ToString());
                    await DrainInputAsync();

                    FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor").Should().BeSameAs(editor);
                }

                editor.Text.Should().Be("X11InlineCommit");

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(address).Should().Be(new TextValue("X11InlineCommit"));
                window.InlineCellEditorTextForTest.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawF2PerCharacterTextInputAndEscape_RestoresCommittedCellAndFormulaBar()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var address = window.Session.ActiveCell;
                sheet.SetCell(address, new TextValue("Original"));
                Refresh(window);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor").IsFocused.Should().BeTrue();

                var rawTarget = window.ActiveCellBorderForTest!;
                foreach (var character in "X11Cancel")
                {
                    rawTarget.Focus().Should().BeTrue();
                    RaiseRawTextInput(rawTarget, character.ToString());
                    await DrainInputAsync();
                }

                FindByAutomationId<TextBox>(window, "FormulaBox").Focus().Should().BeTrue();
                Press(window, Key.Escape, PhysicalKey.Escape);
                await DrainInputAsync();

                sheet.GetValue(address).Should().Be(new TextValue("Original"));
                window.FormulaBoxTextForTest.Should().Be("Original");
                window.InlineCellEditorTextForTest.Should().BeNull();
                window.Session.FormulaEditAddress.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawInlinePointModeClick_InsertsReferenceAndCommitsFormula()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                window.ActiveCellBorderForTest!.Focus();
                Press(window, Key.F2, PhysicalKey.F2);
                await DrainInputAsync();

                window.KeyTextInput("=");
                await DrainInputAsync();
                window.FormulaPointModeForTest.Should().BeTrue();

                ClickCell(window, "Cell_B2");
                await DrainInputAsync();

                window.InlineCellEditorTextForTest.Should().Be("=B2");
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("B2");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RawFormulaBarPointModeClick_AfterF2Toggle_InsertsReferenceAndCommitsFormula()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var formulaAddress = window.Session.ActiveCell;
                var formulaBox = FindByAutomationId<TextBox>(window, "FormulaBox");
                formulaBox.Focus().Should().BeTrue();
                await DrainInputAsync();

                formulaBox.SelectAll();
                window.KeyTextInput("=");
                await DrainInputAsync();
                window.FormulaPointModeForTest.Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                window.FormulaPointModeForTest.Should().BeFalse();
                Press(window, Key.F2, PhysicalKey.F2);
                window.FormulaPointModeForTest.Should().BeTrue();

                ClickCell(window, "Cell_B2");
                await DrainInputAsync();

                formulaBox.Text.Should().Be("=B2");
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("B2");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("PhysicalEditingFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static void Press(MainWindow window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
    }

    private static void RaiseRawTextInput(InputElement target, string text) =>
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = target,
            Text = text,
        });

    private static void ClickCell(MainWindow window, string automationId)
    {
        var cell = FindByAutomationId<Border>(window, automationId);
        var translatedPoint = cell.TranslatePoint(
            new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2),
            window);
        translatedPoint.Should().NotBeNull();
        var point = translatedPoint!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static T FindByAutomationId<T>(MainWindow window, string automationId)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
