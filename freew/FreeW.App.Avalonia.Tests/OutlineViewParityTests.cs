using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Functional parity coverage for the Avalonia View &gt; Outline surface and its production MainWindow
/// callback. These tests exercise the actual rows, filtering, heading commands, caret navigation, and
/// mode transitions rather than source-string guards or injected callback fakes.
/// </summary>
public sealed class OutlineViewParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static Paragraph Heading(int level, string text) =>
        new(text) { StyleId = level == 0 ? "Title" : $"Heading{level}" };

    private static TextDocument Sample()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Heading(0, "My Title"));
        document.Blocks.Add(new Paragraph("intro line one\nintro line two"));
        document.Blocks.Add(Heading(1, "Chapter One"));
        document.Blocks.Add(new Paragraph("one body"));
        document.Blocks.Add(Heading(2, "Section A"));
        document.Blocks.Add(new Paragraph("section body"));
        document.Blocks.Add(Heading(1, "Chapter Two"));
        return document;
    }

    [Fact]
    public async Task Outline_rows_filter_and_first_line_option_match_Wpf_shape()
    {
        IReadOnlyList<OutlineRow> allRows = [];
        IReadOnlyList<OutlineRow> levelOneRows = [];
        IReadOnlyList<OutlineRow> firstLineRows = [];

        await OnUiThread(() =>
        {
            var editor = new DocumentView();
            editor.LoadDocument(Sample());
            var outline = new OutlineView(editor);

            outline.Refresh();
            allRows = outline.VisibleRows;
            outline.SetShowLevel(1);
            levelOneRows = outline.VisibleRows;
            outline.SetShowLevel(OutlineViewModel.ShowAllLevels);
            outline.SetFirstLineOnly(true);
            firstLineRows = outline.VisibleRows;
        });

        allRows.Select(row => row.Text).Should().Equal(
            "My Title", "intro line one\nintro line two", "Chapter One", "one body",
            "Section A", "section body", "Chapter Two");
        levelOneRows.Select(row => row.Text).Should().Equal("My Title", "Chapter One", "Chapter Two");
        levelOneRows.Should().OnlyContain(row => row.IsHeading);
        firstLineRows[1].Text.Should().Be("intro line one");
    }

    [Fact]
    public async Task Outline_selection_navigates_and_heading_actions_use_undoable_editor_paths()
    {
        (int Block, int Offset) caret = (-1, -1);
        IReadOnlyList<OutlineRow> rowsAfter = [];
        string plainTextBefore = string.Empty;
        string plainTextAfter = string.Empty;
        int? selectedAfterMove = null;
        bool collapsed = false;

        await OnUiThread(() =>
        {
            var editor = new DocumentView();
            editor.LoadDocument(Sample());
            var outline = new OutlineView(editor);
            outline.Refresh();
            plainTextBefore = editor.Document.PlainText;

            outline.SelectBlockIndex(4);
            caret = editor.CaretPosition;
            outline.SetOutlineLevel(1);
            outline.Refresh();
            rowsAfter = outline.VisibleRows;
            plainTextAfter = editor.Document.PlainText;

            outline.SelectBlockIndex(2);
            outline.PromoteSelectedToHeading1ForTests();
            outline.SelectBlockIndex(6);
            outline.MoveSelectedForTests(moveUp: true);
            selectedAfterMove = outline.SelectedBlockIndex;
            outline.SelectBlockIndex(2);
            outline.CollapseSelectedForTests();
            collapsed = editor.IsHeadingCollapsed(2);
            outline.ExpandSelectedForTests();
        });

        caret.Should().Be((4, 0));
        rowsAfter.Single(row => row.BlockIndex == 4).Level.Should().Be(1);
        plainTextAfter.Should().Be(plainTextBefore);
        selectedAfterMove.Should().Be(4);
        collapsed.Should().BeTrue();
    }

    [Fact]
    public async Task Production_outline_callback_swaps_workspace_and_is_mutually_exclusive_with_view_modes()
    {
        bool activeAfterEnter = false;
        bool activeAfterExit = true;
        bool workspaceIsOutline = false;
        bool workspaceIsLive = false;
        DocumentViewMode modeAfterDraft = DocumentViewMode.PrintLayout;
        bool pageEditRestoredAfterOutline = false;

        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var registry = window.RibbonRegistryForTests;
            registry.Should().NotBeNull();
            registry.TryGet(new RibbonCommandId("freew.outline-view"), out var outlineCommand).Should().BeTrue();
            var state = outlineCommand.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
            registry.TryGet(new RibbonCommandId("freew.paged-edit-view"), out var pageEditCommand).Should().BeTrue();
            pageEditCommand!.Execute(RibbonCommandContext.Empty);

            state.GetState().IsChecked.Should().BeFalse();
            outlineCommand.Execute(RibbonCommandContext.Empty);
            activeAfterEnter = window.IsOutlineModeActiveForTests;
            workspaceIsOutline = window.IsWorkspaceShowingOutline;
            state.GetState().IsChecked.Should().BeTrue();
            pageEditCommand.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject
                .GetState().IsChecked.Should().BeFalse();

            outlineCommand.Execute(RibbonCommandContext.Empty);
            activeAfterExit = window.IsOutlineModeActiveForTests;
            workspaceIsLive = window.IsWorkspaceShowingLiveEditor;
            pageEditRestoredAfterOutline = pageEditCommand
                .Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsChecked;

            outlineCommand.Execute(RibbonCommandContext.Empty);
            registry.TryGet(new RibbonCommandId("freew.draft-view"), out var draftCommand).Should().BeTrue();
            draftCommand!.Execute(RibbonCommandContext.Empty);
            modeAfterDraft = window.Editor.ViewMode;
            workspaceIsLive = workspaceIsLive && window.IsWorkspaceShowingLiveEditor;
        });

        activeAfterEnter.Should().BeTrue();
        workspaceIsOutline.Should().BeTrue();
        activeAfterExit.Should().BeFalse();
        workspaceIsLive.Should().BeTrue();
        modeAfterDraft.Should().Be(DocumentViewMode.Draft);
        pageEditRestoredAfterOutline.Should().BeTrue();
    }
}
