// _corpus_charts — Generates the FreeW visual-fidelity corpus for charts, SmartArt, and icons/SVG.
// Produces 12 .docx files under freew-fidelity-corpus/files/charts/ using the FreeW model + writer.
// Usage: dotnet <dll> [outDir]
// Default outDir: freew-fidelity-corpus/files/charts (relative to the repo root, resolved from the exe path)

using System.IO;
using Free.ToolsShared;
using FreeW.Core.IO;
using FreeW.Core.Model;

string outDir = args.Length > 0
    ? args[0]
    : ResolveDefaultOutDir();

Directory.CreateDirectory(outDir);
Console.WriteLine($"Writing corpus to: {outDir}");

int written = 0;

// ── File 01: column-chart.docx ──────────────────────────────────────────────────────────────────
{
    var chart = Chart.Create(
        ChartKind.Column,
        categories: ["Q1", "Q2", "Q3", "Q4"],
        values:     [42, 67, 58, 75],
        seriesName: "Revenue",
        title:      "Quarterly Revenue");
    chart.WidthPt = 360; chart.HeightPt = 216;
    Write("01-column-chart.docx", "Column Chart — Quarterly Revenue", chart);
}

// ── File 02: bar-chart.docx ─────────────────────────────────────────────────────────────────────
{
    var chart = Chart.Create(
        ChartKind.Bar,
        categories: ["North", "South", "East", "West"],
        values:     [31, 52, 44, 38],
        seriesName: "Units Sold",
        title:      "Sales by Region");
    chart.WidthPt = 360; chart.HeightPt = 216;
    Write("02-bar-chart.docx", "Bar Chart — Sales by Region", chart);
}

// ── File 03: line-chart.docx ────────────────────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    var para = new Paragraph();
    para.Runs.Add(new Run("Line Chart — Monthly Trend", new RunFormatting { Bold = true, FontSizePt = 14 }));
    doc.Blocks.Add(para);

    var chart = new Chart
    {
        Kind = ChartKind.Line,
        Title = "Monthly Temperature",
        WidthPt = 360,
        HeightPt = 216,
        ShowLegend = true,
    };
    chart.Categories.AddRange(["Jan", "Feb", "Mar", "Apr", "May", "Jun"]);
    chart.Series.Add(new ChartSeries("City A", [4, 6, 12, 18, 22, 25]));
    chart.Series.Add(new ChartSeries("City B", [1, 3,  8, 14, 19, 23]));

    var chartPara = new Paragraph();
    chartPara.Runs.Add(Run.FromChart(chart));
    doc.Blocks.Add(chartPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "03-line-chart.docx"));
    Console.WriteLine($"  wrote 03-line-chart.docx");
    written++;
}

// ── File 04: pie-chart.docx ─────────────────────────────────────────────────────────────────────
{
    var chart = Chart.Create(
        ChartKind.Pie,
        categories: ["Apple", "Banana", "Cherry", "Date", "Elderberry"],
        values:     [30, 25, 20, 15, 10],
        seriesName: "Fruit",
        title:      "Fruit Distribution");
    chart.ShowLegend = true;
    chart.WidthPt = 300; chart.HeightPt = 240;
    Write("04-pie-chart.docx", "Pie Chart — Fruit Distribution", chart);
}

// ── File 05: scatter-chart.docx ─────────────────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("Scatter Chart — Height vs Weight", 14));

    var chart = new Chart
    {
        Kind = ChartKind.Scatter,
        Title = "Height vs Weight",
        WidthPt = 360,
        HeightPt = 216,
        ShowLegend = false,
        CategoryAxisTitle = "Height (cm)",
        ValueAxisTitle = "Weight (kg)",
    };
    // Categories supply x-values for Scatter
    chart.Categories.AddRange(["155", "160", "165", "170", "175", "180", "185"]);
    chart.Series.Add(new ChartSeries("Sample", [52, 58, 63, 68, 74, 80, 86]));

    var chartPara = new Paragraph();
    chartPara.Runs.Add(Run.FromChart(chart));
    doc.Blocks.Add(chartPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "05-scatter-chart.docx"));
    Console.WriteLine($"  wrote 05-scatter-chart.docx");
    written++;
}

// ── File 06: chart-styled-legend-title-labels.docx ──────────────────────────────────────────────
{
    // Column chart with Style 5 (gridlines + data labels), colorful2 palette, QuickLayout 5
    // (title + legend + data labels), axis titles
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("Styled Chart — Non-default Style, Legend, Title, Data Labels", 13));

    var chart = new Chart
    {
        Kind = ChartKind.Column,
        Title = "Product Performance",
        WidthPt = 432,
        HeightPt = 252,
        ShowLegend = true,
        StyleId = 5,
        ColorSchemeId = "colorful2",
        QuickLayoutId = 5,
        CategoryAxisTitle = "Product",
        ValueAxisTitle = "Score",
    };
    chart.Categories.AddRange(["Alpha", "Beta", "Gamma", "Delta"]);
    chart.Series.Add(new ChartSeries("2024", [78, 65, 90, 55]));
    chart.Series.Add(new ChartSeries("2025", [82, 70, 88, 62]));

    var chartPara = new Paragraph();
    chartPara.Runs.Add(Run.FromChart(chart));
    doc.Blocks.Add(chartPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "06-chart-styled-legend-title-labels.docx"));
    Console.WriteLine($"  wrote 06-chart-styled-legend-title-labels.docx");
    written++;
}

// ── File 07: chart-color-theme.docx ─────────────────────────────────────────────────────────────
{
    // Line chart with mono-blue color scheme (monochromatic variation) and Style 3 (plot area fill)
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("Chart — Monochromatic Color Theme", 13));

    var chart = new Chart
    {
        Kind = ChartKind.Line,
        Title = "Annual Growth",
        WidthPt = 360,
        HeightPt = 216,
        ShowLegend = true,
        StyleId = 3,
        ColorSchemeId = "mono-blue",
    };
    chart.Categories.AddRange(["2020", "2021", "2022", "2023", "2024"]);
    chart.Series.Add(new ChartSeries("Series A", [12, 18, 15, 24, 30]));
    chart.Series.Add(new ChartSeries("Series B", [8, 11, 14, 19, 25]));
    chart.Series.Add(new ChartSeries("Series C", [5, 7, 10, 13, 18]));

    var chartPara = new Paragraph();
    chartPara.Runs.Add(Run.FromChart(chart));
    doc.Blocks.Add(chartPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "07-chart-color-theme.docx"));
    Console.WriteLine($"  wrote 07-chart-color-theme.docx");
    written++;
}

// ── File 08: smartart-list.docx ─────────────────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("SmartArt — Basic List", 13));

    var sa = SmartArt.Create(SmartArtKind.List,
        ["Requirements Gathering", "Design", "Implementation", "Testing", "Deployment"]);
    sa.LayoutId = "list1";
    sa.WidthPt = 468; sa.HeightPt = 216;

    var saPara = new Paragraph();
    saPara.Runs.Add(Run.FromSmartArt(sa));
    doc.Blocks.Add(saPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "08-smartart-list.docx"));
    Console.WriteLine($"  wrote 08-smartart-list.docx");
    written++;
}

// ── File 09: smartart-process.docx ─────────────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("SmartArt — Basic Process", 13));

    var sa = SmartArt.Create(SmartArtKind.Process,
        ["Idea", "Prototype", "Review", "Launch"]);
    sa.LayoutId = "process1";
    sa.WidthPt = 468; sa.HeightPt = 180;

    var saPara = new Paragraph();
    saPara.Runs.Add(Run.FromSmartArt(sa));
    doc.Blocks.Add(saPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "09-smartart-process.docx"));
    Console.WriteLine($"  wrote 09-smartart-process.docx");
    written++;
}

// ── File 10: smartart-hierarchy-cycle.docx ──────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("SmartArt — Hierarchy + Cycle", 13));

    // Hierarchy
    var hierarchy = new SmartArt { Kind = SmartArtKind.Hierarchy, LayoutId = "hierarchy1", WidthPt = 432, HeightPt = 200 };
    var ceo = new SmartArtNode("CEO");
    ceo.AddChild("CFO");
    ceo.AddChild("CTO");
    ceo.AddChild("COO");
    hierarchy.Nodes.Add(ceo);

    var hierPara = new Paragraph();
    hierPara.Runs.Add(Run.FromSmartArt(hierarchy));
    doc.Blocks.Add(hierPara);

    // Cycle
    var cycle = SmartArt.Create(SmartArtKind.List,
        ["Plan", "Do", "Check", "Act"]);
    cycle.LayoutId = "cycle1";
    cycle.WidthPt = 280; cycle.HeightPt = 200;

    var cyclePara = new Paragraph();
    cyclePara.Runs.Add(Run.FromSmartArt(cycle));
    doc.Blocks.Add(cyclePara);

    DocxWriter.Write(doc, Path.Combine(outDir, "10-smartart-hierarchy-cycle.docx"));
    Console.WriteLine($"  wrote 10-smartart-hierarchy-cycle.docx");
    written++;
}

// ── File 11: smartart-styled-color.docx ─────────────────────────────────────────────────────────
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("SmartArt — Style + Color Variation", 13));

    // Process with colorful2 + intense1 style
    var sa = SmartArt.Create(SmartArtKind.Process,
        ["Discover", "Define", "Develop", "Deliver"]);
    sa.LayoutId = "process1";
    sa.ColorSchemeId = "colorful2";
    sa.StyleId = "intense1";
    sa.WidthPt = 468; sa.HeightPt = 180;

    var saPara = new Paragraph();
    saPara.Runs.Add(Run.FromSmartArt(sa));
    doc.Blocks.Add(saPara);

    // Radial with accent1 (monochromatic blue) + subtle1 style
    var radial = SmartArt.Create(SmartArtKind.List,
        ["Core", "Branch A", "Branch B", "Branch C", "Branch D"]);
    radial.LayoutId = "radial1";
    radial.ColorSchemeId = "accent1";
    radial.StyleId = "subtle1";
    radial.WidthPt = 300; radial.HeightPt = 220;

    var radPara = new Paragraph();
    radPara.Runs.Add(Run.FromSmartArt(radial));
    doc.Blocks.Add(radPara);

    DocxWriter.Write(doc, Path.Combine(outDir, "11-smartart-styled-color.docx"));
    Console.WriteLine($"  wrote 11-smartart-styled-color.docx");
    written++;
}

// ── File 12: icon-svg-graphic.docx ──────────────────────────────────────────────────────────────
{
    // Insert a simple SVG graphic as an InlineImage (the same path taken by FreeW's Insert > Icons).
    // We embed a hand-crafted 100x100 SVG (a blue circle with a white star) as PNG bytes (generated
    // inline so no external file dependency) to simulate an icon insertion.
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara("Inserted Icon / SVG Graphic", 13));

    var para1 = new Paragraph();
    para1.Runs.Add(new Run("The following element is a rasterized SVG icon (simulating Insert > Icons):"));
    doc.Blocks.Add(para1);

    // Build a simple 1×1 red pixel PNG as a stand-in (smallest valid PNG).
    // For a realistic test, use a minimal but recognisable SVG-style monochrome icon as a PNG.
    // We build a 64x64 blue square icon directly as a valid PNG in code.
    byte[] iconPng = BuildSimpleIconPng(64, 64);
    var iconImage = new InlineImage(iconPng, widthPt: 48, heightPt: 48);

    var iconPara = new Paragraph();
    iconPara.Runs.Add(Run.FromImage(iconImage));
    doc.Blocks.Add(iconPara);

    var para2 = new Paragraph();
    para2.Runs.Add(new Run("The icon above should appear as a crisp 48pt×48pt square image."));
    doc.Blocks.Add(para2);

    // Second icon: a simple geometric SVG-style image (a green circle on white, 80x80)
    byte[] iconPng2 = BuildSimpleIconPng2(80, 80);
    var iconImage2 = new InlineImage(iconPng2, widthPt: 60, heightPt: 60);

    var iconPara2 = new Paragraph();
    iconPara2.Runs.Add(Run.FromImage(iconImage2));
    doc.Blocks.Add(iconPara2);

    DocxWriter.Write(doc, Path.Combine(outDir, "12-icon-svg-graphic.docx"));
    Console.WriteLine($"  wrote 12-icon-svg-graphic.docx");
    written++;
}

Console.WriteLine($"Done. Wrote {written} + 3 (helpers) = {written + 3} files — wait, counting correct files:");
// Recount from filesystem
var files = Directory.GetFiles(outDir, "*.docx");
Console.WriteLine($"Total .docx files in {outDir}: {files.Length}");

// ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

static string ResolveDefaultOutDir()
{
    // Walk up from the exe to find the repo root (contains freew-fidelity-corpus/)
    var root = RepositoryRootLocator.FindByDirectoryMarker(
        AppContext.BaseDirectory,
        "freew-fidelity-corpus");
    return root is not null
        ? Path.Combine(root, "freew-fidelity-corpus", "files", "charts")
        : Path.Combine(AppContext.BaseDirectory, "charts-corpus-out");
}

void Write(string filename, string headingText, Chart chart)
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(HeadingPara(headingText, 13));
    var chartPara = new Paragraph();
    chartPara.Runs.Add(Run.FromChart(chart));
    doc.Blocks.Add(chartPara);
    DocxWriter.Write(doc, Path.Combine(outDir, filename));
    Console.WriteLine($"  wrote {filename}");
    written++;
}

static Paragraph HeadingPara(string text, double sizePt)
{
    var para = new Paragraph();
    para.Runs.Add(new Run(text, new RunFormatting { Bold = true, FontSizePt = sizePt }));
    return para;
}

// Build a minimal but recognizable PNG icon: a solid blue (#4472C4) square with a white cross,
// using raw PNG construction (no external libraries needed).
static byte[] BuildSimpleIconPng(int w, int h)
{
    // RGBA pixels: blue background with white cross
    var pixels = new byte[w * h * 4];
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int i = (y * w + x) * 4;
        bool cross = (x > w / 2 - 4 && x < w / 2 + 4) || (y > h / 2 - 4 && y < h / 2 + 4);
        if (cross) { pixels[i] = 255; pixels[i+1] = 255; pixels[i+2] = 255; pixels[i+3] = 255; }
        else       { pixels[i] = 0x44; pixels[i+1] = 0x72; pixels[i+2] = 0xC4; pixels[i+3] = 255; }
    }
    return EncodePng(w, h, pixels);
}

// Build a simple green circle PNG (#70AD47)
static byte[] BuildSimpleIconPng2(int w, int h)
{
    var pixels = new byte[w * h * 4];
    double cx = w / 2.0, cy = h / 2.0, r = Math.Min(w, h) / 2.0 - 2;
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        int i = (y * w + x) * 4;
        double dx = x - cx, dy = y - cy;
        bool inside = dx * dx + dy * dy <= r * r;
        if (inside) { pixels[i] = 0x70; pixels[i+1] = 0xAD; pixels[i+2] = 0x47; pixels[i+3] = 255; }
        else        { pixels[i] = 255;  pixels[i+1] = 255;  pixels[i+2] = 255;  pixels[i+3] = 255; }
    }
    return EncodePng(w, h, pixels);
}

static byte[] EncodePng(int w, int h, byte[] rgbaPixels)
{
    // Build a valid PNG from raw RGBA pixels using System.IO + zlib raw deflate.
    // PNG = signature + IHDR + IDAT (filtered raw) + IEND
    using var ms = new MemoryStream();

    // PNG signature
    ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);

    // IHDR (13 bytes): width, height, bitdepth=8, colortype=6 (RGBA), comp=0, filter=0, interlace=0
    var ihdr = new byte[13];
    WriteU32(ihdr, 0, (uint)w);
    WriteU32(ihdr, 4, (uint)h);
    ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
    WriteChunk(ms, "IHDR"u8.ToArray(), ihdr);

    // IDAT: filter each row with filter type 0 (None), then zlib-compress
    var rawRows = new byte[h * (1 + w * 4)];
    for (int y = 0; y < h; y++)
    {
        rawRows[y * (1 + w * 4)] = 0; // filter byte = None
        Array.Copy(rgbaPixels, y * w * 4, rawRows, y * (1 + w * 4) + 1, w * 4);
    }
    var compressed = ZlibCompress(rawRows);
    WriteChunk(ms, "IDAT"u8.ToArray(), compressed);

    // IEND
    WriteChunk(ms, "IEND"u8.ToArray(), []);

    return ms.ToArray();
}

static void WriteChunk(Stream s, byte[] type, byte[] data)
{
    var lenBytes = new byte[4];
    WriteU32(lenBytes, 0, (uint)data.Length);
    s.Write(lenBytes);
    s.Write(type);
    s.Write(data);
    // CRC32 over type + data
    uint crc = Crc32(type);
    crc = Crc32(data, crc);
    var crcBytes = new byte[4];
    WriteU32(crcBytes, 0, crc);
    s.Write(crcBytes);
}

static byte[] ZlibCompress(byte[] data)
{
    using var output = new MemoryStream();
    // zlib header: CMF=0x78 (deflate, window 32K), FLG=0x9C (no dict, check bits for CMF*256+FLG divisible by 31 → 0x789C)
    output.WriteByte(0x78);
    output.WriteByte(0x9C);
    using (var ds = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        ds.Write(data, 0, data.Length);
    // Adler-32 checksum
    uint s1 = 1, s2 = 0;
    foreach (var b in data) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
    uint adler = (s2 << 16) | s1;
    var adlerBytes = new byte[4];
    adlerBytes[0] = (byte)(adler >> 24); adlerBytes[1] = (byte)(adler >> 16);
    adlerBytes[2] = (byte)(adler >> 8);  adlerBytes[3] = (byte)(adler);
    output.Write(adlerBytes);
    return output.ToArray();
}

static void WriteU32(byte[] buf, int offset, uint value)
{
    buf[offset]   = (byte)(value >> 24);
    buf[offset+1] = (byte)(value >> 16);
    buf[offset+2] = (byte)(value >> 8);
    buf[offset+3] = (byte)value;
}

static uint Crc32(byte[] data, uint crc = 0xFFFFFFFF)
{
    foreach (var b in data)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
    }
    return crc ^ 0xFFFFFFFF;
}
