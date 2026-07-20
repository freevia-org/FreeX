using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaRichTextEditorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    [Fact]
    public async Task NativeInputIsTopmostHitTarget_WithVisibleCaretAndSelectionBrushes()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(MixedBody(), backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.Children.Should().ContainInOrder(editor.RichTextView, editor.InputBox);
                editor.RichTextView.IsHitTestVisible.Should().BeFalse();
                editor.InputBox.IsHitTestVisible.Should().BeTrue();

                window.MouseMove(new Point(40, 30), RawInputModifiers.None);
                window.MouseDown(
                    new Point(40, 30),
                    MouseButton.Left,
                    RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(40, 30), MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.InputBox.IsFocused.Should().BeTrue(
                    "the transparent native input must remain above the rich rendering layer");
                editor.InputBox.CaretBrush.Should().Be(Brushes.Black);
                editor.InputBox.SelectionBrush.Should().BeOfType<SolidColorBrush>()
                    .Which.Color.A.Should().BeGreaterThan(0);
                editor.InputBox.SelectionForegroundBrush.Should().Be(Brushes.Transparent);
                editor.InputBox.Foreground.Should().Be(Brushes.Transparent);

                editor.InputBox.SelectionStart = 1;
                editor.InputBox.SelectionEnd = 5;
                editor.InputBox.SelectionStart.Should().NotBe(editor.InputBox.SelectionEnd);
                editor.InputBox.CaretIndex.Should().BeInRange(1, 5);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImeLikeReplacementClipboardAndLocalUndoRedo_KeepRichBufferSynchronized()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(MixedBody(), backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 0;
                editor.SelectionEnd = 4;

                RaiseRawTextInput(editor.InputBox, "\u65e5\u672c");
                await DrainInputAsync();

                editor.Text.Should().Be("\u65e5\u672cItalic");
                editor.EditedBody.Paragraphs[0].Runs[0].Text.Should().Be("\u65e5\u672c");
                editor.EditedBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
                editor.InputBox.CanUndo.Should().BeTrue();

                Press(window, Key.Z, PhysicalKey.Z, RawInputModifiers.Control);
                await DrainInputAsync();
                editor.Text.Should().Be("BoldItalic");
                InCanvasTextEditPlanner.ExtractPlainText(editor.EditedBody).Should().Be("BoldItalic");

                Press(window, Key.Y, PhysicalKey.Y, RawInputModifiers.Control);
                await DrainInputAsync();
                editor.Text.Should().Be("\u65e5\u672cItalic");
                editor.EditedBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue();

                editor.SelectionStart = 0;
                editor.SelectionEnd = 2;
                Press(window, Key.C, PhysicalKey.C, RawInputModifiers.Control);
                await DrainInputAsync();
                editor.SelectionStart = editor.Text.Length;
                editor.SelectionEnd = editor.Text.Length;
                Press(window, Key.V, PhysicalKey.V, RawInputModifiers.Control);
                await DrainInputAsync();

                editor.Text.Should().Be("\u65e5\u672cItalic\u65e5\u672c");
                InCanvasTextEditPlanner.ExtractPlainText(editor.EditedBody)
                    .Should().Be("\u65e5\u672cItalic\u65e5\u672c");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeArrowHomeAndEndKeys_MoveTheCaretThroughRichText()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(MixedBody(), backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 4;
                editor.SelectionEnd = 4;

                Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.None);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(3);

                Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.None);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(4);

                Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.None);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(0);

                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.None);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(editor.Text.Length);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Window Show(Control content)
    {
        var window = new Window
        {
            Width = 320,
            Height = 90,
            Content = content,
        };
        window.Show();
        window.Measure(new Size(320, 90));
        window.Arrange(new Rect(0, 0, 320, 90));
        return window;
    }

    private static void Press(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static void RaiseRawTextInput(InputElement target, string text) =>
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = target,
            Text = text,
        });

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static TextBody MixedBody()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = "Bold", Bold = true },
                new Run { Text = "Italic", Italic = true },
            },
        });
        return body;
    }
}
