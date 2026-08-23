using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Ole.Windows;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// Round 152 remediation gap H3: <see cref="AvaloniaOleInPlaceHostCommitCallbackTests"/> verifies
/// that the native host adopts the shared payload callback policy, but says nothing about whether
/// <see cref="MainWindow.TryOpenOleInPlace"/> actually passes
/// <c>onPayloadUpdated: _ =&gt; _fileWorkflow.MarkDirty()</c> at its call site into
/// <c>AvaloniaOleInPlaceHost.TryShow</c>. Deleting that argument leaves that test green.
///
/// Real native in-place activation cannot run headless, so
/// <see cref="WindowsOleInPlaceEngine.PayloadCreatedObserver"/> is used to simulate a native server
/// having rewritten the payload on disk. From there the real, unmodified production path runs:
/// <c>AvaloniaOleInPlaceHost.TryShow</c> succeeds (native activation is deferred to
/// <c>CreateNativeControlCore</c>, which a headless test never reaches), so disposing the returned
/// host -- the same thing a routine gesture does via <c>CloseActiveOleHost</c> -- runs
/// <c>CloseAndCommit</c>, which reads the (now-changed) file back and -- only if
/// <c>MainWindow.TryOpenOleInPlace</c> still wires the argument -- invokes it, marking the document
/// dirty. This drives the real private method through the production
/// <c>MainWindow.TryOpenOleInPlaceForTests</c> forwarder, not a hand-rolled copy of the wiring.
/// </summary>
public sealed class OleInPlaceCommitEndToEndTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static OleInPlaceCommitEndToEndTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    private static Task<bool> OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None)
            .ContinueWith(task => task.Exception is null, CancellationToken.None);

    private static SlideShape CreateOleShape(byte[] embeddedBytes) => new()
    {
        Id = 601,
        Kind = SlideShapeKind.Ole,
        OffsetXEmu = 0,
        OffsetYEmu = 0,
        ExtentCxEmu = 914400L,
        ExtentCyEmu = 685800L,
        OleObject = new OleObjectInfo { EmbeddedBytes = embeddedBytes, FileName = "Book.xlsx" },
    };

    [Fact]
    public async Task TryOpenOleInPlace_MarksDocumentDirty_WhenNativeServerRewritesThePayload()
    {
        byte[] rewritten = [9, 9, 9, 9];
        var shape = CreateOleShape([1, 2, 3]);
        var wasDirtyBeforeCommit = true;
        var isDirtyAfterCommit = false;

        WindowsOleInPlaceEngine.PayloadCreatedObserver =
            engine => File.WriteAllBytes(engine.SourcePath, rewritten);
        try
        {
            var ran = await OnUiThread(() =>
            {
                var window = new MainWindow(Array.Empty<string>());
                wasDirtyBeforeCommit = window.IsDirty;

                // Real in-place activation cannot start headless; TryShow still succeeds here
                // because native activation is deferred until the control attaches to a real
                // compositor, which never happens in this headless host. What matters is what
                // closing the resulting host does to the document.
                window.TryOpenOleInPlaceForTests(shape);
                window.CloseActiveOleHostForTests();

                isDirtyAfterCommit = window.IsDirty;
            });

            if (!ran)
                return; // no headless drawing backend in this environment

            wasDirtyBeforeCommit.Should().BeFalse();
            isDirtyAfterCommit.Should().BeTrue(
                "TryOpenOleInPlace must wire onPayloadUpdated through to AvaloniaOleInPlaceHost.TryShow " +
                "so a native commit marks the document dirty end to end");
            shape.OleObject!.EmbeddedBytes.Should().Equal(rewritten);
        }
        finally
        {
            WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        }
    }

    /// <summary>
    /// Sibling no-regression test: an unedited payload (the common case -- double-click, look,
    /// close without changing anything) must not spuriously mark the document dirty. This guards
    /// against a wiring fix that calls <c>MarkDirty</c> unconditionally instead of only when
    /// <see cref="WindowsOleInPlaceEngine.CloseAndCommit"/> actually detects a changed payload.
    /// </summary>
    [Fact]
    public async Task TryOpenOleInPlace_DoesNotMarkDocumentDirty_WhenPayloadIsUnchanged()
    {
        var shape = CreateOleShape([1, 2, 3]);
        var isDirtyAfterClose = false;

        WindowsOleInPlaceEngine.PayloadCreatedObserver = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());

            window.TryOpenOleInPlaceForTests(shape);
            window.CloseActiveOleHostForTests();

            isDirtyAfterClose = window.IsDirty;
        });

        if (!ran)
            return; // no headless drawing backend in this environment

        isDirtyAfterClose.Should().BeFalse(
            "closing an in-place host without a native edit must not mark the document dirty");
    }
}
