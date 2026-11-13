using System.IO;
using System.Linq;
using System.Windows.Markup;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace FreeW.App.Host.Tests;

/// <summary>
/// W9 (paged-edit performance): the range render (<see cref="DocumentView.RenderBodyUnitRange"/>) assembles
/// to exactly the full render, and the incremental layout session (<see cref="PagedLayoutSession"/>)
/// produces exactly the full page assignment -- on first use and after every edit, while reusing what the
/// edit did not touch.
/// </summary>
public sealed class PagedLayoutSessionTests(ITestOutputHelper output)
{
    [StaTheory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void RangeRender_AssembledInPieces_MatchesFullRender(int seed)
    {
        var document = BuildMixedDocument(new Random(seed), footnotes: true, restartOverrides: true);
        var editor = new DocumentView();
        editor.LoadModel(document);

        var full = editor.CreateLayoutRenderTwin(renderBody: true)!.Document.Blocks.ToList();

        var twin = editor.CreateLayoutRenderTwin(renderBody: false)!;
        var boundaries = twin.RenderBodyUnitRange(0, document.Blocks.Count);
        var units = new List<DocumentView.BodyRenderUnit>();
        // Render in uneven pieces, every piece on a unit boundary, as the cache does.
        var pieceSizes = new[] { 1, 3, 2, 5 };
        var u = 0;
        var piece = 0;
        while (u < boundaries.Count)
        {
            var count = Math.Min(pieceSizes[piece++ % pieceSizes.Length], boundaries.Count - u);
            units.AddRange(twin.RenderBodyUnitRange(boundaries[u].Start, boundaries[u + count - 1].End));
            u += count;
        }

        var natural = units.ToDictionary(unit => unit, unit => unit.Blocks[0].Margin);
        DocumentView.ApplyContextualSpacingAcrossUnits(document, units, unit => natural[unit]);
        var assembled = units.SelectMany(unit => unit.Blocks).ToList();

        assembled.Should().HaveCount(full.Count);
        for (var i = 0; i < full.Count; i++)
            Xaml(assembled[i]).Should().Be(Xaml(full[i]), $"block {i}");
    }

    /// <summary>
    /// Opt-in (<c>FREEW_PAGED_CORPUS_DIR</c> + <c>FREEW_PAGED_CORPUS_ONLY</c>): the layout twin's range render
    /// of a corpus document, unit by unit, equals its whole render block for block.
    /// </summary>
    [StaFact]
    public void RangeRender_OnCorpusDocuments_MatchesFullRender()
    {
        var directory = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_DIR");
        var only = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_ONLY");
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(only))
            return;

        foreach (var name in only.Split(';'))
        {
            var document = DocxReader.Read(Path.Combine(directory, name));
            var editor = new DocumentView();
            editor.LoadModel(document);
            var fullFlow = editor.CreateLayoutRenderTwin(renderBody: true)!.Document;
            var full = fullFlow.Blocks.ToList();
            var twin = editor.CreateLayoutRenderTwin(renderBody: false)!;
            var units = twin.RenderBodyUnitRange(0, document.Blocks.Count).ToList();
            var natural = units.ToDictionary(unit => unit, unit => unit.Blocks[0].Margin);
            DocumentView.ApplyContextualSpacingAcrossUnits(document, units, unit => natural[unit]);
            var assembled = units.SelectMany(unit => unit.Blocks).ToList();
            // Inherited properties as the layout flow gives them.
            var assembledFlow = new System.Windows.Documents.FlowDocument { FontFamily = fullFlow.FontFamily, FontSize = fullFlow.FontSize };
            System.Windows.Documents.Typography.SetKerning(assembledFlow, System.Windows.Documents.Typography.GetKerning(fullFlow));
            foreach (var block in assembled)
                assembledFlow.Blocks.Add(block);
            assembled.Should().HaveCount(full.Count, name);
            for (var i = 0; i < full.Count; i++)
            {
                var a = Describe(assembled[i]);
                var b = Describe(full[i]);
                if (a != b)
                {
                    var at = 0;
                    while (at < a.Length && at < b.Length && a[at] == b[at])
                        at++;
                    output.WriteLine($"{name} block {i}: range …{a[Math.Max(0, at - 200)..Math.Min(a.Length, at + 200)]}…");
                    output.WriteLine($"{name} block {i}: full  …{b[Math.Max(0, at - 200)..Math.Min(b.Length, at + 200)]}…");
                }
                a.Should().Be(b, $"{name} block {i}");
            }
        }
    }

    [StaTheory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void Session_MatchesFullAssignment_AfterEdits(int seed, bool footnotes)
    {
        var random = new Random(seed);
        var document = BuildMixedDocument(random, footnotes);
        var editor = new DocumentView();
        editor.LoadModel(document);
        var session = new PagedLayoutSession(editor);

        AssertSessionMatches(editor, session, $"seed {seed} initial");
        session.LastStatistics.FallbackReason.Should().BeNull();

        var reusedSomewhere = false;
        for (var round = 0; round < 8; round++)
        {
            ApplyRandomEdit(editor.Model, random);
            editor.LoadModel(editor.Model);
            AssertSessionMatches(editor, session, $"seed {seed} edit {round}");
            session.LastStatistics.Incremental.Should().BeTrue();
            reusedSomewhere |= session.LastStatistics.PagesReused > 0;
        }

        reusedSomewhere.Should().BeTrue("an edit must not re-lay out every page");
    }

    /// <summary>
    /// W12: a Number list restarted by an override renders as a second List; the full computation now
    /// predicts that pairing too, so the session lays it out itself (no fallback) with the same result.
    /// </summary>
    [StaFact]
    public void Session_PairsARestartedNumberList_WithoutFallingBack()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("one") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        document.Blocks.Add(new Paragraph("restart")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListStartOverride = 4 }
        });
        document.Blocks.Add(new Paragraph("after"));
        var editor = new DocumentView();
        editor.LoadModel(document);
        var session = new PagedLayoutSession(editor);

        AssertSessionMatches(editor, session, "override restart");
        session.LastStatistics.FallbackReason.Should().BeNull();
        PaginationEngine.MapModelBlocksToRenderedBlocks(document, 3).Should().Equal([0, 1, 2],
            "the restarted paragraph starts a second rendered List");
    }

    /// <summary>Opt-in corpus sweep: set <c>FREEW_PAGED_CORPUS_DIR</c> to a folder of .docx files.</summary>
    [StaFact]
    public void Session_MatchesFullAssignment_OnCorpus()
    {
        var directory = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var only = Environment.GetEnvironmentVariable("FREEW_PAGED_CORPUS_ONLY");
        foreach (var path in Directory.GetFiles(directory, "*.docx").OrderBy(file => new FileInfo(file).Length))
        {
            if (!string.IsNullOrEmpty(only) && !only.Split(';').Contains(Path.GetFileName(path)))
                continue;
            TextDocument document;
            try
            {
                document = DocxReader.Read(path);
            }
            catch (Exception ex)
            {
                output.WriteLine($"{Path.GetFileName(path)}: unreadable ({ex.GetType().Name})");
                continue;
            }

            var editor = new DocumentView();
            editor.LoadModel(document);
            var session = new PagedLayoutSession(editor);
            var name = Path.GetFileName(path);
            AssertSessionMatches(editor, session, name);
            var random = new Random(7);
            for (var round = 0; round < 3; round++)
            {
                ApplyRandomEdit(editor.Model, random);
                editor.LoadModel(editor.Model);
                AssertSessionMatches(editor, session, $"{name} after edit {round}");
            }
        }
    }

    private void AssertSessionMatches(DocumentView editor, PagedLayoutSession session, string label)
    {
        var full = PaginationEngine.ComputeBlockPageAssignment(editor, out var fullPages, null, layoutOnly: true);

        // W10: step the run a page at a time; whatever it reports as final must already be the final result.
        var run = session.BeginRun();
        var steps = 0;
        while (!run.Step())
        {
            steps++;
            var frontier = run.FrontierModelBlock;
            var finalPages = run.FinalPageCount;
            var partial = run.PartialAssignment();
            for (var m = 0; m < full.Length; m++)
            {
                if (m < frontier)
                {
                    partial[m].Should().Be(full[m], $"{label}: block {m} reported final after step {steps}");
                    full[m].Should().BeLessThan(finalPages, $"{label}: block {m} lies on a final page");
                }
                else
                {
                    full[m].Should().BeGreaterThanOrEqualTo(finalPages,
                        $"{label}: block {m} past the frontier starts on a page that is not final");
                }
            }
        }
        var incremental = run.Assignment;
        var incrementalPages = run.TotalPageCount;
        var stats = session.LastStatistics;
        output.WriteLine($"{label}: blocks={full.Length} pages={fullPages} incremental={stats.Incremental} "
            + $"fallback={stats.FallbackReason ?? "-"} rendered={stats.UnitsRendered} reusedUnits={stats.UnitsReused} "
            + $"laidOut={stats.PagesLaidOut} reusedPages={stats.PagesReused}");
        if (!incremental.SequenceEqual(full))
        {
            output.WriteLine("full:        " + string.Join(",", full));
            output.WriteLine("incremental: " + string.Join(",", incremental));
            output.WriteLine("kinds:       " + string.Join(",", editor.Model.Blocks.Select(block => block switch
            {
                Table => "T",
                Paragraph { Formatting.ListKind: not ListKind.None } => "L",
                Paragraph paragraph when paragraph.Runs.Any(run => run.FootnoteId is not null) => "F",
                Paragraph { Formatting.KeepWithNext: true } => "K",
                Paragraph { Formatting.PageBreakBefore: true } => "B",
                _ => "p"
            })));
        }

        incremental.Should().Equal(full, $"{label}: assignment");
        incrementalPages.Should().Be(fullPages, $"{label}: page count");
    }

    /// <summary>A layout-relevant structural description (XAML cannot serialise decoded images).</summary>
    private static string Describe(System.Windows.DependencyObject node)
    {
        var builder = new System.Text.StringBuilder();
        void Walk(object item, int depth)
        {
            switch (item)
            {
                case System.Windows.Documents.Run run:
                    builder.Append('[').Append(run.Text).Append('|').Append(run.FontSize).Append('|').Append(run.FontFamily).Append(']');
                    return;
                case System.Windows.Documents.Block block:
                    builder.Append('<').Append(block.GetType().Name)
                        .Append(" m=").Append(block.Margin).Append(" p=").Append(block.Padding)
                        .Append(" bpb=").Append(block.BreakPageBefore).Append(" bcb=").Append(block.BreakColumnBefore)
                        .Append(" lh=").Append(block.LineHeight).Append(" fs=").Append(block.FontSize)
                        .Append(" bt=").Append(block.BorderThickness);
                    if (block is System.Windows.Documents.Paragraph paragraph)
                        builder.Append(" ti=").Append(paragraph.TextIndent).Append(" kwn=").Append(paragraph.KeepWithNext).Append(" kt=").Append(paragraph.KeepTogether);
                    builder.Append('>');
                    break;
                case System.Windows.FrameworkContentElement element:
                    builder.Append('<').Append(element.GetType().Name).Append('>');
                    break;
                case string text:
                    builder.Append(text);
                    return;
            }
            if (item is System.Windows.DependencyObject dependency)
            {
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(dependency))
                    Walk(child, depth + 1);
            }
            builder.Append("</>");
        }
        Walk(node, 0);
        return builder.ToString();
    }

    private static string Xaml(System.Windows.Documents.Block block)
    {
        PaginationEngine.ClearTags(block);
        return XamlWriter.Save(block);
    }

    internal static TextDocument BuildMixedDocument(Random random, bool footnotes, bool restartOverrides = false)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 612;
        document.Page.HeightPt = 500;
        document.Page.MarginLeftPt = 54;
        document.Page.MarginRightPt = 54;
        document.Page.MarginTopPt = 54;
        document.Page.MarginBottomPt = 54;

        var nextNote = 1;
        for (var i = 0; i < 90; i++)
        {
            var paragraph = new Paragraph(Sentence(random, random.Next(3, 70)));
            switch (random.Next(14))
            {
                case 0 when footnotes:
                    document.Footnotes[nextNote] = new Footnote(nextNote, Sentence(random, random.Next(5, 80)));
                    paragraph.Runs.Add(Run.FootnoteReference(nextNote));
                    nextNote++;
                    break;
                case 1:
                case 2:
                    paragraph.Formatting = paragraph.Formatting with { ListKind = ListKind.Bullet };
                    break;
                case 3:
                    paragraph.Formatting = paragraph.Formatting with
                    {
                        ListKind = ListKind.Number,
                        ListStartOverride = restartOverrides && random.Next(4) == 0 ? 5 : null
                    };
                    break;
                case 4 when i > 0:
                    paragraph.Formatting = paragraph.Formatting with { PageBreakBefore = true };
                    break;
                case 5:
                case 6:
                    paragraph.StyleId = "Quote";
                    paragraph.Formatting = paragraph.Formatting with { ContextualSpacing = true, SpaceBeforePt = 14, SpaceBeforeIsSet = true };
                    break;
                case 7:
                    paragraph.Formatting = paragraph.Formatting with { SpaceBeforePt = 24, SpaceBeforeIsSet = true, KeepWithNext = true };
                    break;
                case 8 when i > 0:
                    var table = Table.Create(random.Next(1, 5), 2);
                    for (var r = 0; r < table.Rows.Count; r++)
                        for (var c = 0; c < 2; c++)
                            table.Rows[r].Cells[c] = new TableCell(Sentence(random, random.Next(1, 12)));
                    document.Blocks.Add(table);
                    continue;
            }
            document.Blocks.Add(paragraph);
        }

        return document;
    }

    internal static void ApplyRandomEdit(TextDocument document, Random random)
    {
        var paragraphs = document.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.SectionBreak is null && paragraph.Runs.Any(run => run.FootnoteId is null))
            .ToList();
        if (paragraphs.Count == 0)
            return;

        var target = paragraphs[random.Next(paragraphs.Count)];
        var index = document.Blocks.IndexOf(target);
        switch (random.Next(4))
        {
            case 0:
                document.Blocks[index] = WithAppendedText(target, " " + Sentence(random, random.Next(20, 90)));
                break;
            case 1:
                document.Blocks.Insert(index, new Paragraph(Sentence(random, random.Next(10, 60))));
                break;
            case 2:
                // A one-word change that rarely moves a line.
                document.Blocks[index] = WithAppendedText(target, "s");
                break;
            default:
                if (document.Blocks.Count > 2 && target.Runs.All(run => run.FootnoteId is null))
                    document.Blocks.RemoveAt(index);
                else
                    document.Blocks.Insert(index + 1, new Paragraph(Sentence(random, 12)));
                break;
        }
    }

    private static Paragraph WithAppendedText(Paragraph source, string text)
    {
        var paragraph = new Paragraph { Formatting = source.Formatting, StyleId = source.StyleId };
        paragraph.Runs.AddRange(source.Runs);
        paragraph.Runs.Add(new Run(text));
        return paragraph;
    }

    private static string Sentence(Random random, int words)
    {
        string[] vocabulary = ["page", "layout", "editor", "paragraph", "footnote", "measure", "flow", "word", "line", "block", "reflow", "stable"];
        return string.Join(" ", Enumerable.Range(0, words).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
    }
}
