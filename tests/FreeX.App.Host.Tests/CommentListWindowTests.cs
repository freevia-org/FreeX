using FluentAssertions;
using FreeX.Core.Model;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed class CommentListWindowTests
{
    [Fact]
    public void CreateThreadedCommentItems_SortsAndFormatsThreadedComments()
    {
        var sheetId = SheetId.New();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 3, 2)] = new("Later thread", "Quinn"),
            [new(sheetId, 1, 1)] = new("First thread")
        };

        var items = CommentListWindow.CreateThreadedCommentItems(threadedComments);

        items.Should().ContainInOrder(
            new CommentListWindowItem(new CellAddress(sheetId, 1, 1), "A1", "FreeX: First thread"),
            new CommentListWindowItem(new CellAddress(sheetId, 3, 2), "B3", "Quinn: Later thread"));
    }

    [Fact]
    public void CreateNoteItems_SortsAndKeepsPlainNoteText()
    {
        var sheetId = SheetId.New();
        var notes = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later note",
            [new(sheetId, 1, 1)] = "First note"
        };

        var items = CommentListWindow.CreateNoteItems(notes);

        items.Should().ContainInOrder(
            new CommentListWindowItem(new CellAddress(sheetId, 1, 1), "A1", "First note"),
            new CommentListWindowItem(new CellAddress(sheetId, 3, 2), "B3", "Later note"));
    }

    [Fact]
    public void RowFactories_AdaptSharedCommentListPlans()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CommentListWindow.cs");

        source.Should().Contain("CommentNavigationPlanner.CreateThreadedCommentRows(threadedComments)");
        source.Should().Contain("CommentNavigationPlanner.CreateNoteRows(notes)");
        source.Should().Contain("IReadOnlyList<CommentListRowPlan> plans");
        source.Should().NotContain("CommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)");
        source.Should().NotContain("CommentNavigationPlanner.FormatThreadedComment(threadedComments[address])");
        source.Should().NotContain("CommentNavigationPlanner.OrderedNoteAddresses(notes)");
    }

    [Fact]
    public void DialogChrome_UsesLocalizedLabelsAndAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var item = new CommentListWindowItem(new CellAddress(SheetId.New(), 1, 1), "A1", "Review this");
            var window = new CommentListWindow("Comments", [item], _ => { });

            var buttons = WpfTestTree.FindLogicalDescendants<Button>(window)
                .ToDictionary(button => AutomationProperties.GetAutomationId(button), StringComparer.Ordinal);
            buttons["ReviewCommentListOpenButton"].Content.Should().Be(UiText.Get("ReviewCommentList_OpenButton"));
            AutomationProperties.GetName(buttons["ReviewCommentListOpenButton"])
                .Should().Be(UiText.Get("ReviewCommentList_OpenButtonAutomationName"));
            AutomationProperties.GetHelpText(buttons["ReviewCommentListOpenButton"])
                .Should().Be(UiText.Get("ReviewCommentList_OpenButtonHelpText"));
            buttons["ReviewCommentListCloseButton"].Content.Should().Be(UiText.Get("ReviewCommentList_CloseButton"));
            AutomationProperties.GetName(buttons["ReviewCommentListCloseButton"])
                .Should().Be(UiText.Get("ReviewCommentList_CloseButtonAutomationName"));

            var list = WpfTestTree.FindLogicalDescendants<ListView>(window).Single();
            AutomationProperties.GetHelpText(list).Should().Be(UiText.Get("ReviewCommentList_ListHelpText"));
            var gridView = list.View.Should().BeOfType<GridView>().Subject;
            gridView.Columns.Select(column => column.Header?.ToString())
                .Should()
                .Equal(UiText.Get("ReviewCommentList_CellColumnHeader"), UiText.Get("ReviewCommentList_TextColumnHeader"));
        });
    }
}
