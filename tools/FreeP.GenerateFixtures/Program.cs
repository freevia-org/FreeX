/// <summary>
/// Generates corpus fixtures for FreeP animation features.
/// Usage: dotnet run --project tools/FreeP.GenerateFixtures -- [outputDir]
/// Default outputDir: tools/FreeP.RenderCompare/corpus
/// </summary>
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

var outDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "tools", "FreeP.RenderCompare", "corpus"));

Directory.CreateDirectory(outDir);

static SlideShape MakeShape(uint id, string name, DrawingShapeKind kind,
    long x, long y, long cx, long cy, SrgbColor color, string? label = null)
{
    var shape = new SlideShape
    {
        Id            = id,
        Name          = name,
        Kind          = SlideShapeKind.AutoShape,
        AutoShapeKind = kind,
        OffsetXEmu    = x,
        OffsetYEmu    = y,
        ExtentCxEmu   = cx,
        ExtentCyEmu   = cy,
        Fill          = new ShapeFill.Solid(new SrgbColor(color.R, color.G, color.B)),
    };
    if (label is not null)
    {
        shape.TextBody = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs  = { new Run { Text = label, Bold = true } },
                    Align = TextAlign.Center,
                }
            }
        };
    }
    return shape;
}

static TextBody MakeTextBody(params string[] paragraphs)
{
    var body = new TextBody();
    foreach (var text in paragraphs)
    {
        body.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run { Text = text } },
        });
    }

    return body;
}

static SlideShape MakeChartShape(uint id, string name, ChartShape chart, long x, long y, long cx, long cy) =>
    new()
    {
        Id          = id,
        Name        = name,
        Kind        = SlideShapeKind.Chart,
        OffsetXEmu  = x,
        OffsetYEmu  = y,
        ExtentCxEmu = cx,
        ExtentCyEmu = cy,
        Chart       = chart,
    };

static ChartSeries MakeSeries(string name, params double?[] values)
{
    var series = new ChartSeries { Name = name };
    series.Values.AddRange(values);
    return series;
}

static ChartShape MakeStockChart()
{
    var chart = new ChartShape
    {
        ChartType                = ChartType.Stock,
        Title                    = "Stock: rising, falling, unchanged",
        RegenerateWorkbookOnSave = true,
        HasHighLowLines          = false,
    };
    chart.Categories.AddRange(new[] { "Day 1", "Day 2", "Day 3" });
    chart.Series.Add(MakeSeries("Open", 10, 14, 12));
    chart.Series.Add(MakeSeries("High", 15, 16, 14));
    chart.Series.Add(MakeSeries("Low", 8, 11, 10));
    chart.Series.Add(MakeSeries("Close", 14, 12, 12));
    return chart;
}

static ChartShape MakeSurfaceChart()
{
    var chart = new ChartShape
    {
        ChartType                = ChartType.Surface3D,
        Title                    = "Surface: blank cell grid retention",
        RegenerateWorkbookOnSave = true,
        VaryColors               = true,
    };
    chart.Categories.AddRange(new[] { "North", "East", "South" });
    chart.Series.Add(MakeSeries("Low band", 10, null, 18));
    chart.Series.Add(MakeSeries("Mid band", 18, 22, 26));
    chart.Series.Add(MakeSeries("High band", 28, 24, 35));
    return chart;
}

static ChartShape MakeSmoothScatterChart()
{
    var chart = new ChartShape
    {
        ChartType                = ChartType.Scatter,
        ScatterStyle             = ScatterStyle.SmoothMarker,
        Title                    = "Scatter: smooth and straight series",
        RegenerateWorkbookOnSave = true,
    };

    var smooth = MakeSeries("Smoothed", 12, 32, 22, 44);
    smooth.XValues.AddRange(new double?[] { 0, 30, 70, 100 });
    smooth.SmoothLine = true;
    chart.Series.Add(smooth);

    var straight = MakeSeries("Straight override", 18, 16, 34, 28);
    straight.XValues.AddRange(new double?[] { 0, 30, 70, 100 });
    straight.SmoothLine = false;
    chart.Series.Add(straight);

    return chart;
}

static ChartShape MakeHundredPercentStackedChart()
{
    var chart = new ChartShape
    {
        ChartType                = ChartType.ColumnStacked100,
        Title                    = "100% stacked: normalized bands",
        RegenerateWorkbookOnSave = true,
        DataLabels               = new ChartDataLabels
        {
            ShowValue   = true,
            ShowPercent = true,
            Position    = DataLabelPosition.Center,
        },
    };
    chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3" });
    chart.Series.Add(MakeSeries("Actual", 20, 30, 40));
    chart.Series.Add(MakeSeries("Forecast", 30, 20, 10));
    return chart;
}

// ── 10-motionpath.pptx ──────────────────────────────────────────────────────────
// A slide with:
//   Shape 2 "Mover"   — line motion path (moves right 40% of slide width)
//   Shape 3 "Curver"  — cubic-bezier arc motion path
//   Shape 4 "Button"  — trigger shape (clicking it triggers shape 5's entrance)
//   Shape 5 "Target"  — trigger-animated entrance (Appear)
{
    var pres = Presentation.CreateEmpty();
    var slide = pres.Slides[0];
    slide.Shapes.Clear(); // remove default placeholder

    slide.Shapes.Add(MakeShape(2, "Mover",  DrawingShapeKind.Rectangle,
        914400, 914400, 914400, 914400, new SrgbColor(0x42, 0x72, 0xC4)));

    slide.Shapes.Add(MakeShape(3, "Curver", DrawingShapeKind.Ellipse,
        2743200, 1371600, 914400, 914400, new SrgbColor(0xE3, 0x70, 0x00)));

    slide.Shapes.Add(MakeShape(4, "Button", DrawingShapeKind.RoundedRectangle,
        5029200, 3200400, 1828800, 685800, new SrgbColor(0x70, 0xAD, 0x47), "Click Me"));

    slide.Shapes.Add(MakeShape(5, "Target", DrawingShapeKind.Rectangle,
        5029200, 1828800, 1828800, 685800, new SrgbColor(0x76, 0x30, 0x9B), "Triggered!"));

    // Animation 1: Shape 2 — straight line motion path
    {
        var mp = new MotionPath { Origin = "parent" };
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.LineTo(0.4, 0));
        mp.Segments.Add(MotionPathSegment.Close());

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 2,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 1500,
            Motion     = mp,
        });
    }

    // Animation 2: Shape 3 — arc (cubic bezier)
    {
        var mp = new MotionPath { Origin = "parent" };
        mp.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        mp.Segments.Add(MotionPathSegment.CubicTo(0.1, -0.15, 0.3, -0.15, 0.4, 0));
        mp.Segments.Add(MotionPathSegment.Close());

        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId    = 3,
            Kind       = AnimationKind.Motion,
            Trigger    = AnimationTrigger.OnClick,
            DurationMs = 2000,
            Motion     = mp,
        });
    }

    // Animation 3: Shape 5 — trigger-animated entrance (clicking Button fires it)
    slide.Animations.Add(new ShapeAnimation
    {
        ShapeId        = 5,
        Kind           = AnimationKind.Entrance,
        Preset         = AnimationPreset.Appear,
        Trigger        = AnimationTrigger.OnClick,
        DurationMs     = 500,
        TriggerShapeId = 4u,
    });

    var outPath = Path.Combine(outDir, "10-motionpath.pptx");
    using var fs = File.Create(outPath);
    PptxPackageWriter.Write(pres, fs);
    Console.WriteLine($"Generated: {outPath}");
}

// 21-comments-notes.pptx
// A deterministic FreeP-authored deck that exercises speaker notes plus legacy
// slide comments/commentAuthors package parts without requiring PowerPoint COM.
{
    var pres = Presentation.CreateEmpty();
    var slide1 = pres.Slides[0];
    slide1.Title = "Comments and notes";
    slide1.Shapes.Clear();
    slide1.Shapes.Add(MakeShape(2, "Notes marker", DrawingShapeKind.Rectangle,
        914400, 914400, 2743200, 914400, new SrgbColor(0x44, 0x72, 0xC4),
        "Slide 1 has speaker notes"));
    slide1.Notes = MakeTextBody(
        "Speaker note: introduce the review workflow.",
        "Mention that comments should round-trip through package save.");
    slide1.Comments.Add(new SlideComment
    {
        Author = "Alice Reviewer",
        Initials = "AR",
        Text = "Confirm the title before publishing.",
        Xemu = 914400,
        Yemu = 457200,
        Idx = 1,
    });

    var slide2 = new Slide
    {
        Title = "Follow-up comments",
        Notes = MakeTextBody("Speaker note: summarize the comment decisions."),
    };
    slide2.Shapes.Add(MakeShape(2, "Decision marker", DrawingShapeKind.RoundedRectangle,
        914400, 1371600, 3200400, 914400, new SrgbColor(0x70, 0xAD, 0x47),
        "Slide 2 has two comments"));
    slide2.Comments.Add(new SlideComment
    {
        Author = "Bob Reviewer",
        Initials = "BR",
        Text = "Add a data source footnote.",
        Xemu = 1371600,
        Yemu = 914400,
        Idx = 1,
    });
    slide2.Comments.Add(new SlideComment
    {
        Author = "Alice Reviewer",
        Initials = "AR",
        Text = "Keep this callout for presenter notes.",
        Xemu = 2743200,
        Yemu = 1371600,
        Idx = 2,
    });
    pres.Slides.Add(slide2);

    var outPath = Path.Combine(outDir, "21-comments-notes.pptx");
    using var fs = File.Create(outPath);
    PptxPackageWriter.Write(pres, fs);
    Console.WriteLine($"Generated: {outPath}");
}

// 22-chart-baseline-depth.pptx
// A deterministic FreeP-authored deck for no-COM chart visual baseline depth.
// It exercises stock ticks, surface grid retention/mesh contours, smooth scatter paths, and 100% stacks.
{
    var pres = Presentation.CreateEmpty();
    var slide = pres.Slides[0];
    slide.Title = "Chart baseline depth";
    slide.Shapes.Clear();

    const long left = 457200;
    const long top = 457200;
    const long chartWidth = 4572000;
    const long chartHeight = 2743200;
    const long gap = 228600;

    slide.Shapes.Add(MakeChartShape(2, "Stock baseline", MakeStockChart(),
        left, top, chartWidth, chartHeight));
    slide.Shapes.Add(MakeChartShape(3, "Surface baseline", MakeSurfaceChart(),
        left + chartWidth + gap, top, chartWidth, chartHeight));
    slide.Shapes.Add(MakeChartShape(4, "Smooth scatter baseline", MakeSmoothScatterChart(),
        left, top + chartHeight + gap, chartWidth, chartHeight));
    slide.Shapes.Add(MakeChartShape(5, "Stacked 100 baseline", MakeHundredPercentStackedChart(),
        left + chartWidth + gap, top + chartHeight + gap, chartWidth, chartHeight));

    var outPath = Path.Combine(outDir, "22-chart-baseline-depth.pptx");
    using var fs = File.Create(outPath);
    PptxPackageWriter.Write(pres, fs);
    Console.WriteLine($"Generated: {outPath}");
}

Console.WriteLine("Done.");
