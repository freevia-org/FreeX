using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun = FreeP.Core.Model.Run;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// The Avalonia sibling of the WPF inline end-to-end tests: <see cref="OleInPlaceCommitEndToEndTests"/>
/// covers the slide-level route (<c>AvaloniaOleInPlaceHost.TryShow</c>), while the inline route --
/// an embedded object living inside a shape's rich text, hosted through the
/// <c>AvaloniaOleInPlaceHost.TryCreate</c> factory that <c>MainWindow.WireInteraction</c> hands to
/// <c>AvaloniaInCanvasTextEditor</c> -- composes its own commit callback and was never wired to
/// document dirty-marking.
///
/// Real native in-place activation cannot run headless, so
/// <see cref="WindowsOleInPlaceEngine.PayloadCreatedObserver"/> simulates a native server having
/// rewritten the payload on disk; everything else is the production path. The edit is ended with
/// Cancel (the Escape route) on purpose: Commit would mark the document dirty by itself through
/// the ordinary text-edit command, which would hide whether the inline commit is wired at all.
/// </summary>
public sealed class InlineOleInPlaceCommitEndToEndTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static InlineOleInPlaceCommitEndToEndTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    private static Task<bool> OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None)
            .ContinueWith(task => task.Exception is null, CancellationToken.None);

    private static SlideShape CreateInlineOleShape(byte[] embeddedBytes)
    {
        var body = new TextBody();
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(new ModelRun
        {
            Text = "￼",
            InlineOleObject = new InlineOleObjectInfo
            {
                EmbeddedBytes = embeddedBytes,
                // A blocked extension on purpose: if the in-place host factory is ever skipped,
                // the editor falls back to external activation, and this keeps that fallback from
                // launching a real application from a test process. The in-place route itself does
                // not consult the block list, so the path under test is unaffected.
                FileName = "Book.exe",
                ClassName = "Excel.Sheet.12",
            },
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Id = 611,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = body,
        };
    }

    [Fact]
    public async Task InlineOleInPlaceCommit_MarksDocumentDirty_WhenNativeServerRewritesThePayload()
    {
        byte[] rewritten = [9, 9, 9, 9];
        var wasDirtyBeforeCommit = true;
        var activated = false;
        var isDirtyAfterCommit = false;
        byte[] liveBytesAfterCommit = [];

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            var ran = await OnUiThread(() =>
            {
                var window = new MainWindow(Array.Empty<string>());
                var shape = CreateInlineOleShape([1, 2, 3]);
                var slide = window.Editor.CurrentSlide!;
                slide.Shapes.Clear();
                slide.Shapes.Add(shape);
                wasDirtyBeforeCommit = window.IsDirty;

                window.ActivateShapeTextEditForTests(shape.Id);
                activated = window.TryActivateInlineOleObjectForTests();
                window.CancelShapeTextEditForTests();

                isDirtyAfterCommit = window.IsDirty;
                liveBytesAfterCommit = LiveInlineOleBytes(shape);
            });

            if (!ran)
                return; // no headless drawing backend in this environment

            wasDirtyBeforeCommit.Should().BeFalse();
            activated.Should().BeTrue(
                "the inline embedded object must be hosted in place, not handed to external activation");
            isDirtyAfterCommit.Should().BeTrue(
                "the inline OLE host factory must mark the document dirty when a native server " +
                "commits an edited payload, the same way the slide-level route does");
            liveBytesAfterCommit.Should().Equal(
                rewritten,
                "the editor renders a copy of the body, so a payload the server already committed " +
                "must be routed to the live model rather than discarded with the canceled edit");
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        }
    }

    /// <summary>
    /// Sibling no-regression test: opening and closing an inline object without a native edit is
    /// the common case and must leave the document clean.
    /// </summary>
    private static byte[] LiveInlineOleBytes(SlideShape shape) =>
        shape.TextBody!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.InlineOleObject)
            .OfType<InlineOleObjectInfo>()
            .Single()
            .EmbeddedBytes;

    [Fact]
    public async Task InlineOleInPlaceCommit_DoesNotMarkDocumentDirty_WhenPayloadIsUnchanged()
    {
        var isDirtyAfterClose = true;

        WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var shape = CreateInlineOleShape([1, 2, 3]);
            var slide = window.Editor.CurrentSlide!;
            slide.Shapes.Clear();
            slide.Shapes.Add(shape);

            window.ActivateShapeTextEditForTests(shape.Id);
            window.TryActivateInlineOleObjectForTests();
            window.CancelShapeTextEditForTests();

            isDirtyAfterClose = window.IsDirty;
        });

        if (!ran)
            return; // no headless drawing backend in this environment

        isDirtyAfterClose.Should().BeFalse(
            "closing an inline in-place host without a native edit must leave the document clean");
    }
}
