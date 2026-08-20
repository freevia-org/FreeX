using System.IO;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 152 remediation gap H3: the ten existing OLE in-place commit tests
/// (<see cref="WpfOleInPlaceHostTests"/> and the Avalonia sibling) call
/// <c>WpfOleInPlaceHost.BuildCommitCallback</c> directly, so they verify the helper composes two
/// delegates correctly but say nothing about whether <see cref="MainWindow.TryOpenOleInPlace"/>
/// actually passes <c>onPayloadUpdated: _ =&gt; _fileSession.MarkDirty()</c> at its call site into
/// <c>WpfOleInPlaceHost.TryShow</c>. Deleting that argument leaves all ten tests green.
///
/// Real native in-place activation cannot run headless (no OLE server is available in a test
/// process -- see the comment on <see cref="WpfOleInPlaceHostTests.CommitCallback_UpdatesModelAndNotifiesCaller_ForNativeInPlaceRoute"/>),
/// so <see cref="WindowsOleInPlaceEngine.PayloadCreatedObserver"/> is used to simulate a native
/// server having rewritten the payload on disk. From there the real, unmodified production path
/// runs: <c>TryStart</c> fails (no live window handle in this headless host), the host is
/// disposed, <c>CloseAndCommit</c> reads the (now-changed) file back, and -- only if
/// <c>MainWindow.TryOpenOleInPlace</c> still wires the argument -- invokes it, marking the
/// document dirty. This drives the real private method through the production
/// <c>MainWindow.TryOpenOleInPlaceForTests</c> forwarder, not a hand-rolled copy of the wiring.
/// </summary>
public sealed class OleInPlaceCommitEndToEndTests
{
    private static SlideShape CreateOleShape(byte[] embeddedBytes) => new()
    {
        Id = 501,
        Kind = SlideShapeKind.Ole,
        OffsetXEmu = 0,
        OffsetYEmu = 0,
        ExtentCxEmu = 914400L,
        ExtentCyEmu = 685800L,
        OleObject = new OleObjectInfo { EmbeddedBytes = embeddedBytes, FileName = "Book.xlsx" },
    };

    [StaFact]
    public void TryOpenOleInPlace_MarksDocumentDirty_WhenNativeServerRewritesThePayload()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        byte[] rewritten = [9, 9, 9, 9];
        var shape = CreateOleShape([1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            window.IsDirty.Should().BeFalse();

            // Real in-place activation cannot start headless, so this returns false; what matters
            // is what happens to the document on the way to that false, via the disposal path.
            window.TryOpenOleInPlaceForTests(shape);

            window.IsDirty.Should().BeTrue(
                "TryOpenOleInPlace must wire onPayloadUpdated through to WpfOleInPlaceHost.TryShow " +
                "so a native commit marks the document dirty end to end");
            shape.OleObject!.EmbeddedBytes.Should().Equal(rewritten);
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
            window.Close();
        }
    }

    /// <summary>
    /// Sibling no-regression test: an unedited payload (the common case -- double-click, look,
    /// close without changing anything) must not spuriously mark the document dirty. This guards
    /// against a wiring fix that calls <c>MarkDirty</c> unconditionally instead of only when
    /// <see cref="WindowsOleInPlaceEngine.CloseAndCommit"/> actually detects a changed payload.
    /// </summary>
    [StaFact]
    public void TryOpenOleInPlace_DoesNotMarkDocumentDirty_WhenPayloadIsUnchanged()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        var shape = CreateOleShape([1, 2, 3]);

        WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        try
        {
            window.IsDirty.Should().BeFalse();

            window.TryOpenOleInPlaceForTests(shape);

            window.IsDirty.Should().BeFalse(
                "closing an in-place host without a native edit must not mark the document dirty");
        }
        finally
        {
            window.Close();
        }
    }
}
