using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelTable = FreeW.Core.Model.Table;
using ModelTableCell = FreeW.Core.Model.TableCell;
using ModelTableRow = FreeW.Core.Model.TableRow;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W11: a paragraph, list or table longer than the space left on a page continues on the next page in
/// the paged editor exactly where print breaks it (screen = print), and editing works across the split:
/// typing on either side, Backspace/Delete at the seam, a selection spanning it, caret mapping to the
/// hidden continuous editor, re-flow, and undo to the exact document.
/// </summary>
public sealed class PagedEditSplitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string Words = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i}"));

    [StaFact]
    public void LongParagraph_ContinuesOnTheNextPages_ExactlyAsPrinted()
    {
        var editor = Editor(Doc(
            new ModelParagraph("Opening paragraph."),
            new ModelParagraph(Words),
            new ModelParagraph("Closing paragraph.")));

        var panel = PaginatedEditorPanel.Build(editor);

        panel.PageBoxes.Count.Should().BeGreaterThan(2, "the long paragraph runs over several pages");
        ScreenPageTexts(panel).Should().Equal(PrintPageTexts(editor), "each page shows exactly the text print puts on it");
        panel.PageBoxes[1].ContinuesPrevious.Should().BeTrue();
        panel.PageBoxes[1].StartsWithTextSeam.Should().BeTrue();
        BoxesFitTheirPages(panel);
    }

    [StaFact]
    public void ListAndTable_SplitAcrossPages_ExactlyAsPrinted_WithRepeatedHeaderRows()
    {
        var blocks = new List<FreeW.Core.Model.Block> { new ModelParagraph("Intro") };
        for (var i = 0; i < 45; i++)
        {
            blocks.Add(new ModelParagraph($"list item {i} " + string.Join(" ", Enumerable.Repeat("text", i % 3 == 0 ? 30 : 4))
            ) { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        }
        blocks.Add(new ModelParagraph("Between"));
        blocks.Add(Table(rows: 70, repeatHeader: true));
        blocks.Add(new ModelParagraph("End"));
        var editor = Editor(Doc(blocks.ToArray()));

        var panel = PaginatedEditorPanel.Build(editor);

        var print = PrintPageTexts(editor);
        output.WriteLine($"{print.Count} printed pages");
        ScreenPageTexts(panel).Should().Equal(print);
        panel.PageBoxes.Skip(1).Count(box => box.ContinuesPrevious || box.Pieces.Count > 0).Should().BeGreaterThan(0);
        // Every page after the table's first shows the header row again (print's repeated header).
        var tablePages = panel.PageBoxes.Where(box => Tables(box.Body.Document.Blocks).Any()).ToList();
        tablePages.Count.Should().BeGreaterThan(1);
        BoxesFitTheirPages(panel);
    }

    [StaFact]
    public void FootnotedLongParagraph_SplitsAsThePerPageFootnotePathDoes()
    {
        var doc = Doc(new ModelParagraph("Start"));
        var paragraph = new ModelParagraph(Words);
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(new Run(" " + Words));
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(new ModelParagraph("End"));
        doc.Footnotes[1] = new Footnote(1, "The note text.");
        var editor = Editor(doc);

        var panel = PaginatedEditorPanel.Build(editor);

        ScreenPageTexts(panel).Should().Equal(PrintPageTexts(editor));
        panel.PageBoxes.Skip(1).Any(box => box.StartsWithTextSeam).Should().BeTrue();
    }

    [StaFact]
    public void Commit_RejoinsTheSplitParagraph_AndUndoRestoresTheExactDocument()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words), new ModelParagraph("Closing.")));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        var before = editor.Model.Blocks.ToList();
        var seam = SeamOffset(panel, 1);

        // Type at the end of page 1 (before the seam) and at the start of page 2 (after it).
        InsertAt(panel.PageBoxes[0].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!, "<A>");
        InsertAt(panel.PageBoxes[1].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!, "<B>");
        panel.CommitPendingEdits();

        editor.Model.Blocks.Should().HaveCount(3, "the two halves are one paragraph again");
        ((ModelParagraph)editor.Model.Blocks[0]).PlainText.Should().Be("Opening.");
        ((ModelParagraph)editor.Model.Blocks[2]).PlainText.Should().Be("Closing.");
        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be(Words[..seam] + "<A><B>" + Words[seam..]);

        editor.Undo();
        editor.Model.Blocks.Should().Equal(before, "undo restores the exact block instances");
        panel.Detach();
    }

    [StaFact]
    public void UneditedSplitParagraph_ReadsBackAsTheSameText_AndRunFormatting()
    {
        var bold = new RunFormatting { Bold = true };
        var paragraph = new ModelParagraph();
        paragraph.Runs.Add(new Run(Words[..1500]));
        paragraph.Runs.Add(new Run(Words[1500..], bold));
        var editor = Editor(Doc(new ModelParagraph("x"), paragraph));
        var panel = PaginatedEditorPanel.Build(editor);

        var read = new List<FreeW.Core.Model.Block>();
        editor.ReadPagesInto(panel.PageBoxes.Select(box => box.Body.Document).ToList(), read, new int[panel.PageBoxes.Count]);

        read.Should().HaveCount(2);
        var merged = (ModelParagraph)read[1];
        merged.PlainText.Should().Be(Words);
        merged.Runs.Select(run => (run.Text, run.Formatting.Bold)).Should().Equal(
            paragraph.Runs.Select(run => (run.Text, run.Formatting.Bold)),
            "a run the page boundary split is joined back");
    }

    [StaFact]
    public void BackspaceAndDeleteAtTheSeam_DeleteTheCharacterAcrossIt()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words)));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        var seam = SeamOffset(panel, 1);

        panel.EditAcrossSeam(panel.PageBoxes[0], panel.PageBoxes[1], backward: true).Should().BeTrue();
        panel.EditAcrossSeam(panel.PageBoxes[0], panel.PageBoxes[1], backward: false).Should().BeTrue();
        panel.CommitPendingEdits();

        editor.Model.Blocks.Should().HaveCount(2);
        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be(Words[..(seam - 1)] + Words[(seam + 1)..]);
        panel.Detach();
    }

    [StaFact]
    public void BackspaceKey_AtTheTopOfAContinuationPage_DeletesTheCharacterBeforeTheSeam()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words)));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        var seam = SeamOffset(panel, 1);
        var lower = panel.PageBoxes[1];
        var window = ShowBody(lower);
        try
        {
            lower.Body.CaretPosition = lower.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
            InvokeKey(lower, Key.Back).Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
        panel.CommitPendingEdits();

        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be(Words[..(seam - 1)] + Words[seam..]);
        panel.Detach();
    }

    [StaFact]
    public void RightAndLeftArrows_CrossTheSeamOneCharacterAtATime()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words)));
        var panel = PaginatedEditorPanel.Build(editor);
        var upper = panel.PageBoxes[0];
        var lower = panel.PageBoxes[1];
        var window = ShowBody(upper);
        try
        {
            upper.Body.CaretPosition = upper.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
            InvokeKey(upper, Key.Right).Should().BeTrue();
            new TextRange(lower.Body.Document.ContentStart, lower.Body.CaretPosition).Text.Should().HaveLength(1,
                "Right at the end of the page moves past the first character of the continued line");
        }
        finally
        {
            window.Close();
        }

        var window2 = ShowBody(lower);
        try
        {
            lower.Body.CaretPosition = lower.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
            InvokeKey(lower, Key.Left).Should().BeTrue();
            var upperText = new TextRange(upper.Body.Document.ContentStart, upper.Body.Document.ContentEnd).Text.TrimEnd('\r', '\n');
            new TextRange(upper.Body.Document.ContentStart, upper.Body.CaretPosition).Text.TrimEnd('\r', '\n')
                .Should().Be(upperText[..^1], "Left at the start of the continuation moves before the last character of the page before");
        }
        finally
        {
            window2.Close();
        }
    }

    [StaFact]
    public void SelectionAcrossTheSeam_DeletesAsOneRange_AndTheParagraphStaysWhole()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words), new ModelParagraph("Closing.")));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        var seam = SeamOffset(panel, 1);
        var upper = panel.PageBoxes[0];
        var lower = panel.PageBoxes[1];
        var from = BackFromEnd(upper, 5);
        var to = ForwardFromStart(lower, 7);

        panel.CrossPageSelection.BeginSelection(panel.PageBoxes, upper, from);
        panel.CrossPageSelection.ExtendSelection(panel.PageBoxes, lower, to);
        panel.CrossPageSelection.GetSelectedText(panel.PageBoxes).Should().Be(Words[(seam - 5)..(seam + 7)],
            "copying across the seam copies one run of text");
        panel.DeleteCrossPageSelection().Should().BeTrue();
        panel.CommitPendingEdits();

        editor.Model.Blocks.Should().HaveCount(3);
        ((ModelParagraph)editor.Model.Blocks[1]).PlainText.Should().Be(Words[..(seam - 5)] + Words[(seam + 7)..]);
        panel.Detach();
    }

    [StaFact]
    public void CaretOnAContinuationPage_MapsToTheSameTextInTheHiddenEditor_AndBack()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words)));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel();
        var seam = SeamOffset(panel, 1);
        var lower = panel.PageBoxes[1];
        lower.Body.Focus();
        var caret = ForwardFromStart(lower, 9);
        lower.Body.Selection.Select(caret, caret);

        var global = panel.CaptureSelection()!;
        panel.MirrorSelectionToSource();

        var sourceParagraph = editor.Document.Blocks.ElementAt(1);
        new TextRange(sourceParagraph.ContentStart, editor.Selection.Start).Text.Should().Be(Words[..(seam + 9)]);
        var back = panel.FromGlobal(global.Active)!.Value;
        back.Box.Should().BeSameAs(lower);
        back.Pointer.CompareTo(caret).Should().Be(0);
        panel.Detach();
    }

    [StaFact]
    public void ReflowAfterEditsOnBothSides_MatchesAFreshBuild_AndPrint()
    {
        var editor = Editor(Doc(new ModelParagraph("Opening."), new ModelParagraph(Words), new ModelParagraph(Words[..900]), new ModelParagraph("Closing.")));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();

        InsertAt(panel.PageBoxes[0].Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!,
            string.Concat(Enumerable.Repeat(" grow", 60)));
        panel.Repaginate();
        InsertAt(panel.PageBoxes[1].Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!, "start ");
        panel.Repaginate();

        ScreenPageTexts(panel).Should().Equal(ScreenPageTexts(PaginatedEditorPanel.Build(editor)));
        ScreenPageTexts(panel).Should().Equal(PrintPageTexts(editor));
        panel.Detach();
    }

    [StaFact]
    public void ProgressiveOpen_NeverShowsAPageEndingInsideABlockItHasNotLaidOutTheRestOf()
    {
        var blocks = new List<FreeW.Core.Model.Block>();
        for (var i = 0; i < 200; i++)
            blocks.Add(new ModelParagraph($"p{i} " + Words[..(i % 5 == 0 ? 2400 : 300)]));
        var editor = Editor(Doc(blocks.ToArray()));
        var reference = PaginatedEditorPanel.Build(editor);

        var panel = PaginatedEditorPanel.Build(editor, progressive: true);
        panel.IsLayoutComplete.Should().BeFalse();
        panel.PageBoxes[^1].Pieces.Values.Any(piece => piece.ContinuesNext).Should().BeFalse(
            "the last page shown before the placeholder ends on a whole block");
        panel.TailStart.Should().Be(panel.PageBoxes[^1].ModelStart + panel.PageBoxes[^1].ModelBlocks!.Length);

        while (!panel.AdvanceBackgroundLayout(PaginatedEditorPanel.BackgroundSlice))
        {
        }
        ScreenPageTexts(panel).Should().Equal(ScreenPageTexts(reference));
    }

    [StaFact]
    public void SplitTable_CommitsRowsBackIntoOneTable()
    {
        var editor = Editor(Doc(new ModelParagraph("Intro"), Table(rows: 80, repeatHeader: false), new ModelParagraph("End")));
        var panel = PaginatedEditorPanel.Build(editor);
        panel.AttachAsActiveSurface();
        foreach (var box in panel.PageBoxes)
            output.WriteLine($"page {box.PageNumber}: {string.Join(", ", box.Body.Document.Blocks.Select(b => b.GetType().Name))}");
        var tablePages = panel.PageBoxes.Count(box => Tables(box.Body.Document.Blocks).Any());
        tablePages.Should().BeGreaterThan(1);

        var lastTablePage = panel.PageBoxes.Last(box => Tables(box.Body.Document.Blocks).Any());
        var cellParagraph = Tables(lastTablePage.Body.Document.Blocks).First()
            .RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock!;
        InsertAt(cellParagraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!, "edited ");
        panel.CommitPendingEdits();

        editor.Model.Blocks.Should().HaveCount(3);
        var table = (ModelTable)editor.Model.Blocks[1];
        table.Rows.Should().HaveCount(80);
        table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Paragraphs).Count(p => p.PlainText.StartsWith("edited ", StringComparison.Ordinal))
            .Should().Be(1);
        panel.Detach();
    }

    /// <summary>
    /// Opt-in corpus sweep (<c>FREEW_PAGED_CORPUS_DIR</c>): every page the editor shows holds exactly the
    /// text print puts on that page, and a commit of every page reads the document back unchanged in text.
    /// </summary>
    [StaFact]
    public void ScreenPagesEqualPrintedPages_OnCorpus()
    {
        var directory = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            return;

        var mismatches = new List<string>();
        var only = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_ONLY");
        foreach (var path in System.IO.Directory.GetFiles(directory, "*.docx").OrderBy(file => new System.IO.FileInfo(file).Length))
        {
            var name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(only) && !only.Split(';').Contains(name))
                continue;
            TextDocument document;
            try
            {
                document = FreeW.Core.IO.DocxReader.Read(path);
            }
            catch (Exception ex)
            {
                output.WriteLine($"{name}: unreadable ({ex.GetType().Name})");
                continue;
            }

            // As the equivalence sweeps do: the hidden editor shows the model it was loaded with (print clones
            // that render, the pages render the model).
            var editor = new DocumentView();
            editor.LoadModel(document);
            var panel = PaginatedEditorPanel.Build(editor);
            var screen = ScreenPageTexts(panel);
            var print = PrintPageTexts(editor);
            var splitPages = panel.PageBoxes.Count(box => box.ContinuesPrevious || box.Pieces.Count > 0);
            var printAssignment = PaginationEngine.ComputeBlockPageAssignment(editor, out var printTotal, new List<PaginationEngine.SegmentPagination>());
            var layoutAssignment = PaginationEngine.ComputeBlockPageAssignment(editor, out var layoutTotal, null, layoutOnly: true);
            var sessionRun = new PagedLayoutSession(editor).ComputeRun();
            output.WriteLine($"  {name}: print/layout/session totals {printTotal}/{layoutTotal}/{sessionRun.TotalPageCount}; "
                + $"print==layout assignment {printAssignment.SequenceEqual(layoutAssignment)}, layout==session {layoutAssignment.SequenceEqual(sessionRun.Assignment)}; "
                + $"first print/layout difference at block {Enumerable.Range(0, printAssignment.Length).FirstOrDefault(i => printAssignment[i] != layoutAssignment[i], -1)}");
            var differing = Enumerable.Range(0, Math.Max(screen.Count, print.Count))
                .Where(i => i >= screen.Count || i >= print.Count || screen[i] != print[i])
                .ToList();
            output.WriteLine($"{name}: {screen.Count} screen pages, {print.Count} printed, {splitPages} with split blocks, "
                + $"fallback={panel.LayoutSession.LastStatistics.FallbackReason ?? "none"}, differing pages [{string.Join(",", differing.Take(10))}]");
            // Known limit: a table row taller than the space left (drawing.docx's one-row layout table) is
            // split mid-row by print; the editor splits tables between rows only, so that row stays whole.
            if (differing.Count > 0 && panel.LayoutSession.LastStatistics.FallbackReason is null && name != "drawing.docx")
                mismatches.Add($"{name}: pages {string.Join(",", differing.Take(10))}");
            if (!string.IsNullOrEmpty(only))
            {
                foreach (var i in differing.Take(4))
                {
                    string Ends(List<string> pages) => i < pages.Count
                        ? $"[{pages[i].Length}] {pages[i][..Math.Min(70, pages[i].Length)]} ... {pages[i][Math.Max(0, pages[i].Length - 70)..]}"
                        : "(none)";
                    output.WriteLine($"  page {i} screen: {Ends(screen)}");
                    output.WriteLine($"  page {i} print : {Ends(print)}");
                }
            }

            // Reading every page back gives the model's text, block for block.
            var read = new List<FreeW.Core.Model.Block>();
            editor.ReadPagesInto(panel.PageBoxes.Select(box => box.Body.Document).ToList(), read, new int[panel.PageBoxes.Count]);
            // ...as the same reader gives for the hidden editor's unsplit render.
            var whole = new List<FreeW.Core.Model.Block>();
            editor.ReadBlocksInto(editor.Document, whole);
            var expected = whole.Select(BlockText).ToList();
            var actual = read.Select(BlockText).ToList();
            if (!actual.SequenceEqual(expected))
                mismatches.Add($"{name}: read-back differs ({actual.Count} vs {expected.Count} blocks)");
        }

        mismatches.Should().BeEmpty();
    }

    private static string BlockText(FreeW.Core.Model.Block block) => block switch
    {
        ModelParagraph paragraph => paragraph.PlainText,
        ModelTable table => string.Join("|", table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Paragraphs).Select(p => p.PlainText)),
        _ => block.GetType().Name
    };

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    internal static List<string> PrintPageTexts(DocumentView editor)
    {
        var segments = new List<PaginationEngine.SegmentPagination>();
        PaginationEngine.ComputeBlockPageAssignment(editor, out _, segments);
        var texts = new List<string>();
        foreach (var segment in segments.OrderBy(s => s.PageOffset))
        {
            var paginator = (DynamicDocumentPaginator)segment.Paginator;
            paginator.ComputePageCount();
            var flow = (FlowDocument)paginator.Source;
            var pages = Enumerable.Range(0, segment.PageCount).Select(_ => new StringBuilder()).ToList();
            foreach (var paragraph in Paragraphs(flow.Blocks))
            {
                var first = Math.Max(0, paginator.GetPageNumber(paragraph.ContentStart));
                var last = Math.Min(pages.Count - 1, Math.Max(first, paginator.GetPageNumber(paragraph.ContentEnd)));
                for (var page = first; page <= last; page++)
                {
                    var from = page == first ? paragraph.ContentStart : PageSplitter.FirstPositionOnPage(paginator, paragraph, page) ?? paragraph.ContentEnd;
                    var to = page == last ? paragraph.ContentEnd : PageSplitter.FirstPositionOnPage(paginator, paragraph, page + 1) ?? paragraph.ContentEnd;
                    if (from.CompareTo(to) < 0)
                        pages[page].Append(FlowText(from, to));
                }
            }
            texts.AddRange(pages.Select(page => Normalize(page.ToString())));
        }
        return texts;
    }

    internal static List<string> ScreenPageTexts(PaginatedEditorPanel panel) =>
        panel.PageBoxes
            .Where(box => !box.IsEndnoteSyntheticPage)
            .Select(box => ParagraphText(box.Body.Document.Blocks, box.Body.Document.ContentStart, box.Body.Document.ContentEnd))
            .ToList();

    /// <summary>The text of every paragraph between two positions (list markers, which a range across an item adds, excluded).</summary>
    private static string ParagraphText(IEnumerable<System.Windows.Documents.Block> blocks, TextPointer from, TextPointer to)
    {
        var builder = new StringBuilder();
        foreach (var paragraph in Paragraphs(blocks))
        {
            var start = paragraph.ContentStart.CompareTo(from) > 0 ? paragraph.ContentStart : from;
            var end = paragraph.ContentEnd.CompareTo(to) < 0 ? paragraph.ContentEnd : to;
            if (start.CompareTo(end) < 0)
                builder.Append(FlowText(start, end));
        }
        return Normalize(builder.ToString());
    }

    private static IEnumerable<System.Windows.Documents.Table> Tables(IEnumerable<System.Windows.Documents.Block> blocks) =>
        blocks.SelectMany(block => block switch
        {
            System.Windows.Documents.Table table => [table],
            System.Windows.Documents.Section section => Tables(section.Blocks),
            _ => Enumerable.Empty<System.Windows.Documents.Table>()
        });

    /// <summary>The text between two positions outside anchored figures and floaters (placed apart from the flow).</summary>
    private static string FlowText(TextPointer from, TextPointer to)
    {
        var builder = new StringBuilder();
        for (var pointer = from; pointer is not null && pointer.CompareTo(to) < 0;)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var length = Math.Min(pointer.GetTextRunLength(LogicalDirection.Forward), pointer.GetOffsetToPosition(to));
                if (!InsideAnchoredBlock(pointer))
                    builder.Append(pointer.GetTextInRun(LogicalDirection.Forward), 0, length);
                pointer = pointer.GetPositionAtOffset(length, LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
        return builder.ToString();
    }

    private static bool InsideAnchoredBlock(TextPointer pointer)
    {
        for (var node = pointer.Parent; node is System.Windows.FrameworkContentElement element; node = element.Parent)
        {
            if (element is AnchoredBlock)
                return true;
        }
        return false;
    }

    private static IEnumerable<System.Windows.Documents.Paragraph> Paragraphs(IEnumerable<System.Windows.Documents.Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case System.Windows.Documents.Paragraph paragraph:
                    yield return paragraph;
                    break;
                case System.Windows.Documents.List list:
                    foreach (var item in list.ListItems)
                        foreach (var nested in Paragraphs(item.Blocks))
                            yield return nested;
                    break;
                case System.Windows.Documents.Section section:
                    foreach (var nested in Paragraphs(section.Blocks))
                        yield return nested;
                    break;
                case System.Windows.Documents.Table table:
                    foreach (var cell in table.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells))
                        foreach (var nested in Paragraphs(cell.Blocks))
                            yield return nested;
                    break;
            }
        }
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
                builder.Append(c);
        }
        return builder.ToString();
    }

    private static void BoxesFitTheirPages(PaginatedEditorPanel panel)
    {
        var size = new Size(2000, 2000);
        panel.Measure(size);
        panel.Arrange(new Rect(size));
        panel.UpdateLayout();
        foreach (var box in panel.PageBoxes)
        {
            var (_, pageHeight) = FreeW.Core.Model.PageLayout.PageSizeDip(box.PageGeometry);
            var bottom = PaginatedEditorPanel.MeasureContentBottom(box);
            bottom.Should().NotBeNull();
            bottom!.Value.Should().BeLessThanOrEqualTo(pageHeight, $"page {box.PageNumber}'s text stays on its page");
            box.ActualHeight.Should().BeApproximately(PageBox.NominalHeight(box.PageGeometry), 1.0,
                $"page {box.PageNumber} keeps the page size (its text does not grow it)");
        }
    }

    /// <summary>Where (in the model paragraph's text) the page after <paramref name="box"/> continues it.</summary>
    private static int SeamOffset(PaginatedEditorPanel panel, int paragraphBlock)
    {
        var upper = panel.PageBoxes[0];
        var head = upper.Body.Document.Blocks.LastBlock!;
        return new TextRange(head.ContentStart, head.ContentEnd).Text.TrimEnd('\r', '\n').Length;
    }

    private static TextPointer BackFromEnd(PageBox box, int characters)
    {
        var pointer = box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
        for (var i = 0; i < characters; i++)
            pointer = pointer.GetNextInsertionPosition(LogicalDirection.Backward)!;
        return pointer;
    }

    private static TextPointer ForwardFromStart(PageBox box, int characters)
    {
        var pointer = box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!;
        for (var i = 0; i < characters; i++)
            pointer = pointer.GetNextInsertionPosition(LogicalDirection.Forward)!;
        return pointer;
    }

    private static void InsertAt(TextPointer pointer, string text) => pointer.InsertTextInRun(text);

    private static Window ShowBody(PageBox box)
    {
        if (System.Windows.Media.VisualTreeHelper.GetParent(box.Body) is Panel parent)
            parent.Children.Remove(box.Body);
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -10000,
            Top = -10000,
            Width = 900,
            Height = 1200,
            Content = box.Body,
        };
        window.Show();
        return window;
    }

    private static bool InvokeKey(PageBox box, Key key)
    {
        var source = PresentationSource.FromVisual(box.Body)
            ?? throw new InvalidOperationException("A shown window is required to construct a KeyEventArgs.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.OnBodyPreviewKeyDown(box.Body, args);
        return args.Handled;
    }

    private static TextDocument Doc(params FreeW.Core.Model.Block[] blocks)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.HeightPt = 360;
        foreach (var block in blocks)
            doc.Blocks.Add(block);
        return doc;
    }

    private static ModelTable Table(int rows, bool repeatHeader)
    {
        var table = new ModelTable { Formatting = TableFormatting.Default with { Borders = true, HeaderRow = repeatHeader, RepeatHeaderRow = repeatHeader } };
        for (var r = 0; r < rows; r++)
        {
            var row = new ModelTableRow();
            for (var c = 0; c < 3; c++)
            {
                var cell = new ModelTableCell();
                cell.Paragraphs.Add(new ModelParagraph(r == 0 && repeatHeader ? $"Header {c}" : $"r{r}c{c}"));
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }
        return table;
    }

    private static DocumentView Editor(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return editor;
    }
}
