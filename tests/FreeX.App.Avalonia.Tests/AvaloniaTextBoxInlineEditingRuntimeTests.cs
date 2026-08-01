using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless production proof for the real Avalonia drawing text-box editor. These tests exercise
/// the mounted worksheet TextBox and its command-backed commit path, rather than only inspecting
/// source text or a planner in isolation.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaTextBoxInlineEditingRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task BeginTextBoxInlineEdit_MountsRealMultilineEditorAtScaledObjectBoundsWithoutDrawingTransform()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var textBox = AddTextBox(window, "Before", width: 200, height: 80, rotation: 18);

                window.BeginTextBoxInlineEditForTest(textBox.Id);
                var renderedGrid = window.RebuildSheetGridForTest();
                var editor = FindByAutomationId<TextBox>(renderedGrid, "TextBoxInlineEditor");
                var chrome = FindByAutomationId<Border>(renderedGrid, "TextBoxInlineEditorChrome");

                editor.Should().NotBeNull();
                chrome.Should().NotBeNull();
                editor!.Text.Should().Be("Before");
                editor.AcceptsReturn.Should().BeTrue();
                editor.TextWrapping.Should().Be(TextWrapping.Wrap);
                editor.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty)
                    .Should().Be(ScrollBarVisibility.Auto);
                editor.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty)
                    .Should().Be(ScrollBarVisibility.Disabled);
                editor.RenderTransform.Should().BeNull();
                chrome!.RenderTransform.Should().BeNull();
                Canvas.GetLeft(editor).Should().BeGreaterThan(0);
                Canvas.GetTop(editor).Should().BeGreaterThan(0);
                editor.Width.Should().BeGreaterThan(0);
                editor.Height.Should().BeGreaterThan(0);
                AutomationProperties.GetAutomationId(editor).Should().Be("TextBoxInlineEditor");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ModifiedEnterRemainsMultiline_EnterCommitsAsOneUndoableCommand_AndEscapeCancelsLaterEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var textBox = AddTextBox(window, "Before", width: 180, height: 70);
                window.BeginTextBoxInlineEditForTest(textBox.Id);
                var editor = window.TextBoxInlineEditorForTest!;
                editor.Text = "First line";

                var modifiedEnter = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    KeyModifiers = KeyModifiers.Control,
                };
                window.RaiseTextBoxInlineEditorKeyDownForTest(modifiedEnter);
                modifiedEnter.Handled.Should().BeFalse();
                textBox.Text.Should().Be("Before");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeTrue();

                editor.Text = "First line\nSecond line";

                window.RaiseTextBoxInlineEditorKeyDownForTest(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    KeyModifiers = KeyModifiers.None,
                });

                textBox.Text.Should().Be("First line\nSecond line");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeFalse();
                window.Session.CanUndo.Should().BeTrue();
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                textBox.Text.Should().Be("Before");

                window.BeginTextBoxInlineEditForTest(textBox.Id);
                window.TextBoxInlineEditorForTest!.Text = "Cancelled";
                window.RaiseTextBoxInlineEditorKeyDownForTest(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    KeyModifiers = KeyModifiers.None,
                });

                textBox.Text.Should().Be("Before");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TabCommitsEditedTextBoxAndClosesEditor()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var textBox = AddTextBox(window, "Before", width: 180, height: 70);
                window.BeginTextBoxInlineEditForTest(textBox.Id);
                window.TextBoxInlineEditorForTest!.Text = "Tabbed";

                var tab = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Tab,
                    KeyModifiers = KeyModifiers.None,
                };
                window.RaiseTextBoxInlineEditorKeyDownForTest(tab);

                tab.Handled.Should().BeTrue();
                textBox.Text.Should().Be("Tabbed");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LostFocusCommitsAfterDeferredFocusSettlement()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var textBox = AddTextBox(window, "Before", width: 180, height: 70);
                window.BeginTextBoxInlineEditForTest(textBox.Id);
                var editor = window.TextBoxInlineEditorForTest!;
                editor.Text = "After focus loss";
                editor.IsFocused.Should().BeTrue();

                window.FocusManager!.Focus(window.SheetGridHostForTest).Should().BeTrue();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                textBox.Text.Should().Be("After focus loss");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeFalse();
                window.FocusManager.GetFocusedElement().Should().NotBe(editor);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InsertTextBoxStartsEmptyInlineEditor()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                window.InsertTextBoxAtActiveCellForTest();

                var textBox = window.Session.ActiveSheet.TextBoxes.Should().ContainSingle().Subject;
                window.IsTextBoxInlineEditorActiveForTest.Should().BeTrue();
                window.TextBoxInlineEditorForTest!.Text.Should().BeEmpty();
                window.SelectedDrawingObjectIdForTest.Should().Be(textBox.Id);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PendingFormulaEditCommitsBeforeTextBoxEditorStarts()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var sheet = window.Session.ActiveSheet;
                var formulaAddress = window.Session.ActiveCell;
                window.BeginFormulaEditForTest(formulaAddress, "Original");
                window.FormulaBoxTextForTest = "Pending";
                var textBox = AddTextBox(window, "Before", width: 180, height: 70, row: 5, col: 5);

                window.BeginTextBoxInlineEditForTest(textBox.Id);

                sheet.GetValue(formulaAddress).Should().Be(new TextValue("Pending"));
                window.Session.FormulaEditAddress.Should().BeNull();
                window.IsTextBoxInlineEditorActiveForTest.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ViewportPanRebuildKeepsEditorMountedPositionedAndFocused()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateShownWindow();
            try
            {
                var textBox = AddTextBox(window, "Before", width: 180, height: 70, row: 5, col: 5);
                window.BeginTextBoxInlineEditForTest(textBox.Id);
                var editor = window.TextBoxInlineEditorForTest!;
                var initialLeft = Canvas.GetLeft(editor);
                var initialTop = Canvas.GetTop(editor);

                window.Session.PanViewport(2, 2).Should().BeTrue();
                window.RefreshShellForViewportPanForTest();

                var remounted = FindByAutomationId<TextBox>(window, "TextBoxInlineEditor");
                remounted.Should().BeSameAs(editor);
                remounted!.IsVisible.Should().BeTrue();
                remounted.IsFocused.Should().BeTrue();
                Canvas.GetLeft(remounted).Should().NotBe(initialLeft);
                Canvas.GetTop(remounted).Should().NotBe(initialTop);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(remounted);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ProtectedTextBox_LeavesEditorOpenAndTextUnchangedWhenCommitIsRejected()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var textBox = AddTextBox(window, "Protected", width: 180, height: 70);
                var sheet = window.Session.ActiveSheet;
                sheet.IsProtected = true;
                sheet.ProtectionPermissions.Clear();
                sheet.ProtectionPermissions.Add(SheetProtectionPermission.SelectLockedCells);

                window.BeginTextBoxInlineEditForTest(textBox.Id);
                window.TextBoxInlineEditorForTest!.Text = "Rejected";
                window.RaiseTextBoxInlineEditorKeyDownForTest(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    KeyModifiers = KeyModifiers.None,
                });

                textBox.Text.Should().Be("Protected");
                window.IsTextBoxInlineEditorActiveForTest.Should().BeTrue();
                window.RaiseTextBoxInlineEditorKeyDownForTest(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    KeyModifiers = KeyModifiers.None,
                });
                window.IsTextBoxInlineEditorActiveForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static TextBoxModel AddTextBox(
        MainWindow window,
        string text,
        double width,
        double height,
        double rotation = 0,
        uint row = 2,
        uint col = 2)
    {
        var sheet = window.Session.ActiveSheet;
        sheet.ShowHeadings = false;
        var textBox = new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, row, col),
            Height = height,
            RotationDegrees = rotation,
            Text = text,
            Width = width,
        };
        sheet.TextBoxes.Add(textBox);
        window.Session.UpdateViewportSize(700, 1000);
        return textBox;
    }

    private static MainWindow CreateShownWindow()
    {
        var window = new MainWindow([]);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        window.Session.ActiveSheet.TextBoxes.Clear();
        return window;
    }

    private static T? FindByAutomationId<T>(Control? root, string automationId)
        where T : Control =>
        root is null
            ? null
            : root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
}
