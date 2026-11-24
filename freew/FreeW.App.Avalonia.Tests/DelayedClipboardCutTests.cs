using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DelayedClipboardCutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("selection")]
    [InlineData("formatting")]
    [InlineData("external-model")]
    [InlineData("reload")]
    public Task DelayedCutDoesNotDeleteAfterSelectionOrModelDrift(string drift) => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var view = window.Editor;
            var cut = CutAsync(window);
            clipboard.WriteCalls.Should().Be(1);
            clipboard.Written!.Text.Should().Be("same");
            view.Document.PlainText.Should().Be("same same\nuntouched");
            switch (drift)
            {
                case "selection":
                    // The new selection has identical text, so text-only comparison is insufficient.
                    view.SetBodySelectionForTest(0, 5, 0, 9);
                    break;
                case "formatting":
                    // Keep text/endpoints unchanged while changing the selected model runs.
                    view.ToggleBold();
                    break;
                case "external-model":
                    ((Paragraph)view.Document.Blocks[1]).Runs[0].Text = "changed outside selection";
                    view.InvalidateAfterExternalMutation();
                    break;
                case "reload":
                    view.LoadDocument(Document());
                    view.SetBodySelectionForTest(0, 0, 0, 4);
                    break;
            }
            // External model refresh legitimately clamps/collapses the selection. The
            // pending Cut must preserve whichever selection/caret the intervening action left.
            var postDriftSelection = view.SelectedText;
            var postDriftSnapshot = view.AutomationSnapshot();
            var postDriftText = view.Document.PlainText;
            clipboard.Complete(PlatformClipboardWriteResult.Success());
            await cut;

            ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("same same");
            view.Document.PlainText.Should().Be(postDriftText);
            view.SelectedText.Should().Be(postDriftSelection);
            var afterCompletion = view.AutomationSnapshot();
            afterCompletion.CaretOffset.Should().Be(postDriftSnapshot.CaretOffset);
            afterCompletion.Selection.Should().BeEquivalentTo(postDriftSnapshot.Selection);
            clipboard.WriteCalls.Should().Be(1);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnchangedDelayedCutDeletesTheCapturedSelectionExactlyOnce() => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var view = window.Editor;
            var cut = CutAsync(window);
            clipboard.WriteCalls.Should().Be(1);
            view.Document.PlainText.Should().Be("same same\nuntouched");
            clipboard.Complete(PlatformClipboardWriteResult.Success());
            await cut;

            view.Document.PlainText.Should().Be(" same\nuntouched");
            view.SelectedText.Should().BeEmpty();
            view.Undo();
            view.Document.PlainText.Should().Be("same same\nuntouched");
            view.CanUndo.Should().BeFalse("a successful Cut creates only one deletion undo unit");
            clipboard.WriteCalls.Should().Be(1);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FailedOrUnavailableClipboardWriteDoesNotDeleteAnUnchangedProjectedSelection(bool unavailable) => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var cut = CutAsync(window);
            clipboard.Complete(unavailable ? PlatformClipboardWriteResult.Unavailable() : PlatformClipboardWriteResult.Failed("clipboard unavailable"));
            await cut;

            window.Editor.Document.PlainText.Should().Be("same same\nuntouched");
            window.Editor.SelectedText.Should().Be("same");
            window.Editor.CanUndo.Should().BeFalse();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task UnsupportedAtomicCutDoesNotWriteOrDeleteItsDrawingPayload() => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var drawing = SelectInlineDrawing(window.Editor);
            window.Editor.IsDisplaySelectionDeletionBlocked().Should().BeFalse("the whole atom is a valid model deletion");
            window.Editor.TryGetDisplaySelectionProjection(out _, out var plan).Should().BeTrue();
            plan.CanMutate.Should().BeTrue();
            await CutAsync(window);

            clipboard.WriteCalls.Should().Be(0,"Cut cannot flatten and destroy an unsupported atomic payload");
            ((Paragraph)window.Editor.Document.Blocks[0]).Runs.Single().Shape.Should().BeSameAs(drawing);
            window.Editor.SelectedText.Should().Be("drawing");
            window.Editor.CanUndo.Should().BeFalse();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task OrdinaryCopyMayFlattenAnUnsupportedAtomicDisplay() => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var drawing = SelectInlineDrawing(window.Editor);
            var copy = (Task)typeof(MainWindow).GetMethod("CopyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [null, false])!;
            clipboard.WriteCalls.Should().Be(1);
            clipboard.Written!.Text.Should().Be("drawing");
            clipboard.Written.GetBytes(FreeW.App.Presentation.Editing.FreeWClipboardApplicationWorkflow.NativeDocumentFormat,
                PlatformClipboardFormatScope.Application).Should().BeNull();
            clipboard.Complete(PlatformClipboardWriteResult.Success());
            await copy;

            ((Paragraph)window.Editor.Document.Blocks[0]).Runs.Single().Shape.Should().BeSameAs(drawing);
            window.Editor.CanUndo.Should().BeFalse();
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DelayedCutDoesNotCommitDuringAnOpenFormattingUndoGroup() => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        var groupOpen = false;
        try
        {
            var view = window.Editor;
            var cut = CutAsync(window);
            view.BeginFontUndoGroup();
            groupOpen = true;
            view.ToggleBold();
            clipboard.Complete(PlatformClipboardWriteResult.Success());
            await cut;

            view.Document.PlainText.Should().Be("same same\nuntouched");
            view.SelectedText.Should().Be("same");
            // The Cut guard must neither close the caller's group nor roll formatting back.
            view.CommitFontUndoGroup("Formatting");
            groupOpen = false;
            ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.Bold.Should().BeTrue();
            view.Undo();
            view.Document.PlainText.Should().Be("same same\nuntouched");
        }
        finally
        {
            if (groupOpen) window.Editor.CommitFontUndoGroup("Formatting");
            window.Close();
        }
    });

    [Fact]
    public Task DelayedCutDoesNotCommitAfterASilentlyAbortedFormattingUndoGroup() => OnUi(async () =>
    {
        var clipboard = new DelayedClipboard();
        var window = CreateWindow(clipboard);
        try
        {
            var view = window.Editor;
            var cut = CutAsync(window);
            view.BeginFontUndoGroup();
            view.ToggleBold();
            // Abort closes the batch without rolling its applied model edits back or raising Changed.
            view.AbortFontUndoGroup();
            clipboard.Complete(PlatformClipboardWriteResult.Success());
            await cut;

            view.Document.PlainText.Should().Be("same same\nuntouched");
            view.SelectedText.Should().Be("same");
            ((Paragraph)view.Document.Blocks[0]).Runs[0].Formatting.Bold.Should().BeTrue();
            view.CanUndo.Should().BeFalse("the Cut guard does not synthesize or commit the aborted group");
            clipboard.WriteCalls.Should().Be(1);
        }
        finally
        {
            window.Editor.AbortFontUndoGroup();
            window.Close();
        }
    });

    [Fact]
    public Task StampCapturedInsideAnUndoGroupCannotMatchAfterItCloses() => OnUi(() =>
    {
        var view = new DocumentView();
        view.LoadDocument(Document());
        view.SetBodySelectionForTest(0, 0, 0, 4);
        view.BeginFontUndoGroup();
        var stamp = view.CaptureClipboardSelectionStamp();
        view.AbortFontUndoGroup();

        view.MatchesClipboardSelectionStamp(stamp).Should().BeFalse();
        view.Document.PlainText.Should().Be("same same\nuntouched");
        view.CanUndo.Should().BeFalse();
        return Task.CompletedTask;
    });

    private static Shape SelectInlineDrawing(DocumentView view)
    {
        var drawing = new Shape(ShapeKind.Rectangle, 60, 40);
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { new Run("drawing") { Shape = drawing } } });
        view.LoadDocument(document);
        view.SetBodySelectionForTest(0, 0, 0, 7);
        return drawing;
    }

    [Fact]
    public Task StampsAreOpaqueAndCannotBeReusedForAnotherEditor() => OnUi(() =>
    {
        var first = new DocumentView();
        first.LoadDocument(Document());
        first.SetBodySelectionForTest(0, 0, 0, 4);
        var second = new DocumentView();
        second.LoadDocument(first.Document);
        second.SetBodySelectionForTest(0, 0, 0, 4);
        var stamp = first.CaptureClipboardSelectionStamp();

        first.MatchesClipboardSelectionStamp(stamp).Should().BeTrue();
        second.MatchesClipboardSelectionStamp(stamp).Should().BeFalse();
        first.MatchesClipboardSelectionStamp(new object()).Should().BeFalse();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShapeChildAndActiveTextPathsCompareByContents() => OnUi(() =>
    {
        var shape = new Shape(ShapeKind.TextBox, 80, 40);
        shape.TextParagraphs.Add(new Paragraph("text"));
        var group = new DrawingGroup
        {
            WidthPt = 180, HeightPt = 80,
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
        };
        group.Children.Add(shape);
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(new Shape(ShapeKind.Rectangle, 40, 40));
        group.ChildOffsets.Add((100, 0));
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.FromDrawingGroup(group) } });
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1200));
        view.SelectFloating(0, 0);
        view.SelectFloatingGroupChildForTest(view.FloatingGroupChildRectForPathForTest(0, 0, [0])!.Value.Center)
            .Should().BeTrue();
        view.EnterSelectedShapeTextEditing().Should().BeTrue();
        view.SelectShapeTextRangeForTest(0, 0, 4).Should().BeTrue();
        var stamp = view.CaptureClipboardSelectionStamp();

        view.MatchesClipboardSelectionStamp(stamp).Should().BeTrue("fresh path snapshots contain the same indexes");
        view.SelectFloating(0, 0);
        view.SelectFloatingGroupChildForTest(view.FloatingGroupChildRectForPathForTest(0, 0, [1])!.Value.Center)
            .Should().BeTrue();
        view.MatchesClipboardSelectionStamp(stamp).Should().BeFalse();
        return Task.CompletedTask;
    });

    [Fact]
    public Task MultipleDrawingSelectionsCompareByContents() => OnUi(() =>
    {
        var paragraph = new Paragraph();
        foreach (var offset in new[] { 0, 100 })
            paragraph.Runs.Add(new Run("")
            {
                Shape = new Shape(ShapeKind.Rectangle, 60, 40)
                {
                    Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront, HorizontalOffsetPt = offset },
                },
            });
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1200));
        view.SelectFloating(0, 0);
        view.SelectFloating(0, 1, addToMultiSelect: true);
        var stamp = view.CaptureClipboardSelectionStamp();

        view.MatchesClipboardSelectionStamp(stamp).Should().BeTrue();
        view.SelectFloating(0, 0);
        view.MatchesClipboardSelectionStamp(stamp).Should().BeFalse();
        return Task.CompletedTask;
    });

    // Await the actual shell method so assertions happen after its post-write guard, including
    // refusal paths that deliberately raise no document-change event.
    private static Task CutAsync(MainWindow window) => (Task)typeof(MainWindow)
        .GetMethod("CutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;

    private static MainWindow CreateWindow(DelayedClipboard clipboard)
    {
        var window = new MainWindow([], new FreeWOptions(), new InMemoryApplicationOptionsStore<FreeWOptions>(),
            platformClipboard: clipboard, promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
            suppressStartupRecoveryOffer: true);
        window.Editor.LoadDocument(Document());
        window.Editor.SetBodySelectionForTest(0, 0, 0, 4);
        return window;
    }

    private static TextDocument Document()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("same same"));
        document.Blocks.Add(new Paragraph("untouched"));
        return document;
    }

    private static async Task OnUi(Func<Task> action) => await Session.Dispatch(async () =>
    {
        await action();
        return true;
    }, CancellationToken.None);

    private sealed class DelayedClipboard : IPlatformClipboard
    {
        private readonly TaskCompletionSource<PlatformClipboardWriteResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WriteCalls { get; private set; }
        public PlatformClipboardContent? Written { get; private set; }
        public void Complete(PlatformClipboardWriteResult result) => _completion.SetResult(result);
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Unavailable());
        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content, CancellationToken cancellationToken = default)
        {
            Written = content;
            WriteCalls++;
            return new ValueTask<PlatformClipboardWriteResult>(_completion.Task);
        }
        public ValueTask<PlatformClipboardWriteResult> ClearAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }
}
