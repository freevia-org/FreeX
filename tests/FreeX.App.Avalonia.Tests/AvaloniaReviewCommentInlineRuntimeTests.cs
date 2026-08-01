using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAssertions;

using Free.Shared.Drawing;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless production proof that FreeX Avalonia's normal New Comment route opens the worksheet
/// inline editor and commits through the shared undoable review mutation used by WPF.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaReviewCommentInlineRuntimeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(50, 40, 64, 20, 800, 500, 120, 40, 300, 220)]
    [InlineData(720, 40, 64, 20, 800, 500, 414, 40, 300, 220)]
    [InlineData(90, 130, 50, 20, 150, 160, 8, 8, 134, 144)]
    public void InlineNotePlacement_MatchesWpfPopupPlacementAndClampsToSurface(
        double cellLeft,
        double cellTop,
        double cellWidth,
        double cellHeight,
        double viewportWidth,
        double viewportHeight,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedMaxHeight)
    {
        var placement = CommentPreviewPlacementPlanner.Calculate(
            new LayoutRect(cellLeft, cellTop, cellWidth, cellHeight),
            new CommentPreviewLayoutSize(viewportWidth, viewportHeight),
            new CommentPreviewLayoutSize(300, 230));

        placement.HorizontalOffset.Should().Be(expectedLeft);
        placement.VerticalOffset.Should().Be(expectedTop);
        placement.Width.Should().Be(expectedWidth);
        placement.MaxHeight.Should().Be(expectedMaxHeight);
    }

    [Fact]
    public async Task NewNoteRoute_OpensAnchoredInlineEditorAndCtrlEnterCommitsUndoableNote()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewNoteFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 2, 3);
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);

            ((Task)InvokePrivate(window, "ShowNewNoteDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();
            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetNoteInlineEditor");
            editor.Should().NotBeNull();
            editor!.Background.Should().BeOfType<SolidColorBrush>().Which.Color
                .Should().Be(Color.FromRgb(255, 255, 225));
            editor.Padding.Should().Be(new Thickness(8));
            editor.Width.Should().Be(300);
            editor.MaxHeight.Should().Be(220);
            editor.BoxShadow.Should().NotBeNull();

            var noteBox = FindByAutomationId<TextBox>(editor, "GridNoteInlineTextBox");
            noteBox.Should().NotBeNull();
            noteBox!.Text.Should().BeEmpty();
            noteBox.Padding.Should().Be(new Thickness(5));
            noteBox.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Top);
            noteBox.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty)
                .Should().Be(ScrollBarVisibility.Auto);
            noteBox.GetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty)
                .Should().Be(ScrollBarVisibility.Disabled);
            var save = FindByAutomationId<Button>(editor, "GridCommentInlineSaveButton");
            var cancel = FindByAutomationId<Button>(editor, "GridCommentInlineCancelButton");
            save.Should().NotBeNull();
            cancel.Should().NotBeNull();
            save!.Content.Should().Be("Save");
            save.Width.Should().Be(72);
            save.MinHeight.Should().Be(24);
            cancel!.Content.Should().Be("Cancel");
            cancel.Width.Should().Be(72);
            cancel.MinHeight.Should().Be(24);

            noteBox.Text = "First note";
            noteBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = KeyModifiers.Control,
            });

            sheet.Comments.Should().ContainKey(address);
            sheet.Comments[address].Should().Be("First note");
            window.Session.CanUndo.Should().BeTrue();
            window.Session.UndoLastEdit().Success.Should().BeTrue();
            sheet.Comments.Should().NotContainKey(address);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditNoteRoute_SeedsExistingTextAndEscapeLeavesWorkbookUnchanged()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewNoteCancelFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 4);
            sheet.Comments[address] = "Existing note";
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);

            ((Task)InvokePrivate(window, "ShowEditNoteDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();
            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetNoteInlineEditor");
            var noteBox = FindByAutomationId<TextBox>(editor!, "GridNoteInlineTextBox");
            noteBox.Should().NotBeNull();
            noteBox!.Text.Should().Be("Existing note");
            InvokePrivate(window, "FocusInlineNoteEditor");
            noteBox.CaretIndex.Should().Be(noteBox.Text.Length);
            noteBox.SelectionStart.Should().Be(noteBox.Text.Length);
            noteBox.SelectionEnd.Should().Be(noteBox.Text.Length);
            noteBox.Text = "Changed but cancelled";
            noteBox.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
                KeyModifiers = KeyModifiers.None,
            });

            sheet.Comments[address].Should().Be("Existing note");
            window.Session.CanUndo.Should().BeFalse();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NewCommentRibbonCommand_OpensInlineEditorAndCommitsUndoableComment()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommentFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);

            ((Task)InvokePrivate(window, "ShowNewThreadedCommentDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();

            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetThreadedCommentInlineEditor");
            editor.Should().NotBeNull();
            var rootBox = FindByAutomationId<TextBox>(editor!, "GridThreadedCommentRootBox");
            rootBox.Should().NotBeNull();
            rootBox!.Text = "First comment";

            var save = FindByAutomationId<Button>(editor, "GridCommentInlineSaveButton");
            save.Should().NotBeNull();
            save!.Content.Should().Be("Save");
            save.Width.Should().Be(72);
            save.MinHeight.Should().Be(24);
            var cancel = FindByAutomationId<Button>(editor, "GridCommentInlineCancelButton");
            cancel.Should().NotBeNull();
            cancel!.Content.Should().Be("Cancel");
            cancel.Width.Should().Be(72);
            cancel.MinHeight.Should().Be(24);
            editor!.GetVisualDescendants().OfType<Button>().ToList().IndexOf(save)
                .Should().BeLessThan(editor.GetVisualDescendants().OfType<Button>().ToList().IndexOf(cancel));
            save!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            sheet.ThreadedComments.Should().ContainKey(address);
            sheet.ThreadedComments[address].Text.Should().Be("First comment");
            window.Session.CanUndo.Should().BeTrue();

            window.Session.UndoLastEdit().Success.Should().BeTrue();
            sheet.ThreadedComments.Should().NotContainKey(address);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NewCommentInlineEditor_CancelLeavesWorkbookUnchanged()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommentCancelFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);

            ((Task)InvokePrivate(window, "ShowNewThreadedCommentDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();
            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetThreadedCommentInlineEditor");
            var rootBox = FindByAutomationId<TextBox>(editor!, "GridThreadedCommentRootBox");
            rootBox!.Text = "Cancelled comment";

            var cancel = FindByAutomationId<Button>(editor, "GridCommentInlineCancelButton");
            cancel.Should().NotBeNull();
            cancel!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            sheet.ThreadedComments.Should().NotContainKey(address);
            window.Session.CanUndo.Should().BeFalse();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingComment_SelectedReplyCtrlEnterUpdatesReplyNotRootOrNewReply()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewReplyFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.ThreadedComments[address] = new ThreadedComment("Root text")
            {
                Replies = [new CommentReply("Original reply")],
            };
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);

            ((Task)InvokePrivate(window, "ShowEditThreadedCommentDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();
            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetThreadedCommentInlineEditor");
            editor.Should().NotBeNull();
            editor!.Background.Should().BeOfType<SolidColorBrush>().Which.Color
                .Should().Be(Color.FromRgb(255, 255, 225));
            editor.Padding.Should().Be(new Thickness(8));

            var rootBox = FindByAutomationId<TextBox>(editor, "GridThreadedCommentRootBox");
            var replyBox = FindByAutomationId<TextBox>(editor, "GridThreadedCommentReplyBox");
            var selectedReplyBox = FindByAutomationId<TextBox>(editor, "GridThreadedCommentSelectedReplyBox");
            var conversation = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
            rootBox.Should().NotBeNull();
            replyBox.Should().NotBeNull();
            selectedReplyBox.Should().NotBeNull();
            conversation.MaxHeight.Should().Be(92);
            var updateReply = FindByAutomationId<Button>(editor, "GridThreadedCommentUpdateReplyButton");
            var deleteReply = FindByAutomationId<Button>(editor, "GridThreadedCommentDeleteReplyButton");
            updateReply.Should().NotBeNull();
            deleteReply.Should().NotBeNull();
            updateReply!.Width.Should().Be(104);
            deleteReply!.Width.Should().Be(104);
            updateReply.Parent.Should().BeOfType<StackPanel>().Which.HorizontalAlignment
                .Should().Be(global::Avalonia.Layout.HorizontalAlignment.Left);
            var save = FindByAutomationId<Button>(editor, "GridCommentInlineSaveButton");
            save.Should().NotBeNull();
            save!.Content.Should().Be("Apply");

            selectedReplyBox!.Text = "Updated reply";
            var keyEvent = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                KeyModifiers = KeyModifiers.Control,
            };
            selectedReplyBox.RaiseEvent(keyEvent);

            sheet.ThreadedComments[address].Text.Should().Be("Root text");
            sheet.ThreadedComments[address].Replies.Should().ContainSingle();
            sheet.ThreadedComments[address].Replies[0].Text.Should().Be("Updated reply");
            window.Session.CanUndo.Should().BeTrue();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OpenReviewCommentList_RefreshesAfterInlineCommentMutation()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommentListFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 2, 2);
            sheet.ThreadedComments[address] = new ThreadedComment("Original root");
            window.Session.SelectCell(address);
            window.Session.UpdateViewportSize(880, 1440);
            window.Show();

            ((Task)InvokePrivate(window, "ShowCommentsListAsync")!).GetAwaiter().GetResult();
            var listWindow = GetPrivateField<Window>(window, "_commentListWindow");
            listWindow.Should().NotBeNull();
            var list = FindByAutomationId<ListBox>(listWindow, "ReviewCommentList");
            list.Should().NotBeNull();
            list!.Items.Should().ContainSingle();
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListText_B2")!.Text
                .Should().Be("FreeX: Original root");

            ((Task)InvokePrivate(window, "ShowEditThreadedCommentDialogAsync")!).GetAwaiter().GetResult();
            var renderedGrid = window.RebuildSheetGridForTest();
            var editor = FindByAutomationId<Border>(renderedGrid, "WorksheetThreadedCommentInlineEditor");
            var rootBox = FindByAutomationId<TextBox>(editor, "GridThreadedCommentRootBox");
            var save = FindByAutomationId<Button>(editor, "GridCommentInlineSaveButton");
            rootBox.Should().NotBeNull();
            save.Should().NotBeNull();
            rootBox!.Text = "Updated root";
            save!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            list.Items.Should().ContainSingle();
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListText_B2")!.Text
                .Should().Be("FreeX: Updated root");

            listWindow!.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShowCommentsList_ExcludesLegacyNotes_AndShowNotesTogglesNoteVisibility()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommandFixture");
            window.Session.SelectSheet(sheet.Id);
            var noteAddress = new CellAddress(sheet.Id, 1, 1);
            var commentAddress = new CellAddress(sheet.Id, 2, 2);
            sheet.Comments[noteAddress] = "Legacy note";
            sheet.ThreadedComments[commentAddress] = new ThreadedComment("Threaded comment");
            window.Session.SelectCell(commentAddress);
            window.Session.UpdateViewportSize(880, 1440);
            window.Show();

            ((Task)InvokePrivate(window, "ShowCommentsListAsync")!).GetAwaiter().GetResult();
            var listWindow = GetPrivateField<Window>(window, "_commentListWindow");
            var list = FindByAutomationId<ListBox>(listWindow, "ReviewCommentList");
            list.Should().NotBeNull();
            list!.Items.Should().ContainSingle();
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListText_B2")!.Text
                .Should().Be("FreeX: Threaded comment");
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListText_B2")!.Text
                .Should().NotContain("Legacy note");

            InvokePrivate(window, "ToggleAllNotesVisibility");
            sheet.ShownComments.Should().Contain(noteAddress);
            InvokePrivate(window, "ToggleAllNotesVisibility");
            sheet.ShownComments.Should().NotContain(noteAddress);

            listWindow!.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReviewCommentList_UsesWpfTwoColumnPresentationAndThreadFormatting()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommentColumnsFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 3, 4);
            sheet.ThreadedComments[address] = new ThreadedComment("Root", "Reviewer") with
            {
                Replies = [new CommentReply("Reply", "Responder")],
                IsResolved = true,
            };
            window.Session.UpdateViewportSize(880, 1440);
            window.Show();

            ((Task)InvokePrivate(window, "ShowCommentsListAsync")!).GetAwaiter().GetResult();
            var listWindow = GetPrivateField<Window>(window, "_commentListWindow");
            var list = FindByAutomationId<ListBox>(listWindow, "ReviewCommentList");
            list.Should().NotBeNull();
            AutomationProperties.GetHelpText(list!).Should().Be(UiText.Get("ReviewCommentList_ListHelpText"));
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListCellHeader")!.Text
                .Should().Be(UiText.Get("ReviewCommentList_CellColumnHeader"));
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListTextHeader")!.Text
                .Should().Be(UiText.Get("ReviewCommentList_TextColumnHeader"));
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListCell_D3")!.Text.Should().Be("D3");
            FindByAutomationId<TextBlock>(listWindow, "ReviewCommentListText_D3")!.Text
                .Should().Be("Reviewer: Root | Responder: Reply | Resolved");

            listWindow!.Close();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReviewCommentList_OpenStateAndEnterNavigationMatchWpfBehavior()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ReviewCommentInteractionFixture");
            window.Session.SelectSheet(sheet.Id);
            var firstAddress = new CellAddress(sheet.Id, 1, 1);
            var secondAddress = new CellAddress(sheet.Id, 2, 3);
            sheet.ThreadedComments[firstAddress] = new ThreadedComment("First");
            sheet.ThreadedComments[secondAddress] = new ThreadedComment("Second");
            window.Session.UpdateViewportSize(880, 1440);
            window.Show();

            ((Task)InvokePrivate(window, "ShowCommentsListAsync")!).GetAwaiter().GetResult();
            var listWindow = GetPrivateField<Window>(window, "_commentListWindow");
            var list = FindByAutomationId<ListBox>(listWindow, "ReviewCommentList");
            var openButton = FindByAutomationId<Button>(listWindow, "ReviewCommentListOpenButton");
            list.Should().NotBeNull();
            openButton.Should().NotBeNull();

            list!.SelectedIndex = -1;
            openButton!.IsEnabled.Should().BeFalse();
            list.SelectedIndex = 1;
            openButton.IsEnabled.Should().BeTrue();
            list.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            window.Session.ActiveCell.Should().Be(secondAddress);

            listWindow!.Close();
            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);

    private static T? GetPrivateField<T>(MainWindow window, string fieldName)
        where T : class =>
        typeof(MainWindow)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(window) as T;

    private static T? FindByAutomationId<T>(Control? root, string automationId)
        where T : Control =>
        root is null
            ? null
            : root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
}
