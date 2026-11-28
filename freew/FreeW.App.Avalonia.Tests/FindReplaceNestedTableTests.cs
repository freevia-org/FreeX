using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Find Next / single Replace reach text in a table nested in a table cell through the shared body planner
/// (FindNextNested / PlanReplaceNested), walking it like the WPF host does: the body from the caret, the
/// nested tables, the other stories, then the body again from its top. This surface does not lay a nested
/// table out, so the match is reported in the status line and the cell that holds the table is shaded.
/// </summary>
public sealed class FindReplaceNestedTableTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView Load(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var block in blocks)
            document.Blocks.Add(block);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static Table TableOf(params string[] cellParagraphs)
    {
        var cell = new TableCell();
        foreach (var text in cellParagraphs)
            cell.Paragraphs.Add(new Paragraph(text));
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        return table;
    }

    private static Table OuterWithNested(string outerText, params string[] nestedParagraphs)
    {
        var outer = TableOf(outerText);
        outer.Rows[0].Cells[0].NestedTables.Add(TableOf(nestedParagraphs));
        return outer;
    }

    private static string NestedText(DocumentView view, int blockIndex, int paragraph = 0) =>
        ((Table)view.Document.Blocks[blockIndex]).Rows[0].Cells[0].NestedTables[0].Rows[0].Cells[0]
            .Paragraphs[paragraph].PlainText;

    private static FindReplaceReplaceRequest Replace(string find, string with) =>
        new(find, with, new FindReplaceSearchOptions());

    [Fact]
    public async Task FindNext_ReachesTextInANestedTable_AndTheHostSaysWhereItIs()
    {
        var found = false;
        string? status = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(OuterWithNested("outer text", "nested cat here"));
            var host = new AvaloniaFindReplaceCommandHost(view);

            found = view.FindNext("cat");
            status = host.LastMatchStatus;
        });
        ran.Should().BeTrue();

        found.Should().BeTrue("Find Next reaches a nested table through the model");
        status.Should().Contain("nested").And.Contain("cat");
    }

    [Fact]
    public async Task FindNext_WalksTheBodyThenTheNestedTable_ThenWrapsToTheBody()
    {
        string? bodyStatus = null;
        string? nestedStatus = null;
        string? wrappedStatus = null;
        string? firstSelection = null;
        string? wrappedSelection = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(new Paragraph("body cat"), OuterWithNested("outer text", "nested cat"));
            var host = new AvaloniaFindReplaceCommandHost(view);

            view.FindNext("cat");
            firstSelection = view.SelectedText;
            bodyStatus = host.LastMatchStatus;

            view.FindNext("cat");
            nestedStatus = host.LastMatchStatus;

            view.FindNext("cat");
            wrappedSelection = view.SelectedText;
            wrappedStatus = host.LastMatchStatus;
        });
        ran.Should().BeTrue();

        firstSelection.Should().Be("cat", "the body match is selected on the surface");
        bodyStatus.Should().BeNull();
        nestedStatus.Should().Contain("nested");
        wrappedSelection.Should().Be("cat", "the walk wrapped to the body match");
        wrappedStatus.Should().BeNull();
    }

    [Fact]
    public async Task FindNext_FromTheLastBodyMatch_StepsThroughEveryNestedMatchBeforeWrapping()
    {
        var contexts = new System.Collections.Generic.List<string>();
        var ran = await OnUiThread(() =>
        {
            var view = Load(OuterWithNested("outer text", "cat one", "cat two"));
            var host = new AvaloniaFindReplaceCommandHost(view);
            for (var step = 0; step < 3; step++)
            {
                view.FindNext("cat");
                contexts.Add(host.LastMatchStatus ?? string.Empty);
            }
        });
        ran.Should().BeTrue();

        contexts[0].Should().Contain("cat one");
        contexts[1].Should().Contain("cat two");
        contexts[2].Should().Contain("cat one", "with no body match the walk wraps to the first nested match");
    }

    [Fact]
    public async Task FindNext_ReachesTheNestedTable_BeforeTheDefaultHeaderFooter()
    {
        string? status = null;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            document.Blocks.Add(OuterWithNested("outer text", "nested cat"));
            document.Header = new HeaderFooter();
            document.Header.Paragraphs.Add(new Paragraph("header cat"));
            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));
            var host = new AvaloniaFindReplaceCommandHost(view);

            view.FindNext("cat");
            status = host.LastMatchStatus;
        });
        ran.Should().BeTrue();

        status.Should().Contain("nested", "the nested tables come before the other stories, as in the WPF walk");
    }

    [Fact]
    public async Task ReplaceNext_InANestedTable_MakesReplaceAllsEdit_AndStepsOnToTheNextMatch()
    {
        string first = string.Empty;
        string second = string.Empty;
        string last = string.Empty;
        var ran = await OnUiThread(() =>
        {
            var view = Load(OuterWithNested("outer text", "cat and cat", "last cat"));

            view.FindNext("cat");
            view.ReplaceNext(Replace("cat", "tiger"));
            first = NestedText(view, 0);

            view.ReplaceNext(Replace("cat", "tiger"));
            view.ReplaceNext(Replace("cat", "tiger"));
            second = NestedText(view, 0);
            last = NestedText(view, 0, 1);
        });
        ran.Should().BeTrue();

        first.Should().Be("tiger and cat", "a single Replace changes only the match it is on");
        second.Should().Be("tiger and tiger");
        last.Should().Be("last tiger");
    }

    [Fact]
    public async Task ReplaceNext_InANestedTable_IsOneUndoStep()
    {
        string afterReplace = string.Empty;
        string afterUndo = string.Empty;
        var ran = await OnUiThread(() =>
        {
            var view = Load(OuterWithNested("outer text", "see cat here"));

            view.FindNext("cat");
            view.ReplaceNext(Replace("cat", "tiger"));
            afterReplace = NestedText(view, 0);

            view.Undo();
            afterUndo = NestedText(view, 0);
        });
        ran.Should().BeTrue();

        afterReplace.Should().Be("see tiger here");
        afterUndo.Should().Be("see cat here");
    }

    [Fact]
    public async Task FindNext_InANestedTable_ShadesAndScrollsToTheCellHoldingIt_UntilTheCaretMoves()
    {
        Rect? cue = null;
        double scrollTop = -1;
        Rect? cellRect = null;
        Rect? afterMove = null;
        var scrollRequests = 0;
        var ran = await OnUiThread(() =>
        {
            var view = Load(new Paragraph("intro"), OuterWithNested("outer text", "nested cat"));
            view.ScrollToCaretRequested += () => scrollRequests++;

            view.FindNext("cat");
            cue = view.FindNestedCueRect();
            scrollTop = view.ScrollTargetTop;
            cellRect = cue;

            view.PlaceCaretInCell(1, 0, 0, 0, 0);
            afterMove = view.FindNestedCueRect();
        });
        ran.Should().BeTrue();

        cue.Should().NotBeNull("the cell that holds the nested table is shaded");
        cue!.Value.Height.Should().BeGreaterThan(0);
        scrollTop.Should().Be(cellRect!.Value.Y, "the view is scrolled to the cell, not to the caret");
        scrollRequests.Should().BeGreaterThan(0);
        afterMove.Should().BeNull("a caret move ends the cue");
    }

    [Fact]
    public async Task FindNext_InANestedTable_CueEndsOnAnEdit()
    {
        Rect? before = null;
        Rect? after = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(new Paragraph("intro"), OuterWithNested("outer text", "nested cat"));
            view.FindNext("cat");
            before = view.FindNestedCueRect();

            view.ReplaceAll(Replace("outer", "inner"));
            after = view.FindNestedCueRect();
        });
        ran.Should().BeTrue();

        before.Should().NotBeNull();
        after.Should().BeNull("an edit ends the cue");
    }

    [Fact]
    public async Task DialogSession_ReportsTheNestedTableInItsStatusLine()
    {
        string status = string.Empty;
        var ran = await OnUiThread(() =>
        {
            var view = Load(OuterWithNested("outer text", "nested cat"));
            var session = new FindReplaceDialogSession(new AvaloniaFindReplaceCommandHost(view));
            session.SetInput(new FindReplaceDialogInput("cat", string.Empty, false, false, false));
            status = session.FindNext().StatusText;
        });
        ran.Should().BeTrue();

        status.Should().Contain("nested").And.Contain("cat");
    }
}
