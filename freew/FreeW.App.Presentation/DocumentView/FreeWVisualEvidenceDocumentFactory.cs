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
    public static TextDocument BuildDrawingObjectsCompositionDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Drawing Object Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style drawing object composition: floating shapes, charts, " +
            "SmartArt, WordArt, grouping, wrap modes, behind-text layering, in-front layering, and z-order."));

        var anchor = new Paragraph();
        anchor.Runs.Add(new Run(
            "The drawing objects in this paragraph should retain their shared placement metadata while " +
            "WPF and Avalonia renderers emit a common visual-evidence manifest. "));
        anchor.Runs.Add(Run.FromShape(BuildFloatingShape()));
        anchor.Runs.Add(Run.FromChart(BuildFloatingChart()));
        anchor.Runs.Add(Run.FromSmartArt(BuildFloatingSmartArt()));
        anchor.Runs.Add(Run.FromWordArt(BuildFloatingWordArt()));
        anchor.Runs.Add(Run.FromDrawingGroup(BuildFloatingGroup()));
        doc.Blocks.Add(anchor);

        for (var i = 1; i <= 10; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Drawing object body paragraph {i}: surrounding text gives square and top-and-bottom " +
                "wrap modes real layout context for comparison."));
        }

        return doc;
    }

    public static TextDocument BuildChartSmartArtCompositionDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chart and SmartArt Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style chart and SmartArt visual planning: named chart " +
            "palettes, quick layouts, scatter markers, data labels, axis titles, plot fills, SmartArt " +
            "layouts, color schemes, styles, and node fill sequences."));

        var chartParagraph = new Paragraph();
        chartParagraph.Runs.Add(new Run("Column chart with quick-layout annotations: "));
        chartParagraph.Runs.Add(Run.FromChart(BuildQuickLayoutColumnChart()));
        doc.Blocks.Add(chartParagraph);

        var scatterParagraph = new Paragraph();
        scatterParagraph.Runs.Add(new Run("Scatter chart must render marker-only geometry: "));
        scatterParagraph.Runs.Add(Run.FromChart(BuildMarkerOnlyScatterChart()));
        doc.Blocks.Add(scatterParagraph);

        var smartArtParagraph = new Paragraph();
        smartArtParagraph.Runs.Add(new Run("SmartArt process colors and style: "));
        smartArtParagraph.Runs.Add(Run.FromSmartArt(BuildStyledSmartArt()));
        doc.Blocks.Add(smartArtParagraph);

        doc.Blocks.Add(new Paragraph(
            "The same model is rendered by WPF FidelityRender and Avalonia PageLayoutShot, and both " +
            "emit the shared chart/SmartArt expectation into the visual evidence manifest."));

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

    private static Shape BuildFloatingShape()
    {
        var shape = Shape.TextBoxWith("Behind text box\nwith shadow", widthPt: 150, heightPt: 60, fillColorHex: "#D9EAD3");
        shape.OutlineColorHex = "#38761D";
        shape.OutlineWidthPt = 1.5;
        shape.Placement = Placement(ImageWrapping.Behind, xPt: 18, yPt: 12, zOrder: 1);
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowAlpha = 35000 };
        return shape;
    }

    private static Chart BuildFloatingChart()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [1.2, 1.7, 1.4, 2.1],
            seriesName: "Revenue",
            title: "Quarterly revenue");
        chart.WidthPt = 210;
        chart.HeightPt = 126;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        chart.Placement = Placement(ImageWrapping.TopAndBottom, xPt: 210, yPt: 120, zOrder: 4);

        return chart;
    }

    private static Chart BuildQuickLayoutColumnChart()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [1.4, 1.8, 1.6, 2.2],
            seriesName: "Revenue",
            title: "Revenue by quarter");
        chart.WidthPt = 300;
        chart.HeightPt = 168;
        chart.ColorSchemeId = "mono-blue";
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        return chart;
    }

    private static Chart BuildMarkerOnlyScatterChart()
    {
        var chart = Chart.Create(
            ChartKind.Scatter,
            ["155", "160", "165", "170"],
            [52, 58, 62, 66],
            seriesName: "Sample",
            title: "Height and weight");
        chart.WidthPt = 270;
        chart.HeightPt = 150;
        chart.ColorSchemeId = "colorful1";
        chart.StyleId = 4;
        chart.ShowLegend = false;
        chart.CategoryAxisTitle = "Height";
        chart.ValueAxisTitle = "Weight";
        return chart;
    }

    private static SmartArt BuildFloatingSmartArt()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.WidthPt = 216;
        smartArt.HeightPt = 90;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "colorful1";
        smartArt.StyleId = "subtle1";
        smartArt.Placement = Placement(ImageWrapping.Square, xPt: 36, yPt: 210, zOrder: 6);

        return smartArt;
    }

    private static SmartArt BuildStyledSmartArt()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.WidthPt = 300;
        smartArt.HeightPt = 110;
        smartArt.LayoutId = "stepup1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "intense1";
        return smartArt;
    }

    private static WordArt BuildFloatingWordArt() =>
        new("FreeW", WordArtStyle.GlowBlue, fontSizePt: 30)
        {
            AltText = "Floating WordArt",
            Warp = WordArtWarp.Wave1,
            Placement = Placement(ImageWrapping.InFront, xPt: 300, yPt: 30, zOrder: 8)
        };

    private static DrawingGroup BuildFloatingGroup()
    {
        var group = new DrawingGroup
        {
            WidthPt = 180,
            HeightPt = 82,
            Placement = Placement(ImageWrapping.Square, xPt: 280, yPt: 260, zOrder: 10)
        };
        group.Children.Add(new Shape(ShapeKind.Ellipse, 82, 50)
        {
            FillColorHex = "#CFE2F3",
            OutlineColorHex = "#1155CC",
            Effects = new ShapeEffectLst { HasGlow = true, GlowColorHex = "4472C4", GlowRad = 63500 }
        });
        group.ChildOffsets.Add((0, 16));
        group.Children.Add(new WordArt("Group", WordArtStyle.FillGold, 22));
        group.ChildOffsets.Add((70, 8));
        return group;
    }

    private static FloatingPlacement Placement(
        ImageWrapping wrapping,
        double xPt,
        double yPt,
        int zOrder) =>
        new()
        {
            Wrapping = wrapping,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            HorizontalOffsetPt = xPt,
            VerticalOffsetPt = yPt,
            ZOrderIndex = zOrder
        };

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
