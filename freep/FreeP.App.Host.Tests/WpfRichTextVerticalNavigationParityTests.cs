using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using FreeP.App.Rendering.Wpf;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Host.Tests;

public sealed class WpfRichTextVerticalNavigationParityTests
{
    [StaFact]
    public void WpfRichTextBox_NativeVerticalCommandsMoveAndNoOpAtEdges()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new ModelParagraph
        {
                Runs = { new ModelRun { Text = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmn" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = "tail" } },
        });

        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12))
        {
            AcceptsReturn = true,
            Width = 96,
            Height = 180,
            IsUndoEnabled = false,
        };
        box.Document.PageWidth = 80;
        box.Document.ColumnWidth = 80;
        var window = new Window { Content = box, Width = 96, Height = 180 };
        window.Show();
        window.UpdateLayout();
        try
        {
            box.Focus().Should().BeTrue();
            var initial = PointerAtLogicalOffset(box.Document, 2);
            box.Selection.Select(initial, initial);
            Point initialCaret = CaretOrigin(box);

            RaiseKey(box, Key.Down);
            Point firstDown = CaretOrigin(box);
            RaiseKey(box, Key.Down);
            Point secondDown = CaretOrigin(box);
            RaiseKey(box, Key.Up);
            RaiseKey(box, Key.Up);
            Point returned = CaretOrigin(box);

            firstDown.Y.Should().BeGreaterThan(initialCaret.Y);
            secondDown.Y.Should().BeGreaterThan(firstDown.Y);
            returned.Y.Should().BeApproximately(initialCaret.Y, 1.5);

            var firstLine = PointerAtLogicalOffset(box.Document, 2);
            box.Selection.Select(firstLine, firstLine);
            RaiseKey(box, Key.Up);
            LogicalOffsetAt(box.Document, box.CaretPosition).Should().Be(2);

            var documentEnd = PointerAtLogicalOffset(box.Document, 45);
            box.Selection.Select(documentEnd, documentEnd);
            RaiseKey(box, Key.Down);
            LogicalOffsetAt(box.Document, box.CaretPosition).Should().Be(45);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfRichTextBox_NativePointerRangeMatchesSharedCrossParagraphSelection()
    {
        const string firstText = "Wide words make this first paragraph wrap at unequal visual line widths";
        const string secondText = "tail paragraph crosses the boundary";
        var body = new TextBody();
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = firstText } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = secondText } },
        });

        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12))
        {
            AcceptsReturn = true,
            Width = 96,
            Height = 220,
            IsUndoEnabled = false,
        };
        box.Document.PageWidth = 80;
        box.Document.ColumnWidth = 80;
        var window = new Window { Content = box, Width = 96, Height = 220 };
        window.Show();
        window.UpdateLayout();
        try
        {
            box.Focus().Should().BeTrue();
            const int anchor = 8;
            int secondParagraphStart = firstText.Length + 1;
            int caret = secondParagraphStart + 4;
            var expected = InCanvasRichTextPointerSelectionPlanner.Plan(
                anchor,
                caret,
                firstText.Length + 1 + secondText.Length);

            var start = PointerAtLogicalOffset(box.Document, expected.Start);
            var end = PointerAtLogicalOffset(box.Document, expected.End);
            box.Selection.Select(start, end);

            LogicalOffsetAt(box.Document, box.Selection.Start).Should().Be(expected.Start);
            LogicalOffsetAt(box.Document, box.Selection.End).Should().Be(expected.End);
            box.Selection.Text.Should().Contain("\r\n");
            box.Selection.Text.Should().Contain("tail");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfRichTextBox_NativePointerSelectionClampsToDocumentEdges()
    {
        const string firstText = "A long first paragraph that wraps across several visual lines";
        const string secondText = "A final paragraph at the bottom of the editor";
        var body = new TextBody();
        body.Paragraphs.Add(new ModelParagraph { Runs = { new ModelRun { Text = firstText } } });
        body.Paragraphs.Add(new ModelParagraph { Runs = { new ModelRun { Text = secondText } } });

        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12))
        {
            AcceptsReturn = true,
            Width = 96,
            Height = 90,
            IsUndoEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        box.Document.PageWidth = 80;
        box.Document.ColumnWidth = 80;
        var window = new Window { Content = box, Width = 96, Height = 90 };
        window.Show();
        window.UpdateLayout();
        try
        {
            box.Focus().Should().BeTrue();
            int documentEnd = LogicalOffsetAt(box.Document, box.Document.ContentEnd);
            var start = PointerAtLogicalOffset(box.Document, -20);
            var end = PointerAtLogicalOffset(box.Document, int.MaxValue);
            box.Selection.Select(start, end);

            LogicalOffsetAt(box.Document, box.Selection.Start).Should().Be(0);
            LogicalOffsetAt(box.Document, box.Selection.End)
                .Should().Be(documentEnd);
            box.Selection.Text.Should().Contain("final paragraph");

            box.Selection.Select(end, start);
            LogicalOffsetAt(box.Document, box.Selection.Start).Should().Be(0);
            LogicalOffsetAt(box.Document, box.Selection.End)
                .Should().Be(documentEnd);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void WpfRichTextBox_ParagraphSelectionIncludesFollowingParagraphBoundary()
    {
        const string firstText = "Alpha beta gamma";
        const string secondText = "Delta epsilon";
        var body = new TextBody();
        body.Paragraphs.Add(new ModelParagraph { Runs = { new ModelRun { Text = firstText } } });
        body.Paragraphs.Add(new ModelParagraph { Runs = { new ModelRun { Text = secondText } } });

        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(body, 12))
        {
            AcceptsReturn = true,
            Width = 220,
            Height = 120,
            IsUndoEnabled = false,
        };
        var window = new Window { Content = box, Width = 220, Height = 120 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var expected = InCanvasRichTextPointerSelectionPlanner.PlanParagraph(
                firstText + "\n" + secondText,
                logicalPosition: 4);
            var start = PointerAtLogicalOffset(box.Document, expected.Start);
            var end = PointerAtLogicalOffset(box.Document, expected.End);
            box.Selection.Select(start, end);

            LogicalOffsetAt(box.Document, box.Selection.Start).Should().Be(expected.Start);
            LogicalOffsetAt(box.Document, box.Selection.End).Should().Be(expected.End);
            box.Selection.Text.Should().Contain("\r\n");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void WpfRichTextEditor_LeavesVisualLineNavigationToNativeRichTextBox()
    {
        string source = ReadWorkspaceFile(
            "freep",
            "FreeP.App.Rendering.Wpf",
            "InCanvasTextEditor.cs");

        var editorType = typeof(RichTextBox);
        editorType.Name.Should().Be("RichTextBox");
        source.Should().Contain("new RichTextBox(doc)");
        source.Should().Contain("AcceptsReturn = true");
        source.Should().NotContain("MoveCaretVertically");
        source.Should().NotContain("InCanvasTextVerticalDirection");
    }

    private static Point CaretOrigin(RichTextBox box) =>
        box.CaretPosition.GetCharacterRect(LogicalDirection.Forward).TopLeft;

    private static void RaiseKey(RichTextBox box, Key key)
    {
        var source = PresentationSource.FromVisual(box)
            ?? throw new InvalidOperationException("WPF RichTextBox has no presentation source.");
        box.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            0,
            key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
    }

    private static TextPointer PointerAtLogicalOffset(FlowDocument document, int offset)
    {
        int target = Math.Max(0, offset);
        for (var position = document.ContentStart;
             position is not null;
             position = position.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (position.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                continue;

            int length = position.GetTextRunLength(LogicalDirection.Forward);
            for (int localOffset = 0; localOffset <= length; localOffset++)
            {
                var candidate = position.GetPositionAtOffset(localOffset);
                if (LogicalOffsetAt(document, candidate) >= target)
                    return candidate;
            }
        }

        return document.ContentEnd;
    }

    private static int LogicalOffsetAt(FlowDocument document, TextPointer position) =>
        new TextRange(document.ContentStart, position)
            .Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Length;

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(ReadWorkspaceFile);
}
