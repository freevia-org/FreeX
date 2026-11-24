using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaReviewContextTargetTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("NewNote", false)]
    [InlineData("EditNote", false)]
    [InlineData("NewComment", false)]
    [InlineData("EditComment", false)]
    [InlineData("NewNote", true)]
    [InlineData("EditNote", true)]
    [InlineData("NewComment", true)]
    [InlineData("EditComment", true)]
    public Task ContextEditor_PrefillsAndCommitsClickedCellEvenAfterSelectionChanges(string action, bool moveSelection) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        var threaded = action.EndsWith("Comment", StringComparison.Ordinal);
        var existing = action.StartsWith("Edit", StringComparison.Ordinal);
        Seed(sheet, anchor, "Anchor");
        if (existing) Seed(sheet, clicked, "Clicked");
        var selection = SelectAreas(window, anchor);

        await ClickMenu(window, action, clicked);
        var editor = Editor(window, threaded);
        var box = Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox");
        box.Text.Should().Be(existing ? "Clicked" : string.Empty);
        box.Text = "Changed clicked";
        if (moveSelection)
        {
            window.Session.SelectCell(At(sheet, 3, 3));
            selection = Capture(window);
        }
        if (threaded)
            Find<Button>(editor, "GridCommentInlineSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

        Text(sheet, clicked, threaded).Should().Be("Changed clicked");
        Text(sheet, anchor, threaded).Should().Be("Anchor");
        Text(sheet, At(sheet, 3, 3), threaded).Should().BeNull();
        if (threaded && !existing) sheet.ThreadedComments[clicked].Author.Should().Be("Configured Reviewer");
        AssertSelection(window, selection);
        window.Session.GetUndoHistory(10).Should().ContainSingle();
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        Text(sheet, clicked, threaded).Should().Be(existing ? "Clicked" : null);
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        Text(sheet, clicked, threaded).Should().Be("Changed clicked");
    });

    [Theory]
    [InlineData("NewNote")]
    [InlineData("EditNote")]
    [InlineData("NewComment")]
    [InlineData("EditComment")]
    public Task ContextEditor_CancelLeavesBothCellsAndSelectionUnchanged(string action) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        Seed(sheet, anchor, "Anchor");
        Seed(sheet, clicked, "Clicked");
        var selection = SelectAreas(window, anchor);
        await ClickMenu(window, action, clicked);
        var threaded = action.EndsWith("Comment", StringComparison.Ordinal);
        var editor = Editor(window, threaded);
        Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox").Text = "Cancelled";
        Find<Button>(editor, "GridCommentInlineCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Text(sheet, clicked, threaded).Should().Be("Clicked");
        Text(sheet, anchor, threaded).Should().Be("Anchor");
        window.Session.CanUndo.Should().BeFalse();
        AssertSelection(window, selection);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ContextDelete_RemovesOnlyClickedAnnotationAndRestoresMetadataOnUndo(bool threaded) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        Seed(sheet, anchor, "Anchor");
        Seed(sheet, clicked, "Clicked");
        sheet.CommentAuthors[clicked] = "Original Reviewer";
        sheet.ShownComments.Add(clicked);
        var selection = SelectAreas(window, anchor);
        await ClickMenu(window, threaded ? "DeleteComment" : "DeleteNote", clicked);
        Text(sheet, clicked, threaded).Should().BeNull();
        Text(sheet, clicked, !threaded).Should().Be("Clicked");
        Text(sheet, anchor, threaded).Should().Be("Anchor");
        sheet.ShownComments.Contains(clicked).Should().Be(threaded);
        sheet.CommentAuthors.ContainsKey(clicked).Should().Be(threaded);
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        Text(sheet, clicked, threaded).Should().Be("Clicked");
        sheet.CommentAuthors[clicked].Should().Be("Original Reviewer");
        sheet.ShownComments.Should().Contain(clicked);
        sheet.ThreadedComments[clicked].Replies.Should().ContainSingle(reply => reply.Text == "Clicked reply");
        window.Session.RedoLastEdit().Success.Should().BeTrue();
        Text(sheet, clicked, threaded).Should().BeNull();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PassiveCard_ActsOnActiveB2RatherThanPrimaryRangeAnchor(bool delete) => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        Seed(sheet, anchor, "Anchor");
        Seed(sheet, clicked, "Clicked");
        var selection = SelectAreas(window, clicked);
        var editor = Editor(window, true);
        Find<TextBox>(editor, "GridThreadedCommentRootBox").Text.Should().Be("Clicked");
        Find<Button>(editor, delete ? "GridThreadedCommentDeleteThreadButton" : "GridThreadedCommentToggleResolvedButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        sheet.ThreadedComments[anchor].IsResolved.Should().BeFalse();
        sheet.ThreadedComments[anchor].Text.Should().Be("Anchor");
        if (delete) sheet.ThreadedComments.Should().NotContainKey(clicked);
        else sheet.ThreadedComments[clicked].IsResolved.Should().BeTrue();
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[clicked].Text.Should().Be("Clicked");
        sheet.ThreadedComments[clicked].IsResolved.Should().BeFalse();
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IndicatorCard_ReplyControlsKeepCapturedTarget(bool delete) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        Seed(sheet, anchor, "Anchor");
        Seed(sheet, clicked, "Clicked");
        var selection = SelectAreas(window, anchor);
        Invoke(window, "OpenThreadedCommentCardFromIndicator", clicked);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var editor = Editor(window, true);
        Find<TextBox>(editor, "GridThreadedCommentSelectedReplyBox").Text = "Updated reply";
        Find<Button>(editor, delete ? "GridThreadedCommentDeleteReplyButton" : "GridThreadedCommentUpdateReplyButton")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        sheet.ThreadedComments[anchor].Replies.Single().Text.Should().Be("Anchor reply");
        if (delete) sheet.ThreadedComments[clicked].Replies.Should().BeEmpty();
        else sheet.ThreadedComments[clicked].Replies.Single().Text.Should().Be("Updated reply");
        AssertSelection(window, selection);
        window.Session.UndoLastEdit().Success.Should().BeTrue();
        sheet.ThreadedComments[clicked].Replies.Single().Text.Should().Be("Clicked reply");
    });

    public static IEnumerable<object[]> ProtectedActions =>
        from action in new[] { "NewNote", "EditNote", "DeleteNote", "NewComment", "EditComment", "DeleteComment" }
        from allowed in new[] { false, true }
        select new object[] { action, allowed };

    [Theory]
    [MemberData(nameof(ProtectedActions))]
    public Task ContextMutation_HonorsEditObjectsProtection(string action, bool editObjectsAllowed) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = At(sheet, 2, 2);
        var threaded = action.EndsWith("Comment", StringComparison.Ordinal);
        var creating = action.StartsWith("New", StringComparison.Ordinal);
        var deleting = action.StartsWith("Delete", StringComparison.Ordinal);
        if (!creating) Seed(sheet, clicked, "Clicked");
        var selection = SelectAreas(window, At(sheet, 1, 1));
        void ProtectAfterOpening()
        {
            sheet.IsProtected = true;
            if (editObjectsAllowed) sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
            else sheet.ProtectionPermissions.Remove(SheetProtectionPermission.EditObjects);
        }
        // Protected menus conservatively disable annotation entries. Open the actual UI first,
        // then protect the sheet to verify execution also enforces the current permissions.
        if (deleting)
        {
            var menu = (ContextMenu)Invoke(window, "BuildWorksheetCellContextMenuFor", clicked)!;
            try
            {
                menu.Open(window.SheetGridHostForTest);
                var item = Items(menu.Items).Single(item => item.Tag as string == action);
                item.IsEnabled.Should().BeTrue();
                ProtectAfterOpening();
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            finally { menu.Close(); }
        }
        else
        {
            await ClickMenu(window, action, clicked);
            var editor = Editor(window, threaded);
            Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox").Text = "Changed";
            ProtectAfterOpening();
            Find<Button>(editor, "GridCommentInlineSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Text(sheet, clicked, threaded).Should().Be(editObjectsAllowed
            ? deleting ? null : "Changed"
            : creating ? null : "Clicked");
        window.Session.CanUndo.Should().Be(editObjectsAllowed);
        window.Session.IsDirty.Should().Be(editObjectsAllowed);
        AssertSelection(window, selection);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StaleSheetEditor_CannotMutateOriginalOrNewSheet(bool threaded) => Run(async window =>
    {
        var original = window.Session.ActiveSheet;
        var clicked = At(original, 2, 2);
        Seed(original, clicked, "Original");
        SelectAreas(window, At(original, 1, 1));
        await ClickMenu(window, threaded ? "EditComment" : "EditNote", clicked);
        var editor = Editor(window, threaded);
        Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox").Text = "Wrong sheet";
        var other = window.Session.Workbook.AddSheet("Other target fixture");
        var otherCell = At(other, 2, 2);
        Seed(other, otherCell, "Other");
        window.Session.SelectSheet(other.Id);
        Find<Button>(editor, "GridCommentInlineSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Text(original, clicked, threaded).Should().Be("Original");
        Text(other, otherCell, threaded).Should().Be("Other");
        window.Session.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData(false, "_isSaving")]
    [InlineData(true, "_isSaving")]
    [InlineData(false, "_isOpening")]
    [InlineData(true, "_isOpening")]
    public Task EditorSubmission_RefusesDuringOpenOrSave(bool threaded, string busyField) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var clicked = At(sheet, 2, 2);
        Seed(sheet, clicked, "Clicked");
        await ClickMenu(window, threaded ? "EditComment" : "EditNote", clicked);
        var editor = Editor(window, threaded);
        Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox").Text = "Busy edit";
        var field = typeof(MainWindow).GetField(busyField, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, true);
        try { Find<Button>(editor, "GridCommentInlineSaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { field.SetValue(window, false); }
        Text(sheet, clicked, threaded).Should().Be("Clicked");
        window.Session.CanUndo.Should().BeFalse();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TargetlessRibbonRoute_RetainsPrimarySelectionAnchor(bool threaded) => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        Seed(sheet, anchor, "Anchor");
        Seed(sheet, At(sheet, 2, 2), "Active");
        SelectAreas(window, At(sheet, 2, 2));
        WorkbookApplicationCommandRouter.TryRouteWorksheetContextMenu(threaded ? "EditComment" : "EditNote", out var route).Should().BeTrue();
        var bindings = (WorkbookApplicationCommandBindings)typeof(MainWindow).GetProperty("WorkbookApplicationCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        await bindings.TryExecuteAsync(route!);
        var editor = Editor(window, threaded);
        Find<TextBox>(editor, threaded ? "GridThreadedCommentRootBox" : "GridNoteInlineTextBox").Text.Should().Be("Anchor");
        Find<Button>(editor, "GridCommentInlineCancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Session.CanUndo.Should().BeFalse();
    });

    [Fact]
    public Task ContextDelete_ClearsPreviousF4LikeOrdinaryReviewMutation() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = At(sheet, 1, 1);
        var clicked = At(sheet, 2, 2);
        var next = At(sheet, 3, 3);
        foreach (var address in new[] { anchor, clicked, next }) Seed(sheet, address, "Note");
        SelectAreas(window, anchor);
        await ClickMenu(window, "ShowHideNote", anchor);
        window.Session.CanRepeatLastAction.Should().BeTrue();
        await ClickMenu(window, "DeleteNote", clicked);
        window.Session.CanRepeatLastAction.Should().BeFalse();
        window.Session.SelectCell(next);
        window.SheetGridHostForTest.Focus();
        var key = new KeyEventArgs { Key = Key.F4, PhysicalKey = PhysicalKey.F4 };
        await window.RaiseKeyDownForTest(key);
        key.Handled.Should().BeTrue();
        sheet.Comments.Should().ContainKey(next);
        sheet.ShownComments.Should().NotContain(next);
        sheet.Comments.Should().NotContainKey(clicked);
    });

    private static string? Text(Sheet sheet, CellAddress address, bool threaded) => threaded
        ? sheet.ThreadedComments.GetValueOrDefault(address)?.Text : sheet.Comments.GetValueOrDefault(address);
    private static void Seed(Sheet sheet, CellAddress address, string text)
    {
        sheet.Comments[address] = text;
        sheet.ThreadedComments[address] = new ThreadedComment(text) { Replies = [new CommentReply(text + " reply")] };
    }
    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
    private static (GridRange Range, GridRange[] Areas, CellAddress Active) SelectAreas(MainWindow window, CellAddress active)
    {
        var sheet = window.Session.ActiveSheet;
        var primary = new GridRange(At(sheet, 1, 1), At(sheet, 8, 5));
        var extra = new GridRange(At(sheet, 10, 7), At(sheet, 11, 8));
        window.Session.SelectRanges(primary, [primary, extra], active);
        return Capture(window);
    }
    private static (GridRange Range, GridRange[] Areas, CellAddress Active) Capture(MainWindow window) =>
        (window.Session.SelectedRange, window.Session.SelectedRanges.ToArray(), window.Session.ActiveCell);
    private static void AssertSelection(MainWindow window, (GridRange Range, GridRange[] Areas, CellAddress Active) expected)
    {
        window.Session.SelectedRange.Should().Be(expected.Range);
        window.Session.SelectedRanges.Should().Equal(expected.Areas);
        window.Session.ActiveCell.Should().Be(expected.Active);
    }
    private static Border Editor(MainWindow window, bool threaded) => Find<Border>(window.RebuildSheetGridForTest(),
        threaded ? "WorksheetThreadedCommentInlineEditor" : "WorksheetNoteInlineEditor");
    private static T Find<T>(Control parent, string id) where T : Control => parent.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static object? Invoke(MainWindow window, string method, params object?[] args) => typeof(MainWindow)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static async Task ClickMenu(MainWindow window, string action, CellAddress clicked)
    {
        var menu = (ContextMenu)Invoke(window, "BuildWorksheetCellContextMenuFor", clicked)!;
        try
        {
            menu.Open(window.SheetGridHostForTest);
            var item = Items(menu.Items).Single(item => item.Tag as string == action);
            item.IsEnabled.Should().BeTrue();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally { menu.Close(); }
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
    private static IEnumerable<MenuItem> Items(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Items(item.Items)) yield return child;
        }
    }
    private static Task Run(Func<MainWindow, Task> test) => Headless.Dispatch(async () =>
    {
        var window = new MainWindow([], null!, new FreeXOptionsRuntimeSession(
            load: static () => new AppOptions { UserName = "Configured Reviewer" }, save: static _ => true));
        try
        {
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            await test(window);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);
}
