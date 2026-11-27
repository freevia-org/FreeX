using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// The Replace with box's <c>^c</c> (Clipboard Contents) on the Avalonia dialog. The clipboard is read
/// asynchronously, but the shared replace path is synchronous, so an action whose Replace with box uses
/// <c>^c</c> reads the clipboard first and then runs against that snapshot; Find Next never touches it.
/// </summary>
public sealed class FindReplaceClipboardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static DocumentView ViewWith(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string[] Body(DocumentView view) =>
        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToArray();

    [Fact]
    public async Task ReplaceAll_reads_the_clipboard_asynchronously_before_replacing_and_inserts_its_text()
    {
        var clipboard = new FakeClipboard("pasted") { YieldBeforeReading = true };
        string[] body = [];
        var ran = await HeadlessUiThread.RunAsync(async () =>
        {
            var view = ViewWith("alpha beta alpha");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetPlatformClipboardForTest(clipboard);
                dialog.SetFindTextForTest("alpha");
                dialog.SetReplaceTextForTest("<^c>");

                var pending = dialog.ExecuteWithClipboardForTest(FindReplaceDialogActionKind.ReplaceAll);
                Body(view).Should().Equal(["alpha beta alpha"], "nothing is replaced until the clipboard read completes");
                await pending;
                body = Body(view);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        body.Should().Equal("<pasted> beta <pasted>");
        clipboard.Reads.Should().Be(1, "the clipboard is read once per action, not once per match");
    }

    [Fact]
    public async Task Replace_button_with_a_clipboard_code_reads_the_clipboard_and_replaces_the_selected_match()
    {
        var clipboard = new FakeClipboard("X");
        string[] body = [];
        var ran = await HeadlessUiThread.RunAsync(async () =>
        {
            var view = ViewWith("one two one");
            var dialog = new FindReplaceDialog(view, FindReplaceOpenMode.Replace);
            try
            {
                dialog.SetPlatformClipboardForTest(clipboard);
                dialog.SetFindTextForTest("one");
                dialog.SetReplaceTextForTest("^c");

                dialog.FindNextForTest();
                clipboard.Reads.Should().Be(0, "Find Next has no use for the clipboard");
                dialog.ReplaceForTest();
                await Task.Yield();
                body = Body(view);
            }
            finally
            {
                dialog.Close();
            }
        });
        ran.Should().BeTrue();

        clipboard.Reads.Should().Be(1);
        body[0].Should().StartWith("X two");
    }

    [Fact]
    public async Task Clipboard_lines_become_paragraph_marks_and_an_unavailable_clipboard_reads_as_empty()
    {
        string[] multiline = [];
        string[] emptied = [];
        var ran = await HeadlessUiThread.RunAsync(async () =>
        {
            var lines = ViewWith("a-b");
            var first = new FindReplaceDialog(lines, FindReplaceOpenMode.Replace);
            try
            {
                first.SetPlatformClipboardForTest(new FakeClipboard("x\r\ny"));
                first.SetFindTextForTest("-");
                first.SetReplaceTextForTest("^c");
                await first.ExecuteWithClipboardForTest(FindReplaceDialogActionKind.ReplaceAll);
                multiline = Body(lines);
            }
            finally
            {
                first.Close();
            }

            var missing = ViewWith("a-b");
            var second = new FindReplaceDialog(missing, FindReplaceOpenMode.Replace);
            try
            {
                second.SetPlatformClipboardForTest(new FakeClipboard(null) { Throws = true });
                second.SetFindTextForTest("-");
                second.SetReplaceTextForTest("^c");
                await second.ExecuteWithClipboardForTest(FindReplaceDialogActionKind.ReplaceAll);
                emptied = Body(missing);
            }
            finally
            {
                second.Close();
            }
        });
        ran.Should().BeTrue();

        multiline.Should().Equal("ax", "yb");
        emptied.Should().Equal(["ab"], "an unreadable clipboard is an empty one, as in Word");
    }

    private sealed class FakeClipboard(string? text) : IPlatformClipboard
    {
        public int Reads { get; private set; }

        public bool YieldBeforeReading { get; init; }

        public bool Throws { get; init; }

        public async ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            if (YieldBeforeReading)
                await Task.Yield();
            if (Throws)
                throw new InvalidOperationException("clipboard unavailable");
            return text is null
                ? PlatformClipboardReadResult<PlatformClipboardContent>.Empty()
                : PlatformClipboardReadResult<PlatformClipboardContent>.Success(new PlatformClipboardContent(text));
        }

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
