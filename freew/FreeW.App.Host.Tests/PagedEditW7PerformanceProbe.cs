using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using Xunit;
using Xunit.Abstractions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// On-demand timing probe for the paged editor on a long document (opt-in: set
/// <c>FREEW_PAGED_PERF_DOC</c> to a .docx path; otherwise the test returns immediately). Reports open
/// (read + build pages), per-keystroke latency, and the commit + re-pagination cost the debounce pays
/// after each typing burst.
/// </summary>
public sealed class PagedEditW7PerformanceProbe(ITestOutputHelper output)
{
    [StaFact]
    public void MeasurePagedEditOnLongDocument()
    {
        var path = Environment.GetEnvironmentVariable("FREEW_PAGED_PERF_DOC");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var clock = Stopwatch.StartNew();
        var doc = DocxReader.Read(path);
        var read = clock.Elapsed;

        clock.Restart();
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var continuousLoad = clock.Elapsed;

        clock.Restart();
        _ = PaginationEngine.ComputeBlockPageAssignment(editor);
        var assignment = clock.Elapsed;
        clock.Restart();
        _ = PaginationEngine.ComputeBlockPageAssignment(editor, out _, null, layoutOnly: true);
        output.WriteLine($"page assignment: print clone {assignment.TotalMilliseconds:F0} ms, layout-only direct render {clock.Elapsed.TotalMilliseconds:F0} ms");

        // W9: the incremental layout session -- first run, then one edit in the middle that adds lines and
        // one that only changes a word, each re-paginated from what changed.
        {
            var session = new PagedLayoutSession(editor);
            clock.Restart();
            _ = session.ComputeAssignment(out _);
            var sessionFirst = clock.Elapsed;
            var firstStats = session.LastStatistics;
            var blocks = editor.Model.Blocks;
            var middle = Enumerable.Range(blocks.Count / 2, blocks.Count / 2)
                .First(i => blocks[i] is FreeW.Core.Model.Paragraph { Runs.Count: > 0 });
            var grown = (FreeW.Core.Model.Paragraph)blocks[middle];
            var replacement = new FreeW.Core.Model.Paragraph { Formatting = grown.Formatting, StyleId = grown.StyleId };
            replacement.Runs.AddRange(grown.Runs);
            replacement.Runs.Add(new FreeW.Core.Model.Run(string.Concat(Enumerable.Repeat(" more words to wrap", 30))));
            blocks[middle] = replacement;
            clock.Restart();
            _ = session.ComputeAssignment(out _);
            var sessionGrow = clock.Elapsed;
            var growStats = session.LastStatistics;
            var word = (FreeW.Core.Model.Paragraph)blocks[middle];
            var wordEdit = new FreeW.Core.Model.Paragraph { Formatting = word.Formatting, StyleId = word.StyleId };
            wordEdit.Runs.AddRange(word.Runs);
            wordEdit.Runs.Add(new FreeW.Core.Model.Run("s"));
            blocks[middle] = wordEdit;
            clock.Restart();
            _ = session.ComputeAssignment(out _);
            var sessionWord = clock.Elapsed;
            var wordStats = session.LastStatistics;
            blocks[middle] = grown;
            output.WriteLine($"session first run: {sessionFirst.TotalMilliseconds:F0} ms ({firstStats})");
            output.WriteLine($"session re-flow after a growing edit mid-document: {sessionGrow.TotalMilliseconds:F0} ms ({growStats})");
            output.WriteLine($"session re-flow after a one-word edit: {sessionWord.TotalMilliseconds:F0} ms ({wordStats})");
        }

        clock.Restart();
        var scratch = new DocumentView();
        scratch.LoadModel(editor.Model);
        var scratchRender = clock.Elapsed;

        clock.Restart();
        var panel = PaginatedEditorPanel.Build(editor);
        var buildNoLayout = clock.Elapsed;
        clock.Restart();
        panel.Measure(new System.Windows.Size(1200, 900));
        panel.Arrange(new System.Windows.Rect(0, 0, 1200, 900));
        panel.UpdateLayout();
        var build = clock.Elapsed;

        // As in the app: the pages are the editing surface, so the hidden editor tracks its units.
        panel.AttachAsActiveSurface();
        editor.RefreshFromModel();

        var box = panel.PageBoxes[panel.PageBoxes.Count / 2];
        var keystrokes = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            clock.Restart();
            var end = box.Body.Document.Blocks.FirstBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            end.InsertTextInRun("x");
            box.Body.UpdateLayout();
            keystrokes.Add(clock.Elapsed.TotalMilliseconds);
        }

        clock.Restart();
        PaginatedCommitCoordinator.Commit(panel, editor);
        var commit = clock.Elapsed;

        clock.Restart();
        panel.Repaginate();
        panel.UpdateLayout();
        var repaginate = clock.Elapsed;
        output.WriteLine($"  re-flow detail: {panel.LayoutSession.LastStatistics}; kept boxes {panel.LastRebuildKeptBoxes}/{panel.PageBoxes.Count}; hidden incremental refreshes {editor.IncrementalBodyRefreshCount}");

        // A burst that adds several lines (the page's blocks move on).
        var growBox = panel.PageBoxes[panel.PageBoxes.Count / 2];
        growBox.Body.Document.Blocks.FirstBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
            .InsertTextInRun(string.Concat(Enumerable.Repeat(" more words to wrap", 30)));
        growBox.Body.UpdateLayout();
        clock.Restart();
        panel.Repaginate();
        panel.UpdateLayout();
        output.WriteLine($"re-pagination after a growing burst: {clock.Elapsed.TotalMilliseconds:F0} ms "
            + $"({panel.LayoutSession.LastStatistics}; kept boxes {panel.LastRebuildKeptBoxes}/{panel.PageBoxes.Count})");

        panel.CaptureContentBottoms();
        var target = panel.PageBoxes.First(b => b.Body.Document.Blocks.Count > 0);
        target.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)!.InsertTextInRun("y");
        target.Body.UpdateLayout();
        clock.Restart();
        panel.CommitAndRepaginateIfLayoutChanged();
        var inLineTick = clock.Elapsed;
        output.WriteLine($"debounce tick for an in-line edit (commit, no re-pagination): {inLineTick.TotalMilliseconds:F0} ms");

        output.WriteLine($"document: {Path.GetFileName(path)} blocks={editor.Model.Blocks.Count} pages={panel.PageBoxes.Count}");
        output.WriteLine($"read docx: {read.TotalMilliseconds:F0} ms; continuous LoadModel: {continuousLoad.TotalMilliseconds:F0} ms");
        output.WriteLine($"paged open (Build + layout): {buildNoLayout.TotalMilliseconds + build.TotalMilliseconds:F0} ms "
            + $"(Build {buildNoLayout.TotalMilliseconds:F0} ms, of which page assignment ~{assignment.TotalMilliseconds:F0} ms "
            + $"and block render ~{scratchRender.TotalMilliseconds:F0} ms; first layout {build.TotalMilliseconds:F0} ms)");
        output.WriteLine($"keystroke: median {Median(keystrokes):F1} ms, max {keystrokes.Max():F1} ms (20 keystrokes)");
        output.WriteLine($"burst commit (bus + hidden render): {commit.TotalMilliseconds:F0} ms");
        output.WriteLine($"re-pagination after burst: {repaginate.TotalMilliseconds:F0} ms");
        panel.PageBoxes.Should().NotBeEmpty();
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
