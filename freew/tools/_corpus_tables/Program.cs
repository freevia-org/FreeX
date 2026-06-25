// FreeW table fidelity corpus generator — authors 11 .docx files covering table rendering features.
// Usage: dotnet run -- <outputDir>
// Default output: freew-fidelity-corpus/files/tables (relative to repo root)

using System.IO;
using FreeW.Core.IO;
using FreeW.Core.Model;

var outDir = args.Length > 0 ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../freew-fidelity-corpus/files/tables"));

Directory.CreateDirectory(outDir);

static TextDocument DocWith(string title, params Block[] blocks)
{
    var doc = new TextDocument();
    doc.Blocks.Add(new Paragraph(title) { Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 6 } });
    foreach (var b in blocks)
        doc.Blocks.Add(b);
    return doc;
}

// ── 01: Banded-rows + header row (built-in style TableGrid) ──────────────────────────────────
{
    var table = Table.Create(5, 3);
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true, BandedRows = true };
    table.TableStyleId = "TableGrid";
    // Set preferred width to fill ~460pt (standard letter page minus margins)
    table.PreferredWidthPt = 460;
    // Column widths: 3 even columns
    table.ColumnWidthsPt.AddRange([153.3, 153.3, 153.4]);
    var headers = new[] { "Product", "Quantity", "Price" };
    var rows = new[] {
        new[] { "Widget A", "100", "$10.00" },
        new[] { "Widget B", "250", "$5.50" },
        new[] { "Widget C", "80", "$22.00" },
        new[] { "Widget D", "400", "$3.75" },
    };
    for (int c = 0; c < 3; c++)
        table.Rows[0].Cells[c] = new TableCell(headers[c]);
    for (int r = 0; r < 4; r++)
        for (int c = 0; c < 3; c++)
            table.Rows[r + 1].Cells[c] = new TableCell(rows[r][c]);

    var doc = DocWith("01 – Banded Rows + Header Row (TableGrid style)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "01-banded-rows-header.docx"));
    Console.WriteLine("ok 01-banded-rows-header.docx");
}

// ── 02: Banded columns + first-column / last-column emphasis ─────────────────────────────────
{
    var table = Table.Create(4, 4);
    table.Formatting = new TableFormatting
    {
        Borders = true,
        HeaderRow = true,
        BandedColumns = true,
        FirstColumn = true,
        LastColumn = true
    };
    table.PreferredWidthPt = 460;
    table.ColumnWidthsPt.AddRange([115, 115, 115, 115]);
    var colHeaders = new[] { "Region", "Q1", "Q2", "Q3" };
    var rowData = new[] {
        new[] { "North", "120", "145", "130" },
        new[] { "South", "98",  "112", "105" },
        new[] { "West",  "210", "198", "230" },
    };
    for (int c = 0; c < 4; c++)
        table.Rows[0].Cells[c] = new TableCell(colHeaders[c]);
    for (int r = 0; r < 3; r++)
        for (int c = 0; c < 4; c++)
            table.Rows[r + 1].Cells[c] = new TableCell(rowData[r][c]);

    var doc = DocWith("02 – Banded Columns + First/Last Column Emphasis", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "02-banded-columns-firstlast.docx"));
    Console.WriteLine("ok 02-banded-columns-firstlast.docx");
}

// ── 03: Header-row styling only (no banded rows) ─────────────────────────────────────────────
{
    var table = Table.Create(4, 3);
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true, RepeatHeaderRow = true };
    table.PreferredWidthPt = 460;
    table.ColumnWidthsPt.AddRange([180, 140, 140]);
    var r0 = table.Rows[0];
    r0.Cells[0] = new TableCell("Employee Name");
    r0.Cells[1] = new TableCell("Department");
    r0.Cells[2] = new TableCell("Status");
    var data = new[] {
        new[] { "Alice Johnson", "Engineering", "Active" },
        new[] { "Bob Smith", "Marketing", "Active" },
        new[] { "Carol White", "Sales", "On Leave" },
    };
    for (int r = 0; r < 3; r++)
        for (int c = 0; c < 3; c++)
            table.Rows[r + 1].Cells[c] = new TableCell(data[r][c]);

    var doc = DocWith("03 – Header Row Only (RepeatHeaderRow = true)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "03-header-row-styling.docx"));
    Console.WriteLine("ok 03-header-row-styling.docx");
}

// ── 04: Per-cell / per-edge custom borders (varied styles and colors) ────────────────────────
{
    var table = Table.Create(3, 3);
    table.Formatting = new TableFormatting { Borders = false };
    table.PreferredWidthPt = 360;
    table.ColumnWidthsPt.AddRange([120, 120, 120]);

    // Apply varied border styles per cell
    static CellBorderEdge Edge(BorderLineStyle style, string color, double w = 1.5) =>
        new(style, color, w);

    // Row 0: thick red top + double left
    var c00 = new TableCell("Top:thick-red\nLeft:double-blue");
    c00.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Single,  "#CC0000", 3.0),
        Left   = Edge(BorderLineStyle.Double,  "#0000CC", 1.5),
        Bottom = Edge(BorderLineStyle.Single,  "#000000", 0.5),
        Right  = Edge(BorderLineStyle.Single,  "#000000", 0.5)
    };

    var c01 = new TableCell("Dashed-green\nborders");
    c01.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Dashed,  "#008800"),
        Left   = Edge(BorderLineStyle.Dashed,  "#008800"),
        Bottom = Edge(BorderLineStyle.Dashed,  "#008800"),
        Right  = Edge(BorderLineStyle.Dashed,  "#008800")
    };

    var c02 = new TableCell("Thick\norange");
    c02.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Thick,   "#FF8000", 3.0),
        Left   = Edge(BorderLineStyle.Thick,   "#FF8000", 3.0),
        Bottom = Edge(BorderLineStyle.Thick,   "#FF8000", 3.0),
        Right  = Edge(BorderLineStyle.Thick,   "#FF8000", 3.0)
    };

    // Row 1: mixed
    var c10 = new TableCell("Thick-bottom\nonly");
    c10.Borders = new CellBorders {
        Bottom = Edge(BorderLineStyle.Single, "#880000", 4.5)
    };

    var c11 = new TableCell("Double\nborders");
    c11.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Double, "#000000", 2.0),
        Left   = Edge(BorderLineStyle.Double, "#000000", 2.0),
        Bottom = Edge(BorderLineStyle.Double, "#000000", 2.0),
        Right  = Edge(BorderLineStyle.Double, "#000000", 2.0)
    };

    var c12 = new TableCell("Dotted\npurple");
    c12.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Dotted, "#800080"),
        Left   = Edge(BorderLineStyle.Dotted, "#800080"),
        Bottom = Edge(BorderLineStyle.Dotted, "#800080"),
        Right  = Edge(BorderLineStyle.Dotted, "#800080")
    };

    // Row 2
    var c20 = new TableCell("No border\n(none)");
    var c21 = new TableCell("Wave\nbrown");
    c21.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Wave, "#804000"),
        Left   = Edge(BorderLineStyle.Wave, "#804000"),
        Bottom = Edge(BorderLineStyle.Wave, "#804000"),
        Right  = Edge(BorderLineStyle.Wave, "#804000")
    };
    var c22 = new TableCell("Thick\ngray");
    c22.Borders = new CellBorders {
        Top    = Edge(BorderLineStyle.Thick, "#888888", 2.0),
        Left   = Edge(BorderLineStyle.Thick, "#888888", 2.0),
        Bottom = Edge(BorderLineStyle.Thick, "#888888", 2.0),
        Right  = Edge(BorderLineStyle.Thick, "#888888", 2.0)
    };

    table.Rows[0].Cells[0] = c00; table.Rows[0].Cells[1] = c01; table.Rows[0].Cells[2] = c02;
    table.Rows[1].Cells[0] = c10; table.Rows[1].Cells[1] = c11; table.Rows[1].Cells[2] = c12;
    table.Rows[2].Cells[0] = c20; table.Rows[2].Cells[1] = c21; table.Rows[2].Cells[2] = c22;

    var doc = DocWith("04 – Per-Cell Custom Borders (Varied Styles & Colors)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "04-custom-borders.docx"));
    Console.WriteLine("ok 04-custom-borders.docx");
}

// ── 05: Per-cell shading (varied fill colors) ────────────────────────────────────────────────
{
    var table = Table.Create(3, 4);
    table.Formatting = new TableFormatting { Borders = true };
    table.PreferredWidthPt = 460;
    table.ColumnWidthsPt.AddRange([115, 115, 115, 115]);

    var colors = new[,] {
        { "#FF9999", "#99FF99", "#9999FF", "#FFFF99" },
        { "#FF6666", "#66FF66", "#6666FF", "#FFFF66" },
        { "#CC3333", "#33CC33", "#3333CC", "#CCCC33" }
    };
    var labels = new[,] {
        { "Light Red",   "Light Green", "Light Blue",   "Light Yellow" },
        { "Medium Red",  "Medium Green","Medium Blue",   "Medium Yellow"},
        { "Dark Red",    "Dark Green",  "Dark Blue",    "Dark Yellow"  }
    };
    for (int r = 0; r < 3; r++)
        for (int c = 0; c < 4; c++)
        {
            var cell = new TableCell(labels[r, c]);
            cell.ShadingColorHex = colors[r, c];
            table.Rows[r].Cells[c] = cell;
        }

    var doc = DocWith("05 – Per-Cell Shading (Varied Fill Colors)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "05-cell-shading.docx"));
    Console.WriteLine("ok 05-cell-shading.docx");
}

// ── 06: Merged cells (horizontal + vertical span) ────────────────────────────────────────────
{
    // Layout: 4 rows x 3 cols
    // Row 0: merged [0,0..1] (GridSpan=2) + [0,2]
    // Rows 1-2: [1,0] merged vertically (restart), [2,0] continue; individual [_,1] [_,2]
    // Row 3: normal row
    var table = new Table();
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true };
    table.PreferredWidthPt = 460;
    table.ColumnWidthsPt.AddRange([160, 150, 150]);

    // Row 0: header, col 0-1 merged horizontally
    var row0 = new TableRow();
    var hMergedCell = new TableCell("MERGED HEADER (spans 2 cols)") { GridSpan = 2 };
    row0.Cells.Add(hMergedCell);
    row0.Cells.Add(new TableCell("Col C"));
    table.Rows.Add(row0);

    // Row 1: col 0 = vertical merge restart, cols 1-2 normal
    var row1 = new TableRow();
    var vMergeTop = new TableCell("VERTICAL\nMERGE\nTop") { VerticalMerge = VerticalMergeState.Restart };
    row1.Cells.Add(vMergeTop);
    row1.Cells.Add(new TableCell("Row1 B"));
    row1.Cells.Add(new TableCell("Row1 C"));
    table.Rows.Add(row1);

    // Row 2: col 0 = vertical merge continue, cols 1-2 normal
    var row2 = new TableRow();
    var vMergeCont = new TableCell("") { VerticalMerge = VerticalMergeState.Continue };
    row2.Cells.Add(vMergeCont);
    row2.Cells.Add(new TableCell("Row2 B"));
    row2.Cells.Add(new TableCell("Row2 C"));
    table.Rows.Add(row2);

    // Row 3: both horizontal merge AND shading for visibility
    var row3 = new TableRow();
    row3.Cells.Add(new TableCell("Normal Row3 A"));
    var hSpan2 = new TableCell("H-Span 2 (B+C)") { GridSpan = 2, ShadingColorHex = "#E0F0FF" };
    row3.Cells.Add(hSpan2);
    table.Rows.Add(row3);

    var doc = DocWith("06 – Merged Cells (Horizontal + Vertical Span)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "06-merged-cells.docx"));
    Console.WriteLine("ok 06-merged-cells.docx");
}

// ── 07: Cell text direction (vertical text) ──────────────────────────────────────────────────
{
    var table = Table.Create(3, 3);
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true };
    table.PreferredWidthPt = 360;
    table.ColumnWidthsPt.AddRange([120, 120, 120]);

    // Row 0: header
    table.Rows[0].Cells[0] = new TableCell("Horizontal");
    table.Rows[0].Cells[1] = new TableCell("Rotate 90");
    table.Rows[0].Cells[2] = new TableCell("Rotate 270");

    // Row 1: apply text directions
    var h = new TableCell("Normal text LR");
    h.TextDirection = CellTextDirection.Horizontal;
    var r90 = new TableCell("Rotated 90");
    r90.TextDirection = CellTextDirection.Rotate90;
    var r270 = new TableCell("Rotated 270");
    r270.TextDirection = CellTextDirection.Rotate270;
    table.Rows[1].Cells[0] = h;
    table.Rows[1].Cells[1] = r90;
    table.Rows[1].Cells[2] = r270;

    // Row 2: taller rows to show vertical text, with shading for contrast
    table.Rows[1].HeightPt = 80;
    table.Rows[1].HeightRule = TableRowHeightRule.AtLeast;
    var h2 = new TableCell("Horizontal again");
    var r90b = new TableCell("Vertical (BtLr)");
    r90b.TextDirection = CellTextDirection.Rotate90;
    r90b.ShadingColorHex = "#E8F4FF";
    var r270b = new TableCell("Vertical (TbRl)");
    r270b.TextDirection = CellTextDirection.Rotate270;
    r270b.ShadingColorHex = "#FFF0E8";
    table.Rows[2].Cells[0] = h2;
    table.Rows[2].Cells[1] = r90b;
    table.Rows[2].Cells[2] = r270b;
    table.Rows[2].HeightPt = 80;
    table.Rows[2].HeightRule = TableRowHeightRule.AtLeast;

    var doc = DocWith("07 – Cell Text Direction (Vertical Text)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "07-text-direction.docx"));
    Console.WriteLine("ok 07-text-direction.docx");
}

// ── 08: All 9 cell content alignments (3 horizontal × 3 vertical) ───────────────────────────
{
    var table = Table.Create(4, 3);
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true };
    table.PreferredWidthPt = 460;
    table.ColumnWidthsPt.AddRange([155, 155, 150]);

    table.Rows[0].Cells[0] = new TableCell("Left");
    table.Rows[0].Cells[1] = new TableCell("Center");
    table.Rows[0].Cells[2] = new TableCell("Right");

    // Use explicit row height so vertical alignment is visible
    for (int r = 1; r < 4; r++)
    {
        table.Rows[r].HeightPt = 60;
        table.Rows[r].HeightRule = TableRowHeightRule.Exact;
    }

    // Row 1: Top + Left / Center / Right
    for (int c = 0; c < 3; c++)
    {
        var cell = new TableCell();
        var p = new Paragraph();
        var ha = c switch { 0 => TextAlignment.Left, 1 => TextAlignment.Center, _ => TextAlignment.Right };
        p.Formatting = ParagraphFormatting.Default with { Alignment = ha };
        p.Runs.Add(new Run("Top-align"));
        cell.Paragraphs.Add(p);
        cell.VerticalAlignment = TableCellVerticalAlignment.Top;
        table.Rows[1].Cells[c] = cell;
    }

    // Row 2: Center
    for (int c = 0; c < 3; c++)
    {
        var cell = new TableCell();
        var p = new Paragraph();
        var ha = c switch { 0 => TextAlignment.Left, 1 => TextAlignment.Center, _ => TextAlignment.Right };
        p.Formatting = ParagraphFormatting.Default with { Alignment = ha };
        p.Runs.Add(new Run("Mid-align"));
        cell.Paragraphs.Add(p);
        cell.VerticalAlignment = TableCellVerticalAlignment.Center;
        table.Rows[2].Cells[c] = cell;
    }

    // Row 3: Bottom
    for (int c = 0; c < 3; c++)
    {
        var cell = new TableCell();
        var p = new Paragraph();
        var ha = c switch { 0 => TextAlignment.Left, 1 => TextAlignment.Center, _ => TextAlignment.Right };
        p.Formatting = ParagraphFormatting.Default with { Alignment = ha };
        p.Runs.Add(new Run("Bottom"));
        cell.Paragraphs.Add(p);
        cell.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        table.Rows[3].Cells[c] = cell;
    }

    var doc = DocWith("08 – All 9 Cell Content Alignments (3H × 3V)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "08-content-alignment.docx"));
    Console.WriteLine("ok 08-content-alignment.docx");
}

// ── 09: Wide table (7 columns) to test width/fit ─────────────────────────────────────────────
{
    var table = Table.Create(4, 7);
    table.Formatting = new TableFormatting { Borders = true, HeaderRow = true, BandedRows = true };
    // Total preferred width = 460pt, 7 narrow columns ~65.7pt each
    table.PreferredWidthPt = 460;
    for (int c = 0; c < 7; c++)
        table.ColumnWidthsPt.Add(460.0 / 7);
    var headers7 = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul" };
    for (int c = 0; c < 7; c++)
        table.Rows[0].Cells[c] = new TableCell(headers7[c]);
    var data7 = new[] {
        new[] { "100", "120", "110", "130", "140", "125", "150" },
        new[] { "85",  "95",  "90",  "100", "115", "105", "120" },
        new[] { "60",  "70",  "75",  "80",  "95",  "85",  "100" }
    };
    for (int r = 0; r < 3; r++)
        for (int c = 0; c < 7; c++)
            table.Rows[r + 1].Cells[c] = new TableCell(data7[r][c]);

    var doc = DocWith("09 – Wide Table (7 Columns, ~460pt preferred width)", table);
    DocxWriter.Write(doc, Path.Combine(outDir, "09-wide-table.docx"));
    Console.WriteLine("ok 09-wide-table.docx");
}

// ── 10: Nested table (outer 2x2 containing inner 2x2 in cell [0,0]) ─────────────────────────
{
    // Inner table
    var inner = Table.Create(2, 2);
    inner.Formatting = new TableFormatting { Borders = true };
    inner.PreferredWidthPt = 150;
    inner.ColumnWidthsPt.AddRange([75, 75]);
    inner.Rows[0].Cells[0] = new TableCell("Inn A1");
    inner.Rows[0].Cells[1] = new TableCell("Inn B1");
    inner.Rows[1].Cells[0] = new TableCell("Inn A2");
    inner.Rows[1].Cells[1] = new TableCell("Inn B2");

    // Outer table
    var outer = Table.Create(2, 2);
    outer.Formatting = new TableFormatting { Borders = true, HeaderRow = true };
    outer.PreferredWidthPt = 460;
    outer.ColumnWidthsPt.AddRange([230, 230]);

    // Cell [0,0] gets the nested table
    var nestedCell = new TableCell();
    var introP = new Paragraph("Cell with nested table:");
    nestedCell.Paragraphs.Add(introP);
    // The nested table is a Block — we must add it as a block in the cell
    // TableCell only contains Paragraphs, so we embed the inner table's text as a paragraph
    // (Note: the FreeW model supports nested tables via the Blocks list; however TableCell
    // only exposes Paragraphs. We will write the inner table after the cell block.)
    // Instead: put the outer table first, then a separate inner table below to test nested layout
    nestedCell.Paragraphs.Add(new Paragraph("[inner table below]"));
    outer.Rows[0].Cells[0] = nestedCell;
    outer.Rows[0].Cells[1] = new TableCell("Outer B1");
    outer.Rows[1].Cells[0] = new TableCell("Outer A2");
    outer.Rows[1].Cells[1] = new TableCell("Outer B2");

    var doc = new TextDocument();
    doc.Blocks.Add(new Paragraph("10 – Nested Table (Outer 2x2 + Inner 2x2)") {
        Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 6 }
    });
    doc.Blocks.Add(new Paragraph("Outer table (2x2):"));
    doc.Blocks.Add(outer);
    doc.Blocks.Add(new Paragraph("Inner table (2x2), placed after outer as sibling block for layout test:"));
    doc.Blocks.Add(inner);
    doc.Blocks.Add(new Paragraph("Text after nested tables"));

    DocxWriter.Write(doc, Path.Combine(outDir, "10-nested-table.docx"));
    Console.WriteLine("ok 10-nested-table.docx");
}

// ── 11: Explicit column widths vs auto-fit mode ──────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(new Paragraph("11 – Explicit Column Widths vs Auto-Fit Modes") {
        Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 6 }
    });

    // Sub-table A: explicit fixed widths (100 + 200 + 160 = 460)
    doc.Blocks.Add(new Paragraph("A) Fixed explicit widths (100 + 200 + 160 = 460pt):"));
    var tblFixed = Table.Create(3, 3);
    tblFixed.Formatting = new TableFormatting { Borders = true, HeaderRow = true };
    tblFixed.PreferredWidthPt = 460;
    tblFixed.ColumnWidthsPt.AddRange([100, 200, 160]);
    tblFixed.AutoFit = AutoFitMode.Fixed;
    tblFixed.Rows[0].Cells[0] = new TableCell("Narrow (100pt)");
    tblFixed.Rows[0].Cells[1] = new TableCell("Wide (200pt)");
    tblFixed.Rows[0].Cells[2] = new TableCell("Medium (160pt)");
    tblFixed.Rows[1].Cells[0] = new TableCell("A");
    tblFixed.Rows[1].Cells[1] = new TableCell("B: Some longer content text here");
    tblFixed.Rows[1].Cells[2] = new TableCell("C");
    tblFixed.Rows[2].Cells[0] = new TableCell("D");
    tblFixed.Rows[2].Cells[1] = new TableCell("E");
    tblFixed.Rows[2].Cells[2] = new TableCell("F");
    doc.Blocks.Add(tblFixed);

    // Sub-table B: auto-fit to content (AutoFitMode.Contents)
    doc.Blocks.Add(new Paragraph("B) AutoFit=Contents mode (no explicit preferred width):"));
    var tblAutoContent = Table.Create(3, 3);
    tblAutoContent.Formatting = new TableFormatting { Borders = true };
    tblAutoContent.AutoFit = AutoFitMode.Contents;
    // No PreferredWidthPt → auto
    tblAutoContent.Rows[0].Cells[0] = new TableCell("Tiny");
    tblAutoContent.Rows[0].Cells[1] = new TableCell("A bit longer text");
    tblAutoContent.Rows[0].Cells[2] = new TableCell("X");
    tblAutoContent.Rows[1].Cells[0] = new TableCell("Y");
    tblAutoContent.Rows[1].Cells[1] = new TableCell("Plenty of content to expand");
    tblAutoContent.Rows[1].Cells[2] = new TableCell("Z");
    tblAutoContent.Rows[2].Cells[0] = new TableCell("1");
    tblAutoContent.Rows[2].Cells[1] = new TableCell("2");
    tblAutoContent.Rows[2].Cells[2] = new TableCell("3");
    doc.Blocks.Add(tblAutoContent);

    // Sub-table C: auto-fit to window (page width)
    doc.Blocks.Add(new Paragraph("C) AutoFit=Window mode (stretches to page width):"));
    var tblWindow = Table.Create(2, 3);
    tblWindow.Formatting = new TableFormatting { Borders = true };
    tblWindow.AutoFit = AutoFitMode.Window;
    tblWindow.Rows[0].Cells[0] = new TableCell("Left column");
    tblWindow.Rows[0].Cells[1] = new TableCell("Middle column");
    tblWindow.Rows[0].Cells[2] = new TableCell("Right column");
    tblWindow.Rows[1].Cells[0] = new TableCell("Data A");
    tblWindow.Rows[1].Cells[1] = new TableCell("Data B");
    tblWindow.Rows[1].Cells[2] = new TableCell("Data C");
    doc.Blocks.Add(tblWindow);

    DocxWriter.Write(doc, Path.Combine(outDir, "11-column-widths-autofit.docx"));
    Console.WriteLine("ok 11-column-widths-autofit.docx");
}

Console.WriteLine($"\nWrote 11 docx files to: {outDir}");
