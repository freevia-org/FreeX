using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaRichTextEditorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    [Fact]
    public async Task SelectedRunHyperlink_UsesRichTextBufferAndRoundTripsThroughEditedBody()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run { Text = "AlphaBeta" } },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 1;
                editor.SelectionEnd = 6;
                editor.ApplyHyperlink(new Hyperlink
                {
                    TargetSlideId = "slide-2",
                    Tooltip = "Jump",
                }).Should().BeTrue();

                editor.SelectedRunHyperlink()!.TargetSlideId.Should().Be("slide-2");
                editor.EditedBody.Paragraphs[0].Runs
                    .Where(run => run.Text.Length > 0)
                    .Any(run => run.Hyperlink?.TargetSlideId == "slide-2")
                    .Should().BeTrue();

                editor.ApplyHyperlink(null).Should().BeTrue();
                editor.EditedBody.Paragraphs[0].Runs
                    .All(run => run.Hyperlink is null)
                    .Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPayload_PreservesAllModeledInlineEffectsFromAvaloniaBuffer()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run
                    {
                        Text = "effects",
                        TextFill = new ShapeFill.Solid(
                            new ThemeAwareColor(SrgbColor.FromRgb(0x336699), 0xC0)),
                        TextOutline = new ShapeOutline.Visible(
                            new ThemeAwareColor(SrgbColor.FromRgb(0x102030)),
                            widthPt: 1.5),
                        TextShadow = new RunTextShadow
                        {
                            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x202020)),
                            Alpha = 0x70,
                            BlurPt = 3.0,
                            DistPt = 2.0,
                            DirDeg = 45.0,
                        },
                        TextReflection = new RunTextReflection
                        {
                            Alpha = 0x60,
                            BlurPt = 1.0,
                            DistPt = 2.0,
                            DirDeg = 90.0,
                            ScaleY = -0.5,
                            EndPos = 0.75,
                        },
                        TextGlow = new RunTextGlow
                        {
                            Color = new ThemeAwareColor(SrgbColor.FromRgb(0xF0C000)),
                            Alpha = 0x90,
                            RadiusPt = 5.0,
                        },
                        TextSoftEdge = new RunTextSoftEdge { RadiusPt = 2.0 },
                    },
                },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;

                var payload = editor.CreateClipboardPayload();
                var decoded = InCanvasRichClipboardPlanner.Deserialize(
                    InCanvasRichClipboardPlanner.Serialize(payload));

                decoded.Should().NotBeNull();
                var run = decoded!.Body.Paragraphs.Single().Runs.Single();
                run.TextFill.Should().BeOfType<ShapeFill.Solid>();
                run.TextOutline.Should().BeOfType<ShapeOutline.Visible>();
                run.TextShadow.Should().NotBeNull();
                run.TextReflection.Should().NotBeNull();
                run.TextGlow.Should().NotBeNull();
                run.TextSoftEdge!.RadiusPt.Should().BeApproximately(2.0, 0.0001);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_UsesRtfBeforePlainTextAndUsesCustomPayloadBeforeRtf()
    {
        await Session.Dispatch(async () =>
        {
            AvaloniaRichTextEditor.ExternalRtfWindowsFormat.Identifier
                .Should().Be(PresentationClipboardFormats.WindowsRtf);
            AvaloniaRichTextEditor.ExternalRtfLinuxFormat.Identifier
                .Should().Be(PresentationClipboardFormats.LinuxRtf);

            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("target").Body,
                backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var rtfTransfer = new DataTransfer();
                var rtfItem = new DataTransferItem();
                rtfItem.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b Rich\b0\par text}"));
                rtfItem.SetText("plain");
                rtfTransfer.Add(rtfItem);

                (await editor.PasteDataTransferAsync(rtfTransfer)).Should().BeTrue();
                editor.Text.Should().Be("Rich\ntext");
                editor.EditedBody.Paragraphs[0].Runs.Single().Bold.Should().BeTrue();

                editor.Text = "target";
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var customTransfer = new DataTransfer();
                var customItem = new DataTransferItem();
                var customPayload = InCanvasRichClipboardPayload.FromPlainText("custom");
                customItem.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.RichTextPlatformFormat
                        : AvaloniaRichTextEditor.RichTextFormat,
                    InCanvasRichClipboardPlanner.Serialize(customPayload));
                customItem.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b ignored\b0}"));
                customItem.SetText("plain");
                customTransfer.Add(customItem);

                (await editor.PasteDataTransferAsync(customTransfer)).Should().BeTrue();
                editor.Text.Should().Be("custom");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_ExternalRtfAppliesSharedParagraphAndHyperlinkMetadata()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("target").Body,
                backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var transfer = new DataTransfer();
                var item = new DataTransferItem();
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(
                        @"{\rtf1\ansi\pard\qc\li360\sa80 {\field{\*\fldinst HYPERLINK ""https://example.com/paste""}{\fldrslt Linked}}}"));
                item.SetText("plain");
                transfer.Add(item);

                (await editor.PasteDataTransferAsync(transfer)).Should().BeTrue();
                var paragraph = editor.EditedBody.Paragraphs.Single();
                paragraph.Align.Should().Be(TextAlign.Center);
                paragraph.MarginLeftEmu.Should().Be(228600);
                paragraph.SpaceAfterPt.Should().Be(4);
                paragraph.Runs.Single().Hyperlink!.Url.Should().Be("https://example.com/paste");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_XamlPackagePrecedesRtfAndPlainText()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("target").Body,
                backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var transfer = new DataTransfer();
                var item = new DataTransferItem();
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalXamlPackageWindowsFormat
                        : AvaloniaRichTextEditor.ExternalXamlPackageLinuxFormat,
                    CreateXamlPackage(
                        "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Bold>Package</Bold><Italic> text</Italic></Paragraph></FlowDocument>"));
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b ignored\b0}"));
                item.SetText("plain fallback");
                transfer.Add(item);

                (await editor.PasteDataTransferAsync(transfer)).Should().BeTrue();
                editor.Text.Should().Be("Package text");
                editor.EditedBody.Paragraphs.Single().Runs[0].Bold.Should().BeTrue();
                editor.EditedBody.Paragraphs.Single().Runs[1].Italic.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_ExternalRtfTableUsesSharedWpfCompatibleRowCellProjection()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("target").Body,
                backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var transfer = new DataTransfer();
                var item = new DataTransferItem();
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(
                        @"{\rtf1\ansi\trowd\cellx1440\cellx2880\b A\b0\cell\i B\i0\cell\row
\trowd\cellx1440\cellx2880 C\cell{\ul D}\ul0\cell\row}"));
                item.SetText("plain fallback");
                transfer.Add(item);

                (await editor.PasteDataTransferAsync(transfer)).Should().BeTrue();
                editor.Text.Should().Be("A\tB\nC\tD");
                editor.EditedBody.Paragraphs[0].Runs.Should().Contain(run => run.Text == "A" && run.Bold);
                editor.EditedBody.Paragraphs[0].Runs.Should().Contain(run => run.Text == "B" && run.Italic);
                editor.EditedBody.Paragraphs[1].Runs.Should().Contain(run => run.Text == "D" && run.Underline);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_MalformedRtfFallsBackToPlainText()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("target").Body,
                backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.SelectionStart = 0;
                editor.SelectionEnd = editor.Text.Length;
                using var transfer = new DataTransfer();
                var item = new DataTransferItem();
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes("not an rtf payload"));
                item.SetText("plain fallback");
                transfer.Add(item);

                (await editor.PasteDataTransferAsync(transfer)).Should().BeTrue();
                editor.Text.Should().Be("plain fallback");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EnterSplit_PreservesNumberingMetadataThroughHostBuffer()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Level = 2,
                BulletKind = BulletKind.Auto,
                AutoNumType = AutoNumType.AlphaLcParenBoth,
                AutoNumStartAt = 3,
                AutoNumStartAtSpecified = true,
                Runs = { new Run { Text = "AlphaBeta" } },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.Text = "Alpha\nBeta";

                editor.EditedBody.Paragraphs.Should().HaveCount(2);
                editor.EditedBody.Paragraphs.Should().OnlyContain(paragraph =>
                    paragraph.Level == 2
                    && paragraph.BulletKind == BulletKind.Auto
                    && paragraph.AutoNumType == AutoNumType.AlphaLcParenBoth
                    && paragraph.AutoNumStartAt == 3);
                editor.EditedBody.Paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
                editor.EditedBody.Paragraphs[1].AutoNumStartAtSpecified.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftEnter_InsertsSoftBreakInsideParagraph_AndKeepsCaretAfterBreak()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run { Text = "AlphaBeta" } },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 5;
                editor.SelectionEnd = 5;

                Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Shift);
                await DrainInputAsync();

                editor.Text.Should().Be("Alpha\nBeta");
                editor.SelectionStart.Should().Be(6);
                editor.SelectionEnd.Should().Be(6);

                var edited = editor.EditedBody;
                edited.Paragraphs.Should().ContainSingle();
                edited.Paragraphs[0].Runs.Select(run => run.Text)
                    .Should().Equal("Alpha", "\n", "Beta");
                InCanvasTextEditPlanner.ExtractPlainText(edited)
                    .Should().Be("Alpha\nBeta");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InheritedRuns_UseSharedWpfFallbackInsteadOfNativeTextBoxTheme()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Inherited" },
                    new Run { Text = "Explicit", FontFamily = "Aptos", FontSizePt = 18 },
                },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.RichTextView.FallbackFontFamily.Should().Be("Calibri");
                editor.RichTextView.FallbackFontSizePt.Should().Be(14);
                var inheritedRun = editor.RichTextView.VisualPlan.Paragraphs
                    .SelectMany(paragraph => paragraph.Runs)
                    .First();
                inheritedRun.FontFamily.Should().BeNull();
                inheritedRun.FontSizePt.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

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
                editor.InputBox.Opacity.Should().Be(0,
                    "the native TextBox template must not obscure the custom rich-text raster");

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
                int down = editor.RichTextView.MoveCaretVertically(11, 1).LogicalPosition;
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

    [Fact]
    public async Task VerticalKeys_KeepPreferredXAcrossWrappedLinesAndParagraphBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run { Text = "ABCDEFGHIJKLMNOPQRST" } },
            });
            body.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run { Text = "tail" } },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 90,
                Height = 160,
            };
            var window = Show(editor, 90, 160);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 2;
                editor.SelectionEnd = 2;

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                int firstDown = editor.InputBox.CaretIndex;
                firstDown.Should().BeGreaterThan(2);

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                int paragraphDown = editor.InputBox.CaretIndex;
                paragraphDown.Should().BeGreaterThan(firstDown);

                Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.None);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(firstDown);

                Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
                await DrainInputAsync();
                new[] { editor.SelectionStart, editor.SelectionEnd }
                    .Order().Should().Equal(2, firstDown);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VerticalPreferredX_ResetsAfterHorizontalBoundaryPointerAndMutationInput()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                TextBodyWithParagraphs("ABCDEFGHIJKLMNOPQRST", "tail"),
                backgroundAlpha: 0xCC)
            {
                Width = 90,
                Height = 160,
            };
            var window = Show(editor, 90, 160);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 2;
                editor.SelectionEnd = 2;

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().NotBeNull();

                Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().BeNull();

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().NotBeNull();

                Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().BeNull();

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().BeNull();

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().NotBeNull();

                window.MouseDown(
                    new Point(8, 8),
                    MouseButton.Left,
                    RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(8, 8), MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().BeNull();

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.None);
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().NotBeNull();
                RaiseRawTextInput(editor.InputBox, "Z");
                await DrainInputAsync();
                editor.PreferredVerticalCaretX.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftVerticalKeys_PreserveAnchorAcrossRepeatedDownAndUp()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                TextBodyWithParagraphs("ABCDEFGHIJKLMNOPQRST", "tail"),
                backgroundAlpha: 0xCC)
            {
                Width = 90,
                Height = 160,
            };
            var window = Show(editor, 90, 160);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 2;
                editor.SelectionEnd = 2;

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
                await DrainInputAsync();
                int firstDown = editor.InputBox.CaretIndex;
                new[] { editor.SelectionStart, editor.SelectionEnd }
                    .Order().Should().Equal(2, firstDown);

                Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
                await DrainInputAsync();
                int secondDown = editor.InputBox.CaretIndex;
                secondDown.Should().BeGreaterThan(firstDown);
                new[] { editor.SelectionStart, editor.SelectionEnd }
                    .Order().Should().Equal(2, secondDown);

                Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(firstDown);
                new[] { editor.SelectionStart, editor.SelectionEnd }
                    .Order().Should().Equal(2, firstDown);

                Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
                await DrainInputAsync();
                editor.SelectionStart.Should().Be(2);
                editor.SelectionEnd.Should().Be(2);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LogicalNavigation_CtrlBoundariesAndShiftAnchorCrossParagraphs()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Alpha", Bold = true },
                    new Run { Text = "One", Italic = true },
                },
            });
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Beta", Underline = true },
                    new Run { Text = "Two" },
                },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 140,
            };
            var window = Show(editor, 320, 140);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = 6;
                editor.SelectionEnd = 6;

                Press(window, Key.End, PhysicalKey.End, RawInputModifiers.Control);
                await DrainInputAsync();
                editor.SelectionStart.Should().Be(editor.Text.Length);
                editor.SelectionEnd.Should().Be(editor.Text.Length);

                Press(window, Key.Home, PhysicalKey.Home, RawInputModifiers.Control);
                await DrainInputAsync();
                editor.InputBox.CaretIndex.Should().Be(0);

                editor.SelectionStart = 4;
                editor.SelectionEnd = 4;
                Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
                await DrainInputAsync();
                Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
                await DrainInputAsync();
                new[] { editor.SelectionStart, editor.SelectionEnd }
                    .Order().Should().Equal(4, 6);

                editor.SelectionStart = 3;
                editor.SelectionEnd = 8;
                RaiseRawTextInput(editor.InputBox, "X");
                await DrainInputAsync();

                editor.Text.Should().Be("AlXta\nBetaTwo");
                editor.EditedBody.Paragraphs.Should().HaveCount(2);
                editor.EditedBody.Paragraphs[0].Runs
                    .Select(run => run.Text).Should().Equal("AlX", "ta");
                editor.EditedBody.Paragraphs[1].Runs
                    .Select(run => run.Text).Should().Equal("Beta", "Two");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SplitContinuation_ResolvesSharedMarkersAfterExplicitStart()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                BulletKind = BulletKind.Auto,
                AutoNumType = AutoNumType.ArabicPeriod,
                AutoNumStartAt = 4,
                AutoNumStartAtSpecified = true,
                Runs = { new Run { Text = "AB" } },
            });
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 90,
            };
            var window = Show(editor);
            try
            {
                editor.Text = "A\nB\nC";

                var edited = editor.EditedBody;
                edited.Paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
                edited.Paragraphs.Skip(1).Should().OnlyContain(paragraph =>
                    !paragraph.AutoNumStartAtSpecified);
                ComposeText(edited).Paragraphs.Select(paragraph => paragraph.BulletText)
                    .Should().Equal("4.", "5.", "6.");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RichSurface_UsesSharedMarkerContinuationForMixedParagraphEditing()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(Numbered("First", AutoNumType.ArabicPeriod, 4, startSpecified: true));
            body.Paragraphs.Add(Numbered("Nested", AutoNumType.ArabicPeriod, 1, level: 1));
            body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Plain" } } });
            body.Paragraphs.Add(Numbered("Restart", AutoNumType.ArabicPeriod, 7, startSpecified: true));

            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC)
            {
                Width = 320,
                Height = 160,
            };
            var window = Show(editor, 320, 160);
            try
            {
                editor.RichTextView.VisualPlan.Paragraphs
                    .Select(paragraph => paragraph.BulletText)
                    .Should().Equal("4.", "1.", "", "7.");

                editor.SelectionStart = "First\nNested\nPlain\nRestart".Length;
                editor.SelectionEnd = editor.SelectionStart;
                editor.RichTextView.CaretRect.Height.Should().BeGreaterThan(0);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static ResolvedTextLayout ComposeText(TextBody body)
    {
        var presentation = FreeP.Core.Model.Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3000000,
            TextBody = body,
        });

        return SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single()
            .Text!;
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

    private static TextBody TextBodyWithParagraphs(string first, string second)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = first } } });
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = second } } });
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

    private static Paragraph Numbered(
        string text,
        AutoNumType type,
        int startAt,
        int level = 0,
        bool startSpecified = false)
    {
        return new Paragraph
        {
            Level = level,
            BulletKind = BulletKind.Auto,
            AutoNumType = type,
            AutoNumStartAt = startAt,
            AutoNumStartAtSpecified = startSpecified,
            Runs = { new Run { Text = text } },
        };
    }

    private static byte[] CreateXamlPackage(string xaml)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(package.CreateEntry("Xaml/Document.xaml").Open(), Encoding.UTF8))
            writer.Write(xaml);
        return output.ToArray();
    }

}
