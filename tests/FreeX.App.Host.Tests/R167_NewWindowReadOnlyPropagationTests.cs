using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the round-167 finding (shared-readonly-locking F1): View &gt; New Window
/// creates a sibling over the SAME shared Workbook/WorkbookSession (<c>AdoptSharedWorkbook</c>,
/// MainWindow.MultiWindow.cs), but the sibling's own per-window <c>_workbookReadOnlySession</c>
/// (declared in MainWindow.xaml.cs, populated only by <c>ApplyWorkbookReadOnlyOpenPolicy</c> during
/// File &gt; Open -- MainWindow.Backstage.cs) previously defaulted to <c>IsReadOnly=false</c> even
/// when the originating window's session was marked read-only (Read-Only-Recommended declined, or a
/// write-reservation password not supplied/incorrect). Because
/// <c>ResolveExistingSaveTarget()</c> (MainWindow.WorkbookLifecycle.cs) consults ONLY the window's own
/// session, the sibling would resolve the real on-disk path and a direct Ctrl+S there would silently
/// overwrite the protected file -- zero warning, zero re-prompt, zero Save-As fallback.
///
/// <c>AdoptSharedWorkbook</c> now calls <c>ApplyAdoptedReadOnlySession</c>, which copies the
/// originating window's <c>IsReadOnly</c> flag into the new window before it is ever shown.
///
/// These tests exercise the REAL production decision chain a Ctrl+S makes: the actual (reflected)
/// private <c>ResolveExistingSaveTarget()</c> instance method feeds the actual
/// <see cref="WorkbookFileLifecycleCoordinator.SaveResolvedAsync(bool,string?,Func{FileSaveTarget?},Func{FileSaveTarget,Task{bool}},Func{Task{bool}})"/>
/// -- the same static coordinator <c>MainWindow.WorkbookLifecycle.cs</c>'s private
/// <c>SaveResolvedAsync()</c> calls for every Ctrl+S / Save button click. Only the two terminal,
/// UI-bound actions (the direct overwrite write, and the Save-As dialog) are stubbed, since driving
/// a real WPF SaveFileDialog is not something a headless test can safely do -- but the gate between
/// them is 100% the shipping code, reached via a real on-disk file so "the file is unchanged" is a
/// literal byte-for-byte disk assertion, not an inference.
/// </summary>
public sealed class R167_NewWindowReadOnlyPropagationTests
{
    private static MainWindow CreateWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState,
        IEnumerable<IFileAdapter> adapters)
    {
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        return new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
            adapters,
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            documentState,
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static FieldInfo ReadOnlySessionField { get; } =
        typeof(MainWindow).GetField("_workbookReadOnlySession", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(MainWindow), "_workbookReadOnlySession");

    private static PropertyInfo CurrentFilePathProperty { get; } =
        typeof(MainWindow).GetProperty("_currentFilePath", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(nameof(MainWindow), "_currentFilePath");

    private static MethodInfo ResolveExistingSaveTargetMethod { get; } =
        typeof(MainWindow).GetMethod("ResolveExistingSaveTarget", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(nameof(MainWindow), "ResolveExistingSaveTarget");

    /// <summary>Marks <paramref name="window"/>'s OWN read-only session, exactly as the real
    /// File &gt; Open flow's <c>ApplyWorkbookReadOnlyOpenPolicy</c> does when the user declines
    /// Read-Only-Recommended or fails/cancels a write-reservation password prompt (see
    /// R69_ReadOnlyRecommendedPromptTests / WorkbookReadOnlySessionTests for coverage of that prompt
    /// logic itself -- this helper stands in for its terminal effect, the same technique
    /// R83_ReadOnlyWorkbookSaveEnforcementTests already uses).</summary>
    private static void MarkReadOnly(MainWindow window) =>
        ((WorkbookReadOnlySession)ReadOnlySessionField.GetValue(window)!).ApplyPromptDecision(openReadOnly: true);

    private static bool IsReadOnly(MainWindow window) =>
        ((WorkbookReadOnlySession)ReadOnlySessionField.GetValue(window)!).IsReadOnly;

    private static void SetCurrentFilePath(MainWindow window, string path) =>
        CurrentFilePathProperty.SetValue(window, path);

    private static FileSaveTarget? ResolveExistingSaveTarget(MainWindow window) =>
        (FileSaveTarget?)ResolveExistingSaveTargetMethod.Invoke(window, null);

    /// <summary>
    /// Attempts a save through the REAL production gate/coordinator (see class remarks), on a real
    /// on-disk file, and returns the file's bytes afterward so the caller can assert "unchanged".
    /// <paramref name="directOverwriteInvoked"/> is set to true only if the coordinator actually took
    /// the direct-write branch -- i.e. only if <c>ResolveExistingSaveTarget()</c> failed to withhold
    /// the existing path.
    /// </summary>
    private static byte[] AttemptSaveAndReadFileBytes(MainWindow window, string path, out bool directOverwriteInvoked)
    {
        var invoked = false;
        WorkbookFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty: true,
            currentFilePath: path,
            resolveCurrentTarget: () => ResolveExistingSaveTarget(window),
            saveTargetAsync: target =>
            {
                invoked = true;
                // Stand-in for MainWindow.Backstage.cs's real SaveWorkbookToTargetAsync: if this
                // branch is reached, the file really would be overwritten -- prove it by actually
                // overwriting it, so a byte comparison catches the bypass instead of trusting a bool.
                File.WriteAllBytes(path, "OVERWRITTEN-BY-SIBLING-DIRECT-SAVE"u8.ToArray());
                return Task.FromResult(true);
            },
            // Stand-in for SaveWorkbookWithDialogAsync (Save-As dialog): never touches the original
            // protected file -- exactly what Excel-parity Save-As does regardless of what the user
            // ultimately picks.
            saveAsAsync: () => Task.FromResult(true)).GetAwaiter().GetResult();

        directOverwriteInvoked = invoked;
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// The primary regression scenario, matching the finding's USER GESTURE exactly: window A opens
    /// a protected workbook and is marked read-only (step 1); View &gt; New Window creates sibling B
    /// over the same shared document, with A recorded as B's <c>_newWindowSourceHint</c> -- exactly
    /// as the real <c>ViewNewWindowBtn_Click</c> does via <c>SetNewWindowSourceHint</c> before
    /// <c>Show()</c> (step 2); B attempts to save the shared document (step 3). The file on disk must
    /// be byte-for-byte unchanged, and the direct-overwrite branch must never even be reached.
    /// </summary>
    [Fact]
    public void NewWindowSibling_InheritsSourceWindowsReadOnlySession_SoDirectSaveNeverOverwritesTheFile() =>
        StaTestRunner.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var protectedPath = Path.Combine(tempDir, "Protected.fxjson");
            var originalBytes = "ORIGINAL-PROTECTED-CONTENT"u8.ToArray();
            File.WriteAllBytes(protectedPath, originalBytes);

            try
            {
                var workbook = new Workbook("Book1");
                workbook.AddSheet("Sheet1");
                var workbookRef = new WorkbookRef { Current = workbook };
                var registry = new WorkbookWindowRegistry();
                var documentState = new WorkbookDocumentState();
                var adapters = new IFileAdapter[] { new TestFileAdapter(extension: ".fxjson") };

                var primary = CreateWindow(workbookRef, registry, documentState, adapters);
                primary.Show();
                primary.Activate();
                PumpDispatcher();

                // Step 1: the primary window's Read-Only-Recommended / write-reservation-password
                // decision, exactly as ApplyWorkbookReadOnlyOpenPolicy would have set it on open.
                MarkReadOnly(primary);
                IsReadOnly(primary).Should().BeTrue("test setup sanity check");

                // Step 2: View > New Window -- the same wiring ViewNewWindowBtn_Click performs
                // (SetNewWindowSourceHint before Show/Loaded triggers AdoptSharedWorkbook).
                var secondary = CreateWindow(workbookRef, registry, documentState, adapters);
                secondary.SetNewWindowSourceHint(primary);
                secondary.Show();
                secondary.Activate();
                PumpDispatcher();

                try
                {
                    registry.Count.Should().Be(2);
                    secondary.DocumentId.Should().Be(primary.DocumentId, "New Window siblings share the same document");

                    IsReadOnly(secondary).Should().BeTrue(
                        "AdoptSharedWorkbook must propagate the originating window's read-only " +
                        "decision into the sibling's own _workbookReadOnlySession");

                    ResolveExistingSaveTarget(secondary).Should().BeNull(
                        "a read-only sibling must never resolve back to the protected file's own path");

                    // Step 3: attempt to save from the NEW (sibling) window.
                    SetCurrentFilePath(secondary, protectedPath);
                    var bytesAfter = AttemptSaveAndReadFileBytes(secondary, protectedPath, out var directOverwriteInvoked);

                    directOverwriteInvoked.Should().BeFalse(
                        "the sibling's save must fall through to Save-As, never the direct-overwrite branch");
                    bytesAfter.Should().Equal(originalBytes,
                        "the protected file on disk must be byte-for-byte unchanged after the sibling's save attempt");
                }
                finally
                {
                    MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                    MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                    PumpDispatcher();
                }
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });

    /// <summary>
    /// Same scenario, but B is constructed the way an already-shared-document window would be
    /// adopted WITHOUT a recorded <c>_newWindowSourceHint</c> (mirrors the fallback
    /// <c>WithoutASourceHint_FallsBackToFirstRegisteredSiblingsSheet</c> coverage in
    /// R90_NewWindowSourceHintSheetResolutionTests for sheet resolution) -- <c>ApplyAdoptedReadOnlySession</c>
    /// must still find A's read-only state by scanning the registry for any other window over the
    /// same document, not only via the hint.
    /// </summary>
    [Fact]
    public void NewWindowSibling_WithoutSourceHint_StillInheritsReadOnlyViaRegistryFallback() =>
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var adapters = new IFileAdapter[] { new TestFileAdapter(extension: ".fxjson") };

            var primary = CreateWindow(workbookRef, registry, documentState, adapters);
            primary.Show();
            primary.Activate();
            PumpDispatcher();
            MarkReadOnly(primary);

            // No SetNewWindowSourceHint call this time.
            var secondary = CreateWindow(workbookRef, registry, documentState, adapters);
            secondary.Show();
            secondary.Activate();
            PumpDispatcher();

            try
            {
                IsReadOnly(secondary).Should().BeTrue(
                    "with no source hint, ApplyAdoptedReadOnlySession must fall back to scanning the " +
                    "registry for any other window over the same document, mirroring ResolveAdoptedSheetId's fallback");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                PumpDispatcher();
            }
        });

    /// <summary>
    /// No-regression sibling (adjacent case): when the originating document is NOT read-only, the
    /// new window must remain fully editable and its direct save must still go through normally --
    /// the fix must only ever RAISE the sibling's read-only state to match the source, never force
    /// every sibling read-only regardless of the source's actual state.
    /// </summary>
    [Fact]
    public void NewWindowSibling_OfAnEditableDocument_StillResolvesDirectSaveNormally() =>
        StaTestRunner.Run(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var editablePath = Path.Combine(tempDir, "Editable.fxjson");
            File.WriteAllBytes(editablePath, "ORIGINAL-EDITABLE-CONTENT"u8.ToArray());

            try
            {
                var workbook = new Workbook("Book1");
                workbook.AddSheet("Sheet1");
                var workbookRef = new WorkbookRef { Current = workbook };
                var registry = new WorkbookWindowRegistry();
                var documentState = new WorkbookDocumentState();
                var adapters = new IFileAdapter[] { new TestFileAdapter(extension: ".fxjson") };

                var primary = CreateWindow(workbookRef, registry, documentState, adapters);
                primary.Show();
                primary.Activate();
                PumpDispatcher();
                // Deliberately NOT marked read-only -- an ordinary, unprotected workbook.

                var secondary = CreateWindow(workbookRef, registry, documentState, adapters);
                secondary.SetNewWindowSourceHint(primary);
                secondary.Show();
                secondary.Activate();
                PumpDispatcher();

                try
                {
                    IsReadOnly(secondary).Should().BeFalse(
                        "an editable source window must not force its sibling read-only");

                    SetCurrentFilePath(secondary, editablePath);
                    var target = ResolveExistingSaveTarget(secondary);
                    target.Should().NotBeNull(
                        "an ordinary (non-protected) sibling must still resolve its existing path for a direct save");
                    target!.Path.Should().Be(editablePath);
                }
                finally
                {
                    MainWindowTestCleanup.CloseWithoutSavePrompt(secondary);
                    MainWindowTestCleanup.CloseWithoutSavePrompt(primary);
                    PumpDispatcher();
                }
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });

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
