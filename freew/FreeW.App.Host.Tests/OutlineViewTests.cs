using System.IO;
using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the Outline view surface (View &gt; Outline): entering the view lists the document's
/// headings/body in structure order, the Outlining "Show Level" filter narrows to the chosen depth, and the
/// outline's restructuring buttons reuse the editor's reversible heading commands
/// (<see cref="DocumentView.PromoteHeading"/> / <see cref="DocumentView.MoveHeading"/>). The view never
/// mutates the model by itself, so it composes with the existing outline plumbing.
/// </summary>
public sealed class OutlineViewTests
{
    private static Paragraph H(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : "Heading" + level };

    // [Title, body, H1, body, H2, body, H1]
    private static TextDocument Sample()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(H(0, "My Title"));
        doc.Blocks.Add(new Paragraph("intro body"));
        doc.Blocks.Add(H(1, "Chapter One"));
        doc.Blocks.Add(new Paragraph("one body"));
        doc.Blocks.Add(H(2, "Section A"));
        doc.Blocks.Add(new Paragraph("section body"));
        doc.Blocks.Add(H(1, "Chapter Two"));
        return doc;
    }

    [Fact]
    public void Renderer_delegates_outline_state_and_operations_to_presentation_controller()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Editing",
            "OutlineView.cs"));

        source.Should().Contain("using FreeW.App.Presentation.Editing;");
        source.Should().Contain("new OutlineViewController(GetCommittedDocument, _editor.SetHeadingLevel, _editor.MoveHeading)");
        source.Should().Contain("_controller.RowsChanged += RenderRows;");
        source.Should().Contain("_controller.Refresh();");
        source.Should().Contain("_controller.Apply(_editor.PromoteHeading)");
        source.Should().Contain("_controller.Move(moveUp: true)");
        source.Should().Contain("_controller.Move(moveUp: false)");
        source.Should().Contain("_controller.SelectBlock(blockIndex)");
        source.Should().Contain("_controller.SetShowLevel(level)");
        source.Should().Contain("_controller.SetFirstLineOnly(firstLineOnly)");
        source.Should().Contain("_controller.SetOutlineLevel(level)");
        source.Should().Contain("_controller.CurrentOutlineLevel");
        source.Should().Contain("_controller.VisibleRows");
        source.Should().Contain("private TextDocument GetCommittedDocument()");
        source.Should().Contain("_editor.CommitToModel();", "WPF must still commit native edits before shared refresh");
        source.Should().Contain("\"⊞ \"").And.Contain("\"▢ \"", "WPF owns its visual marker glyphs");
        source.Should().NotContain("OutlineViewModel.Build(");
        source.Should().NotContain("_selectedShowLevel");
        source.Should().NotContain("_firstLineOnly");
    }

    [StaFact]
    public void Entering_ShowsHeadingsAndBodyInStructureOrder()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var outline = new OutlineView(view);

        outline.Refresh();

        outline.VisibleRows.Select(r => r.Text).Should().Equal(
            "My Title", "intro body", "Chapter One", "one body", "Section A", "section body", "Chapter Two");
        outline.VisibleRows.Where(r => r.IsHeading).Select(r => r.Text)
            .Should().Equal("My Title", "Chapter One", "Section A", "Chapter Two");
    }

    [StaFact]
    public void ShowLevel1_FiltersToTopHeadings_HidingDeeperHeadingsAndBody()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var outline = new OutlineView(view);

        outline.SetShowLevel(1);

        outline.VisibleRows.Select(r => r.Text).Should().Equal("My Title", "Chapter One", "Chapter Two");
        outline.VisibleRows.Should().OnlyContain(r => r.IsHeading);
    }

    [StaFact]
    public void Promote_FromOutline_ChangesHeadingLevelAndReflectsInRows()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var outline = new OutlineView(view);
        outline.Refresh();

        // Promote "Section A" (Heading 2, block index 4) — it should become Heading 1.
        outline.SelectBlockIndex(4);
        view.PromoteHeading(4);
        outline.Refresh();

        outline.VisibleRows.Single(r => r.Text == "Section A").Level.Should().Be(1);
    }

    [StaFact]
    public void OutlineCommand_IsRegistered_AndItsCheckedStateReflectsOutlineMode()
    {
        var view = new DocumentView();
        view.LoadModel(Sample());
        var store = new RibbonStateStore();
        var active = false;

        var registry = FreeWRibbonCommands.Build(
            view, store, onPrintPreview: null, onToggleNavPane: null, isNavPaneVisible: null,
            onToggleReadMode: null, isReadModeActive: null, onTogglePrintLayout: null, isPrintLayoutActive: null,
            onToggleOutlineView: () => active = !active, isOutlineViewActive: () => active);

        registry.TryGet("freew.outline-view", out var command).Should().BeTrue();
        var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        stateful.GetState().IsChecked.Should().BeFalse("outline mode starts off");

        command!.Execute(RibbonCommandContext.Empty);

        active.Should().BeTrue("executing the outline-view command toggles the host's outline mode on");
        stateful.GetState().IsChecked.Should().BeTrue("the command reports the new outline mode as checked");
    }
}
