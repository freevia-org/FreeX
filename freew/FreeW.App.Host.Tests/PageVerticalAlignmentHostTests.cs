using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class PageVerticalAlignmentHostTests
{
    [StaTheory]
    [InlineData(PageVerticalAlignment.Top, VerticalAlignment.Top)]
    [InlineData(PageVerticalAlignment.Center, VerticalAlignment.Center)]
    [InlineData(PageVerticalAlignment.Bottom, VerticalAlignment.Bottom)]
    [InlineData(PageVerticalAlignment.Justified, VerticalAlignment.Top)]
    public void PageBox_UsesSectionBodyVerticalAlignment(
        PageVerticalAlignment alignment,
        VerticalAlignment expected)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body"));
        document.Page.VerticalAlignment = alignment;

        var editor = new DocumentView();
        editor.LoadModel(document);

        var page = PaginatedEditorPanel.Build(editor).PageBoxes.Single();

        page.Body.VerticalContentAlignment.Should().Be(expected);
    }
}
