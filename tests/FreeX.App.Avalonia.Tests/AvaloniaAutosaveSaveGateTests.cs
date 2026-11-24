using System.Reflection;
using System.Threading;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;

using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaAutosaveSaveGateTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PeriodicTick_DuringOwnOrSiblingSave_DoesNotReconcileOrWrite_ThenResumes(bool savingViewTicks) =>
        Headless.Dispatch(() =>
        {
            using var temp = new TestTemporaryDirectory("freex-avalonia-autosave-save-gate-");
            using var applicationData = new IsolatedApplicationData(temp.Path);
            var first = CreateWindow();
            MainWindow? second = null;
            AvaloniaAutosaveCoordinator? coordinator = null;
            try
            {
                second = first.CreateSharedViewForTest();
                Show(first);
                Show(second);
                first.Session.CommitCellText("unsaved content").Success.Should().BeTrue();
                first.Session.SetFreezePanes(5, 3).Success.Should().BeTrue();
                first.Session.SetViewportOrigin(20, 10).Should().BeTrue();
                second.Session.SetViewportOrigin(40, 30).Should().BeTrue();

                first.Session.Workbook.Should().BeSameAs(second.Session.Workbook);
                first.Session.GetEffectiveFrozenRows().Should().Be(5);
                first.Session.GetEffectiveFrozenCols().Should().Be(3);
                second.Session.GetEffectiveFrozenRows().Should().Be(0);
                second.Session.GetEffectiveFrozenCols().Should().Be(0);
                first.Session.IsDirty.Should().BeTrue();

                var ticking = savingViewTicks ? first : second;
                var other = savingViewTicks ? second : first;
                var store = new AutosaveSnapshotStore(temp.Path);
                coordinator = new AvaloniaAutosaveCoordinator(ticking, store);
                ticking.AttachAutosaveCoordinator(coordinator);

                // Make reconciliation observable independently of disk writes: this view's local
                // state differs from the shared sheet state that the other view projected.
                other.Session.ReconcileViewStateForSave();
                var sharedSheet = first.Session.ActiveSheet;
                var sharedBeforeTick = PersistedView(sharedSheet);
                var existingFiles = Directory.GetFiles(temp.Path).Order().ToArray();
                var history = (ticking.Session.IsDirty, ticking.Session.CanUndo, ticking.Session.CanRedo);
                var selection = ticking.Session.SelectedRange;
                store.EnumerateCandidates().Should().BeEmpty();

                SetSaveGate(first, true);
                first.ShouldSkipAutosaveTickForSave.Should().BeTrue();
                second.ShouldSkipAutosaveTickForSave.Should().BeTrue(
                    "the actual window registry must broadcast the saving view's gate");

                coordinator.OnTimerTick();

                PersistedView(sharedSheet).Should().Be(sharedBeforeTick,
                    "a skipped tick must not project this view onto a workbook being serialized");
                Directory.GetFiles(temp.Path).Order().Should().Equal(existingFiles,
                    "only the existing ownership lock may remain while the save gate is held");
                store.EnumerateCandidates().Should().BeEmpty();
                (ticking.Session.IsDirty, ticking.Session.CanUndo, ticking.Session.CanRedo).Should().Be(history);
                ticking.Session.SelectedRange.Should().Be(selection);

                SetSaveGate(first, false);
                first.ShouldSkipAutosaveTickForSave.Should().BeFalse();
                second.ShouldSkipAutosaveTickForSave.Should().BeFalse();
                coordinator.OnTimerTick();

                var candidate = store.EnumerateCandidates().Should().ContainSingle().Subject;
                using var stream = File.OpenRead(candidate.SnapshotPath);
                var restored = new NativeJsonAdapter().Load(stream);
                var restoredSheet = restored.GetSheet(sharedSheet.Name)!;
                (uint Rows, uint Columns, uint? TopRow, uint? LeftColumn) expected =
                    savingViewTicks ? (5u, 3u, 20u, 10u) : (0u, 0u, 40u, 30u);
                PersistedView(restoredSheet).Should().Be(expected,
                    "the first resumed tick must persist its own view, not the sibling's view");
                restoredSheet.GetValue(new CellAddress(restoredSheet.Id, 1, 1))
                    .Should().Be(new TextValue("unsaved content"));
                first.Session.ViewportOrigin.Should().Be((20u, 10u));
                second.Session.ViewportOrigin.Should().Be((40u, 30u));
                (ticking.Session.IsDirty, ticking.Session.CanUndo, ticking.Session.CanRedo).Should().Be(history);
                ticking.Session.SelectedRange.Should().Be(selection);
            }
            finally
            {
                SetSaveGate(first, false);
                coordinator?.OnWindowClosed();
                if (second is not null) Close(second);
                Close(first);
            }
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task PeriodicTick_WhileAnotherDocumentSaves_StillWritesItsOwnSnapshot() => Headless.Dispatch(() =>
    {
        using var temp = new TestTemporaryDirectory("freex-avalonia-autosave-other-document-");
        using var applicationData = new IsolatedApplicationData(temp.Path);
        var saving = CreateWindow();
        var unrelated = CreateWindow();
        AvaloniaAutosaveCoordinator? coordinator = null;
        try
        {
            Show(saving);
            Show(unrelated);
            saving.DocumentId.Should().NotBe(unrelated.DocumentId);
            unrelated.Session.CommitCellText("unrelated unsaved content").Success.Should().BeTrue();
            var store = new AutosaveSnapshotStore(temp.Path);
            coordinator = new AvaloniaAutosaveCoordinator(unrelated, store);
            unrelated.AttachAutosaveCoordinator(coordinator);

            SetSaveGate(saving, true);
            saving.ShouldSkipAutosaveTickForSave.Should().BeTrue();
            unrelated.ShouldSkipAutosaveTickForSave.Should().BeFalse();
            coordinator.OnTimerTick();

            var candidate = store.EnumerateCandidates().Should().ContainSingle().Subject;
            using var stream = File.OpenRead(candidate.SnapshotPath);
            var restored = new NativeJsonAdapter().Load(stream);
            var sheet = restored.GetSheet(unrelated.Session.ActiveSheet.Name)!;
            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(new TextValue("unrelated unsaved content"));
        }
        finally
        {
            SetSaveGate(saving, false);
            coordinator?.OnWindowClosed();
            Close(unrelated);
            Close(saving);
        }
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task SaveAs_RetiresHiddenSiblingsUntitledSnapshot() => Headless.Dispatch(async () =>
    {
        using var temp = new TestTemporaryDirectory("freex-avalonia-autosave-hidden-save-");
        using var applicationData = new IsolatedApplicationData(temp.Path);
        var first = CreateWindow();
        MainWindow? second = null;
        AvaloniaAutosaveCoordinator? firstCoordinator = null;
        AvaloniaAutosaveCoordinator? secondCoordinator = null;
        try
        {
            second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);
            first.Session.CommitCellText("saved content").Success.Should().BeTrue();
            first.Session.CurrentFilePath.Should().BeNull();
            second.Session.CurrentFilePath.Should().BeNull();

            var firstStore = new AutosaveSnapshotStore(Path.Combine(temp.Path, "first-recovery"));
            var secondStore = new AutosaveSnapshotStore(Path.Combine(temp.Path, "second-recovery"));
            firstCoordinator = new AvaloniaAutosaveCoordinator(first, firstStore);
            secondCoordinator = new AvaloniaAutosaveCoordinator(second, secondStore);
            first.AttachAutosaveCoordinator(firstCoordinator);
            second.AttachAutosaveCoordinator(secondCoordinator);
            firstCoordinator.OnTimerTick();
            secondCoordinator.OnTimerTick();
            firstStore.EnumerateCandidates().Should().ContainSingle().Subject.Sidecar.OriginalFilePath.Should().BeNull();
            var hiddenSnapshot = secondStore.EnumerateCandidates().Should().ContainSingle().Subject;
            hiddenSnapshot.Sidecar.OriginalFilePath.Should().BeNull(
                "an untitled snapshot cannot be rejected later by a newer-original-file check");

            MainWindow.WindowRegistryForTest.Hide(second).Should().BeTrue();
            second.IsVisible.Should().BeFalse();
            MainWindow.WindowRegistryForTest.HiddenWindows.Should().Contain(second);
            var savedPath = Path.Combine(temp.Path, "saved.fxl");

            await SaveAs(first, savedPath);

            first.Session.IsDirty.Should().BeFalse();
            second.Session.IsDirty.Should().BeFalse();
            second.IsVisible.Should().BeFalse("snapshot cleanup must not reveal a hidden view");
            firstStore.EnumerateCandidates().Should().BeEmpty();
            secondStore.EnumerateCandidates().Should().BeEmpty(
                "a hidden sibling must not retain pre-save content for a later crash recovery offer");
            File.Exists(hiddenSnapshot.SnapshotPath).Should().BeFalse();
            File.Exists(hiddenSnapshot.SidecarPath).Should().BeFalse();
            ReadFirstCell(savedPath, first.Session.ActiveSheet.Name).Should().Be(new TextValue("saved content"));
        }
        finally
        {
            firstCoordinator?.OnWindowClosed();
            secondCoordinator?.OnWindowClosed();
            if (second is not null) Close(second);
            Close(first);
        }
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task UndoToSavedState_RetiresHiddenSiblingsDirtySnapshot() => Headless.Dispatch(async () =>
    {
        using var temp = new TestTemporaryDirectory("freex-avalonia-autosave-hidden-undo-");
        using var applicationData = new IsolatedApplicationData(temp.Path);
        var first = CreateWindow();
        MainWindow? second = null;
        AvaloniaAutosaveCoordinator? coordinator = null;
        try
        {
            second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);
            first.Session.CommitCellText("saved content").Success.Should().BeTrue();
            var savedPath = Path.Combine(temp.Path, "saved.fxl");
            await SaveAs(first, savedPath);
            first.Session.IsDirty.Should().BeFalse();

            var store = new AutosaveSnapshotStore(Path.Combine(temp.Path, "hidden-recovery"));
            coordinator = new AvaloniaAutosaveCoordinator(second, store);
            second.AttachAutosaveCoordinator(coordinator);
            first.Session.CommitCellText("unsaved edit").Success.Should().BeTrue();
            second.Session.IsDirty.Should().BeTrue();
            coordinator.OnTimerTick();
            var hiddenSnapshot = store.EnumerateCandidates().Should().ContainSingle().Subject;
            ReadFirstCell(hiddenSnapshot.SnapshotPath, first.Session.ActiveSheet.Name)
                .Should().Be(new TextValue("unsaved edit"));
            MainWindow.WindowRegistryForTest.Hide(second).Should().BeTrue();

            var undo = new KeyEventArgs { Key = Key.Z, KeyModifiers = KeyModifiers.Control };
            await first.RaiseKeyDownForTest(undo);

            undo.Handled.Should().BeTrue();
            first.Session.IsDirty.Should().BeFalse();
            second.Session.IsDirty.Should().BeFalse();
            first.Session.ActiveSheet.GetValue(first.Session.ActiveCell).Should().Be(new TextValue("saved content"));
            second.IsVisible.Should().BeFalse();
            store.EnumerateCandidates().Should().BeEmpty(
                "undo returning the shared document to its save point must retire hidden-view snapshots");
            File.Exists(hiddenSnapshot.SnapshotPath).Should().BeFalse();
            File.Exists(hiddenSnapshot.SidecarPath).Should().BeFalse();
            ReadFirstCell(savedPath, first.Session.ActiveSheet.Name).Should().Be(new TextValue("saved content"));
        }
        finally
        {
            coordinator?.OnWindowClosed();
            if (second is not null) Close(second);
            Close(first);
        }
        return true;
    }, CancellationToken.None);

    private static async Task SaveAs(MainWindow window, string path)
    {
        var pickerCalls = 0;
        window.WorkbookSaveAsPickerOverrideForTest = _ =>
        {
            pickerCalls++;
            return Task.FromResult<MainWindow.WorkbookSaveAsPickerSelection?>(
                window.CreateTransientWorkbookSaveAsSelection(path));
        };
        var save = new KeyEventArgs { Key = Key.S, KeyModifiers = KeyModifiers.Control };
        await window.RaiseKeyDownForTest(save);
        save.Handled.Should().BeTrue();
        pickerCalls.Should().Be(1);
        window.Session.CurrentFilePath.Should().Be(path);
        File.Exists(path).Should().BeTrue();
    }

    private static ScalarValue ReadFirstCell(string path, string sheetName)
    {
        using var stream = File.OpenRead(path);
        var workbook = new NativeJsonAdapter().Load(stream);
        var sheet = workbook.GetSheet(sheetName)!;
        return sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
    }

    private sealed class IsolatedApplicationData : IDisposable
    {
        private readonly AppProductIdentity _previous = AppProduct.Current;

        public IsolatedApplicationData(string root) => AppProduct.Current = new AppProductIdentity(
            Path.Combine(root, "application-data"), _previous.DiagnosticsEnvironmentVariable, _previous.ProductName);

        public void Dispose() => AppProduct.Current = _previous;
    }

    private static MainWindow CreateWindow()
    {
        var options = new AppOptions();
        return new MainWindow([], null!, new FreeXOptionsRuntimeSession(
            load: () => options.Clone(), save: static _ => true));
    }

    private static void Show(MainWindow window)
    {
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
    }

    private static void SetSaveGate(MainWindow window, bool saving) => typeof(MainWindow)
        .GetMethod("SetSavingAndTellSiblings", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [saving]);

    private static (uint Rows, uint Columns, uint? TopRow, uint? LeftColumn) PersistedView(Sheet sheet) =>
        (sheet.FrozenRows, sheet.FrozenCols, sheet.ViewTopRow, sheet.ViewLeftCol);

    private static void Close(MainWindow window)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }
}
