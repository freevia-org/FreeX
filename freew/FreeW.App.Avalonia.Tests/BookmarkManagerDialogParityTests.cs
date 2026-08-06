using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class BookmarkManagerDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Matches_Wpf_geometry_action_order_focus_and_automation_contract()
    {
        await Session.Dispatch(() =>
        {
            var paragraph = new Paragraph("Target");
            paragraph.BookmarkNames.Add("Here");
            var dialog = new BookmarkManagerDialog(ViewWith(paragraph));
            var surface = BookmarkManagerDialogPlanner.Surface;

            dialog.Width.Should().Be(surface.DialogWidth);
            AutomationProperties.GetAutomationId(dialog).Should().Be(surface.WindowAutomationId);

            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal(surface.Actions.Select(action => action.Label));
            buttons.Select(button => AutomationProperties.GetAutomationId(button)).Should().Equal(
                surface.Actions.Select(action => action.AutomationId));

            var list = dialog.GetLogicalDescendants().OfType<ListBox>().Single();
            AutomationProperties.GetAutomationId(list).Should().Be(surface.ListAutomationId);
            var textBlocks = dialog.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            AutomationProperties.GetAutomationId(textBlocks.Single(text => text.Text == surface.Heading)).Should().Be(surface.HeadingAutomationId);
            AutomationProperties.GetAutomationId(textBlocks.Single(text => text.Text is null or "")).Should().Be(surface.StatusAutomationId);

            buttons.Should().OnlyContain(button => button.MinWidth == surface.ButtonMinWidth);
            buttons.Should().OnlyContain(button => button.Margin == new Thickness(surface.ButtonLeadingMargin, 0, 0, 0));
            buttons.Should().OnlyContain(button => button.Padding == new Thickness(surface.ButtonHorizontalPadding, surface.ButtonVerticalPadding));

            try
            {
                dialog.Show();
                dialog.Measure(new Size(380, 320));
                dialog.Arrange(new Rect(0, 0, 380, 320));
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);

                dialog.ItemCountForTest.Should().Be(1);
                list.SelectedIndex.Should().Be(0);
                buttons[0].IsEnabled.Should().BeTrue();
                buttons[1].IsEnabled.Should().BeTrue();
                list.IsFocused.Should().BeTrue();

                dialog.DeleteForTest();
                dialog.ItemCountForTest.Should().Be(0);
                dialog.StatusTextForTest.Should().Be(BookmarkManagerDialogPlanner.RemovedStatusText("Here"));
                buttons[0].IsEnabled.Should().BeFalse();
                buttons[1].IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Empty_document_keeps_actions_disabled_and_uses_the_Wpf_empty_status()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new BookmarkManagerDialog(ViewWith(new Paragraph("Body")));
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                dialog.ItemCountForTest.Should().Be(0);
                dialog.StatusTextForTest.Should().Be(BookmarkManagerDialogPlanner.EmptyStatusText);
                dialog.GetLogicalDescendants().OfType<Button>()
                    .Take(2).Should().OnlyContain(button => !button.IsEnabled);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void Wpf_authority_and_route_adapters_keep_the_same_contract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "BookmarkManagerDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "BookmarkManagerDialog.cs"));
        var presentation = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Presentation", "Dialogs", "BookmarkManagerDialogSession.cs"));
        var wpfFactory = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Wpf", "WpfDialogRouteFactory.cs"));
        var avaloniaFactory = File.ReadAllText(Path.Combine(root, "freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "AvaloniaDialogRouteFactory.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("BookmarkManagerDialog");
            source.Should().Contain("BookmarkManagerDialogPlanner.Surface");
            source.Should().Contain("Surface.ListAutomationId");
            source.Should().Contain("Surface.Action(");
        }

        presentation.Should().Contain("BookmarkManagerGoToButton");
        presentation.Should().Contain("BookmarkManagerDeleteButton");
        presentation.Should().Contain("BookmarkManagerCloseButton");
        presentation.Should().Contain("BookmarkManagerStatus");
        wpfFactory.Should().Contain("routeId == \"bookmark-manager\"").And.Contain("CreateBookmarkManager(state, owner)");
        avaloniaFactory.Should().Contain("routeId == \"bookmark-manager\"").And.Contain("CreateBookmarkManager(state)");
    }

    private static DocumentView ViewWith(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
