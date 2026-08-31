using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// The traversal has to reach every place a hyperlink can hide, because both callers -- slide
/// deletion and cross-deck paste -- silently leave a stale target behind wherever it does not.
/// </summary>
public sealed class SlideHyperlinkTraversalTests
{
    [Fact]
    public void EnumerateHyperlinks_ReachesShapeRunTableCellAndNestedGroupLinks()
    {
        var group = new SlideShape { Id = 1, Kind = SlideShapeKind.Group };
        group.Children.Add(WithRunLink("in-group"));

        var tableShape = new SlideShape { Id = 2, Kind = SlideShapeKind.Table, Table = new TableShape() };
        tableShape.Table!.Rows.Add(RowWithLinkedCell("in-table-cell"));

        var shapeLevel = new SlideShape
        {
            Id = 3,
            Hyperlink = new Hyperlink { TargetSlideId = "shape-level" },
        };

        var links = SlideHyperlinkTraversal
            .EnumerateHyperlinks(new[] { group, tableShape, shapeLevel, WithRunLink("plain-run") })
            .Select(link => link.TargetSlideId)
            .ToArray();

        links.Should().BeEquivalentTo(
            new[] { "in-group", "in-table-cell", "shape-level", "plain-run" });
    }

    [Fact]
    public void EnumerateHyperlinks_ReachesALinkInsideAnInlineTable()
    {
        var host = new SlideShape { Id = 4, TextBody = new TextBody() };
        var inlineTable = new InlineTableInfo();
        inlineTable.Table.Rows.Add(RowWithLinkedCell("in-inline-table"));
        host.TextBody!.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = "￼", InlineTable = inlineTable } },
        });

        SlideHyperlinkTraversal.EnumerateHyperlinks(new[] { host })
            .Select(link => link.TargetSlideId)
            .Should().BeEquivalentTo(new[] { "in-inline-table" });
    }

    [Fact]
    public void OrphanUnresolvableSlideJumps_ClearsOnlyTheUnknownTargetsAndReportsTheCount()
    {
        var known = WithRunLink("known");
        var unknown = WithRunLink("unknown");
        var external = new SlideShape
        {
            Id = 5,
            Hyperlink = new Hyperlink { Url = "https://example.test" },
        };

        int cleared = SlideHyperlinkTraversal.OrphanUnresolvableSlideJumps(
            new[] { known, unknown, external },
            new[] { "known" });

        cleared.Should().Be(1);
        known.TextBody!.Paragraphs[0].Runs[0].Hyperlink!.TargetSlideId.Should().Be("known");
        unknown.TextBody!.Paragraphs[0].Runs[0].Hyperlink!.TargetSlideId.Should().BeNull();
        external.Hyperlink!.Url.Should().Be("https://example.test");
    }

    [Fact]
    public void OrphanUnresolvableSlideJumps_PreservesUrlAndTooltipOnTheOrphanedLink()
    {
        var shape = new SlideShape
        {
            Id = 6,
            Hyperlink = new Hyperlink
            {
                Url = "https://example.test",
                TargetSlideId = "gone",
                Tooltip = "jump",
            },
        };

        SlideHyperlinkTraversal.OrphanUnresolvableSlideJumps(new[] { shape }, Array.Empty<string>());

        shape.Hyperlink!.TargetSlideId.Should().BeNull();
        shape.Hyperlink.Url.Should().Be("https://example.test");
        shape.Hyperlink.Tooltip.Should().Be("jump");
    }

    private static SlideShape WithRunLink(string targetSlideId)
    {
        var shape = new SlideShape { Id = 99, TextBody = new TextBody() };
        shape.TextBody!.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Jump",
                    Hyperlink = new Hyperlink { TargetSlideId = targetSlideId },
                },
            },
        });
        return shape;
    }

    private static TableRow RowWithLinkedCell(string targetSlideId)
    {
        var cell = new TableCell { TextBody = new TextBody() };
        cell.TextBody!.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "Jump",
                    Hyperlink = new Hyperlink { TargetSlideId = targetSlideId },
                },
            },
        });
        var row = new TableRow();
        row.Cells.Add(cell);
        return row;
    }
}
