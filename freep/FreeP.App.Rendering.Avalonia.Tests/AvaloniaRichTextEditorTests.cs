using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    public async Task Input_uses_the_portable_rich_text_semantic_identity()
    {
        await Session.Dispatch(() =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("semantic input").Body,
                backgroundAlpha: 0xCC);

            AutomationProperties.GetAutomationId(editor.InputBox).Should().Be(
                PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardContextMenu_UsesRichEditorRoutesAndSelectionEnablement()
    {
        await Session.Dispatch(() =>
        {
            var editor = new AvaloniaRichTextEditor(
                InCanvasRichClipboardPayload.FromPlainText("context text").Body,
                backgroundAlpha: 0xCC);
            var menu = editor.InputBox.ContextMenu;

            menu.Should().NotBeNull();
            menu!.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString())
                .Should().Equal(
                    PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand),
                    PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand),
                    PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditPasteCommand),
                    PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditSelectAllCommand));
            menu.Items.OfType<MenuItem>().Take(2)
                .Should().OnlyContain(item => !item.IsEnabled);

            editor.SelectionStart = 0;
            editor.SelectionEnd = editor.Text.Length;
            menu.Items.OfType<MenuItem>().Take(2)
                .Should().OnlyContain(item => item.IsEnabled);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineImageRun_IsRetainedBySharedVisualPlan()
    {
        await Session.Dispatch(() =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Before" },
                    new Run
                    {
                        Text = "\uFFFC",
                        InlineImage = new ImagePart
                        {
                            Bytes = [0x01, 0x02],
                            ContentType = "image/png",
                        },
                        InlineImageWidthEmu = 228_600,
                        InlineImageHeightEmu = 114_300,
                    },
                    new Run { Text = "After" },
                },
            });

            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC);
            var run = editor.RichTextView.VisualPlan.Paragraphs.Single().Runs[1];
            run.Text.Should().Be("\uFFFC");
            run.InlineImage!.Bytes.Should().Equal(0x01, 0x02);
            run.InlineImageWidthEmu.Should().Be(228_600);
            run.InlineImageHeightEmu.Should().Be(114_300);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineOleRun_IsRetainedBySharedVisualPlan()
    {
        await Session.Dispatch(() =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Before" },
                    new Run
                    {
                        Text = "\uFFFC",
                        InlineOleObject = new InlineOleObjectInfo
                        {
                            EmbeddedBytes = [0x01, 0x02, 0x03],
                            FileName = "Embedded.xlsx",
                            ClassName = "Excel.Sheet.12",
                        },
                    },
                    new Run { Text = "After" },
                },
            });

            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC);
            var run = editor.RichTextView.VisualPlan.Paragraphs.Single().Runs[1];
            run.Text.Should().Be("\uFFFC");
            run.InlineOleObject!.EmbeddedBytes.Should().Equal(0x01, 0x02, 0x03);
            run.InlineOleObject.FileName.Should().Be("Embedded.xlsx");
            run.InlineOleObject.ClassName.Should().Be("Excel.Sheet.12");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineOleRun_ExposesMeasuredHostRequestAtLogicalPosition()
    {
        await Session.Dispatch(() =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Before" },
                    new Run
                    {
                        Text = "\uFFFC",
                        InlineOleObject = new InlineOleObjectInfo
                        {
                            EmbeddedBytes = [0x01, 0x02, 0x03],
                            FileName = "Embedded.xlsx",
                            ClassName = "Excel.Sheet.12",
                        },
                    },
                },
            });

            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xCC);
            editor.Measure(new Size(320, 90));
            editor.Arrange(new Rect(0, 0, 320, 90));

            editor.TryGetInlineOleHit(6, out var hit).Should().BeTrue();
            hit.InlineObject.FileName.Should().Be("Embedded.xlsx");
            hit.Bounds.Width.Should().Be(42);
            hit.Bounds.Height.Should().BeGreaterThan(18);
            editor.TryGetInlineOleHit(5, out _).Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTableRun_RendersNestedCellBodyRecursively()
    {
        await Session.Dispatch(() =>
        {
            var nested = new TableShape();
            nested.ColumnWidthsEmu.Add(457200);
            nested.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell
                    {
                        Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xE84A4A))),
                        TextBody = new TextBody
                        {
                            Paragraphs =
                            {
                                new Paragraph
                                {
                                    Runs =
                                    {
                                        new Run { Text = "Nested", Bold = true },
                                    },
                                },
                            },
                        },
                    },
                },
            });

            var outer = new TableShape();
            outer.ColumnWidthsEmu.Add(914400);
            outer.Rows.Add(new TableRow
            {
                HeightEmu = 685800,
                Cells =
                {
                    new TableCell
                    {
                        TextBody = new TextBody
                        {
                            Paragraphs =
                            {
                                new Paragraph
                                {
                                    Runs =
                                    {
                                        new Run
                                        {
                                            Text = "\uFFFC",
                                            InlineTable = new InlineTableInfo { Table = nested },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            });

            var editor = new AvaloniaRichTextEditor(new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new Run
                            {
                                Text = "\uFFFC",
                                InlineTable = new InlineTableInfo { Table = outer },
                            },
                        },
                    },
                },
            }, backgroundAlpha: 0xCC)
            {
                Width = 140,
                Height = 90,
            };
            var window = Show(editor, 140, 90);
            try
            {
                editor.RichTextView.TryHitTestInlineTableCell(new Point(10, 10), out var hit)
                    .Should().BeTrue();
                hit.RowIndex.Should().Be(0);
                hit.ColumnIndex.Should().Be(0);

                byte[] pixels = RenderPixels(editor, 140, 90);
                int nestedAreaFill = CountRedPixels(
                    pixels,
                    width: 140,
                    left: 6,
                    top: 2,
                    right: 58,
                    bottom: 28);

                nestedAreaFill.Should().BeGreaterThan(500);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(TextVerticalType.Vertical)]
    [InlineData(TextVerticalType.Vertical270)]
    public async Task InlineTableRun_RendersRotatedCellText(TextVerticalType verticalType)
    {
        await Session.Dispatch(() =>
        {
            var table = new TableShape();
            table.ColumnWidthsEmu.Add(914400);
            table.Rows.Add(new TableRow
            {
                HeightEmu = 685800,
                Cells =
                {
                    new TableCell
                    {
                        TextBody = new TextBody
                        {
                            VerticalType = verticalType,
                            Paragraphs =
                            {
                                new Paragraph
                                {
                                    Runs = { new Run { Text = "Rotate" } },
                                },
                            },
                        },
                    },
                },
            });

            var editor = MakeInlineTableEditor(table, 140, 90);
            var window = Show(editor, 140, 90);
            try
            {
                byte[] pixels = RenderPixels(editor, 140, 90);
                CountDarkPixels(pixels, 140, 90).Should().BeGreaterThan(8);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTableSurface_HitTestReturnsMergedAnchorBounds()
    {
        await Session.Dispatch(() =>
        {
            var table = new TableShape();
            table.ColumnWidthsEmu.Add(457200);
            table.ColumnWidthsEmu.Add(457200);
            table.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell
                    {
                        GridSpan = 2,
                        RowSpan = 2,
                        TextBody = BodyWithText("Merged"),
                    },
                    new TableCell { HMerge = true },
                },
            });
            table.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell { VMerge = true },
                    new TableCell { VMerge = true },
                },
            });

            var editor = new AvaloniaRichTextEditor(new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new Run
                            {
                                Text = "\uFFFC",
                                InlineTable = new InlineTableInfo { Table = table },
                            },
                        },
                    },
                },
            }, backgroundAlpha: 0xCC)
            {
                Width = 140,
                Height = 80,
            };
            var window = Show(editor, 140, 80);
            try
            {
                editor.RichTextView.TryHitTestInlineTableCell(
                        new Point(60, 36),
                        out var hit)
                    .Should().BeTrue();
                hit.RowIndex.Should().Be(0);
                hit.ColumnIndex.Should().Be(0);
                hit.Bounds.Width.Should().BeApproximately(96, 0.01);
                hit.Bounds.Height.Should().BeApproximately(48, 0.01);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTableCellEditor_TabNavigationCommitsAndMovesAcrossCells()
    {
        await Session.Dispatch(async () =>
        {
            var table = new TableShape();
            table.ColumnWidthsEmu.Add(457200);
            table.ColumnWidthsEmu.Add(457200);
            table.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell { TextBody = BodyWithText("First") },
                    new TableCell { TextBody = BodyWithText("Second") },
                },
            });
            var editor = new AvaloniaRichTextEditor(new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new Run
                            {
                                Text = "\uFFFC",
                                InlineTable = new InlineTableInfo { Table = table },
                            },
                        },
                    },
                },
            }, backgroundAlpha: 0xCC)
            {
                Width = 150,
                Height = 50,
            };
            var window = Show(editor, 150, 50);
            try
            {
                var firstCellPoint = new Point(10, 10);
                window.MouseMove(firstCellPoint, RawInputModifiers.None);
                window.MouseDown(firstCellPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(firstCellPoint, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(firstCellPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(firstCellPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                var firstEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                firstEditor.Text.Should().Be("First");
                firstEditor.SelectionStart = firstEditor.Text.Length;
                firstEditor.SelectionEnd = firstEditor.Text.Length;
                RaiseRawTextInput(firstEditor.InputBox, "!");
                await DrainInputAsync();

                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None);
                await DrainInputAsync();

                var secondEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                secondEditor.Should().NotBeSameAs(firstEditor);
                secondEditor.Text.Should().Be("Second");
                secondEditor.InputBox.IsFocused.Should().BeTrue();

                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
                await DrainInputAsync();

                var returnedEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                returnedEditor.Text.Should().Be("First!");
                returnedEditor.Should().NotBeSameAs(secondEditor);

                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
                await DrainInputAsync();
                editor.Children.OfType<AvaloniaRichTextEditor>().Single()
                    .Should().BeSameAs(returnedEditor);

                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None);
                await DrainInputAsync();
                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None);
                await DrainInputAsync();

                var appendedEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                appendedEditor.Text.Should().BeEmpty();
                // The appended row is held in the editor's pending set until the edit commits, so
                // the source table deliberately still has its original row here. The committed shape
                // is asserted through EditedBody below.
                table.Rows.Should().HaveCount(1);

                var edited = editor.EditedBody;
                var editedTable = edited.Paragraphs.Single().Runs.Single().InlineTable!.Table;
                PlainText(editedTable.Rows[0].Cells[0].TextBody).Should().Be("First!");
                PlainText(editedTable.Rows[0].Cells[1].TextBody).Should().Be("Second");
                editedTable.Rows[1].Cells.Should().HaveCount(2);
                PlainText(editedTable.Rows[1].Cells[0].TextBody).Should().BeEmpty();
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTableCellEditor_UsesSharedRichPlanAndEscapeCancelsWithoutLosingRuns()
    {
        await Session.Dispatch(async () =>
        {
            var table = new TableShape();
            table.ColumnWidthsEmu.Add(457200);
            table.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell
                    {
                        TextBody = new TextBody
                        {
                            Paragraphs =
                            {
                                new Paragraph
                                {
                                    Runs =
                                    {
                                        new Run { Text = "Rich", Bold = true, FontFamily = "Consolas" },
                                    },
                                },
                            },
                        },
                    },
                },
            });

            var editor = MakeInlineTableEditor(table, 100, 50);
            var window = Show(editor, 100, 50);
            try
            {
                var point = new Point(10, 10);
                window.MouseMove(point, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                var cellEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                cellEditor.CurrentPlan().HasRichFormatting.Should().BeTrue();
                cellEditor.Selection.Should().Be(new InCanvasEditorTextSelection(0, 4));
                cellEditor.Text = "Discarded";
                Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
                await DrainInputAsync();

                var canceled = editor.EditedBody.Paragraphs.Single().Runs.Single().InlineTable!.Table;
                PlainText(canceled.Rows[0].Cells[0].TextBody).Should().Be("Rich");
                canceled.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Bold.Should().BeTrue();

                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();
                cellEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                cellEditor.ToggleTextFormat(TableCellTextFormatKind.Italic).Should().BeTrue();
                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None);
                await DrainInputAsync();

                var committed = editor.EditedBody.Paragraphs.Single().Runs.Single().InlineTable!.Table;
                var run = committed.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs.Single();
                run.Bold.Should().BeTrue();
                run.Italic.Should().BeTrue();
                run.FontFamily.Should().Be("Consolas");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTableCellEditor_CompactGridSpanTabUsesSourceCell()
    {
        await Session.Dispatch(async () =>
        {
            var table = new TableShape();
            table.ColumnWidthsEmu.Add(457200);
            table.ColumnWidthsEmu.Add(457200);
            table.ColumnWidthsEmu.Add(457200);
            table.Rows.Add(new TableRow
            {
                HeightEmu = 228600,
                Cells =
                {
                    new TableCell { GridSpan = 2, TextBody = BodyWithText("Wide") },
                    new TableCell { TextBody = BodyWithText("Last") },
                },
            });

            var editor = MakeInlineTableEditor(table, 190, 50);
            var window = Show(editor, 190, 50);
            try
            {
                var firstPoint = new Point(20, 10);
                window.MouseMove(firstPoint, RawInputModifiers.None);
                window.MouseDown(firstPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(firstPoint, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(firstPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(firstPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Children.OfType<AvaloniaRichTextEditor>().Single().Text.Should().Be("Wide");
                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.None);
                await DrainInputAsync();

                var lastEditor = editor.Children.OfType<AvaloniaRichTextEditor>().Single();
                lastEditor.Text.Should().Be("Last");
                Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
                await DrainInputAsync();

                editor.Children.OfType<AvaloniaRichTextEditor>().Single().Text.Should().Be("Wide");
                var edited = editor.EditedBody;
                var editedTable = edited.Paragraphs.Single().Runs.Single().InlineTable!.Table;
                PlainText(editedTable.Rows[0].Cells[0].TextBody).Should().Be("Wide");
                PlainText(editedTable.Rows[0].Cells[1].TextBody).Should().Be("Last");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineImageRun_ReservesAuthoredWidthForFollowingText()
    {
        await Session.Dispatch(async () =>
        {
            byte[] png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "Before" },
                    new Run
                    {
                        Text = "\uFFFC",
                        InlineImage = new ImagePart { Bytes = png, ContentType = "image/png" },
                        InlineImageWidthEmu = 228_600,
                        InlineImageHeightEmu = 114_300,
                    },
                    new Run { Text = "After" },
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
                editor.SelectionStart = 7;
                editor.SelectionEnd = 7;
                double imageFollowingX = editor.RichTextView.CaretRect.X;

                editor.Text = "BeforeAfter";
                editor.SelectionStart = 6;
                editor.SelectionEnd = 6;
                double plainFollowingX = editor.RichTextView.CaretRect.X;

                (imageFollowingX - plainFollowingX).Should().BeGreaterThan(20);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_UsesSharedBodyWrapPolicyForInputAndRichLayout()
    {
        await Session.Dispatch(() =>
        {
            var wrapped = new AvaloniaRichTextEditor(
                new TextBody { Wrap = true },
                backgroundAlpha: 0xCC);
            var unwrapped = new AvaloniaRichTextEditor(
                new TextBody { Wrap = false },
                backgroundAlpha: 0xCC);

            wrapped.InputBox.TextWrapping.Should().Be(TextWrapping.Wrap);
            wrapped.RichTextView.VisualPlan.Wrap.Should().BeTrue();
            unwrapped.InputBox.TextWrapping.Should().Be(TextWrapping.NoWrap);
            unwrapped.RichTextView.VisualPlan.Wrap.Should().BeFalse();
        }, CancellationToken.None);
    }

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
            return true;
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
            return true;
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
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardCopyTransfer_PublishesStandardRtfAlongsidePrivatePayload()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "portable ", Bold = true },
                    new Run
                    {
                        Text = "rich",
                        Italic = true,
                        Underline = true,
                        Hyperlink = new Hyperlink { Url = "https://example.com" },
                    },
                },
            });
            var payload = new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body));

            using var transfer = AvaloniaRichTextEditor.BuildRichTextDataTransfer(payload);
            var rtf = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                    : AvaloniaRichTextEditor.ExternalRtfLinuxFormat);
            rtf.Should().NotBeNull();

            var restored = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);
            restored.Should().NotBeNull();
            restored!.PlainText.Should().Be("portable rich");
            restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
                run.Text == "portable " && run.Bold);
            restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
                run.Text == "rich"
                && run.Italic
                && run.Underline
                && run.Hyperlink!.Url == "https://example.com");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardCopyTransfer_PublishesXamlPackageAlongsidePrivatePayloadAndRtf()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody();
            body.Paragraphs.Add(new Paragraph
            {
                Runs =
                {
                    new Run { Text = "portable ", Bold = true },
                    new Run
                    {
                        Text = "rich",
                        Italic = true,
                        Hyperlink = new Hyperlink { Url = "https://example.com" },
                    },
                },
            });
            var payload = new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body));

            using var transfer = AvaloniaRichTextEditor.BuildRichTextDataTransfer(payload);
            var privateBytes = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.RichTextPlatformFormat
                    : AvaloniaRichTextEditor.RichTextFormat);
            var rtf = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                    : AvaloniaRichTextEditor.ExternalRtfLinuxFormat);
            var xaml = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalXamlPackageWindowsFormat
                    : AvaloniaRichTextEditor.ExternalXamlPackageLinuxFormat);

            InCanvasRichClipboardPlanner.Deserialize(privateBytes).Should().NotBeNull();
            ExternalRichTextClipboardPlanner.TryParseRtf(rtf).Should().NotBeNull();
            xaml.Should().NotBeNull();
            var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(xaml);
            restored.Should().NotBeNull();
            restored!.PlainText.Should().Be("portable rich");
            restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
                run.Text == "portable " && run.Bold);
            restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
                run.Text == "rich"
                && run.Italic
                && run.Hyperlink!.Url == "https://example.com");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardCopyTransfer_PublishesNativeNestedListsInXamlPackage()
    {
        await Session.Dispatch(async () =>
        {
            var body = new TextBody
            {
                Paragraphs =
                {
                    Numbered("Outer", AutoNumType.AlphaUcPeriod, startAt: 3, startSpecified: true),
                    new Paragraph
                    {
                        Level = 1,
                        BulletKind = BulletKind.Char,
                        BulletChar = "\u25E6",
                        Runs = { new Run { Text = "Child" } },
                    },
                    Numbered("Next", AutoNumType.AlphaUcPeriod, startAt: 1),
                },
            };
            var payload = new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body));

            using var transfer = AvaloniaRichTextEditor.BuildRichTextDataTransfer(payload);
            var xaml = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalXamlPackageWindowsFormat
                    : AvaloniaRichTextEditor.ExternalXamlPackageLinuxFormat);

            xaml.Should().NotBeNull();
            var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(xaml);
            restored.Should().NotBeNull();
            restored!.Body.Paragraphs.Select(paragraph => paragraph.Level)
                .Should().Equal(0, 1, 0);
            restored.Body.Paragraphs[0].AutoNumType.Should().Be(AutoNumType.AlphaUcPeriod);
            restored.Body.Paragraphs[0].AutoNumStartAt.Should().Be(3);
            restored.Body.Paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
            restored.Body.Paragraphs[1].BulletChar.Should().Be("\u25E6");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardCopyTransfer_WithInlineImage_PreservesAllProductionFormats()
    {
        await Session.Dispatch(async () =>
        {
            var imageBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
            var body = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs =
                        {
                            new Run { Text = "Before " },
                            new Run
                            {
                                Text = "\uFFFC",
                                InlineImage = new ImagePart
                                {
                                    Bytes = imageBytes,
                                    ContentType = "image/png",
                                },
                                InlineImageWidthEmu = 228_600,
                                InlineImageHeightEmu = 114_300,
                            },
                            new Run
                            {
                                Text = " after",
                                Strikethrough = true,
                                Hyperlink = new Hyperlink { Url = "https://example.test/wave161" },
                            },
                        },
                    },
                },
            };
            var payload = new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body));

            using var transfer = AvaloniaRichTextEditor.BuildRichTextDataTransfer(payload);
            var privateBytes = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.RichTextPlatformFormat
                    : AvaloniaRichTextEditor.RichTextFormat);
            var rtf = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                    : AvaloniaRichTextEditor.ExternalRtfLinuxFormat);
            var xaml = await transfer.TryGetValueAsync(
                OperatingSystem.IsWindows()
                    ? AvaloniaRichTextEditor.ExternalXamlPackageWindowsFormat
                    : AvaloniaRichTextEditor.ExternalXamlPackageLinuxFormat);
            var plainText = await transfer.TryGetTextAsync();

            var privatePayload = InCanvasRichClipboardPlanner.Deserialize(privateBytes);
            privatePayload.Should().NotBeNull();
            privatePayload!.Body.Paragraphs.Single().Runs.Select(run => run.Text)
                .Should().Equal("Before ", "\uFFFC", " after");
            privatePayload.Body.Paragraphs.Single().Runs[1].InlineImage!.Bytes
                .Should().Equal(imageBytes);
            privatePayload.Body.Paragraphs.Single().Runs[2].Strikethrough.Should().BeTrue();
            privatePayload.Body.Paragraphs.Single().Runs[2].Hyperlink!.Url
                .Should().Be("https://example.test/wave161");

            var restoredRtf = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);
            restoredRtf.Should().NotBeNull();
            restoredRtf!.Body.Paragraphs.Single().Runs.Should().Contain(run =>
                run.Text == " after"
                && run.Strikethrough
                && run.Hyperlink!.Url == "https://example.test/wave161");
            plainText.Should().Be("Before  after");

            xaml.Should().NotBeNull();
            var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(xaml);
            restored.Should().NotBeNull();
            restored!.Body.Paragraphs.Single().Runs.Select(run => run.Text)
                .Should().Equal("Before ", "\uFFFC", " after");
            restored.Body.Paragraphs.Single().Runs[1].InlineImage!.Bytes
                .Should().Equal(imageBytes);
            restored.Body.Paragraphs.Single().Runs[2].Strikethrough.Should().BeTrue();
            restored.Body.Paragraphs.Single().Runs[2].Hyperlink!.Url
                .Should().Be("https://example.test/wave161");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClipboardPaste_CustomPayloadPrecedesXamlPackageRtfAndPlainText()
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
                var customPayload = InCanvasRichClipboardPayload.FromPlainText("custom");
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.RichTextPlatformFormat
                        : AvaloniaRichTextEditor.RichTextFormat,
                    InCanvasRichClipboardPlanner.Serialize(customPayload));
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalXamlPackageWindowsFormat
                        : AvaloniaRichTextEditor.ExternalXamlPackageLinuxFormat,
                    CreateXamlPackage(
                        "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Bold>ignored package</Bold></Paragraph></FlowDocument>"));
                item.Set(
                    OperatingSystem.IsWindows()
                        ? AvaloniaRichTextEditor.ExternalRtfWindowsFormat
                        : AvaloniaRichTextEditor.ExternalRtfLinuxFormat,
                    Encoding.ASCII.GetBytes(@"{\rtf1\ansi ignored rtf}"));
                item.SetText("plain fallback");
                transfer.Add(item);

                (await editor.PasteDataTransferAsync(transfer)).Should().BeTrue();
                editor.Text.Should().Be("custom");
            }
            finally
            {
                window.Close();
            }
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PointerDrag_UsesMeasuredWrappedLinesAndKeepsParagraphBoundary()
    {
        await Session.Dispatch(async () =>
        {
            var body = TextBodyWithParagraphs(
                "Wide words make this first paragraph wrap at unequal visual line widths",
                "tail paragraph crosses the boundary");
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xFF)
            {
                Width = 150,
                Height = 220,
            };
            var window = Show(editor, 150, 220);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                await DrainInputAsync();

                int secondParagraphStart = editor.RichTextView.VisualPlan.Paragraphs[1].GlobalStart;
                var anchorPoint = new Point(112, 12);
                int anchor = editor.RichTextView.HitTestLogicalPosition(anchorPoint);
                anchor.Should().BeInRange(1, secondParagraphStart - 1,
                    "the drag must begin on the wrapped first paragraph, not its newline");

                Point caretPoint = default;
                int caret = -1;
                for (int y = 20; y < 190; y += 4)
                {
                    var candidate = new Point(8, y);
                    int candidatePosition = editor.RichTextView.HitTestLogicalPosition(candidate);
                    if (candidatePosition >= secondParagraphStart + 4)
                    {
                        caretPoint = candidate;
                        caret = candidatePosition;
                        break;
                    }
                }

                caret.Should().BeGreaterThanOrEqualTo(secondParagraphStart + 4,
                    "the deterministic scan must reach the second paragraph");
                var expected = InCanvasRichTextPointerSelectionPlanner.Plan(
                    anchor,
                    caret,
                    editor.Text.Length);

                window.MouseMove(anchorPoint, RawInputModifiers.None);
                window.MouseDown(
                    anchorPoint,
                    MouseButton.Left,
                    RawInputModifiers.LeftMouseButton);
                window.MouseMove(caretPoint, RawInputModifiers.LeftMouseButton);
                window.MouseUp(caretPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Selection.Should().Be(expected);
                editor.Text[expected.Start..expected.End].Should().Contain("\n");
                editor.RichTextView.SelectionRects.Should().HaveCountGreaterThan(1,
                    "a cross-paragraph drag must render selection geometry for both paragraphs");

                window.MouseDown(
                    caretPoint,
                    MouseButton.Left,
                    RawInputModifiers.LeftMouseButton);
                window.MouseMove(anchorPoint, RawInputModifiers.LeftMouseButton);
                window.MouseUp(anchorPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Selection.Should().Be(
                    InCanvasRichTextPointerSelectionPlanner.Plan(caret, anchor, editor.Text.Length));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PointerDragBeyondVisibleEditor_AutoScrollsAndClampsAtDocumentEnd()
    {
        await Session.Dispatch(async () =>
        {
            var body = TextBodyWithParagraphs(
                string.Join(' ', Enumerable.Repeat(
                    "first paragraph contains enough words to exceed the editor viewport", 8)),
                string.Join(' ', Enumerable.Repeat(
                    "last paragraph remains selectable when the pointer leaves the editor", 8)));
            var editor = new AvaloniaRichTextEditor(body, backgroundAlpha: 0xFF)
            {
                Width = 160,
                Height = 90,
            };
            var window = Show(editor, 160, 90);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                await DrainInputAsync();

                editor.RichTextView.ContentExtentHeight.Should().BeGreaterThan(editor.Bounds.Height);
                Point anchorPoint = new(70, 38);
                int anchor = editor.RichTextView.HitTestLogicalPosition(anchorPoint);

                window.MouseMove(anchorPoint, RawInputModifiers.None);
                window.MouseDown(anchorPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                for (int i = 0; i < 8; i++)
                    window.MouseMove(new Point(8, 88), RawInputModifiers.LeftMouseButton);
                await DrainInputAsync();

                editor.RichTextView.ScrollOffsetY.Should().BeGreaterThan(0,
                    "a captured drag held in the bottom edge band should auto-scroll the document");
                editor.Selection.End.Should().BeGreaterThan(anchor,
                    "the captured pointer endpoint should advance with the scrolled content");

                window.MouseMove(new Point(8, 500), RawInputModifiers.LeftMouseButton);
                for (int i = 0; i < 40; i++)
                    window.MouseMove(new Point(8, 88), RawInputModifiers.LeftMouseButton);
                await DrainInputAsync();

                editor.RichTextView.ScrollOffsetY.Should().BeApproximately(
                    editor.RichTextView.ContentExtentHeight - editor.Bounds.Height,
                    0.1);
                editor.Selection.End.Should().Be(editor.Text.Length,
                    "the bottom edge must clamp the endpoint to the document end");
                window.MouseUp(new Point(8, 500), MouseButton.Left, RawInputModifiers.None);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftClickAndMultiClickSelectionModesRemainStable()
    {
        await Session.Dispatch(async () =>
        {
            var editor = new AvaloniaRichTextEditor(
                TextBodyWithParagraphs("Alpha beta gamma", "Delta epsilon zeta"),
                backgroundAlpha: 0xFF)
            {
                Width = 220,
                Height = 120,
            };
            var window = Show(editor, 220, 120);
            try
            {
                editor.FocusEditor().Should().BeTrue();
                await DrainInputAsync();

                var firstPoint = new Point(8, 8);
                var secondPoint = new Point(65, 8);
                int first = editor.RichTextView.HitTestLogicalPosition(firstPoint);
                int second = editor.RichTextView.HitTestLogicalPosition(secondPoint);
                first.Should().Be(0);
                second.Should().BeGreaterThan(first);

                window.MouseMove(firstPoint, RawInputModifiers.None);
                window.MouseDown(firstPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(firstPoint, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(secondPoint, RawInputModifiers.Shift);
                window.MouseDown(secondPoint, MouseButton.Left, RawInputModifiers.Shift);
                window.MouseUp(secondPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Selection.Should().Be(
                    InCanvasRichTextPointerSelectionPlanner.Plan(first, second, editor.Text.Length),
                    "Shift-click must extend from the original caret");

                var wordPoint = new Point(32, 8);
                window.MouseMove(wordPoint, RawInputModifiers.None);
                window.MouseDown(wordPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(wordPoint, MouseButton.Left, RawInputModifiers.None);
                window.MouseDown(wordPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(wordPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Text[editor.SelectionStart..editor.SelectionEnd].Should().Be("beta",
                    "double-click must select the containing word");

                window.MouseDown(wordPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(wordPoint, MouseButton.Left, RawInputModifiers.None);
                await DrainInputAsync();

                editor.Text[editor.SelectionStart..editor.SelectionEnd]
                    .Should().Be("Alpha beta gamma\n",
                        "triple-click must include the WPF paragraph boundary");
            }
            finally
            {
                window.Close();
            }
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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
            return true;
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

                // "AlphaOne" with [3,8) replaced by "X" is "Alp" + "X"; the second paragraph is
                // untouched. (The original expectation, "AlXta", is not a reachable edit of this
                // body under any selection -- it was authored while the assertions were unreachable.)
                editor.Text.Should().Be("AlpX\nBetaTwo");
                editor.EditedBody.Paragraphs.Should().HaveCount(2);
                // The deletion removes "ha" from the bold run and all of the italic "One" run, so the
                // inserted "X" adopts the surviving run's formatting and merges into it.
                editor.EditedBody.Paragraphs[0].Runs
                    .Select(run => run.Text).Should().Equal("AlpX");
                editor.EditedBody.Paragraphs[1].Runs
                    .Select(run => run.Text).Should().Equal("Beta", "Two");
            }
            finally
            {
                window.Close();
            }
            return true;
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
            return true;
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

                // The caret rect is only built while the editor shows a caret, so the surface has to
                // be focused before asserting on it (same convention as the sibling navigation tests).
                editor.FocusEditor().Should().BeTrue();
                editor.SelectionStart = "First\nNested\nPlain\nRestart".Length;
                editor.SelectionEnd = editor.SelectionStart;
                editor.RichTextView.CaretRect.Height.Should().BeGreaterThan(0);
            }
            finally
            {
                window.Close();
            }
            return true;
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

    private static byte[] RenderPixels(Control control, int width, int height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(control);
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var target = new PinnedFramebuffer(
                handle.AddrOfPinnedObject(),
                new PixelSize(width, height),
                width * 4);
            bitmap.CopyPixels(target);
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    private static int CountRedPixels(
        byte[] pixels,
        int width,
        int left,
        int top,
        int right,
        int bottom)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] < 100
                    && pixels[offset + 1] < 130
                    && pixels[offset + 2] > 180
                    && pixels[offset + 3] > 0)
                    count++;
            }
        }

        return count;
    }

    private static int CountDarkPixels(byte[] pixels, int width, int height)
    {
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] < 80
                    && pixels[offset + 1] < 80
                    && pixels[offset + 2] < 80
                    && pixels[offset + 3] > 0)
                    count++;
            }
        }

        return count;
    }

    private sealed class PinnedFramebuffer : ILockedFramebuffer
    {
        public PinnedFramebuffer(IntPtr address, PixelSize size, int rowBytes)
        {
            Address = address;
            Size = size;
            RowBytes = rowBytes;
        }

        public IntPtr Address { get; }
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi => new(96, 96);
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Premul;
        public void Dispose() { }
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

    private static TextBody BodyWithText(string text) => new()
    {
        Paragraphs = { new Paragraph { Runs = { new Run { Text = text } } } },
    };

    private static AvaloniaRichTextEditor MakeInlineTableEditor(
        TableShape table,
        double width,
        double height) => new(new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineTable = new InlineTableInfo { Table = table },
                        },
                    },
                },
            },
        }, backgroundAlpha: 0xCC)
        {
            Width = width,
            Height = height,
        };

    private static string PlainText(TextBody? body) => body is null
        ? string.Empty
        : string.Join(
            "\n",
            body.Paragraphs.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))));

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
