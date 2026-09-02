using System.Windows.Controls;
using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class WorkspaceScrollOwnershipTests
{
    [StaFact]
    public void PrintLayout_UsesTheWorkspaceScrollerInsteadOfAnEditorLocalScroller()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService());

        try
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            for (var index = 0; index < 160; index++)
                document.Blocks.Add(new Paragraph($"Long print-layout paragraph {index + 1}."));
            window.ActiveDocumentEditorForTests.LoadModel(document);

            window.Show();
            window.UpdateLayout();

            window.ActiveDocumentEditorForTests.VerticalScrollBarVisibility.Should()
                .Be(ScrollBarVisibility.Disabled);
            window.ActiveDocumentEditorForTests.HorizontalScrollBarVisibility.Should()
                .Be(ScrollBarVisibility.Disabled);
            window.WorkspaceScrollerForTests.VerticalScrollBarVisibility.Should()
                .Be(ScrollBarVisibility.Auto);
            window.WorkspaceScrollerForTests.Content.Should().NotBeNull();
            window.WorkspaceScrollerForTests.ScrollableHeight.Should().BeGreaterThan(0,
                "a multi-page document must extend the workspace, rather than become a page-local scroll region");
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class NoUiMessageService : IUserMessageService
    {
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.No;
    }
}
