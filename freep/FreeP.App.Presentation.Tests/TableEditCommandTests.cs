using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for table-edit commands (Wave 9A):
///   SetTableCellTextCommand, SetTableCellFillCommand, SetTableCellAnchorCommand, InsertTableRowCommand, DeleteTableRowCommand,
///   InsertTableColumnCommand, DeleteTableColumnCommand, SetTableColumnWidthCommand,
///   MergeTableCellsCommand, SplitTableCellCommand.
///
/// Also covers EditingSession table API (active-cell, SetTableCellText, InsertRow/Col, etc.)
/// and the framework-free TableCellHitTester helper.
/// </summary>
public sealed class TableEditCommandTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a presentation with one slide containing a (rows x cols) table.</summary>
    private static (Presentation p, PresentationCommandBus bus, SlideShape tableShape)
        MakeTable(int rows = 3, int cols = 3)
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(914400L); // 1 inch each

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = 457200L }; // 0.5 inch each
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        var shape = new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * cols,
            ExtentCyEmu = 457200L * rows,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape);
    }

    private static (Presentation p, PresentationCommandBus bus, SlideShape tableShape)
        MakeTableWithText(int rows = 3, int cols = 3)
    {
        var (p, bus, shape) = MakeTable(rows, cols);
        // Populate cells with text "R{r}C{c}"
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var body = new TextBody();
                var para = new Paragraph();
                para.Runs.Add(new Run { Text = $"R{r}C{c}" });
                body.Paragraphs.Add(para);
                shape.Table!.Rows[r].Cells[c].TextBody = body;
            }
        return (p, bus, shape);
    }

    private static string CellText(SlideShape shape, int r, int c)
    {
        var cell = shape.Table!.Rows[r].Cells[c];
        if (cell.TextBody is null) return string.Empty;
        return string.Join("", cell.TextBody.Paragraphs.SelectMany(p => p.Runs).Select(run => run.Text));
    }

    private static TextBody MakeRichBody(string text)
    {
        var levels = new TextStyleLevels();
        levels[0] = new TextStyleLevel
        {
            Align = TextAlign.Right,
            FontSizePt = 28.0,
            Bold = true,
            Italic = false,
            LatinFont = "Aptos",
            BulletKind = BulletKind.Char,
            BulletChar = "*",
            BulletColor = new ThemeAwareColor(new SrgbColor(10, 20, 30)),
            BulletSizePct = 90000,
            BulletFontFamily = "Wingdings",
        };

        var body = new TextBody
        {
            Anchor = VerticalAnchor.Bottom,
            DefaultParaAlign = TextAlign.Center,
            InsetLeftPt = 1.25,
            InsetRightPt = 2.25,
            InsetTopPt = 3.25,
            InsetBottomPt = 4.25,
            Wrap = false,
            AutoFit = true,
            FontScalePPT = 62500,
            LnSpcReductionPPT = 20000,
            LstStyle = levels,
            VerticalType = TextVerticalType.Vertical270,
            WarpPreset = "textWave1",
            ColumnCount = 2,
            ColumnSpacingEmu = 123456,
        };
        body.WarpAdjusts.Add(("adj1", "val 30000"));

        var para = new Paragraph
        {
            Align = TextAlign.Justify,
            Level = 2,
            BulletKind = BulletKind.Auto,
            BulletSuppressed = true,
            BulletChar = "#",
            AutoNumType = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 4,
            MarginLeftEmu = 457200,
            IndentEmu = -228600,
            BulletColor = new ThemeAwareColor(new SrgbColor(40, 50, 60)),
            BulletSizePct = 75000,
            BulletFontFamily = "Arial",
            SpaceBeforePt = 3.5,
            SpaceAfterPt = 4.5,
        };
        para.TabStops.Add(new TabStop { PositionEmu = 914400, Alignment = TabStopAlignment.Center });
        para.Runs.Add(new Run
        {
            Text = text,
            FontFamily = "Aptos",
            FontSizePt = 18.0,
            Bold = true,
            BoldSet = true,
            Italic = true,
            ItalicSet = true,
            Underline = true,
            Strikethrough = true,
            Color = new ThemeAwareColor(new SrgbColor(70, 80, 90)),
            Hyperlink = new Hyperlink { Url = "https://example.test", Tooltip = "tip" },
            Field = new FieldRun
            {
                FieldType = "slidenum",
                CachedText = "7",
                FontFamily = "Aptos",
                FontSizePt = 14.0,
                Bold = true,
                Italic = true,
                Color = new SrgbColor(100, 110, 120),
            },
            TextShadow = new RunTextShadow
            {
                Color = new ThemeAwareColor(new SrgbColor(130, 140, 150)),
                Alpha = 77,
                BlurPt = 1.5,
                DistPt = 2.5,
                DirDeg = 135.0,
            },
            Math = new MathRunInfo { RawXml = "<m:oMath/>", IsAlternateContent = true },
        });
        body.Paragraphs.Add(para);

        return body;
    }

    private static void AssertRichBody(TextBody body, string expectedText)
    {
        body.Anchor.Should().Be(VerticalAnchor.Bottom);
        body.DefaultParaAlign.Should().Be(TextAlign.Center);
        body.InsetLeftPt.Should().Be(1.25);
        body.Wrap.Should().BeFalse();
        body.AutoFit.Should().BeTrue();
        body.FontScalePPT.Should().Be(62500);
        body.LnSpcReductionPPT.Should().Be(20000);
        body.LstStyle.Should().NotBeNull();
        body.LstStyle![0]!.FontSizePt.Should().Be(28.0);
        body.LstStyle[0]!.BulletFontFamily.Should().Be("Wingdings");
        body.VerticalType.Should().Be(TextVerticalType.Vertical270);
        body.WarpPreset.Should().Be("textWave1");
        body.WarpAdjusts.Should().Contain(("adj1", "val 30000"));
        body.ColumnCount.Should().Be(2);
        body.ColumnSpacingEmu.Should().Be(123456);

        var para = body.Paragraphs.Should().ContainSingle().Subject;
        para.Align.Should().Be(TextAlign.Justify);
        para.Level.Should().Be(2);
        para.BulletSuppressed.Should().BeTrue();
        para.AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        para.AutoNumStartAt.Should().Be(4);
        para.MarginLeftEmu.Should().Be(457200);
        para.IndentEmu.Should().Be(-228600);
        para.BulletFontFamily.Should().Be("Arial");
        para.TabStops.Should().ContainSingle()
            .Which.Alignment.Should().Be(TabStopAlignment.Center);

        var run = para.Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be(expectedText);
        run.FontFamily.Should().Be("Aptos");
        run.Bold.Should().BeTrue();
        run.BoldSet.Should().BeTrue();
        run.Italic.Should().BeTrue();
        run.ItalicSet.Should().BeTrue();
        run.Underline.Should().BeTrue();
        run.Strikethrough.Should().BeTrue();
        run.Hyperlink.Should().NotBeNull();
        run.Hyperlink!.Url.Should().Be("https://example.test");
        run.Field.Should().NotBeNull();
        run.Field!.FieldType.Should().Be("slidenum");
        run.TextShadow.Should().NotBeNull();
        run.TextShadow!.DirDeg.Should().Be(135.0);
        run.Math.Should().NotBeNull();
        run.Math!.IsAlternateContent.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SetTableCellTextCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetTableCellText_Apply_ChangesCellText()
    {
        var (p, bus, shape) = MakeTable();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Hello" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 1, 1, body));

        CellText(shape, 1, 1).Should().Be("Hello");
    }

    [Fact]
    public void SetTableHeaderRowCommand_ApplyAndUndo_TogglesFirstRowFlag()
    {
        var (p, bus, shape) = MakeTable();

        bus.Execute(new SetTableHeaderRowCommand(0, shape.Id, true));

        shape.Table!.Flags.FirstRow.Should().BeTrue();
        bus.CanUndo.Should().BeTrue();

        bus.Undo();

        shape.Table.Flags.FirstRow.Should().BeFalse();
    }

    [Fact]
    public void SetTableHeaderRowCommand_GroupedChild_ApplyAndUndoTogglesFirstRowFlag()
    {
        var (p, bus, shape) = MakeTable();
        var group = new SlideShape { Id = 70, Kind = SlideShapeKind.Group };
        p.Slides[0].Shapes.Remove(shape);
        group.Children.Add(shape);
        p.Slides[0].Shapes.Add(group);

        bus.Execute(new SetTableHeaderRowCommand(0, shape.Id, true));

        shape.Table!.Flags.FirstRow.Should().BeTrue();
        bus.Undo();
        shape.Table.Flags.FirstRow.Should().BeFalse();
    }

    [Theory]
    [InlineData(TableStyleFlagKind.FirstRow)]
    [InlineData(TableStyleFlagKind.LastRow)]
    [InlineData(TableStyleFlagKind.FirstCol)]
    [InlineData(TableStyleFlagKind.LastCol)]
    [InlineData(TableStyleFlagKind.BandRow)]
    [InlineData(TableStyleFlagKind.BandCol)]
    public void SetTableStyleFlagCommand_ApplyAndUndo_RestoresEachDesignFlag(TableStyleFlagKind kind)
    {
        var (_, bus, shape) = MakeTable();
        var before = GetTableStyleFlag(shape.Table!.Flags, kind);

        bus.Execute(new SetTableStyleFlagCommand(0, shape.Id, kind, !before));

        GetTableStyleFlag(shape.Table.Flags, kind).Should().Be(!before);
        bus.Undo();
        GetTableStyleFlag(shape.Table.Flags, kind).Should().Be(before);
    }

    private static bool GetTableStyleFlag(TableStyleFlags flags, TableStyleFlagKind kind) => kind switch
    {
        TableStyleFlagKind.FirstRow => flags.FirstRow,
        TableStyleFlagKind.LastRow => flags.LastRow,
        TableStyleFlagKind.FirstCol => flags.FirstCol,
        TableStyleFlagKind.LastCol => flags.LastCol,
        TableStyleFlagKind.BandRow => flags.BandRow,
        TableStyleFlagKind.BandCol => flags.BandCol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Fact]
    public void SetTableCellText_Revert_RestoresPreviousText()
    {
        var (p, bus, shape) = MakeTableWithText();
        var oldText = CellText(shape, 0, 0); // "R0C0"

        var newBody = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Changed" });
        newBody.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 0, 0, newBody));
        bus.Undo();

        CellText(shape, 0, 0).Should().Be(oldText);
    }

    [Fact]
    public void SetTableCellText_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Redo" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 2, 2, body));
        bus.Undo();
        bus.Redo();

        CellText(shape, 2, 2).Should().Be("Redo");
    }

    [Fact]
    public void SetTableCellFill_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x336699));

        bus.Execute(new SetTableCellFillCommand(0, shape.Id, 1, 1, fill));

        shape.Table!.Rows[1].Cells[1].Fill.Should().BeSameAs(fill);
        bus.Undo();
        shape.Table.Rows[1].Cells[1].Fill.Should().BeNull();
        bus.Redo();
        shape.Table.Rows[1].Cells[1].Fill.Should().BeSameAs(fill);
    }

    [Fact]
    public void SetTableCellAnchor_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();

        bus.Execute(new SetTableCellAnchorCommand(0, shape.Id, 1, 1, TableCellAnchor.Bottom));

        shape.Table!.Rows[1].Cells[1].Anchor.Should().Be(TableCellAnchor.Bottom);
        bus.Undo();
        shape.Table.Rows[1].Cells[1].Anchor.Should().BeNull();
        bus.Redo();
        shape.Table.Rows[1].Cells[1].Anchor.Should().Be(TableCellAnchor.Bottom);
    }

    [Fact]
    public void SetTableCellBorder_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var outline = new ShapeOutline.Visible(ThemeAwareColor.Black, 1.0);

        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 1, 1, TableCellBorderSide.Bottom, outline));

        shape.Table!.Rows[1].Cells[1].Borders!.Bottom.Should().BeSameAs(outline);
        bus.Undo();
        shape.Table.Rows[1].Cells[1].Borders.Should().BeNull();
        bus.Redo();
        shape.Table.Rows[1].Cells[1].Borders!.Bottom.Should().BeSameAs(outline);
    }

    [Fact]
    public void SetTableCellBorder_UndoRestoresDetachedBorderSnapshot()
    {
        var (_, bus, shape) = MakeTable(1, 1);
        var originalLeft = new ShapeOutline.Visible(ThemeAwareColor.Black, 0.75);
        var originalTop = ShapeOutline.None.Instance;
        var replacementBottom = new ShapeOutline.Visible(ThemeAwareColor.Black, 1.5);
        var originalBorders = new TableCellBorders
        {
            Left = originalLeft,
            Top = originalTop,
        };
        var cell = shape.Table!.Rows[0].Cells[0];
        cell.Borders = originalBorders;

        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 0, 0, TableCellBorderSide.Bottom, replacementBottom));

        cell.Borders.Should().NotBeNull();
        var editedBorders = cell.Borders!;
        editedBorders.Should().NotBeSameAs(originalBorders);
        editedBorders.Left.Should().BeSameAs(originalLeft);
        editedBorders.Top.Should().BeSameAs(originalTop);
        editedBorders.Bottom.Should().BeSameAs(replacementBottom);

        originalBorders.Left = null;
        originalBorders.Top = null;
        editedBorders.Left = null;

        bus.Undo();

        cell.Borders.Should().NotBeNull();
        var restoredBorders = cell.Borders!;
        restoredBorders.Should().NotBeSameAs(originalBorders);
        restoredBorders.Should().NotBeSameAs(editedBorders);
        restoredBorders.Left.Should().BeSameAs(originalLeft);
        restoredBorders.Top.Should().BeSameAs(originalTop);
        restoredBorders.Bottom.Should().BeNull();
    }

    [Fact]
    public void SetTableCellDiagonalBorder_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var outline = new ShapeOutline.Visible(ThemeAwareColor.Black, 1.0);
        var cell = shape.Table!.Rows[1].Cells[1];

        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 1, 1, TableCellBorderSide.DiagonalDown, outline));
        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 1, 1, TableCellBorderSide.DiagonalUp, outline));

        cell.Borders!.DiagonalDown.Should().BeSameAs(outline);
        cell.Borders.DiagonalUp.Should().BeSameAs(outline);
        bus.Undo();
        cell.Borders!.DiagonalDown.Should().BeSameAs(outline);
        cell.Borders.DiagonalUp.Should().BeNull();
        bus.Undo();
        cell.Borders.Should().BeNull();
    }

    [Fact]
    public void SetTableCellInset_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();

        bus.Execute(new SetTableCellInsetCommand(
            0, shape.Id, 1, 1, TableCellInsetSide.All, 4.0));

        var cell = shape.Table!.Rows[1].Cells[1];
        cell.InsetLeftPt.Should().Be(4.0);
        cell.InsetRightPt.Should().Be(4.0);
        cell.InsetTopPt.Should().Be(4.0);
        cell.InsetBottomPt.Should().Be(4.0);
        bus.Undo();
        cell.InsetLeftPt.Should().BeNull();
        cell.InsetRightPt.Should().BeNull();
        bus.Redo();
        cell.InsetBottomPt.Should().Be(4.0);
    }

    [Fact]
    public void SetTableRowHeight_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var row = shape.Table!.Rows[1];
        var original = row.HeightEmu;

        bus.Execute(new SetTableRowHeightCommand(0, shape.Id, 1, 914400));

        row.HeightEmu.Should().Be(914400);
        bus.Undo();
        row.HeightEmu.Should().Be(original);
        bus.Redo();
        row.HeightEmu.Should().Be(914400);
    }

    [Fact]
    public void SetTableColumnWidth_UndoRedo_Works()
    {
        var (p, bus, shape) = MakeTable();
        var original = shape.Table!.ColumnWidthsEmu[1];

        bus.Execute(new SetTableColumnWidthCommand(0, shape.Id, 1, 1371600));

        shape.Table.ColumnWidthsEmu[1].Should().Be(1371600);
        bus.Undo();
        shape.Table.ColumnWidthsEmu[1].Should().Be(original);
        bus.Redo();
        shape.Table.ColumnWidthsEmu[1].Should().Be(1371600);
    }

    [Fact]
    public void DistributeTableRows_UndoRedo_PreservesTotalHeight()
    {
        var (p, bus, shape) = MakeTable(3, 2);
        shape.Table!.Rows[0].HeightEmu = 300000;
        shape.Table.Rows[1].HeightEmu = 500000;
        shape.Table.Rows[2].HeightEmu = 700000;
        long total = shape.Table.Rows.Sum(row => row.HeightEmu);

        bus.Execute(new DistributeTableRowsCommand(0, shape.Id));

        shape.Table.Rows.Select(row => row.HeightEmu).Should().Equal(500000, 500000, 500000);
        shape.Table.Rows.Sum(row => row.HeightEmu).Should().Be(total);
        bus.Undo();
        shape.Table.Rows.Select(row => row.HeightEmu).Should().Equal(300000, 500000, 700000);
        bus.Redo();
        shape.Table.Rows.Select(row => row.HeightEmu).Should().Equal(500000, 500000, 500000);
    }

    [Fact]
    public void DistributeTableColumns_UndoRedo_PreservesTotalWidth()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        shape.Table!.ColumnWidthsEmu[0] = 300000;
        shape.Table.ColumnWidthsEmu[1] = 500000;
        shape.Table.ColumnWidthsEmu[2] = 700000;
        long total = shape.Table.ColumnWidthsEmu.Sum();

        bus.Execute(new DistributeTableColumnsCommand(0, shape.Id));

        shape.Table.ColumnWidthsEmu.Should().Equal(500000, 500000, 500000);
        shape.Table.ColumnWidthsEmu.Sum().Should().Be(total);
        bus.Undo();
        shape.Table.ColumnWidthsEmu.Should().Equal(300000, 500000, 700000);
        bus.Redo();
        shape.Table.ColumnWidthsEmu.Should().Equal(500000, 500000, 500000);
    }

    [Fact]
    public void SetTableCellFill_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        bus.Execute(new SetTableCellFillCommand(
            0,
            shape.Id,
            0,
            0,
            new ShapeFill.Solid(SrgbColor.FromRgb(0xE6B800))));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reopened = PptxPackageReader.Read(stream);
        var cell = reopened.Slides[0].Shapes[0].Table!.Rows[0].Cells[0];
        var solid = cell.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        solid.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xE6B800));
    }

    [Fact]
    public void SetTableCellAnchor_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        bus.Execute(new SetTableCellAnchorCommand(0, shape.Id, 0, 0, TableCellAnchor.Middle));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reopened = PptxPackageReader.Read(stream);
        reopened.Slides[0].Shapes[0].Table!.Rows[0].Cells[0].Anchor
            .Should().Be(TableCellAnchor.Middle);
    }

    [Fact]
    public void SetTableCellBorder_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        bus.Execute(new SetTableCellBorderCommand(
            0,
            shape.Id,
            0,
            0,
            TableCellBorderSide.Top,
            new ShapeOutline.Visible(SrgbColor.FromRgb(0x1F4E79), 0.5)));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reopened = PptxPackageReader.Read(stream);
        var outline = reopened.Slides[0].Shapes[0].Table!.Rows[0].Cells[0].Borders!.Top
            .Should().BeOfType<ShapeOutline.Visible>().Subject;
        outline.WidthPt.Should().BeApproximately(0.5, 0.001);
        outline.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Fact]
    public void SetTableCellDiagonalBorders_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        var down = new ShapeOutline.Visible(SrgbColor.FromRgb(0x1F4E79), 0.5);
        var up = new ShapeOutline.Visible(SrgbColor.FromRgb(0xC00000), 1.0);
        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 0, 0, TableCellBorderSide.DiagonalDown, down));
        bus.Execute(new SetTableCellBorderCommand(
            0, shape.Id, 0, 0, TableCellBorderSide.DiagonalUp, up));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var borders = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Table!.Rows[0].Cells[0].Borders;
        borders.Should().NotBeNull();
        borders.DiagonalDown.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        borders.DiagonalUp.Should().BeOfType<ShapeOutline.Visible>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
    }

    [Fact]
    public void SetTableCellInset_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        bus.Execute(new SetTableCellInsetCommand(
            0, shape.Id, 0, 0, TableCellInsetSide.All, 5.5));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var cell = PptxPackageReader.Read(stream).Slides[0].Shapes[0].Table!.Rows[0].Cells[0];
        cell.InsetLeftPt.Should().BeApproximately(5.5, 0.001);
        cell.InsetRightPt.Should().BeApproximately(5.5, 0.001);
        cell.InsetTopPt.Should().BeApproximately(5.5, 0.001);
        cell.InsetBottomPt.Should().BeApproximately(5.5, 0.001);
    }

    [Fact]
    public void SetTableRowHeight_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 1);
        bus.Execute(new SetTableRowHeightCommand(0, shape.Id, 0, 685800));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        PptxPackageReader.Read(stream).Slides[0].Shapes[0].Table!.Rows[0].HeightEmu
            .Should().Be(685800);
    }

    [Fact]
    public void SetTableColumnWidth_RoundTripsThroughPptx()
    {
        var (presentation, bus, shape) = MakeTable(1, 2);
        bus.Execute(new SetTableColumnWidthCommand(0, shape.Id, 1, 1371600));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        PptxPackageReader.Read(stream).Slides[0].Shapes[0].Table!.ColumnWidthsEmu[1]
            .Should().Be(1371600);
    }

    [Fact]
    public void SetTableCellText_OtherCellsUnchanged()
    {
        var (p, bus, shape) = MakeTableWithText();
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "X" });
        body.Paragraphs.Add(para);

        bus.Execute(new SetTableCellTextCommand(0, 1, 1, 1, body));

        // Surrounding cells should be unchanged
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 2, 2).Should().Be("R2C2");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // InsertTableRowCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SetTableCellText_Revert_RestoresRichTextBodyClone()
    {
        var (p, bus, shape) = MakeTable(1, 1);
        var original = MakeRichBody("Original");
        shape.Table!.Rows[0].Cells[0].TextBody = original;

        bus.Execute(new SetTableCellTextCommand(0, 1, 0, 0, MakeRichBody("Changed")));
        bus.Undo();

        var restored = shape.Table.Rows[0].Cells[0].TextBody;
        restored.Should().NotBeNull();
        restored.Should().NotBeSameAs(original);
        restored!.LstStyle.Should().NotBeSameAs(original.LstStyle);
        restored.Paragraphs[0].Should().NotBeSameAs(original.Paragraphs[0]);
        restored.Paragraphs[0].Runs[0].Should().NotBeSameAs(original.Paragraphs[0].Runs[0]);
        restored.Paragraphs[0].Runs[0].Hyperlink.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].Hyperlink);
        restored.Paragraphs[0].Runs[0].Field.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].Field);
        restored.Paragraphs[0].Runs[0].TextShadow.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].TextShadow);
        restored.Paragraphs[0].Runs[0].Math.Should().NotBeSameAs(original.Paragraphs[0].Runs[0].Math);
        AssertRichBody(restored, "Original");
    }

    [Fact]
    public void SlideCloner_CloneShape_TablePayloadUsesRichTextDeepClone()
    {
        var richBody = MakeRichBody("Table");
        var table = new TableShape
        {
            TableStyleId = "{style}",
            StyleData = new TableStyleData
            {
                StyleId = "{style}",
                WholeTbl = new TableStyleEntry
                {
                    TextColor = new ThemeAwareColor(new SrgbColor(1, 2, 3)),
                },
            },
        };
        table.ColumnWidthsEmu.Add(914400L);
        table.Rows.Add(new TableRow
        {
            HeightEmu = 457200L,
            Cells = { new TableCell { TextBody = richBody, GridSpan = 2, RowSpan = 3 } },
        });

        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Table,
            Table = table,
        };

        var clone = SlideCloner.CloneShape(shape);

        clone.Table.Should().NotBeNull();
        clone.Table.Should().NotBeSameAs(table);
        clone.Table!.StyleData.Should().NotBeSameAs(table.StyleData);
        clone.Table.StyleData!.WholeTbl.Should().NotBeSameAs(table.StyleData!.WholeTbl);
        clone.Table.Rows[0].Should().NotBeSameAs(table.Rows[0]);
        clone.Table.Rows[0].Cells[0].Should().NotBeSameAs(table.Rows[0].Cells[0]);
        clone.Table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        clone.Table.Rows[0].Cells[0].RowSpan.Should().Be(3);

        var clonedBody = clone.Table.Rows[0].Cells[0].TextBody;
        clonedBody.Should().NotBeNull();
        clonedBody.Should().NotBeSameAs(richBody);
        AssertRichBody(clonedBody!, "Table");

        clonedBody!.Paragraphs[0].Runs[0].Text = "Clone edit";
        richBody.Paragraphs[0].Runs[0].Text.Should().Be("Table");
    }

    [Fact]
    public void InsertRow_Apply_AddsRowAtIndex()
    {
        var (p, bus, shape) = MakeTable(3, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 1));
        shape.Table!.Rows.Should().HaveCount(4);
        // New row at index 1 should have correct cell count.
        shape.Table.Rows[1].Cells.Should().HaveCount(2);
    }

    [Fact]
    public void InsertRow_Apply_PreservesExistingCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        // Row 0: R0C0, R0C1. Row 1: R1C0, R1C1.
        bus.Execute(new InsertTableRowCommand(0, 1, 1)); // insert between rows 0 and 1
        // After: row 0 = original row 0, row 1 = new blank, row 2 = original row 1.
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 2, 0).Should().Be("R1C0");
        CellText(shape, 1, 0).Should().Be(string.Empty); // new row is blank
    }

    [Fact]
    public void InsertRow_Revert_RestoresOriginalRowCount()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new InsertTableRowCommand(0, 1, 0));
        bus.Undo();
        shape.Table!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void InsertRow_Revert_RestoresCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 1));
        bus.Undo();
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 0).Should().Be("R1C0");
    }

    [Fact]
    public void InsertRow_AtEnd_AppendRow()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        bus.Execute(new InsertTableRowCommand(0, 1, 2)); // insert at end
        shape.Table!.Rows.Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DeleteTableRowCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteRow_Apply_RemovesRowAtIndex()
    {
        var (p, bus, shape) = MakeTableWithText(3, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 1)); // delete middle row
        shape.Table!.Rows.Should().HaveCount(2);
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 0).Should().Be("R2C0");
    }

    [Fact]
    public void DeleteRow_Revert_RestoresAllRows()
    {
        var (p, bus, shape) = MakeTableWithText(3, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 1));
        bus.Undo();
        shape.Table!.Rows.Should().HaveCount(3);
        CellText(shape, 1, 0).Should().Be("R1C0");
    }

    [Fact]
    public void DeleteRow_NoOp_WhenSingleRow()
    {
        var (p, bus, shape) = MakeTable(1, 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 0));
        shape.Table!.Rows.Should().HaveCount(1); // still one row
    }

    // ════════════════════════════════════════════════════════════════════════════
    // InsertTableColumnCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertColumn_Apply_AddsColumnAtIndex()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(4);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(4);
    }

    [Fact]
    public void InsertColumn_Apply_PreservesExistingCellContent()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new InsertTableColumnCommand(0, 1, 1)); // insert between cols 0 and 1
        // After: col 0 = R0C0/R1C0, col 1 = blank, col 2 = R0C1/R1C1
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 0, 1).Should().Be(string.Empty); // new col
        CellText(shape, 0, 2).Should().Be("R0C1");
    }

    [Fact]
    public void InsertColumn_Revert_RestoresOriginalColumnCount()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new InsertTableColumnCommand(0, 1, 0));
        bus.Undo();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(3);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DeleteTableColumnCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DeleteColumn_Apply_RemovesColumnAtIndex()
    {
        var (p, bus, shape) = MakeTableWithText(2, 3);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 1)); // delete middle col
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(2);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(2);
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 0, 1).Should().Be("R0C2");
    }

    [Fact]
    public void DeleteColumn_Revert_RestoresAllColumns()
    {
        var (p, bus, shape) = MakeTableWithText(2, 3);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 1));
        bus.Undo();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        CellText(shape, 0, 1).Should().Be("R0C1");
    }

    [Fact]
    public void DeleteColumn_NoOp_WhenSingleColumn()
    {
        var (p, bus, shape) = MakeTable(2, 1);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 0));
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(1);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // MergeTableCellsCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MergeCells_Apply_SetsAnchorGridSpanAndRowSpan()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1)); // merge 2x2 at top-left
        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(2);
        anchor.RowSpan.Should().Be(2);
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();
    }

    [Fact]
    public void MergeCells_Apply_SetsCoveredCellsHMergeVMerge()
    {
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1)); // merge rows 0-1, cols 0-1
        // Row 0, col 1: same row as anchor → HMerge.
        shape.Table!.Rows[0].Cells[1].HMerge.Should().BeTrue();
        shape.Table.Rows[0].Cells[1].VMerge.Should().BeFalse();
        // Row 1, col 0: below anchor → VMerge.
        shape.Table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        shape.Table.Rows[1].Cells[0].HMerge.Should().BeFalse();
        // Row 1, col 1: below and to the right → VMerge (second row, not anchor's column).
        shape.Table.Rows[1].Cells[1].VMerge.Should().BeTrue();
    }

    [Fact]
    public void MergeCells_Apply_ConcatenatesText()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 1)); // merge row 0, cols 0-1
        // Anchor text should contain both "R0C0" and "R0C1"
        var anchorText = CellText(shape, 0, 0);
        anchorText.Should().Contain("R0C0");
        anchorText.Should().Contain("R0C1");
    }

    [Fact]
    public void MergeCells_Apply_CoversParameterOrderInvariant()
    {
        // r1 > r2 and c1 > c2 should be normalised internally.
        var (p, bus, shape) = MakeTable(3, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 2, 2, 0, 0)); // reversed corners
        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(3);
        anchor.RowSpan.Should().Be(3);
    }

    [Fact]
    public void MergeCells_Revert_RestoresAllCells()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1));
        bus.Undo();
        // All cells should revert to GridSpan=1, RowSpan=1, original text.
        foreach (var row in shape.Table!.Rows)
            foreach (var cell in row.Cells)
            {
                cell.GridSpan.Should().Be(1);
                cell.RowSpan.Should().Be(1);
                cell.HMerge.Should().BeFalse();
                cell.VMerge.Should().BeFalse();
            }
        CellText(shape, 0, 0).Should().Be("R0C0");
        CellText(shape, 1, 1).Should().Be("R1C1");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SplitTableCellCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SplitCell_Apply_ClearsAnchorMerge()
    {
        var (p, bus, shape) = MakeTableWithText(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 1));
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));

        var anchor = shape.Table!.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(1);
        anchor.RowSpan.Should().Be(1);
    }

    [Fact]
    public void SplitCell_Apply_ClearsHMergeOnCoveredCells()
    {
        var (p, bus, shape) = MakeTable(2, 3);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 0, 2)); // merge row 0 cols 0-2
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));

        shape.Table!.Rows[0].Cells[1].HMerge.Should().BeFalse();
        shape.Table.Rows[0].Cells[2].HMerge.Should().BeFalse();
    }

    [Fact]
    public void SplitCell_NoOp_WhenCellIsNotMerged()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        // No merge — apply split should be a no-op (no exception, no undo entry recorded).
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));
        bus.CanUndo.Should().BeFalse("no-op should not push undo entry");
    }

    [Fact]
    public void SplitCell_Revert_ReappliesMerge()
    {
        var (p, bus, shape) = MakeTable(2, 2);
        bus.Execute(new MergeTableCellsCommand(0, 1, 0, 0, 1, 1));
        bus.Execute(new SplitTableCellCommand(0, 1, 0, 0));
        bus.Undo(); // undo the split → merge should be restored
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2);
        shape.Table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        shape.Table.Rows[0].Cells[1].HMerge.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // EditingSession table API
    // ════════════════════════════════════════════════════════════════════════════

    private static EditingSession MakeSession(out SlideShape tableShape, int rows = 3, int cols = 3)
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(914400L);
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = 457200L };
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }

        tableShape = new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * cols,
            ExtentCyEmu = 457200L * rows,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(tableShape);

        var sess = new EditingSession(p, bus);
        sess.Select(1); // select the table shape
        return sess;
    }

    [Fact]
    public void EditingSession_SetActiveTableCell_SetsAndClamps()
    {
        var sess = MakeSession(out _);
        sess.SetActiveTableCell(1, 2);
        sess.ActiveTableCell.Should().Be((1, 2));
    }

    [Fact]
    public void EditingSession_SetActiveTableCell_ClampsToValidRange()
    {
        var sess = MakeSession(out _, 3, 3);
        sess.SetActiveTableCell(99, 99);
        sess.ActiveTableCell.Should().Be((2, 2)); // clamped to last valid
    }

    [Fact]
    public void EditingSession_ClearActiveTableCell_SetsNull()
    {
        var sess = MakeSession(out _);
        sess.SetActiveTableCell(0, 0);
        sess.ClearActiveTableCell();
        sess.ActiveTableCell.Should().BeNull();
    }

    [Fact]
    public void EditingSession_ActiveTableCellChanged_Fires()
    {
        var sess = MakeSession(out _);
        int fired = 0;
        sess.ActiveTableCellChanged += (_, _) => fired++;
        sess.SetActiveTableCell(1, 1);
        fired.Should().Be(1);
    }

    [Fact]
    public void EditingSession_SetTableCellText_UpdatesCell()
    {
        var sess = MakeSession(out var shape);
        sess.SetTableCellText(0, 0, "Hello");
        CellText(shape, 0, 0).Should().Be("Hello");
    }

    [Fact]
    public void EditingSession_SetTableCellText_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetTableCellText(0, 0, "Hello");
        sess.Undo();
        CellText(shape, 0, 0).Should().BeEmpty();
    }

    [Fact]
    public void EditingSession_SetActiveTableCellFill_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableCellFill(new ThemeAwareColor(SrgbColor.FromRgb(0x8844CC)))
            .Should().BeTrue();
        var solid = shape.Table!.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        solid.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x8844CC));

        sess.Undo();
        shape.Table.Rows[0].Cells[0].Fill.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetActiveTableCellAnchor_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableCellAnchor(TableCellAnchor.Bottom).Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].Anchor.Should().Be(TableCellAnchor.Bottom);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].Anchor.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetActiveTableCellBorder_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableCellBorder(
            TableCellBorderSide.Left,
            ShapeOutline.None.Instance).Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].Borders!.Left.Should().BeSameAs(ShapeOutline.None.Instance);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].Borders.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetActiveTableCellInset_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableCellInset(TableCellInsetSide.All, 3.0).Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].InsetLeftPt.Should().Be(3.0);
        shape.Table.Rows[0].Cells[0].InsetBottomPt.Should().Be(3.0);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].InsetLeftPt.Should().BeNull();
        shape.Table.Rows[0].Cells[0].InsetBottomPt.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetActiveTableRowHeight_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        var original = shape.Table!.Rows[0].HeightEmu;
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableRowHeight(914400).Should().BeTrue();
        shape.Table.Rows[0].HeightEmu.Should().Be(914400);

        sess.Undo();
        shape.Table.Rows[0].HeightEmu.Should().Be(original);
    }

    [Fact]
    public void EditingSession_SetActiveTableColumnWidth_IsUndoable()
    {
        var sess = MakeSession(out var shape);
        var original = shape.Table!.ColumnWidthsEmu[0];
        sess.SetActiveTableCell(0, 0);

        sess.TryApplyActiveTableColumnWidth(1371600).Should().BeTrue();
        shape.Table.ColumnWidthsEmu[0].Should().Be(1371600);

        sess.Undo();
        shape.Table.ColumnWidthsEmu[0].Should().Be(original);
    }

    [Fact]
    public void TableCellInsetOptionParser_ParsesAutomaticAndPointValues()
    {
        TableCellInsetOptionParser.TryParse("All:Automatic", out var side, out var automatic)
            .Should().BeTrue();
        side.Should().Be(TableCellInsetSide.All);
        automatic.Should().BeNull();

        TableCellInsetOptionParser.TryParse("Bottom:5.5pt", out side, out var inset)
            .Should().BeTrue();
        side.Should().Be(TableCellInsetSide.Bottom);
        inset.Should().BeApproximately(5.5, 0.001);
    }

    [Fact]
    public void TableRowHeightOptionParser_ParsesAutomaticAndInchValues()
    {
        TableRowHeightOptionParser.TryParse("Automatic", out var automatic)
            .Should().BeTrue();
        automatic.Should().Be(0);

        TableRowHeightOptionParser.TryParse("0.75in", out var height)
            .Should().BeTrue();
        height.Should().Be(685800);
    }

    [Fact]
    public void TableCellBorderOptionParser_ParsesAutomaticNoneAndPen()
    {
        TableCellBorderOptionParser.TryParse("Top:Automatic", out var side, out var automatic)
            .Should().BeTrue();
        side.Should().Be(TableCellBorderSide.Top);
        automatic.Should().BeNull();

        TableCellBorderOptionParser.TryParse("Left:None", out side, out var none)
            .Should().BeTrue();
        side.Should().Be(TableCellBorderSide.Left);
        none.Should().BeSameAs(ShapeOutline.None.Instance);

        TableCellBorderOptionParser.TryParse("Bottom:Black 1pt", out side, out var pen)
            .Should().BeTrue();
        side.Should().Be(TableCellBorderSide.Bottom);
        pen.Should().BeOfType<ShapeOutline.Visible>()
            .Which.WidthPt.Should().Be(1.0);
    }

    [Fact]
    public void EditingSession_ToggleActiveTableCellFormatting_UsesSharedPlanAndUndo()
    {
        var sess = MakeSession(out var shape);
        shape.Table!.Rows[0].Cells[0].TextBody = MakeBody("Cell");
        sess.SetActiveTableCell(0, 0);

        var plan = sess.PlanActiveTableCellTextFormat(TableCellTextFormatKind.Bold);
        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.TargetValue.Should().BeTrue();

        sess.ToggleBoldOnActiveTableCell().Should().BeTrue();

        var run = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0];
        run.Bold.Should().BeTrue();
        run.BoldSet.Should().BeTrue();

        sess.Undo();
        run = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0];
        run.Bold.Should().BeFalse();
        run.BoldSet.Should().BeFalse();
    }

    [Fact]
    public void EditingSession_AlignActiveTableCellParagraph_UsesSharedPlanAndUndo()
    {
        var sess = MakeSession(out var shape);
        shape.Table!.Rows[0].Cells[0].TextBody = MakeBody("Cell");
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Align = TextAlign.Left;
        sess.SetActiveTableCell(0, 0);

        var plan = sess.PlanActiveTableCellParagraphAlignment(TextAlign.Center);
        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Value.Should().Be(TextAlign.Center);

        sess.TryApplyActiveTableCellParagraphAlignment(TextAlign.Center).Should().BeTrue();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Align.Should().Be(TextAlign.Center);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Align.Should().Be(TextAlign.Left);
    }

    [Fact]
    public void EditingSession_TableCellParagraphBulletAndIndent_UseSharedPlansAndUndo()
    {
        var sess = MakeSession(out var shape);
        shape.Table!.Rows[0].Cells[0].TextBody = MakeBody("Cell");
        sess.SetActiveTableCell(0, 0);

        var bulletPlan = sess.PlanActiveTableCellParagraphBulletToggle();
        bulletPlan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        bulletPlan.Kind.Should().Be(TableCellParagraphFormatKind.BulletToggle);
        bulletPlan.BulletEnabled.Should().BeTrue();

        sess.TryApplyActiveTableCellParagraphBulletToggle().Should().BeTrue();
        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("\u2022");

        var indentPlan = sess.PlanActiveTableCellParagraphIndent();
        indentPlan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        indentPlan.LevelDelta.Should().Be(1);

        sess.TryApplyActiveTableCellParagraphIndent().Should().BeTrue();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(1);
        paragraph.MarginLeftEmu.Should().Be(457200);
        paragraph.IndentEmu.Should().Be(-228600);

        sess.TryApplyActiveTableCellParagraphOutdent().Should().BeTrue();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(0);
        paragraph.MarginLeftEmu.Should().BeNull();

        sess.Undo();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(1);
        paragraph.MarginLeftEmu.Should().Be(457200);
    }

    [Fact]
    public void EditingSession_TableCellParagraphNumbering_UsesSharedPlanAndUndo()
    {
        var sess = MakeSession(out var shape);
        shape.Table!.Rows[0].Cells[0].TextBody = MakeBody("Cell");
        sess.SetActiveTableCell(0, 0);

        var plan = sess.PlanActiveTableCellParagraphNumberingToggle();
        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.NumberingToggle);
        plan.BulletEnabled.Should().BeTrue();

        sess.TryApplyActiveTableCellParagraphNumberingToggle().Should().BeTrue();
        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        paragraph.AutoNumStartAt.Should().Be(1);
        paragraph.BulletSuppressed.Should().BeFalse();

        sess.TryApplyActiveTableCellParagraphNumberingToggle().Should().BeTrue();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.None);
        paragraph.BulletSuppressed.Should().BeTrue();

        sess.Undo();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
    }

    [Fact]
    public void EditingSession_TableCellParagraphListPreset_UsesSharedPlanAndUndo()
    {
        var sess = MakeSession(out var shape);
        shape.Table!.Rows[0].Cells[0].TextBody = MakeBody("Cell");
        sess.SetActiveTableCell(0, 0);

        var plan = sess.PlanActiveTableCellParagraphListPreset(
            TableCellListPresetCatalog.NumberAlphaUpperPeriod);
        plan.Status.Should().Be(TableCellTextFormatStatus.Ready);
        plan.Kind.Should().Be(TableCellParagraphFormatKind.ListPreset);
        plan.ListPreset.Should().Be(TableCellListPresetCatalog.NumberAlphaUpperPeriod);

        sess.TryApplyActiveTableCellParagraphListPreset(
            TableCellListPresetCatalog.NumberAlphaUpperPeriodId).Should().BeTrue();

        var paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.AlphaUcPeriod);
        paragraph.AutoNumStartAt.Should().Be(1);
        paragraph.BulletSuppressed.Should().BeFalse();

        sess.Undo();
        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.None);
    }

    [Fact]
    public void EditingSession_InsertRowBelow_GrowsGrid()
    {
        var sess = MakeSession(out var shape, 2, 2);
        sess.SetActiveTableCell(0, 0);
        sess.InsertRowBelow();
        shape.Table!.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void EditingSession_InsertRowAbove_ShiftsActiveCell()
    {
        var sess = MakeSession(out var shape, 3, 2);
        sess.SetActiveTableCell(1, 0);
        sess.InsertRowAbove();
        // Active cell should have shifted down to row 2.
        sess.ActiveTableCell!.Value.Row.Should().Be(2);
    }

    [Fact]
    public void EditingSession_DeleteRow_ShrinkGrid()
    {
        var sess = MakeSession(out var shape, 3, 2);
        sess.SetActiveTableCell(1, 0);
        sess.DeleteRow();
        shape.Table!.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_InsertColumnRight_GrowsGrid()
    {
        var sess = MakeSession(out var shape, 2, 2);
        sess.SetActiveTableCell(0, 0);
        sess.InsertColumnRight();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(3);
        foreach (var row in shape.Table.Rows)
            row.Cells.Should().HaveCount(3);
    }

    [Fact]
    public void EditingSession_DeleteColumn_ShrinkGrid()
    {
        var sess = MakeSession(out var shape, 2, 3);
        sess.SetActiveTableCell(0, 1);
        sess.DeleteColumn();
        shape.Table!.ColumnWidthsEmu.Should().HaveCount(2);
    }

    [Fact]
    public void EditingSession_SplitSelectedCell_Works()
    {
        var sess = MakeSession(out var shape, 2, 2);
        // First merge, then split via session API.
        sess.MergeTableCells(0, 0, 0, 1);
        sess.SetActiveTableCell(0, 0);
        sess.SplitSelectedCell();
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(1);
    }

    [Fact]
    public void EditingSession_TryMergeActiveTableCell_UsesAdjacentCellAndIsUndoable()
    {
        var sess = MakeSession(out var shape, 1, 2);
        sess.SetActiveTableCell(0, 0);

        sess.TryMergeActiveTableCell().Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);
    }

    /// <summary>
    /// Regression for freep-table-graphics F1: clicking "Merge Cells" again on a cell that is
    /// already merged must extend the merge past its own GridSpan, not silently re-merge the
    /// anchor with a cell already inside its own span (a no-op that still pushes an undo entry).
    /// </summary>
    [Fact]
    public void EditingSession_TryMergeActiveTableCell_OnAlreadyMergedCell_ExtendsPastOwnSpan()
    {
        var sess = MakeSession(out var shape, 1, 4);
        sess.SetActiveTableCell(0, 0);

        sess.TryMergeActiveTableCell().Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].GridSpan.Should().Be(2, "first merge should grow the anchor to span columns 0-1");

        sess.TryMergeActiveTableCell().Should().BeTrue();
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(3, "second merge must extend to column 2, not re-merge columns 0-1");
        shape.Table.Rows[0].Cells[2].HMerge.Should().BeTrue("column 2 must now be absorbed into the merge");

        sess.Undo();
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(2, "undo of the second merge must restore the 2-wide state");

        sess.Undo();
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(1, "undo of the first merge must restore the unmerged state");
    }

    /// <summary>
    /// Sibling/no-regression case for freep-table-graphics F1: the pre-existing right-edge
    /// fallback (merge below when there is no column to the right) on a plain, never-merged
    /// cell must still work exactly as before -- the span-aware rewrite must not disturb the
    /// colSpan==1/rowSpan==1 arithmetic.
    /// </summary>
    [Fact]
    public void EditingSession_TryMergeActiveTableCell_AtRightEdge_FallsBackToMergingBelow()
    {
        var sess = MakeSession(out var shape, 2, 1); // 2 rows, 1 column: no right neighbor exists.
        sess.SetActiveTableCell(0, 0);

        sess.TryMergeActiveTableCell().Should().BeTrue();
        shape.Table!.Rows[0].Cells[0].RowSpan.Should().Be(2, "single-column table has no right neighbor, so merge must fall back to the cell below");
        shape.Table.Rows[0].Cells[0].GridSpan.Should().Be(1);

        sess.Undo();
        shape.Table.Rows[0].Cells[0].RowSpan.Should().Be(1);
    }

    [Fact]
    public void EditingSession_TrySplitActiveTableCell_RejectsUnmergedCell()
    {
        var sess = MakeSession(out _, 1, 1);
        sess.SetActiveTableCell(0, 0);

        sess.TrySplitActiveTableCell().Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // GetSelectedTable
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditingSession_GetSelectedTable_ReturnsTableWhenTableSelected()
    {
        var sess = MakeSession(out var shape);
        sess.GetSelectedTable().Should().NotBeNull();
    }

    [Fact]
    public void EditingSession_GetSelectedTable_ReturnsNullWhenNonTableSelected()
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var nonTable = new SlideShape
        {
            Id   = 10,
            Kind = SlideShapeKind.AutoShape,
        };
        p.Slides[0].Shapes.Add(nonTable);
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        sess.Select(10);
        sess.GetSelectedTable().Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // TableCellHitTester  (framework-free)
    // ════════════════════════════════════════════════════════════════════════════

    private static SlideShape MakeTableShape(int rows, int cols,
        long colWidthEmu = 914400L, long rowHeightEmu = 457200L,
        long offsetX = 0, long offsetY = 0)
    {
        var table = new TableShape();
        for (int c = 0; c < cols; c++)
            table.ColumnWidthsEmu.Add(colWidthEmu);
        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow { HeightEmu = rowHeightEmu };
            for (int c = 0; c < cols; c++)
                row.Cells.Add(new TableCell());
            table.Rows.Add(row);
        }
        return new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = offsetX,
            OffsetYEmu  = offsetY,
            ExtentCxEmu = colWidthEmu * cols,
            ExtentCyEmu = rowHeightEmu * rows,
            Table       = table,
        };
    }

    // 1 EMU = 1/9525 DIP
    private const double Dip = 1.0 / 9525.0;

    [Fact]
    public void TableCellHitTester_HitTest_ReturnsNullOutsideFrame()
    {
        var shape = MakeTableShape(2, 2);
        // Point far outside.
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, 1e6, 1e6);
        result.Should().BeNull();
    }

    [Fact]
    public void TableCellHitTester_HitTest_TopLeftCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        // Click at centre of first cell (DIP).
        double x = 914400L / 9525.0 * 0.5;
        double y = 457200L / 9525.0 * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((0, 0));
    }

    [Fact]
    public void TableCellHitTester_HitTest_BottomRightCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW * 2 + colW * 0.5; // centre of column 2
        double y = rowH * 2 + rowH * 0.5; // centre of row 2
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((2, 2));
    }

    [Fact]
    public void TableCellHitTester_HitTest_MiddleCell()
    {
        var shape = MakeTableShape(3, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW * 1 + colW * 0.5;
        double y = rowH * 1 + rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((1, 1));
    }

    [Fact]
    public void TableCellHitTester_HitTest_WithTableOffset()
    {
        // Table starts at (1 inch, 1 inch) = (914400 EMU, 914400 EMU).
        var shape = MakeTableShape(2, 2, offsetX: 914400L, offsetY: 914400L);
        double off = 914400L / 9525.0;
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        // Click on centre of cell (0,1).
        double x = off + colW + colW * 0.5;
        double y = off + rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        result.Should().Be((0, 1));
    }

    [Fact]
    public void TableCellHitTester_GetCellRect_ReturnsCorrectBoundsForFirstCell()
    {
        var shape = MakeTableShape(2, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        var rect = FreeP.App.Compositor.TableCellHitTester.GetCellRect(shape, 0, 0);
        rect.Should().NotBeNull();
        rect!.Value.X.Should().BeApproximately(0, 0.001);
        rect.Value.Y.Should().BeApproximately(0, 0.001);
        rect.Value.Width.Should().BeApproximately(914400L / 9525.0, 0.001);
        rect.Value.Height.Should().BeApproximately(457200L / 9525.0, 0.001);
    }

    [Fact]
    public void TableCellHitTester_GetCellRect_ReturnsNullForOutOfBoundsRow()
    {
        var shape = MakeTableShape(2, 2);
        var rect = FreeP.App.Compositor.TableCellHitTester.GetCellRect(shape, 99, 0);
        rect.Should().BeNull();
    }

    [Fact]
    public void TableCellHitTester_HitTest_HMergeReturnsAnchor()
    {
        var shape = MakeTableShape(2, 3, colWidthEmu: 914400L, rowHeightEmu: 457200L);
        // Manually set up a merge: anchor at (0,0) with GridSpan=2, cells (0,1) as HMerge.
        shape.Table!.Rows[0].Cells[0].GridSpan = 2;
        shape.Table.Rows[0].Cells[1].HMerge = true;

        // Click in the area that is "owned" by cell (0,1) but it is HMerge.
        double colW = 914400L / 9525.0;
        double rowH = 457200L / 9525.0;
        double x = colW + colW * 0.5; // centre of slot (0,1)
        double y = rowH * 0.5;
        var result = FreeP.App.Compositor.TableCellHitTester.HitTest(shape, x, y);
        // Should resolve to anchor (0,0).
        result.Should().Be((0, 0));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // W2 regression tests — DeleteTableColumnCommand + horizontal merges
    // ════════════════════════════════════════════════════════════════════════════

    // Helper: build table [A(gridSpan=2)][HMerge][C] in one row.
    private static (Presentation p, PresentationCommandBus bus, SlideShape shape) MakeHMergedTable()
    {
        // 1 row × 3 grid-columns: cell[0]=anchor(GridSpan=2), cell[1]=HMerge, cell[2]=C
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L); // col 0
        table.ColumnWidthsEmu.Add(914400L); // col 1
        table.ColumnWidthsEmu.Add(914400L); // col 2

        var row = new TableRow { HeightEmu = 457200L };
        row.Cells.Add(new TableCell { GridSpan = 2, TextBody = MakeBody("A") }); // anchor
        row.Cells.Add(new TableCell { HMerge = true });                           // continuation
        row.Cells.Add(new TableCell { TextBody = MakeBody("C") });                // independent

        table.Rows.Add(row);

        var shape = new SlideShape
        {
            Id          = 1,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0, OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * 3, ExtentCyEmu = 457200L,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape);
    }

    private static TextBody MakeBody(string text)
    {
        var body = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(para);
        return body;
    }

    [Fact]
    public void W2_DeleteCol_InsideAnchorSpan_DecrementsGridSpan()
    {
        // Row: [A gridSpan=2][HMerge][C]. Delete col 1 (inside A's span).
        // Expected: [A gridSpan=1][C], 2 grid columns.
        var (p, bus, shape) = MakeHMergedTable();

        bus.Execute(new DeleteTableColumnCommand(0, 1, 1));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        table.Rows[0].Cells[0].HMerge.Should().BeFalse();
        CellText(shape, 0, 1).Should().Be("C");
    }

    [Fact]
    public void W2_DeleteCol_AnchorColumn_PromotesContinuation()
    {
        // Row: [A gridSpan=2][HMerge][C]. Delete col 0 (anchor's leading column).
        // Expected: [NewAnchor gridSpan=1 (text=A)][C], 2 grid columns.
        var (p, bus, shape) = MakeHMergedTable();

        bus.Execute(new DeleteTableColumnCommand(0, 1, 0));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(2);
        table.Rows[0].Cells.Should().HaveCount(2);
        // Former HMerge continuation is now an independent anchor.
        table.Rows[0].Cells[0].HMerge.Should().BeFalse();
        table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        CellText(shape, 0, 1).Should().Be("C");
    }

    [Fact]
    public void W2_DeleteCol_Undo_RestoresExactSpans()
    {
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new DeleteTableColumnCommand(0, 1, 1));
        bus.Undo();

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(3);
        table.Rows[0].Cells.Should().HaveCount(3);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
    }

    [Fact]
    public void W2_DeleteCol_GridIntegrity_AfterDelete()
    {
        // In FreeP's model, every row must have exactly one cell per grid column.
        // After any delete, Cells.Count == ColumnWidthsEmu.Count.
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new DeleteTableColumnCommand(0, 1, 0));
        var table = shape.Table!;
        int gridWidth = table.ColumnWidthsEmu.Count;
        table.Rows[0].Cells.Should().HaveCount(gridWidth);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // W3 regression tests — DeleteTableRowCommand + vertical merges
    // ════════════════════════════════════════════════════════════════════════════

    // Helper: 2-row × 1-col table with a vertical span.
    private static (Presentation p, PresentationCommandBus bus, SlideShape shape) MakeVMergedTable()
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L);

        var row0 = new TableRow { HeightEmu = 457200L };
        row0.Cells.Add(new TableCell { RowSpan = 2, TextBody = MakeBody("TOP") }); // anchor

        var row1 = new TableRow { HeightEmu = 457200L };
        row1.Cells.Add(new TableCell { VMerge = true }); // continuation

        table.Rows.Add(row0);
        table.Rows.Add(row1);

        var shape = new SlideShape
        {
            Id          = 2,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0, OffsetYEmu  = 0,
            ExtentCxEmu = 914400L, ExtentCyEmu = 457200L * 2,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape);
    }

    [Fact]
    public void W3_DeleteRow_Continuation_DecrementsAnchorRowSpan()
    {
        // Row 0 = anchor(RowSpan=2), Row 1 = VMerge. Delete row 1.
        // Expected: row 0 anchor RowSpan becomes 1; table has 1 row.
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 2, 1));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(1);
        table.Rows[0].Cells[0].RowSpan.Should().Be(1);
        table.Rows[0].Cells[0].VMerge.Should().BeFalse();
    }

    [Fact]
    public void W3_DeleteRow_Anchor_PromotesContinuation()
    {
        // 3-row table: row0=anchor(RowSpan=2), row1=VMerge, row2=independent.
        // Delete row 0 (the anchor). Row 1's cell must become the new anchor (VMerge cleared,
        // RowSpan=1), row 2 unchanged.
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L);
        var r0 = new TableRow { HeightEmu = 457200L };
        r0.Cells.Add(new TableCell { RowSpan = 2, TextBody = MakeBody("ANCHOR") });
        var r1 = new TableRow { HeightEmu = 457200L };
        r1.Cells.Add(new TableCell { VMerge = true });
        var r2 = new TableRow { HeightEmu = 457200L };
        r2.Cells.Add(new TableCell { TextBody = MakeBody("IND") });
        table.Rows.Add(r0); table.Rows.Add(r1); table.Rows.Add(r2);
        var shape = new SlideShape { Id = 3, Kind = SlideShapeKind.Table,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400L, ExtentCyEmu = 457200L * 3,
            Table = table };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new DeleteTableRowCommand(0, 3, 0));

        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].VMerge.Should().BeFalse();
        table.Rows[0].Cells[0].RowSpan.Should().Be(1);
        CellText(shape, 1, 0).Should().Be("IND");
    }

    [Fact]
    public void W3_DeleteRow_Undo_RestoresExactRowSpans()
    {
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 2, 1));
        bus.Undo();

        var table = shape.Table!;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // W4 regression tests — InsertTableColumnCommand + horizontal merges
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void W4_InsertCol_InsideAnchorSpan_WidensAnchorAndAddsContinuation()
    {
        // Row: [A gridSpan=2][HMerge][C]. Insert at col 1 (inside A's span).
        // Expected: [A gridSpan=3][HMerge][HMerge][C], 4 grid columns.
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(4);
        table.Rows[0].Cells.Should().HaveCount(4);
        table.Rows[0].Cells[0].GridSpan.Should().Be(3);
        table.Rows[0].Cells[0].HMerge.Should().BeFalse();
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[2].HMerge.Should().BeTrue();
        CellText(shape, 0, 3).Should().Be("C");
    }

    [Fact]
    public void W4_InsertCol_AtBoundary_AddsIndependentCell()
    {
        // Row: [A gridSpan=2][HMerge][C]. Insert at col 2 (boundary before C).
        // Expected: [A gridSpan=2][HMerge][new cell][C], 4 grid columns.
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 1, 2));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(4);
        table.Rows[0].Cells.Should().HaveCount(4);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[2].HMerge.Should().BeFalse();
        table.Rows[0].Cells[2].GridSpan.Should().Be(1);
        CellText(shape, 0, 3).Should().Be("C");
    }

    [Fact]
    public void W4_InsertCol_Undo_RestoresExactStructure()
    {
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));
        bus.Undo();

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(3);
        table.Rows[0].Cells.Should().HaveCount(3);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
    }

    [Fact]
    public void W4_InsertCol_GridIntegrity_AfterInsert()
    {
        // In FreeP's model, every row must have exactly one cell per grid column (HMerge cells
        // stay in the list).  After any insert, Cells.Count == ColumnWidthsEmu.Count.
        var (p, bus, shape) = MakeHMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));
        var table = shape.Table!;
        int gridWidth = table.ColumnWidthsEmu.Count;
        table.Rows[0].Cells.Should().HaveCount(gridWidth);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // W5 regression tests — InsertTableRowCommand + vertical merges
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void W5_InsertRow_InsideVSpan_AddsVMergeContinuationAndWidensAnchor()
    {
        // 2-row × 1-col: row0=anchor(RowSpan=2), row1=VMerge. Insert at row 1 (inside span).
        // Expected: 3 rows, anchor RowSpan=3, inserted row has VMerge=true.
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 2, 1));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells[0].RowSpan.Should().Be(3);
        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        table.Rows[2].Cells[0].VMerge.Should().BeTrue();
    }

    [Fact]
    public void W5_InsertRow_AtSpanBoundary_AddsIndependentCell()
    {
        // 2-row × 1-col: row0=anchor(RowSpan=2), row1=VMerge. Insert at row 2 (after span).
        // Expected: 3 rows, anchor RowSpan stays 2, new row has an independent cell.
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 2, 2));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        table.Rows[2].Cells[0].VMerge.Should().BeFalse();
        table.Rows[2].Cells[0].RowSpan.Should().Be(1);
    }

    [Fact]
    public void W5_InsertRow_Undo_RestoresExactSpans()
    {
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 2, 1));
        bus.Undo();

        var table = shape.Table!;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // X1 regression tests — DeleteTableRowCommand + 2-D merges
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a 3×3 table with a 2×2 anchor merge at (0,0)-(1,1).
    ///
    /// Row 0: [anchor GridSpan=2 RowSpan=2] [HMerge] [C00]
    /// Row 1: [VMerge]                       [VMerge] [C10]
    /// Row 2: [C20]                           [C21]   [C22]
    /// </summary>
    private static (Presentation p, PresentationCommandBus bus, SlideShape shape) Make2DMergedTable()
    {
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);

        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L); // col 0
        table.ColumnWidthsEmu.Add(914400L); // col 1
        table.ColumnWidthsEmu.Add(914400L); // col 2

        // Row 0: anchor (GridSpan=2, RowSpan=2), HMerge continuation, independent cell
        var row0 = new TableRow { HeightEmu = 457200L };
        row0.Cells.Add(new TableCell { GridSpan = 2, RowSpan = 2, TextBody = MakeBody("ANCHOR") });
        row0.Cells.Add(new TableCell { HMerge = true });
        row0.Cells.Add(new TableCell { TextBody = MakeBody("C00") });
        table.Rows.Add(row0);

        // Row 1: VMerge, VMerge (both covered by the 2×2 anchor), independent cell
        var row1 = new TableRow { HeightEmu = 457200L };
        row1.Cells.Add(new TableCell { VMerge = true });
        row1.Cells.Add(new TableCell { VMerge = true });
        row1.Cells.Add(new TableCell { TextBody = MakeBody("C10") });
        table.Rows.Add(row1);

        // Row 2: three independent cells
        var row2 = new TableRow { HeightEmu = 457200L };
        row2.Cells.Add(new TableCell { TextBody = MakeBody("C20") });
        row2.Cells.Add(new TableCell { TextBody = MakeBody("C21") });
        row2.Cells.Add(new TableCell { TextBody = MakeBody("C22") });
        table.Rows.Add(row2);

        var shape = new SlideShape
        {
            Id          = 4,
            Kind        = SlideShapeKind.Table,
            OffsetXEmu  = 0, OffsetYEmu  = 0,
            ExtentCxEmu = 914400L * 3, ExtentCyEmu = 457200L * 3,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape);
    }

    [Fact]
    public void X1_DeleteRow_2DMergeAnchorRow_PromotedRowIsValidHorizontalMerge()
    {
        // Delete row 0 (the 2×2 anchor row).
        // Expected: row 1 becomes the new anchor row.
        //   - cell[0]: VMerge cleared, GridSpan=2, RowSpan=1 (new anchor)
        //   - cell[1]: VMerge cleared, HMerge=true (continuation of promoted anchor)
        //   - cell[2]: unchanged (independent)
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 4, 0));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(2);

        // Promoted anchor
        var promoted = table.Rows[0].Cells[0];
        promoted.VMerge.Should().BeFalse("promoted cell must not be VMerge");
        promoted.HMerge.Should().BeFalse("promoted cell is an anchor, not an HMerge");
        promoted.GridSpan.Should().Be(2, "promoted anchor inherits GridSpan from original anchor");
        promoted.RowSpan.Should().Be(1, "RowSpan decremented from 2 to 1");

        // Horizontal continuation of promoted anchor — must be HMerge, NOT VMerge
        var continuation = table.Rows[0].Cells[1];
        continuation.VMerge.Should().BeFalse("must not remain VMerge after promotion");
        continuation.HMerge.Should().BeTrue("must become HMerge as horizontal continuation");

        // Independent cell in promoted row unchanged
        table.Rows[0].Cells[2].HMerge.Should().BeFalse();
        table.Rows[0].Cells[2].VMerge.Should().BeFalse();

        // Row 2 (now row 1) unchanged
        CellText(shape, 1, 0).Should().Be("C20");
        CellText(shape, 1, 1).Should().Be("C21");
    }

    [Fact]
    public void X1_DeleteRow_2DMergeAnchorRow_NoOrphanVMergeAnywhere()
    {
        // After deleting the anchor row the surviving rows must not contain
        // any VMerge cell that lacks a RowSpan>1 anchor above it.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 4, 0));

        var table = shape.Table!;

        // Row 0 (was row 1) — no VMerge anywhere
        foreach (var cell in table.Rows[0].Cells)
            cell.VMerge.Should().BeFalse("no orphan VMerge in the promoted row");

        // Row 1 (was row 2) — fully independent, no merge flags
        foreach (var cell in table.Rows[1].Cells)
        {
            cell.VMerge.Should().BeFalse();
            cell.HMerge.Should().BeFalse();
        }
    }

    [Fact]
    public void X1_DeleteRow_2DMergeAnchorRow_GridColumnCountConsistent()
    {
        // Every row must have exactly ColumnWidthsEmu.Count cells.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 4, 0));

        var table = shape.Table!;
        int gridWidth = table.ColumnWidthsEmu.Count;
        foreach (var row in table.Rows)
            row.Cells.Should().HaveCount(gridWidth, "grid column count must be consistent after delete");
    }

    [Fact]
    public void X1_DeleteRow_2DMergeAnchorRow_Undo_RestoresExactState()
    {
        // Undo must fully restore the original 2×2 merge state (full snapshot).
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 4, 0));
        bus.Undo();

        var table = shape.Table!;
        table.Rows.Should().HaveCount(3);

        // Original anchor
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        table.Rows[0].Cells[0].HMerge.Should().BeFalse();
        table.Rows[0].Cells[0].VMerge.Should().BeFalse();

        // Original HMerge continuation
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[1].VMerge.Should().BeFalse();

        // Original VMerge cells in row 1
        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        table.Rows[1].Cells[0].HMerge.Should().BeFalse();
        table.Rows[1].Cells[1].VMerge.Should().BeTrue();
        table.Rows[1].Cells[1].HMerge.Should().BeFalse();
    }

    [Fact]
    public void X1_DeleteRow_2DMergeBottomRow_AnchorKeepsGridSpan()
    {
        // Delete row 1 (the VMerge row of the 2×2 merge).
        // Expected: the anchor at (0,0) keeps GridSpan=2 and its RowSpan decrements to 1;
        //           the HMerge continuation at (0,1) is unchanged.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableRowCommand(0, 4, 1));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(2);

        // Anchor row — RowSpan reduced to 1, GridSpan preserved
        var anchor = table.Rows[0].Cells[0];
        anchor.RowSpan.Should().Be(1, "RowSpan decremented from 2 to 1");
        anchor.GridSpan.Should().Be(2, "horizontal span must be preserved");
        anchor.VMerge.Should().BeFalse();

        // HMerge continuation in the anchor row must remain HMerge
        var hcont = table.Rows[0].Cells[1];
        hcont.HMerge.Should().BeTrue("horizontal continuation must remain HMerge");
        hcont.VMerge.Should().BeFalse();

        // Row 1 (was row 2) is unchanged
        table.Rows[1].Cells.Should().HaveCount(3);
        foreach (var cell in table.Rows[1].Cells)
        {
            cell.VMerge.Should().BeFalse();
            cell.HMerge.Should().BeFalse();
        }
    }

    [Fact]
    public void X1_ExistingW3_SingleColumnVerticalMerge_StillPasses()
    {
        // Regression: the W3 1-column vertical merge scenario must still work after X1 fix.
        // Row 0 = anchor(RowSpan=2), Row 1 = VMerge. Delete row 0 → row 1 promoted.
        var p = new Presentation();
        p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(914400L);
        var r0 = new TableRow { HeightEmu = 457200L };
        r0.Cells.Add(new TableCell { RowSpan = 2, TextBody = MakeBody("ANCHOR") });
        var r1 = new TableRow { HeightEmu = 457200L };
        r1.Cells.Add(new TableCell { VMerge = true });
        var r2 = new TableRow { HeightEmu = 457200L };
        r2.Cells.Add(new TableCell { TextBody = MakeBody("IND") });
        table.Rows.Add(r0); table.Rows.Add(r1); table.Rows.Add(r2);
        var shape = new SlideShape { Id = 5, Kind = SlideShapeKind.Table,
            OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400L, ExtentCyEmu = 457200L * 3,
            Table = table };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new DeleteTableRowCommand(0, 5, 0));

        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].VMerge.Should().BeFalse();
        table.Rows[0].Cells[0].RowSpan.Should().Be(1);
        table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        CellText(shape, 1, 0).Should().Be("IND");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // R139 regression tests
    //   (a) InsertTableRowCommand inside a 2-D (row+col) merge must widen the true
    //       anchor's RowSpan exactly once and keep every spanned column's cell
    //       consistently VMerge, not just the merge's leftmost column.
    //   (b) Every structural/resize table command must resync the owning shape's
    //       ExtentCxEmu/ExtentCyEmu to the table's actual grid content, including on
    //       undo.
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void R139a_InsertRow_Inside2DMerge_WidensAnchorOnceAndMarksAllSpannedColumnsVMerge()
    {
        // 2×2 anchor merge at (0,0)-(1,1) in a 3×3 table (see Make2DMergedTable doc comment).
        // Insert a row at index 1 — strictly inside the anchor's vertical span (rows 0-1).
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 4, 1));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(4);

        // The true anchor (col 0) must have its RowSpan widened exactly once: 2 -> 3.
        var anchor = table.Rows[0].Cells[0];
        anchor.RowSpan.Should().Be(3, "the anchor's vertical span must grow by exactly one row");
        anchor.GridSpan.Should().Be(2, "the anchor's horizontal span must be untouched by a row insert");
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();

        // The inserted row (index 1) must carry a VMerge continuation in BOTH columns of the
        // merge, not just column 0 — a real user reaching this through EditingSession.InsertRowBelow
        // after clicking anywhere in the merge (which always resolves to the anchor) hits this row.
        table.Rows[1].Cells[0].VMerge.Should().BeTrue("column 0 is covered by the widened anchor");
        table.Rows[1].Cells[1].VMerge.Should().BeTrue(
            "column 1 is ALSO covered by the widened anchor's GridSpan=2 rectangle — " +
            "leaving it as an independent cell here is exactly the grid corruption this test guards against");
        table.Rows[1].Cells[1].GridSpan.Should().Be(1, "a VMerge continuation must never itself carry a span");
        table.Rows[1].Cells[1].RowSpan.Should().Be(1);

        // The old row 1 (now row 2) must still be VMerge in both merge columns, unaffected.
        table.Rows[2].Cells[0].VMerge.Should().BeTrue();
        table.Rows[2].Cells[1].VMerge.Should().BeTrue();

        // Grid integrity: every row has exactly ColumnWidthsEmu.Count cells.
        int gridWidth = table.ColumnWidthsEmu.Count;
        foreach (var row in table.Rows)
            row.Cells.Should().HaveCount(gridWidth);
    }

    [Fact]
    public void R139a_InsertRow_Inside2DMerge_Undo_RestoresExactOriginalGrid()
    {
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 4, 1));
        bus.Undo();

        var table = shape.Table!;
        table.Rows.Should().HaveCount(3, "undo must remove the inserted row");

        var anchor = table.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(2);
        anchor.RowSpan.Should().Be(2, "undo must restore the pre-insert RowSpan exactly");
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();

        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[1].VMerge.Should().BeFalse();

        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        table.Rows[1].Cells[1].VMerge.Should().BeTrue();

        table.Rows[2].Cells[0].HMerge.Should().BeFalse();
        table.Rows[2].Cells[0].VMerge.Should().BeFalse();
    }

    [Fact]
    public void R139a_Sibling_InsertRow_SingleColumnVSpan_StillWidensExactlyOnce()
    {
        // Sibling/regression guard: the plain 1-column vertical-merge case (no horizontal
        // component at all) must still behave exactly as before — the anchor-walk added for
        // the 2-D fix must be a no-op when there is nothing to walk past (GridSpan==1 throughout).
        var (p, bus, shape) = MakeVMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 2, 1));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells[0].RowSpan.Should().Be(3, "single-column anchor must still widen by exactly one");
        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        table.Rows[2].Cells[0].VMerge.Should().BeTrue();
    }

    [Fact]
    public void R139a_Sibling_InsertRow_AtSpanBoundaryOf2DMerge_AddsFullyIndependentRow()
    {
        // Sibling/regression guard: inserting AFTER the 2-D merge's vertical span (row 2, the
        // boundary) must add an ordinary independent row, not a spurious VMerge continuation —
        // the anchor-walk fix must not over-fire outside the true span.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableRowCommand(0, 4, 2));

        var table = shape.Table!;
        table.Rows.Should().HaveCount(4);
        // Anchor RowSpan must stay 2 — the insertion point (row 2) is the boundary, not inside.
        table.Rows[0].Cells[0].RowSpan.Should().Be(2);
        table.Rows[2].Cells[0].VMerge.Should().BeFalse();
        table.Rows[2].Cells[1].VMerge.Should().BeFalse();
        table.Rows[2].Cells[2].VMerge.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // R139c regression tests — InsertTableColumnCommand + 2-D merges (mirror of R139a,
    // which fixed the same bug for InsertTableRowCommand).
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void R139c_InsertColumn_Inside2DMerge_WidensAnchorOnceAndMarksAllSpannedRowsVMerge()
    {
        // 2×2 anchor merge at (0,0)-(1,1) in a 3×3 table (see Make2DMergedTable doc comment).
        // Insert a column at index 1 — strictly inside the anchor's horizontal span (cols 0-1).
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 4, 1));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(4);

        // The true anchor (row 0) must have its GridSpan widened exactly once: 2 -> 3.
        var anchor = table.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(3, "the anchor's horizontal span must grow by exactly one column");
        anchor.RowSpan.Should().Be(2, "the anchor's vertical span must be untouched by a column insert");
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();

        // Row 0 must carry an HMerge continuation at the inserted slot (index 1) and the
        // original HMerge continuation now shifted to index 2.
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[2].HMerge.Should().BeTrue();
        CellText(shape, 0, 3).Should().Be("C00", "the independent cell must shift right, unaffected");

        // Row 1 (the VMerge continuation row of the 2-D merge) must carry a VMerge continuation
        // in BOTH the original column 1 AND the newly-inserted column — a real user reaching
        // this through EditingSession.InsertColumnRight after clicking anywhere in the merge
        // (which always resolves to the anchor) hits this row. Leaving the inserted cell as an
        // ordinary independent cell here is exactly the grid corruption this test guards against.
        table.Rows[1].Cells[0].VMerge.Should().BeTrue("column 0 is covered by the widened anchor");
        table.Rows[1].Cells[1].VMerge.Should().BeTrue(
            "the newly-inserted column is ALSO covered by the widened anchor's RowSpan=2 rectangle");
        table.Rows[1].Cells[2].VMerge.Should().BeTrue("the original column-1 VMerge cell, now shifted right");
        table.Rows[1].Cells[1].HMerge.Should().BeFalse("a VMerge continuation must never itself be HMerge");
        CellText(shape, 1, 3).Should().Be("C10");

        // Row 2 (fully independent, outside the merge) is unaffected except for the new column.
        table.Rows[2].Cells[0].VMerge.Should().BeFalse();
        table.Rows[2].Cells[1].VMerge.Should().BeFalse();
        table.Rows[2].Cells[1].HMerge.Should().BeFalse();

        // Grid integrity: every row has exactly ColumnWidthsEmu.Count cells.
        int gridWidth = table.ColumnWidthsEmu.Count;
        foreach (var row in table.Rows)
            row.Cells.Should().HaveCount(gridWidth);
    }

    [Fact]
    public void R139c_InsertColumn_Inside2DMerge_Undo_RestoresExactOriginalGrid()
    {
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 4, 1));
        bus.Undo();

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(3, "undo must remove the inserted column");

        var anchor = table.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(2, "undo must restore the pre-insert GridSpan exactly");
        anchor.RowSpan.Should().Be(2);
        anchor.HMerge.Should().BeFalse();
        anchor.VMerge.Should().BeFalse();

        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[1].VMerge.Should().BeFalse();

        table.Rows[1].Cells[0].VMerge.Should().BeTrue();
        table.Rows[1].Cells[1].VMerge.Should().BeTrue();

        table.Rows[2].Cells[0].HMerge.Should().BeFalse();
        table.Rows[2].Cells[0].VMerge.Should().BeFalse();
    }

    [Fact]
    public void R139c_Sibling_InsertColumn_AtSpanBoundaryOf2DMerge_AddsFullyIndependentColumn()
    {
        // Sibling/regression guard: inserting AFTER the 2-D merge's horizontal span (col 2, the
        // boundary) must add an ordinary independent column, not a spurious VMerge/HMerge
        // continuation — the anchor-walk fix must not over-fire outside the true span.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new InsertTableColumnCommand(0, 4, 2));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(4);
        // Anchor GridSpan must stay 2 — the insertion point (col 2) is the boundary, not inside.
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[2].HMerge.Should().BeFalse();
        table.Rows[1].Cells[2].VMerge.Should().BeFalse();
        table.Rows[2].Cells[2].VMerge.Should().BeFalse();
    }

    [Fact]
    public void R139c_DeleteColumn_Inside2DMerge_DecrementsAnchorAndRemovesVMergeCellCleanly()
    {
        // Sibling coverage: DeleteTableColumnCommand walks per-row same-index cell state
        // (HMerge for the anchor's own row, blind removal elsewhere) rather than needing an
        // anchor walk, because VMerge cells never carry GridSpan themselves — so deleting a
        // column inside a 2-D merge's horizontal span should already be safe. This test locks
        // that in as a regression guard rather than a fix.
        var (p, bus, shape) = Make2DMergedTable();
        bus.Execute(new DeleteTableColumnCommand(0, 4, 1));

        var table = shape.Table!;
        table.ColumnWidthsEmu.Should().HaveCount(2);

        var anchor = table.Rows[0].Cells[0];
        anchor.GridSpan.Should().Be(1, "the anchor's horizontal span must shrink by exactly one column");
        anchor.RowSpan.Should().Be(2, "the anchor's vertical span must be untouched by a column delete");
        anchor.HMerge.Should().BeFalse();

        table.Rows[1].Cells[0].VMerge.Should().BeTrue("column 0 is still covered by the shrunken anchor");
        CellText(shape, 1, 1).Should().Be("C10", "the surviving independent cell must shift left");

        int gridWidth = table.ColumnWidthsEmu.Count;
        foreach (var row in table.Rows)
            row.Cells.Should().HaveCount(gridWidth);
    }

    [Fact]
    public void R139b_InsertRow_ResyncsShapeExtentCyToActualRowHeightSum()
    {
        var (p, bus, shape) = MakeTable(rows: 3, cols: 2); // ExtentCyEmu starts at 457200*3
        long originalCy = shape.ExtentCyEmu;

        bus.Execute(new InsertTableRowCommand(0, 1, 3)); // append a 4th row, same height
        bus.Execute(new InsertTableRowCommand(0, 1, 4)); // append a 5th row, same height

        var table = shape.Table!;
        long expectedCy = table.Rows.Sum(r => r.HeightEmu);
        shape.ExtentCyEmu.Should().Be(expectedCy,
            "the graphicFrame extent must track the table's real content height after structural edits");
        shape.ExtentCyEmu.Should().BeGreaterThan(originalCy, "two extra rows must grow the declared extent");
        shape.ExtentCxEmu.Should().Be(table.ColumnWidthsEmu.Sum(), "column extent must be untouched by a row insert");
    }

    [Fact]
    public void R139b_InsertRow_Undo_RestoresOriginalShapeExtentCy()
    {
        var (p, bus, shape) = MakeTable(rows: 3, cols: 2);
        long originalCy = shape.ExtentCyEmu;

        bus.Execute(new InsertTableRowCommand(0, 1, 1));
        shape.ExtentCyEmu.Should().NotBe(originalCy, "sanity: the insert must have changed the extent");

        bus.Undo();
        shape.ExtentCyEmu.Should().Be(originalCy, "undo must resync the extent back to the pre-insert grid, not just leave the post-insert value behind");
    }

    [Fact]
    public void R139b_DeleteRow_ResyncsShapeExtentCy()
    {
        var (p, bus, shape) = MakeTable(rows: 4, cols: 2);
        bus.Execute(new DeleteTableRowCommand(0, 1, 0));

        var table = shape.Table!;
        shape.ExtentCyEmu.Should().Be(table.Rows.Sum(r => r.HeightEmu));
    }

    [Fact]
    public void R139b_InsertColumn_ResyncsShapeExtentCx()
    {
        var (p, bus, shape) = MakeTable(rows: 2, cols: 3);
        bus.Execute(new InsertTableColumnCommand(0, 1, 1));

        var table = shape.Table!;
        shape.ExtentCxEmu.Should().Be(table.ColumnWidthsEmu.Sum());
        shape.ExtentCyEmu.Should().Be(table.Rows.Sum(r => r.HeightEmu), "row extent must be untouched by a column insert");
    }

    [Fact]
    public void R139b_DeleteColumn_ResyncsShapeExtentCx()
    {
        var (p, bus, shape) = MakeTable(rows: 2, cols: 3);
        bus.Execute(new DeleteTableColumnCommand(0, 1, 0));

        var table = shape.Table!;
        shape.ExtentCxEmu.Should().Be(table.ColumnWidthsEmu.Sum());
    }

    [Fact]
    public void R139b_SetRowHeight_ResyncsShapeExtentCy()
    {
        var (p, bus, shape) = MakeTable(rows: 2, cols: 2);
        bus.Execute(new SetTableRowHeightCommand(0, 1, 0, 914400L));

        var table = shape.Table!;
        shape.ExtentCyEmu.Should().Be(table.Rows.Sum(r => r.HeightEmu));
    }

    [Fact]
    public void R139b_SetColumnWidth_ResyncsShapeExtentCx()
    {
        var (p, bus, shape) = MakeTable(rows: 2, cols: 2);
        bus.Execute(new SetTableColumnWidthCommand(0, 1, 0, 1828800L));

        var table = shape.Table!;
        shape.ExtentCxEmu.Should().Be(table.ColumnWidthsEmu.Sum());
    }

    [Fact]
    public void R139b_Sibling_DistributeRows_TotalPreservingEdit_LeavesExtentCyUnchanged()
    {
        // Sibling/regression guard: DistributeTableRowsCommand redistributes height but
        // preserves the total, so resyncing the extent here must be a harmless no-op —
        // it must not, for example, accidentally drop or double-count a row's height.
        var (p, bus, shape) = MakeTable(rows: 3, cols: 2);
        long originalCy = shape.ExtentCyEmu;

        bus.Execute(new SetTableRowHeightCommand(0, 1, 0, 914400L)); // make heights uneven first
        bus.Execute(new DistributeTableRowsCommand(0, 1));

        var table = shape.Table!;
        shape.ExtentCyEmu.Should().Be(table.Rows.Sum(r => r.HeightEmu));
    }
}
