using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 152 remediation gap H3 fixed the slide-level OLE in-place route
/// (<see cref="OleInPlaceCommitEndToEndTests"/>); the inline route -- an embedded object living
/// inside a shape's rich text, hosted by <c>TextBodyFlowDocumentConverter.ModelRunToWpfRun</c>
/// via <c>WpfOleInPlaceHost.AttachInline</c> -- was out of scope then and never wired, so a
/// native in-place commit of an inline object left the document clean.
///
/// Real native in-place activation cannot run headless (no OLE server exists in a test process),
/// so <see cref="WindowsOleInPlaceEngine.PayloadCreatedObserver"/> simulates a native server
/// having rewritten the payload on disk, exactly as the slide-level end-to-end tests do. Every
/// other step is the real production path: <see cref="MainWindow"/> builds the canvas and attaches
/// editing, the real <c>InCanvasTextEditor.Activate</c> converts the model body to a FlowDocument,
/// the real <c>AttachInline</c> handlers create and dispose the host, and <c>CloseAndCommit</c>
/// decides whether to invoke the commit callback. Deleting the <c>onPayloadUpdated</c> argument at
/// any link of that chain fails these tests.
/// </summary>
public sealed class InlineOleInPlaceCommitEndToEndTests
{
    private static TextBody BodyWithInlineOle(byte[] embeddedBytes)
    {
        var body = new TextBody();
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(new ModelRun
        {
            Text = "￼",
            InlineOleObject = new InlineOleObjectInfo
            {
                EmbeddedBytes = embeddedBytes,
                FileName = "Book.xlsx",
                ClassName = "Excel.Sheet.12",
            },
        });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static SlideShape AddInlineOleShape(MainWindow window, byte[] embeddedBytes)
    {
        var slide = window.Editor.CurrentSlide!;
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 701,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = BodyWithInlineOle(embeddedBytes),
        };
        slide.Shapes.Add(shape);
        return shape;
    }

    /// <summary>
    /// Locates the inline OLE placeholder the real converter produced inside the live editor
    /// document. Its Loaded/Unloaded handlers are the production attach/dispose seam.
    /// </summary>
    private static Border InlineOlePlaceholder(MainWindow window)
    {
        var box = window.SlideCanvas.TextEditor!.ActiveRichTextVisual
            .Should().BeOfType<RichTextBox>().Subject;
        return box.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<InlineUIContainer>()
            .Select(container => container.Child)
            .OfType<Border>()
            .Single();
    }

    [StaFact]
    public void InlineOleInPlaceCommit_MarksDocumentDirty_WhenNativeServerRewritesThePayload()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        byte[] rewritten = [9, 9, 9, 9];
        var shape = AddInlineOleShape(window, [1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            window.IsDirty.Should().BeFalse();

            window.SlideCanvas.TextEditor!.Activate(shape.Id);
            var placeholder = InlineOlePlaceholder(window);

            // The real handlers: Loaded creates the native host (the observer rewrites the payload
            // the way a native server would), Unloaded disposes it, which runs CloseAndCommit.
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            window.IsDirty.Should().BeTrue(
                "the inline OLE call site must wire onPayloadUpdated through to " +
                "WpfOleInPlaceHost.AttachInline so a native inline commit marks the document dirty");
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            window.Close();
        }
    }

    /// <summary>
    /// The editor renders an edit-session copy of the shape's text body, so a native commit that
    /// only reaches that copy is lost the moment the user presses Escape. The bytes must land on
    /// the live model instead -- the same thing the external-activation route gets by activating
    /// the live payload directly.
    /// </summary>
    [StaFact]
    public void InlineOleInPlaceCommit_ReachesLiveModel_WhenTheEditIsCanceled()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        byte[] rewritten = [7, 7, 7, 7];
        var shape = AddInlineOleShape(window, [1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            window.SlideCanvas.TextEditor!.Activate(shape.Id);
            var placeholder = InlineOlePlaceholder(window);
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            // Escape: the text edit is discarded, but the OLE server already saved its payload.
            window.SlideCanvas.TextEditor!.Cancel();
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            LiveInlineOleBytes(shape).Should().Equal(
                rewritten,
                "a payload the native server already committed must survive a canceled text edit");
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            window.Close();
        }
    }

    /// <summary>
    /// WPF raises Unloaded through the dispatcher, so the host's commit can arrive after
    /// <c>Commit</c> already converted the document back to a body -- too late to ride that
    /// conversion. The bytes must still reach the live model.
    /// </summary>
    [StaFact]
    public void InlineOleInPlaceCommit_ReachesLiveModel_WhenItArrivesAfterTheEditCommitted()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        byte[] rewritten = [5, 5, 5, 5];
        var shape = AddInlineOleShape(window, [1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            window.SlideCanvas.TextEditor!.Activate(shape.Id);
            var placeholder = InlineOlePlaceholder(window);
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            window.SlideCanvas.TextEditor!.Commit();
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            LiveInlineOleBytes(shape).Should().Equal(
                rewritten,
                "a commit delivered after the edit ended must still reach the live model");
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            window.Close();
        }
    }

    private static byte[] LiveInlineOleBytes(SlideShape shape) =>
        shape.TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.InlineOleObject)
            .OfType<InlineOleObjectInfo>()
            .Single()
            .EmbeddedBytes;

    /// <summary>
    /// Sibling no-regression test: opening and closing an inline object without a native edit is
    /// the common case and must not mark the document dirty. Guards against a wiring fix that
    /// calls MarkDirty unconditionally rather than only when
    /// <see cref="WindowsOleInPlaceEngine.CloseAndCommit"/> sees a changed payload.
    /// </summary>
    [StaFact]
    public void InlineOleInPlaceCommit_DoesNotMarkDocumentDirty_WhenPayloadIsUnchanged()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        var shape = AddInlineOleShape(window, [1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        try
        {
            window.IsDirty.Should().BeFalse();

            window.SlideCanvas.TextEditor!.Activate(shape.Id);
            var placeholder = InlineOlePlaceholder(window);
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            placeholder.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            window.IsDirty.Should().BeFalse(
                "closing an inline in-place host without a native edit must leave the document clean");
        }
        finally
        {
            window.Close();
        }
    }
}
