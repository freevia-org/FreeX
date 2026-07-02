using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public static class FreeWVisualEvidenceDocumentFactory
{
    public static TextDocument BuildComplexTableLayoutDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Complex Table Layout Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style table layout contracts: named style, preferred widths, " +
            "merged cells, vertical merges, repeated header row, banding, cell shading, custom borders, " +
            "cell margins, spacing, vertical text, and vertical alignment."));

        doc.Blocks.Add(BuildComplexTable());
        doc.Blocks.Add(new Paragraph(
            "The same model is rendered by WPF FidelityRender and Avalonia PageLayoutShot, and both emit " +
            "the shared table expectation into the visual evidence manifest."));

        return doc;
    }

    private static Table BuildComplexTable()
    {
        var table = new Table
        {
            Formatting = new TableFormatting
            {
                Borders = true,
                HeaderRow = true,
                RepeatHeaderRow = true,
                BandedRows = true,
                FirstColumn = true
            },
            TableStyleId = "GridTable4",
            PreferredWidthPt = 468,
            Alignment = TableAlignment.Center,
            DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 8, BottomPt: 3, RightPt: 8),
            CellSpacingPt = 2.4,
            AutoFit = AutoFitMode.Fixed
        };
        table.ColumnWidthsPt.AddRange([108, 96, 96, 168]);

        table.Rows.Add(new TableRow
        {
            HeightPt = 30,
            HeightRule = TableRowHeightRule.AtLeast,
            AllowBreakAcrossPages = false,
            Cells =
            {
                HeaderCell("Region", gridSpan: 2),
                HeaderCell("FY2026 outlook", gridSpan: 2)
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 36,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("North account group", shading: "#EAF2F8", verticalMerge: VerticalMergeState.Restart),
                Cell("Q1\n$1.20M", verticalAlignment: TableCellVerticalAlignment.Center),
                Cell("Q2\n$1.42M", verticalAlignment: TableCellVerticalAlignment.Center),
                Cell("Key account", textDirection: CellTextDirection.Rotate90, shading: "#FFF2CC")
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 36,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell(string.Empty, verticalMerge: VerticalMergeState.Continue),
                Cell("Q3\n$1.36M"),
                Cell("Q4\n$1.51M"),
                Cell("Renewal review", verticalAlignment: TableCellVerticalAlignment.Bottom)
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 34,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("South", shading: "#FCE4D6"),
                Cell("Launch", gridSpan: 2, shading: "#E2F0D9"),
                Cell("Merged forecast cell")
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 32,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("Total", gridSpan: 2, shading: "#D9EAD3", customBorder: true),
                Cell("$5.49M", gridSpan: 2, shading: "#D9EAD3", customBorder: true)
            }
        });

        return table;
    }

    private static TableCell HeaderCell(string text, int gridSpan = 1) =>
        Cell(text, gridSpan: gridSpan, shading: "#D9E2F3", customBorder: true);

    private static TableCell Cell(
        string text,
        int gridSpan = 1,
        string? shading = null,
        bool customBorder = false,
        VerticalMergeState verticalMerge = VerticalMergeState.None,
        CellTextDirection textDirection = CellTextDirection.Horizontal,
        TableCellVerticalAlignment verticalAlignment = TableCellVerticalAlignment.Top)
    {
        var cell = new TableCell(text)
        {
            GridSpan = Math.Max(1, gridSpan),
            ShadingColorHex = shading,
            VerticalMerge = verticalMerge,
            TextDirection = textDirection,
            VerticalAlignment = verticalAlignment,
            Margins = new TableCellMargins(TopPt: 2, LeftPt: 6, BottomPt: 2, RightPt: 6)
        };

        if (customBorder)
        {
            cell.Borders = new CellBorders
            {
                Top = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.25),
                Bottom = new CellBorderEdge(BorderLineStyle.Thick, "#1F4E79", 1.25),
                Left = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75),
                Right = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75)
            };
        }

        return cell;
    }
}
