using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPicturePickerWorkflowTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);
    private static readonly byte[] Image = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");

    [Fact]
    public Task GroupedPictures_UsePrimaryAnchorOnEveryGroupedSheetWithOneUndoEntry() => Run(async window =>
    {
        var first = window.Session.ActiveSheet;
        var second = window.Session.Workbook.AddSheet("Grouped detail");
        window.Session.SelectSheet(second.Id);
        window.Session.SelectAllVisibleSheets();
        SelectAreaWithDifferentActiveCell(window);
        using var file = new PickedFile("grouped.png", Image);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice);
        window.RibbonCommandRegistryForTest!.TryGet(new RibbonCommandId("Pictures"), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
        await Drain();
        foreach (var sheet in new[] { first, second })
        {
            var picture = sheet.Pictures.Should().ContainSingle().Subject;
            picture.Anchor.Should().Be(At(sheet, 1, 1));
            picture.ImageBytes.Should().Equal(Image);
        }
        window.Session.ActiveSheet.Should().BeSameAs(second);
        window.Session.GetUndoHistory(10).Should().ContainSingle();
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        first.Pictures.Should().BeEmpty();
        second.Pictures.Should().BeEmpty();
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        first.Pictures.Should().ContainSingle();
        second.Pictures.Should().ContainSingle();
        file.DisposeCount.Should().Be(1);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PicturePicker_RetainsPrimaryAnchorAcrossPickerAndReadAwaits(bool delayRead) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        SelectAreaWithDifferentActiveCell(window);
        var picker = new TaskCompletionSource<MainWindow.InsertObjectPickedFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new PickedFile("captured.png", Image, delayRead ? read.Task : null);
        window.InsertObjectFilePickerForTest = _ => delayRead ? Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice) : picker.Task;
        var insertion = window.InsertPictureFromFileForTestAsync();
        try
        {
            if (delayRead) await file.ReadStarted.Task;
            window.Session.SelectCell(At(sheet, 3, 3));
        }
        finally
        {
            picker.TrySetResult(file.Choice);
            if (delayRead) read.TrySetResult(new MemoryStream(Image, writable: false));
            await insertion;
        }
        sheet.Pictures.Should().ContainSingle().Which.Anchor.Should().Be(At(sheet, 1, 1));
        window.Session.GetUndoHistory(10).Should().ContainSingle();
        file.DisposeCount.Should().Be(1);
    });

    [Theory]
    [InlineData("owner", false)]
    [InlineData("session", false)]
    [InlineData("sheet", false)]
    [InlineData("group", false)]
    [InlineData("removed", false)]
    [InlineData("grouped-sheet-removed", false)]
    [InlineData("_isOpening", false)]
    [InlineData("_isSaving", false)]
    [InlineData("owner", true)]
    [InlineData("session", true)]
    [InlineData("sheet", true)]
    [InlineData("group", true)]
    [InlineData("removed", true)]
    [InlineData("grouped-sheet-removed", true)]
    [InlineData("_isOpening", true)]
    [InlineData("_isSaving", true)]
    public Task PicturePicker_RejectsInvalidatedTargetsBeforeMutation(string invalidation, bool delayRead) => Run(async window =>
    {
        var originalSession = window.Session;
        var first = originalSession.ActiveSheet;
        var second = originalSession.Workbook.AddSheet("Other detail");
        if (invalidation == "grouped-sheet-removed") originalSession.SelectAllVisibleSheets();
        SelectAreaWithDifferentActiveCell(window);
        var picker = new TaskCompletionSource<MainWindow.InsertObjectPickedFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new PickedFile("stale.png", Image, delayRead ? read.Task : null);
        window.InsertObjectFilePickerForTest = _ => delayRead ? Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice) : picker.Task;
        var insertion = window.InsertPictureFromFileForTestAsync();
        FieldInfo? busy = null;
        try
        {
            if (delayRead) await file.ReadStarted.Task;
            switch (invalidation)
            {
                case "owner": window.AllowCloseWithoutDirtyPromptForParityCapture(); window.Close(); break;
                case "session": window.ReplaceSession(new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: false)); break;
                case "sheet": originalSession.SelectSheet(second.Id); break;
                case "group": originalSession.SelectAllVisibleSheets(); break;
                case "removed": originalSession.Workbook.RemoveSheet(first.Id); originalSession.SelectSheet(second.Id); break;
                case "grouped-sheet-removed": originalSession.Workbook.RemoveSheet(second.Id); break;
                default:
                    busy = typeof(MainWindow).GetField(invalidation, BindingFlags.Instance | BindingFlags.NonPublic)!;
                    busy.SetValue(window, true);
                    break;
            }
        }
        finally
        {
            picker.TrySetResult(file.Choice);
            if (delayRead) read.TrySetResult(new MemoryStream(Image, writable: false));
            try { await insertion; }
            finally { busy?.SetValue(window, false); }
        }
        first.Pictures.Should().BeEmpty();
        second.Pictures.Should().BeEmpty();
        foreach (var sheet in window.Session.Workbook.Sheets) sheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
        file.DisposeCount.Should().Be(1);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PicturePicker_DeclinesBeforeOpeningWhileBusy(bool saving) => Run(async window =>
    {
        var calls = 0;
        using var file = new PickedFile("busy.png", Image);
        window.InsertObjectFilePickerForTest = _ => { calls++; return Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice); };
        var field = typeof(MainWindow).GetField(saving ? "_isSaving" : "_isOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, true);
        try { await window.InsertPictureFromFileForTestAsync(); }
        finally { field.SetValue(window, false); }
        calls.Should().Be(0);
        window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task PicturePicker_RechecksObjectProtectionAndRollsBackGroupedInsertion(bool allowObjects, bool protectSibling) => Run(async window =>
    {
        var first = window.Session.ActiveSheet;
        var second = window.Session.Workbook.AddSheet("Protected detail");
        window.Session.SelectAllVisibleSheets();
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new PickedFile("protected.png", Image, read.Task);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice);
        var insertion = window.InsertPictureFromFileForTestAsync();
        try
        {
            await file.ReadStarted.Task;
            var protectedSheet = protectSibling ? second : first;
            protectedSheet.IsProtected = true;
            if (allowObjects) protectedSheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
            else protectedSheet.ProtectionPermissions.Remove(SheetProtectionPermission.EditObjects);
        }
        finally
        {
            read.TrySetResult(new MemoryStream(Image, writable: false));
            await insertion;
        }
        first.Pictures.Should().HaveCount(allowObjects ? 1 : 0);
        second.Pictures.Should().HaveCount(allowObjects ? 1 : 0);
        window.Session.CanUndo.Should().Be(allowObjects);
        if (allowObjects)
        {
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            first.Pictures.Should().BeEmpty();
            second.Pictures.Should().BeEmpty();
        }
    });

    [Theory]
    [InlineData("cancel")]
    [InlineData("unsupported")]
    [InlineData("empty")]
    [InlineData("read-failure")]
    public Task PicturePicker_CancelOrInvalidInputCreatesNoHistoryAndReleasesPickedFile(string outcome) => Run(async window =>
    {
        var read = outcome == "read-failure" ? Task.FromException<Stream>(new IOException("test read failure")) : null;
        using var file = new PickedFile(outcome == "unsupported" ? "image.heic" : "image.png", outcome == "empty" ? [] : Image, read);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(outcome == "cancel" ? null : file.Choice);
        await window.InsertPictureFromFileForTestAsync();
        window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
        file.DisposeCount.Should().Be(outcome == "cancel" ? 0 : 1);
    });

    [Fact]
    public Task InsertObject_CanceledRebrowseRetainsAcceptedFileAndStillInsertsIt() => Run(async window =>
    {
        using var file = new PickedFile("accepted.png", Image);
        var choices = new Queue<MainWindow.InsertObjectPickedFile?>([file.Choice, null]);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult(choices.Dequeue());
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Find<TextBlock>(dialog, "WfInsertObjectChosenPath").Text.Should().Be(file.Name);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Find<TextBlock>(dialog, "WfInsertObjectChosenPath").Text.Should().Be(file.Name);
            Find<Button>(dialog, "WfInsertObjectInsertButton").IsEnabled.Should().BeTrue();
            Click(Find<Button>(dialog, "WfInsertObjectInsertButton"));
            await Drain();
            window.Session.ActiveSheet.Pictures.Should().ContainSingle().Which.ImageBytes.Should().Equal(Image);
            workflow.IsCompleted.Should().BeTrue();
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await workflow;
        }
        file.DisposeCount.Should().Be(1);
    });

    [Fact]
    public Task InsertObject_CanceledInitialBrowseKeepsInsertDisabled() => Run(async window =>
    {
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(null);
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Find<Button>(dialog, "WfInsertObjectInsertButton").IsEnabled.Should().BeFalse();
            window.Session.ActiveSheet.Pictures.Should().BeEmpty();
            window.Session.CanUndo.Should().BeFalse();
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await workflow;
        }
    });

    [Fact]
    public Task InsertObject_AcceptedReplacementDisposesPreviousChoiceAndInsertsReplacement() => Run(async window =>
    {
        var replacement = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var first = new PickedFile("first.png", Image);
        using var second = new PickedFile("replacement.png", replacement);
        var choices = new Queue<MainWindow.InsertObjectPickedFile?>([first.Choice, second.Choice]);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult(choices.Dequeue());
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            first.DisposeCount.Should().Be(1);
            second.DisposeCount.Should().Be(0);
            Find<TextBlock>(dialog, "WfInsertObjectChosenPath").Text.Should().Be(second.Name);
            Click(Find<Button>(dialog, "WfInsertObjectInsertButton"));
            await Drain();
            window.Session.ActiveSheet.Pictures.Should().ContainSingle().Which.ImageBytes.Should().Equal(replacement);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await workflow;
        }
        second.DisposeCount.Should().Be(1);
    });

    [Fact]
    public Task InsertObject_CancelingDialogReleasesAcceptedChoiceWithoutMutation() => Run(async window =>
    {
        using var file = new PickedFile("canceled.png", Image);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice);
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Click(Find<Button>(dialog, "WfInsertObjectCancelButton"));
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await workflow;
        }
        file.DisposeCount.Should().Be(1);
        window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
    });

    [Fact]
    public Task InsertObject_CancelDuringReadJoinsReadBeforeDisposalAndDoesNotInsert() => Run(async window =>
    {
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new PickedFile("in-flight.png", Image, read.Task);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice);
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            Click(Find<Button>(dialog, "WfInsertObjectBrowseButton"));
            await Drain();
            Click(Find<Button>(dialog, "WfInsertObjectInsertButton"));
            await file.ReadStarted.Task;
            Click(Find<Button>(dialog, "WfInsertObjectCancelButton"));
            await Drain();
            workflow.IsCompleted.Should().BeFalse("the picked item must outlive its pending read");
            file.DisposeCount.Should().Be(0);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            read.TrySetResult(new MemoryStream(Image, writable: false));
            await workflow;
        }
        file.DisposeCount.Should().Be(1);
        window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
    });

    [Fact]
    public Task PicturePicker_SupportsNeverShownOwnerWithoutMistakingItForClosed() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        SelectAreaWithDifferentActiveCell(window);
        using var file = new PickedFile("never-shown.png", Image);
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice);
        await window.InsertPictureFromFileForTestAsync();
        sheet.Pictures.Should().ContainSingle().Which.Anchor.Should().Be(At(sheet, 1, 1));
        file.DisposeCount.Should().Be(1);
    }, showWindow: false);

    [Fact]
    public Task InsertObject_QueuedBrowseWhileInsertingCannotReplaceOrDisposeChosenItem() => Run(async window =>
    {
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chosen = new PickedFile("chosen.png", Image, read.Task);
        using var replacement = new PickedFile("queued.png", Image);
        var calls = 0;
        window.InsertObjectFilePickerForTest = _ => Task.FromResult<MainWindow.InsertObjectPickedFile?>(calls++ == 0 ? chosen.Choice : replacement.Choice);
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            var browse = Find<Button>(dialog, "WfInsertObjectBrowseButton");
            Click(browse);
            await Drain();
            Click(Find<Button>(dialog, "WfInsertObjectInsertButton"));
            await chosen.ReadStarted.Task;
            browse.IsEnabled.Should().BeFalse();
            Click(browse); // Simulate an already queued click despite the disabled control.
            await Drain();
            calls.Should().Be(1);
            chosen.DisposeCount.Should().Be(0);
            read.TrySetResult(new MemoryStream(Image, writable: false));
            await workflow;
            window.Session.ActiveSheet.Pictures.Should().ContainSingle().Which.ImageBytes.Should().Equal(Image);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            read.TrySetResult(new MemoryStream(Image, writable: false));
            await workflow;
        }
        chosen.DisposeCount.Should().Be(1);
        replacement.DisposeCount.Should().Be(0);
    });

    [Fact]
    public Task InsertObject_QueuedInsertWhileBrowsingWaitsForAcceptedReplacement() => Run(async window =>
    {
        using var first = new PickedFile("first.png", Image);
        using var replacement = new PickedFile("next.png", Image);
        var pendingBrowse = new TaskCompletionSource<MainWindow.InsertObjectPickedFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        window.InsertObjectFilePickerForTest = _ => calls++ == 0 ? Task.FromResult<MainWindow.InsertObjectPickedFile?>(first.Choice) : pendingBrowse.Task;
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            var browse = Find<Button>(dialog, "WfInsertObjectBrowseButton");
            var insert = Find<Button>(dialog, "WfInsertObjectInsertButton");
            Click(browse);
            await Drain();
            Click(browse);
            insert.IsEnabled.Should().BeFalse();
            Click(insert); // A queued event must not start reading the old choice.
            first.ReadStarted.Task.IsCompleted.Should().BeFalse();
            first.DisposeCount.Should().Be(0);
            pendingBrowse.TrySetResult(replacement.Choice);
            await Drain();
            first.DisposeCount.Should().Be(1);
            insert.IsEnabled.Should().BeTrue();
            Click(insert);
            await workflow;
            window.Session.ActiveSheet.Pictures.Should().ContainSingle();
            replacement.ReadStarted.Task.IsCompleted.Should().BeTrue();
        }
        finally
        {
            pendingBrowse.TrySetResult(replacement.Choice);
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await workflow;
        }
        replacement.DisposeCount.Should().Be(1);
    });

    [Fact]
    public Task InsertObject_FailedBrowseStillJoinsLaterInsertionBeforeDisposingChoice() => Run(async window =>
    {
        var read = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var file = new PickedFile("retained-after-error.png", Image, read.Task);
        var calls = 0;
        window.InsertObjectFilePickerForTest = _ => calls++ == 0
            ? Task.FromResult<MainWindow.InsertObjectPickedFile?>(file.Choice)
            : Task.FromException<MainWindow.InsertObjectPickedFile?>(new IOException("expected picker failure"));
        var workflow = window.ShowInsertObjectForTestAsync();
        try
        {
            await Drain();
            var dialog = ObjectDialog(window);
            var browse = Find<Button>(dialog, "WfInsertObjectBrowseButton");
            Click(browse);
            await Drain();
            Click(browse);
            await Drain();
            Click(Find<Button>(dialog, "WfInsertObjectInsertButton"));
            await file.ReadStarted.Task;
            Click(Find<Button>(dialog, "WfInsertObjectCancelButton"));
            await Drain();
            workflow.IsCompleted.Should().BeFalse("a prior browse failure must not skip joining the current read");
            file.DisposeCount.Should().Be(0);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            read.TrySetResult(new MemoryStream(Image, writable: false));
            Func<Task> observe = () => workflow;
            await observe.Should().ThrowAsync<IOException>().WithMessage("expected picker failure");
        }
        file.DisposeCount.Should().Be(1);
        window.Session.ActiveSheet.Pictures.Should().BeEmpty();
        window.Session.CanUndo.Should().BeFalse();
    });

    private sealed class PickedFile : IDisposable
    {
        private readonly byte[] _bytes;
        private readonly Task<Stream>? _openRead;

        internal PickedFile(string name, byte[] bytes, Task<Stream>? openRead = null)
        {
            Name = name;
            _bytes = bytes;
            _openRead = openRead;
            Choice = new MainWindow.InsertObjectPickedFile(name, "/picker-fixtures/" + name, OpenReadAsync, this);
        }

        internal string Name { get; }
        internal MainWindow.InsertObjectPickedFile Choice { get; }
        internal int DisposeCount { get; private set; }
        internal TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<Stream> OpenReadAsync()
        {
            ReadStarted.TrySetResult();
            return _openRead ?? Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }
        public void Dispose() { if (DisposeCount == 0) DisposeCount++; }
    }

    private static void SelectAreaWithDifferentActiveCell(MainWindow window)
    {
        var sheet = window.Session.ActiveSheet;
        var primary = new GridRange(At(sheet, 1, 1), At(sheet, 5, 5));
        var extra = new GridRange(At(sheet, 8, 8), At(sheet, 9, 9));
        window.Session.SelectRanges(primary, [primary, extra], At(sheet, 2, 2));
    }
    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
    private static Window ObjectDialog(MainWindow owner) => owner.OwnedWindows.Single(window => AutomationProperties.GetAutomationId(window) == "WfInsertObjectDialog");
    private static T Find<T>(Control root, string id) where T : Control => root.GetVisualDescendants().Prepend(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Drain()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(10);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
    private static Task Run(Func<MainWindow, Task> test, bool showWindow = true) => Headless.Dispatch(async () =>
    {
        var window = new MainWindow([], null!, new FreeXOptionsRuntimeSession(load: static () => new AppOptions(), save: static _ => true));
        window.ReplaceSession(new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: false));
        try
        {
            if (showWindow)
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
            }
            await test(window);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);
}
