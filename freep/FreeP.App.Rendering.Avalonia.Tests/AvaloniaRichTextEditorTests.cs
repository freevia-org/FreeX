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
    public async Task NativeInputIsTopmostHitTarget_WhileCustomSurfaceOwnsVisibleCaretAndSelection()
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
                editor.InputBox.CaretBrush.Should().Be(Brushes.Transparent);
                editor.InputBox.SelectionBrush.Should().Be(Brushes.Transparent);
                editor.InputBox.SelectionForegroundBrush.Should().Be(Brushes.Transparent);
                editor.InputBox.Foreground.Should().Be(Brushes.Transparent);

                editor.InputBox.SelectionStart = 1;
                editor.InputBox.SelectionEnd = 5;
                editor.InputBox.SelectionStart.Should().NotBe(editor.InputBox.SelectionEnd);
                editor.InputBox.CaretIndex.Should().BeInRange(1, 5);
                editor.RichTextView.SelectionRects.Should().NotBeEmpty();

                editor.InputBox.SelectionStart = 4;
                editor.InputBox.SelectionEnd = 4;
                editor.RichTextView.CaretRect.Height.Should().BeGreaterThan(0);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LeftEdgeHitTestingAndNewlineSelection_KeepModelOffsetsStable()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(EvidenceBody(), backgroundAlpha: 0xFF)
            {
                Width = 420,
                Height = 180,
            };
            var window = Show(editor, 420, 180);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                await DrainInputAsync();

                editor.RichTextView.HitTestLogicalPosition(new Point(5, 10)).Should().Be(0,
                    "left-edge hit testing must map directly to the first model text offset");

                int secondParagraphStart = editor.RichTextView.VisualPlan.Paragraphs[1].GlobalStart;
                editor.SelectionStart = 2;
                editor.SelectionEnd = secondParagraphStart + 3;
                editor.RichTextView.SelectionRects.Should().HaveCountGreaterThanOrEqualTo(2,
                    "selection crossing the model newline must paint both paragraphs");
                editor.Text.Substring(editor.SelectionStart, editor.SelectionEnd - editor.SelectionStart)
                    .Should().Contain("\n");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MixedSizeWrappedLines_DriveCaretSelectionAndVerticalNavigationGeometry()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(EvidenceBody(), backgroundAlpha: 0xFF)
            {
                Width = 180,
                Height = 220,
            };
            var window = Show(editor, 180, 220);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 11;
                editor.SelectionEnd = 11;
                await DrainInputAsync();

                var caret = editor.RichTextView.CaretRect;
                caret.Height.Should().BeGreaterThan(25,
                    "the caret height must come from the 28pt rendered run, not the uniform input font");
                int down = editor.RichTextView.MoveCaretVertically(11, 1);
                down.Should().NotBe(11);

                editor.SelectionStart = 4;
                editor.SelectionEnd = 16;
                editor.RichTextView.SelectionRects.Should().HaveCountGreaterThan(1,
                    "mixed-size wrapped selection follows the same TextLayout line boxes as the glyphs");
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

                editor.SelectionStart = 2;
                editor.SelectionEnd = 4;
                Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Shift);
                await DrainInputAsync();
                new[] { editor.SelectionStart, editor.SelectionEnd }.Order()
                    .Should().Equal(
                        [0, 2],
                        "shift navigation keeps the existing selection anchor instead of a stale pointer anchor");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Window Show(Control content, double width = 320, double height = 90)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
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

    private static TextBody EvidenceBody()
    {
        var body = new TextBody { DefaultParaAlign = TextAlign.Left };
        body.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Left,
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            Runs =
            {
                new Run { Text = "Small text ", FontFamily = "Arial", FontSizePt = 11 },
                new Run { Text = "LARGE TEXT", FontFamily = "Georgia", FontSizePt = 28, Bold = true },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Align = TextAlign.Center,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            Runs = { new Run { Text = "Centered numbered paragraph", FontFamily = "Calibri", FontSizePt = 16, Italic = true } },
        });
        return body;
    }

}
