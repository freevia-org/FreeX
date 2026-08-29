using System.IO;
using System.Reflection;

using Avalonia.Headless;

using FreeX.App.Services;
using FreeX.Core.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for the round-168 finding (meta F2): the WPF host's round-167 fix
/// (<c>ApplyAdoptedReadOnlySession</c>, <c>src/FreeX.App.Host/MainWindow.MultiWindow.cs</c>)
/// propagates the originating window's Read-Only-Recommended / write-reservation-password decision
/// into a View &gt; New Window sibling before it is shown. The Avalonia shell's own
/// <c>NewWindow()</c> (<c>src/FreeX.App.Avalonia/MainWindow.WindowManagement.cs</c>) never did the
/// same: the new sibling's own per-window <c>_workbookReadOnlySession</c>
/// (<c>src/FreeX.App.Avalonia/MainWindow.cs</c>) stayed at its default <c>IsReadOnly=false</c> even
/// though the shared workbook was opened read-only in the originating window, so
/// <c>ResolveExistingSaveTarget()</c> -- which consults only the sibling's OWN session -- would
/// resolve the real on-disk path and let a direct Ctrl+S there silently overwrite the protected
/// file.
///
/// These tests drive the REAL, private <c>NewWindow()</c> method via reflection (the actual
/// production route <c>view.newWindow</c> reaches -- see the ribbon command dictionary entry
/// <c>["New Window"] = NewWindow</c> in <c>MainWindow.cs</c>) and the REAL, shared
/// <c>WorkbookFileLifecycleCoordinator.SaveResolvedAsync</c> gate that every Ctrl+S / Save button
/// click funnels through (<c>SaveCurrentWorkbookAsync</c> in <c>MainWindow.cs</c>), so "the file on
/// disk is unchanged" is a literal byte-for-byte assertion against the shipping decision chain, not
/// an inference from a flag.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R168_AvaloniaNewWindowReadOnlyPropagationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly MethodInfo NewWindowMethod =
        typeof(MainWindow).GetMethod("NewWindow", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(MainWindow), "NewWindow");

    /// <summary>Drives the real, private view.newWindow route (see class remarks).</summary>
    private static void InvokeNewWindow(MainWindow window) => NewWindowMethod.Invoke(window, null);

    /// <summary>
    /// Finds the sibling window <see cref="InvokeNewWindow"/> just created: the only OTHER registered
    /// window over the same document. Filtering by <c>DocumentId</c> (rather than "everything except
    /// <paramref name="source"/>") keeps this robust against windows a differently-scoped earlier test
    /// left registered in the process-wide registry.
    /// </summary>
    private static MainWindow FindNewSibling(MainWindow source) =>
        MainWindow.WindowRegistryForTest.Windows.Single(
            w => !ReferenceEquals(w, source) && w.DocumentId == source.DocumentId);

    /// <summary>
    /// Attempts a save through the REAL production gate/coordinator (see class remarks), on a real
    /// on-disk file, and returns the file's bytes afterward so the caller can assert "unchanged".
    /// <paramref name="directOverwriteInvoked"/> is set to true only if the coordinator actually took
    /// the direct-write branch -- i.e. only if <c>ResolveExistingSaveTarget()</c> failed to withhold
    /// the existing path.
    /// </summary>
    private static byte[] AttemptSaveAndReadFileBytes(
        MainWindow window, string path, out bool directOverwriteInvoked)
    {
        var invoked = false;
        WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: path,
            resolveCurrentTarget: window.ResolveExistingSaveTargetForTest,
            saveTargetAsync: (FileSaveTarget target) =>
            {
                invoked = true;
                // Stand-in for MainWindow.cs's real SaveWorkbookToTargetAsync: if this branch is
                // reached, the file really would be overwritten -- prove it by actually overwriting
                // it, so a byte comparison catches the bypass instead of trusting a bool.
                File.WriteAllBytes(path, "OVERWRITTEN-BY-SIBLING-DIRECT-SAVE"u8.ToArray());
                return Task.FromResult(true);
            },
            // Stand-in for SaveWorkbookAsAsync (Save-As dialog): never touches the original protected
            // file -- exactly what Excel-parity Save-As does regardless of what the user ultimately
            // picks.
            saveAsAsync: () => Task.FromResult(true)).GetAwaiter().GetResult();

        directOverwriteInvoked = invoked;
        return File.ReadAllBytes(path);
    }

    private static void CloseWindow(MainWindow window)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }

    /// <summary>
    /// The primary regression scenario, matching the finding's USER GESTURE exactly: window A opens
    /// a write-reservation-protected (or Read-Only-Recommended-declined) workbook and is marked
    /// read-only (step 1); View &gt; New Window (the real, private <c>NewWindow()</c>) creates
    /// sibling B over the same shared document (step 2); B attempts to save the shared document
    /// (step 3). The file on disk must be byte-for-byte unchanged, and the direct-overwrite branch
    /// must never even be reached.
    /// </summary>
    [Fact]
    public async Task NewWindowSibling_InheritsSourceWindowsReadOnlySession_SoDirectSaveNeverOverwritesTheFile()
    {
        await Session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var protectedPath = Path.Combine(tempDir, "Protected.fxl");
            var originalBytes = "ORIGINAL-PROTECTED-CONTENT"u8.ToArray();
            File.WriteAllBytes(protectedPath, originalBytes);

            var first = new MainWindow([]);
            MainWindow? sibling = null;
            try
            {
                // Step 1: the primary window's Read-Only-Recommended / write-reservation-password
                // decision, exactly as ApplyWorkbookReadOnlyOpenPolicy would have set it on open.
                first.SetWorkbookReadOnlyForTest(true);
                first.Session.MarkSaved(protectedPath);
                first.IsWorkbookReadOnlyForTest.Should().BeTrue("test setup sanity check");

                // Step 2: View > New Window -- the real production route.
                InvokeNewWindow(first);
                sibling = FindNewSibling(first);

                sibling.Session.Workbook.Should().BeSameAs(first.Session.Workbook,
                    "New Window opens a second view of the SAME shared document");
                sibling.IsWorkbookReadOnlyForTest.Should().BeTrue(
                    "NewWindow must propagate the originating window's read-only decision into the " +
                    "sibling's own _workbookReadOnlySession, mirroring the WPF host's " +
                    "ApplyAdoptedReadOnlySession");
                sibling.ResolveExistingSaveTargetForTest().Should().BeNull(
                    "a read-only sibling must never resolve back to the protected file's own path");

                // Step 3: attempt to save from the NEW (sibling) window, through the real gate.
                var bytesAfter = AttemptSaveAndReadFileBytes(
                    sibling, protectedPath, out var directOverwriteInvoked);

                directOverwriteInvoked.Should().BeFalse(
                    "the sibling's save must fall through to Save-As, never the direct-overwrite branch");
                bytesAfter.Should().Equal(originalBytes,
                    "the protected file on disk must be byte-for-byte unchanged after the sibling's " +
                    "save attempt");
            }
            finally
            {
                if (sibling is not null)
                    CloseWindow(sibling);
                CloseWindow(first);
                TryDeleteDirectory(tempDir);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// No-regression sibling (adjacent case): when the originating document is NOT read-only, the new
    /// window must remain fully editable and its direct save must still go through normally -- the fix
    /// must only ever RAISE the sibling's read-only state to match the source, never force every
    /// sibling read-only regardless of the source's actual state.
    /// </summary>
    [Fact]
    public async Task NewWindowSibling_OfAnEditableDocument_StillResolvesDirectSaveNormally()
    {
        await Session.Dispatch(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var editablePath = Path.Combine(tempDir, "Editable.fxl");
            File.WriteAllBytes(editablePath, "ORIGINAL-EDITABLE-CONTENT"u8.ToArray());

            var first = new MainWindow([]);
            MainWindow? sibling = null;
            try
            {
                // Deliberately NOT marked read-only -- an ordinary, unprotected workbook.
                first.Session.MarkSaved(editablePath);
                first.IsWorkbookReadOnlyForTest.Should().BeFalse("test setup sanity check");

                InvokeNewWindow(first);
                sibling = FindNewSibling(first);

                sibling.IsWorkbookReadOnlyForTest.Should().BeFalse(
                    "an editable source window must not force its sibling read-only");

                var target = sibling.ResolveExistingSaveTargetForTest();
                target.Should().NotBeNull(
                    "an ordinary (non-protected) sibling must still resolve its existing path for a direct save");
                target!.Path.Should().Be(editablePath);
            }
            finally
            {
                if (sibling is not null)
                    CloseWindow(sibling);
                CloseWindow(first);
                TryDeleteDirectory(tempDir);
            }
        }, CancellationToken.None);
    }

    private static void TryDeleteDirectory(string path)
    {
        const int attempts = 50;
        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(100);
            }
        }
    }
}
