using System.Linq;
using System.Windows;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Page renders paint a run with a character scale (w:w) as stretched text and keep a row's w:gridBefore columns
/// empty; the editor keeps the single plain run and drops the placeholder cell on commit. Tables far wider than
/// the text column keep their authored widths, near misses are still pulled back.
/// </summary>
public sealed class CharacterScaleAndGridSkipRenderTests
{
    private static DocumentView MakeScaledView(bool pageOnly, int scalePercent)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Wide words here")
        {
            Formatting = RunFormatting.Default with { CharacterScalePercent = scalePercent, FontSizePt = 12 }
        });
        doc.Blocks.Add(paragraph);
        var view = new DocumentView { RenderPageBreakMarkers = !pageOnly };
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void ScaleInlineWidth_SplitsTheRunIntoStretchedWordBoxes_KeepingAllText()
    {
        var source = new System.Windows.Documents.Run("Wide words here") { FontSize = 16 };

        var result = Assert.IsType<System.Windows.Documents.Span>(DocumentView.ScaleInlineWidth(source, 1.5, 16));

        var boxes = result.Inlines.OfType<System.Windows.Documents.InlineUIContainer>()
            .Select(container => Assert.IsType<System.Windows.Controls.TextBlock>(container.Child))
            .ToList();
        Assert.Equal(3, boxes.Count);
        Assert.Equal("Wide words here", string.Concat(boxes.Select(box => box.Text)));
        Assert.All(boxes, box => Assert.Equal(1.5, Assert.IsType<ScaleTransform>(box.LayoutTransform).ScaleX));
    }

    [StaFact]
    public void PageRender_CharacterScale_UsesStretchedBoxes_WhileANormalRunStaysAPlainRun()
    {
        var scaled = MakeScaledView(pageOnly: true, scalePercent: 150);
        var plain = MakeScaledView(pageOnly: true, scalePercent: 100);

        static bool HasBoxes(DocumentView view) => view.Document.Blocks
            .OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.SelectMany(FlattenInlines))
            .OfType<System.Windows.Documents.InlineUIContainer>()
            .Any();

        Assert.True(HasBoxes(scaled));
        Assert.False(HasBoxes(plain));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(100, null)]
    [InlineData(103, 1.03)]
    [InlineData(95, 0.95)]
    [InlineData(110, null)]
    [InlineData(150, null)]
    public void SmallCharacterScaleFactor_AppliesOnlyToScalesTooCloseTo100ForStretchedBoxes(int percent, double? expected) =>
        Assert.Equal(expected, DocumentView.SmallCharacterScaleFactor(percent));

    private static System.Windows.Documents.Run SingleRun(DocumentView view) => view.Document.Blocks
        .OfType<System.Windows.Documents.Paragraph>()
        .SelectMany(paragraph => paragraph.Inlines.OfType<System.Windows.Documents.Run>())
        .First(run => run.Text == "Wide words here");

    [StaFact]
    public void PageRender_NearHundredScale_WidensTheGlyphEm_WhileTheEditorKeepsTheAuthoredSize()
    {
        var page = SingleRun(MakeScaledView(pageOnly: true, scalePercent: 103));
        var plain = SingleRun(MakeScaledView(pageOnly: true, scalePercent: 100));
        var editor = SingleRun(MakeScaledView(pageOnly: false, scalePercent: 103));

        Assert.Equal(plain.FontSize * 1.03, page.FontSize, 6);
        Assert.Equal(plain.FontSize, editor.FontSize, 6);
    }

    private static System.Collections.Generic.IEnumerable<System.Windows.Documents.Inline> FlattenInlines(
        System.Windows.Documents.Inline inline) =>
        inline is System.Windows.Documents.Span span
            ? span.Inlines.SelectMany(FlattenInlines).Prepend(inline)
            : [inline];

    [StaFact]
    public void Editor_CharacterScale_KeepsTheSingleEditableRun()
    {
        var view = MakeScaledView(pageOnly: false, scalePercent: 150);

        var runs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<System.Windows.Documents.Run>())
            .Select(run => run.Text)
            .ToList();

        Assert.Contains("Wide words here", runs);
    }

    [StaFact]
    public void Editor_RowWithGridBefore_GetsAGapCell_AndCommitRestoresTheModelShape()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = new Table();
        table.ColumnWidthsPt.AddRange([40, 100, 60]);
        var row = new TableRow { GridBefore = 1 };
        row.Cells.Add(new TableCell("a"));
        row.Cells.Add(new TableCell("b"));
        table.Rows.Add(row);
        doc.Blocks.Add(table);
        var view = new DocumentView();
        view.LoadModel(doc);

        var wpfRow = view.Document.Blocks.OfType<System.Windows.Documents.Table>().Single()
            .RowGroups.Single().Rows.Single();
        Assert.Equal(3, wpfRow.Cells.Count);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Table>().Single().Rows.Single();
        Assert.Equal(2, committed.Cells.Count);
        Assert.Equal(1, committed.GridBefore);
    }

    [StaFact]
    public void OverwideGrid_KeepsAuthoredWidths_ButANearMissIsPulledBackToTheTextColumn()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var contentWidthPt = DocumentViewLayoutPlanner.BuildPageMetrics(doc.Page).ContentWidthDip * 72.0 / 96.0;

        Table Build(double totalPt)
        {
            var table = new Table();
            table.ColumnWidthsPt.AddRange([totalPt / 2, totalPt / 2]);
            return table;
        }

        var wide = DocumentView.ResolveRenderedTableColumnWidths(Build(contentWidthPt + 120), doc, 2, null, 0);
        Assert.Equal((contentWidthPt + 120) / 2 * 96.0 / 72.0, wide[0]!.Value, 3);

        var nearMiss = DocumentView.ResolveRenderedTableColumnWidths(Build(contentWidthPt + 6), doc, 2, null, 0);
        Assert.True(nearMiss.Sum(w => w!.Value) <= contentWidthPt * 96.0 / 72.0 + 0.01);
    }
}
