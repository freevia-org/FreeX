using System.IO;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentViewInteractionOwnershipTests
{
    [Fact]
    public void PairedRenderersDelegatePortableEditorInteractionToPresentation()
    {
        var host = ReadDocumentView("FreeW.App.Host");
        var avalonia = ReadDocumentView("FreeW.App.Avalonia");

        foreach (var source in new[] { host, avalonia })
        {
            source.Should().Contain("_editingSession.Interaction.SectionPosition(");
            source.Should().Contain("_editingSession.Interaction.PlanPasteText(");
            source.Should().Contain("DocumentEditorInteractionSession.PlanBodyKey(");
            source.Should().Contain("_editingSession.Interaction.ToggleFormatPainter(");
            source.Should().Contain("_editingSession.Interaction.TryApplyFormatPainter(");
            source.Should().Contain("_editingSession.Interaction.BodyRunStartOffset(");
            source.Should().Contain("FreeWContextMenuPlanner.ApplyContentControlCommand(");
            source.Should().NotContain("ContentChoicePrefix");
            source.Should().NotContain("ContentDatePrefix");
            source.Should().NotContain("_formatPainterLocked");
            source.Should().NotContain("FormatPainterClipboard.Capture(");
            source.Should().NotContain("TextRangeCoversParagraphText(");
            source.Should().NotContain("ModelRunStartOffset(");
            source.Should().NotContain("CurrentRevisionDateXml(");
        }
    }

    [Fact]
    public void AvaloniaKeepsOnlyNativeCaretAndFloatingDragRealization()
    {
        var avalonia = ReadDocumentView("FreeW.App.Avalonia");

        avalonia.Should().Contain("Interaction.NavigateBodyHorizontal(");
        avalonia.Should().Contain("Interaction.NavigateTableHorizontal(");
        avalonia.Should().Contain("Interaction.NavigateTableTab(");
        avalonia.Should().Contain("Interaction.SelectAllBodyText(");
        avalonia.Should().Contain("DocumentFloatingDragSession _floatingDrag = new();");
        avalonia.Should().Contain("FindCellGlyphOffset(");
        avalonia.Should().NotContain("MoveCaretToAdjacentCell(");
        avalonia.Should().NotContain("_floatDragState");
        avalonia.Should().NotContain("DocumentViewLayoutPlanner.BuildFloatingMoveRect(");
        avalonia.Should().NotContain("DocumentViewLayoutPlanner.BuildFloatingResizeRect(");
    }

    [Fact]
    public void WpfRetainsNativeRichTextNavigationWithoutParallelPortableState()
    {
        var host = ReadDocumentView("FreeW.App.Host");

        host.Should().Contain("RichTextBox");
        host.Should().NotContain("MoveCaretToAdjacentCell(");
        host.Should().NotContain("_floatDragState");
        host.Should().NotContain("DocumentFloatingDragSession");
    }

    private static string ReadDocumentView(string project)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, "Editing", "DocumentView.cs"));
    }
}
