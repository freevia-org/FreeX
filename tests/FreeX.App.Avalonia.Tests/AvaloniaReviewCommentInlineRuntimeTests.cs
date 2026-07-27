using System.Reflection;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAssertions;

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
            var rootBox = FindByAutomationId<TextBox>(editor!, "ThreadedCommentRootBox");
            rootBox.Should().NotBeNull();
            rootBox!.Text = "First comment";

            var save = FindByAutomationId<Button>(editor, "GridThreadedCommentInlineSaveButton");
            save.Should().NotBeNull();
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
            var rootBox = FindByAutomationId<TextBox>(editor!, "ThreadedCommentRootBox");
            rootBox!.Text = "Cancelled comment";

            var cancel = FindByAutomationId<Button>(editor, "GridThreadedCommentInlineCancelButton");
            cancel.Should().NotBeNull();
            cancel!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            sheet.ThreadedComments.Should().NotContainKey(address);
            window.Session.CanUndo.Should().BeFalse();
            window.Close();
        }, CancellationToken.None);
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object[] args) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(window, args);

    private static T? FindByAutomationId<T>(Control? root, string automationId)
        where T : Control =>
        root is null
            ? null
            : root.GetVisualDescendants()
                .OfType<T>()
                .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
}
