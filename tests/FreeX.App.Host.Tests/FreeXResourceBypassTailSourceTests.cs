using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeXResourceBypassTailSourceTests
{
    [Fact]
    public void NamedRendererTail_UsesSharedResourcesAndPortablePlans()
    {
        var scenario = Read("src", "FreeX.App.Host", "MainWindow.ScenarioCommands.cs");
        scenario.Should().Contain("ScenarioManagerDialogPlanner.MergeDialogTitle.Resolve");
        scenario.Should().Contain("ScenarioManagerDialogPlanner.MergeOpenFailedMessage.Resolve");
        scenario.Should().NotContain("Title = \"Merge Scenarios\"");
        scenario.Should().NotContain("ShowInfo(\"The selected file could not be opened for merging scenarios.");

        var statistics = Read("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs");
        statistics.Should().Contain("UiText.Get(\"WorkbookStatistics_CopyToClipboard\")");
        statistics.Should().Contain("UiText.Get(\"WorkbookStatistics_CopyToClipboardHelpText\")");
        statistics.Should().NotContain("const string copyContent");

        var backstage = Read("src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs");
        backstage.Should().Contain("FreeXBackstageInfoSurface.AvaloniaLivePane");
        backstage.Should().Contain("UiText.Get(\"Backstage_Home_NoRecentWorkbooks\")");
        backstage.Should().Contain("UiText.Get(\"Backstage_Print_Description\")");
        backstage.Should().NotContain("Text = \"(No recent workbooks)\"");
        backstage.Should().NotContain("CreateLiveBackstageSection(\"Properties\")");
        backstage.Should().NotContain("Preview the active worksheet or send it to an available printer.");

        var insertFunction = Read("src", "FreeX.App.Avalonia", "MainWindow.InsertFunction.cs");
        insertFunction.Should().Contain("ResolveInsertFunctionLabel(\"InsertFunction_SearchForAFunction\")");
        insertFunction.Should().Contain("FunctionArguments_SelectWorksheetReferenceAutomationNameFormat");
        insertFunction.Should().NotContain("AutomationProperties.SetName(searchBox, \"Search for a function\")");
        insertFunction.Should().NotContain("new TextBlock { Text = \"Select a function:\"");
    }

    [Fact]
    public void WpfInlineCommentRenderer_EmitsPortableResultAndOwnsNoVisibleEnglish()
    {
        var events = Read("src", "FreeX.App.UI", "GridView.Events.cs");
        var comments = Read("src", "FreeX.App.UI", "GridView.CommentPreview.cs");
        var host = Read("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs");

        events.Should().Contain("ThreadedCommentDialogResult result");
        events.Should().NotContain("GridThreadedCommentEditResult");
        events.Should().NotContain("GridThreadedCommentEditAction");
        comments.Should().Contain("ThreadedCommentDialogPlanner.TryCreateResult");
        comments.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyEditResult");
        comments.Should().Contain("ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult");
        comments.Should().NotContain("private static bool TryCreateThreadedCommentEditResult");
        comments.Should().NotContain("Content = \"Mark as resolved\"");
        comments.Should().NotContain("Content = \"Update reply\"");
        comments.Should().NotContain("Content = \"Delete reply\"");
        comments.Should().NotContain("Content = \"Cancel\"");
        host.Should().Contain("ReviewSessionController.ApplyThreadedComment(e.Result)");
        host.Should().NotContain("GridThreadedCommentEditAction");
    }

    [Fact]
    public void MacOsReadiness_RequiresResourceBackedFormatPainterText()
    {
        var source = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var readiness = Read("tools", "Test-MacOsAppReadiness.ps1");

        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? UiText.Get(\"MainLoc_FormatPainterFailed\"));");
        source.Should().Contain("HasFormatPainterButton: _formatPainterButton.Content?.ToString() == UiText.Get(\"MainWindow_TooltipTitle_FormatPainter\")");
        readiness.Should().Contain("_formatPainterButton.Content = UiText.Get(`\"MainWindow_TooltipTitle_FormatPainter`\");");
        readiness.Should().NotContain("_formatPainterButton.Content = `\"Format Painter`\";");
    }

    private static string Read(params string[] segments) =>
        WorkspaceFileLocator.ReadAllText(segments);
}
