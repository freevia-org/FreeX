using System.Windows;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class PageVerticalAlignmentHostTests
{
    [StaFact]
    public void RibbonCommand_CyclesThroughBottomBeforeJustified()
    {
        var document = TextDocument.CreateEmpty();
        document.Page.VerticalAlignment = PageVerticalAlignment.Center;
        var editor = new DocumentView();
        editor.LoadModel(document);
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.page-valign", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.VerticalAlignment.Should().Be(PageVerticalAlignment.Bottom);

        command.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.VerticalAlignment.Should().Be(PageVerticalAlignment.Justified);
    }

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
