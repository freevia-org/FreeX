using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO.Compression;
using System.Text;

namespace FreeP.RenderCompare;

/// <summary>
/// Generates a small corpus of deterministic .pptx test decks via PowerPoint COM
/// and exports PowerPoint's reference PNGs alongside them.
///
/// Decks produced:
///   01-title-slide.pptx       — title + subtitle placeholder text
///   02-autoshapes.pptx        — rectangle, rounded-rect, ellipse, chevron with
///                               solid theme-accent fills and outlines
///   03-mixed-text.pptx        — textbox with bold/colored/italic runs
///   04-picture.pptx           — slide with an embedded raster image
///
/// Reference PNGs land in: <outDir>/pptx-ref/<deckName>/slide-01.png etc.
/// </summary>
internal static class CorpusGenerator
{
    private const int MsoFalse   = 0;
    private const int MsoTrue    = -1;
    private const int PpAlertsNone = 2;

    // msoThemeColorAccent1..6 = 5..10
    private const int MsoThemeColorAccent1 = 5;
    private const int MsoThemeColorAccent2 = 6;
    private const int MsoThemeColorAccent3 = 7;
    private const int MsoThemeColorAccent4 = 8;

    // msoShapeRectangle = 1, msoShapeRoundedRectangle = 5, msoShapeOval = 9, msoShapeChevron = 52
    private const int MsoShapeRectangle        = 1;
    private const int MsoShapeRoundedRectangle = 5;
    private const int MsoShapeOval             = 9;
    private const int MsoShapeChevron          = 52;

    // ppLayoutTitle = 1, ppLayoutBlank = 12
    private const int PpLayoutTitle = 1;
    private const int PpLayoutBlank = 12;

    // Export resolution
    private const int ExportWidth  = 1280;
    private const int ExportHeight = 720;

    internal static int Generate(string outDir)
    {
        var beforePids = GetPowerPointProcessIds();
        var ownedPids = new HashSet<int>();

        dynamic? app = null;
        try
        {
            app = CreatePowerPointApplication();
            ownedPids = GetPowerPointProcessIds()
                .Where(pid => !beforePids.Contains(pid))
                .ToHashSet();
            Console.WriteLine("  PowerPoint started for corpus generation.");

            var decks = new List<(string name, Action<dynamic, string, string> generate)>
            {
                ("01-title-slide",  GenerateTitleSlide),
                ("02-autoshapes",   GenerateAutoshapes),
                ("03-mixed-text",   GenerateMixedText),
                ("04-picture",      GeneratePicture),
                ("05-table",        GenerateTable),
                ("06-charts",       GenerateCharts),
                ("07-customgeom",   GenerateCustomGeom),
                ("08-effects",      GenerateEffects),
                ("09-smartart",     GenerateSmartArt),
                ("11-bevel3d",      GenerateBevel3d),
                ("12-fills",        GenerateFills),
                ("13-wordart",      GenerateWordArt),
                ("14-smartart-live", GenerateSmartArtLive),
                ("16-bg-tabs-vtext", GenerateBgTabsVtext),
                ("18-chart-types",   GenerateChartTypes),
                ("19-chart-labels",  GenerateChartLabels19),
            };

            var errors = 0;
            foreach (var (name, gen) in decks)
            {
                var pptxPath = Path.Combine(outDir, $"{name}.pptx");
                var refDir   = Path.Combine(outDir, "pptx-ref", name);
                Directory.CreateDirectory(refDir);

                Console.Write($"  Generating {name}.pptx ... ");
                try
                {
                    gen(app, pptxPath, refDir);
                    Console.WriteLine("ok");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                    errors++;
                }
            }

            FinishApplication(ref app, ownedPids.Count > 0);
            WaitForPowerPointToExit(ownedPids, 15_000);

            Console.WriteLine($"  Corpus generation complete. {decks.Count - errors}/{decks.Count} decks succeeded.");
            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Corpus generation fatal error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            FinishApplication(ref app, ownedPids.Count > 0);
            WaitForPowerPointToExit(ownedPids, 10_000);
            KillPowerPointProcesses(ownedPids);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 01: Title slide — title + subtitle text
    // -----------------------------------------------------------------------
    private static void GenerateTitleSlide(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse); // WithWindow=false

            dynamic slide = pres.Slides.Add(1, PpLayoutTitle);

            // Title placeholder (index 1)
            dynamic title = slide.Shapes.Placeholders.Item(1);
            title.TextFrame.TextRange.Text = "FreeP Render Compare";
            title.TextFrame.TextRange.Font.Size = 40;
            title.TextFrame.TextRange.Font.Bold = MsoTrue;

            // Subtitle placeholder (index 2)
            dynamic sub = slide.Shapes.Placeholders.Item(2);
            sub.TextFrame.TextRange.Text = "Wave 1E interop-compare corpus";
            sub.TextFrame.TextRange.Font.Size = 24;

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 02: Autoshapes with theme-accent fills + outlines
    // -----------------------------------------------------------------------
    private static void GenerateAutoshapes(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);

            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Slide dimensions (EMU -> points for COM: 1 pt = 12700 EMU, but COM uses points)
            // Typical slide: 10 in wide x 7.5 in (or 13.33 x 7.5 for 16:9)
            // We'll use absolute point coordinates. PowerPoint default 16:9 = 960x540 pt
            AddShape(slide, MsoShapeRectangle,        30,  30, 180, 120, MsoThemeColorAccent1, "Rectangle");
            AddShape(slide, MsoShapeRoundedRectangle, 250,  30, 180, 120, MsoThemeColorAccent2, "RoundedRect");
            AddShape(slide, MsoShapeOval,              30, 200, 180, 120, MsoThemeColorAccent3, "Ellipse");
            AddShape(slide, MsoShapeChevron,          250, 200, 180, 120, MsoThemeColorAccent4, "Chevron");

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    private static void AddShape(dynamic slide, int shapeType, float left, float top, float width, float height, int accentColor, string name)
    {
        dynamic shape = slide.Shapes.AddShape(shapeType, left, top, width, height);
        shape.Name = name;

        // Solid theme-color fill
        shape.Fill.ForeColor.ObjectThemeColor = accentColor;
        shape.Fill.Solid();

        // Outline: 1.5pt dark
        shape.Line.Weight       = 1.5f;
        shape.Line.ForeColor.ObjectThemeColor = 1; // msoThemeColorDark1
        shape.Line.Visible      = MsoTrue;

        // Label text
        shape.TextFrame.TextRange.Text = name;
        shape.TextFrame.TextRange.Font.Size  = 14;
        shape.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF; // white
    }

    // -----------------------------------------------------------------------
    // Deck 03: Textbox with mixed bold/colored/italic runs
    // -----------------------------------------------------------------------
    private static void GenerateMixedText(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);

            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Add a textbox
            dynamic tb = slide.Shapes.AddTextbox(
                1,     // msoTextOrientationHorizontal
                50, 80, 860, 380);

            tb.TextFrame.WordWrap = MsoTrue;

            dynamic tr = tb.TextFrame.TextRange;
            tr.Text = "";

            // Run 1: normal
            AppendRun(tr, "FreeP ", isBold: false, isItalic: false, colorRgb: 0x000000, size: 28);
            // Run 2: bold
            AppendRun(tr, "Render", isBold: true,  isItalic: false, colorRgb: 0x1F4E79, size: 28);
            // Run 3: normal space
            AppendRun(tr, " ", isBold: false, isItalic: false, colorRgb: 0x000000, size: 28);
            // Run 4: italic + accent color
            AppendRun(tr, "Compare", isBold: false, isItalic: true, colorRgb: 0xC00000, size: 28);
            // Run 5: newline + subtitle
            AppendRun(tr, "\rWave 1E — PowerPoint Parity Harness", isBold: false, isItalic: false, colorRgb: 0x404040, size: 18);
            // Run 6: another bold run
            AppendRun(tr, "\rInterop compare: pixel-accurate ground truth from MS PowerPoint.", isBold: true, isItalic: false, colorRgb: 0x375623, size: 14);

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    private static void AppendRun(dynamic textRange, string text, bool isBold, bool isItalic, int colorRgb, int size)
    {
        // Add characters by setting the text on a new selection at end
        var existing = (string)textRange.Text;
        textRange.Text = existing + text;

        // Select the newly added characters
        var start = existing.Length + 1; // 1-based
        var len   = text.Length;
        dynamic run = textRange.Characters(start, len);
        run.Font.Bold   = isBold   ? MsoTrue : MsoFalse;
        run.Font.Italic = isItalic ? MsoTrue : MsoFalse;
        run.Font.Color.RGB = colorRgb;
        run.Font.Size = size;
    }

    // -----------------------------------------------------------------------
    // Deck 05: Table — 3 columns x 4 rows, header row, banded, merged cell
    // -----------------------------------------------------------------------
    private static void GenerateTable(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // AddTable(NumRows, NumColumns, Left, Top, Width, Height)
            // Slide is 960 x 540 pt. Centre a 700x260 table.
            dynamic table = slide.Shapes.AddTable(4, 3, 130f, 140f, 700f, 260f).Table;

            // Apply "Medium Style 2 - Accent 1" table style with firstRow/bandRow enabled.
            // ApplyStyle(Style GUID, MakeDefault) — the second param is a bool on some versions;
            // pass as MsoFalse to not make it the default.
            try { table.ApplyStyle("{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}", MsoFalse); }
            catch { /* style may not exist in this PowerPoint installation; continue */ }

            // Column widths (sum ~700pt)
            table.Columns.Item(1).Width = 220f;
            table.Columns.Item(2).Width = 240f;
            table.Columns.Item(3).Width = 240f;

            // Row heights
            for (int r = 1; r <= 4; r++)
                table.Rows.Item(r).Height = 65f;

            // Header row
            SetCell(table, 1, 1, "Category", isBold: true);
            SetCell(table, 1, 2, "Value",    isBold: true);
            SetCell(table, 1, 3, "Notes",    isBold: true);

            // Data rows
            SetCell(table, 2, 1, "Alpha");
            SetCell(table, 2, 2, "1 234");
            SetCell(table, 2, 3, "First row of data");

            SetCell(table, 3, 1, "Beta");
            SetCell(table, 3, 2, "5 678");
            SetCell(table, 3, 3, "Second row of data");

            // Row 4: merge columns 2 and 3
            SetCell(table, 4, 1, "Total");
            SetCell(table, 4, 2, "6 912");
            // Merge columns 2+3 in row 4
            table.Cell(4, 2).Merge(table.Cell(4, 3));

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    private static void SetCell(dynamic table, int row, int col, string text, bool isBold = false)
    {
        dynamic cell = table.Cell(row, col);
        cell.Shape.TextFrame.TextRange.Text = text;
        cell.Shape.TextFrame.TextRange.Font.Bold   = isBold ? MsoTrue : MsoFalse;
        cell.Shape.TextFrame.TextRange.Font.Size   = 16;
        cell.Shape.TextFrame.TextRange.ParagraphFormat.Alignment = 1; // ppAlignLeft
    }

    // -----------------------------------------------------------------------
    // Deck 06: Charts
    // -----------------------------------------------------------------------
    // Strategy:
    //  1. Use PowerPoint COM to create the PPTX (AddChart API with visible window)
    //  2. After saving, patch the chart XML directly in the zip to inject cached series data
    //     into charts whose embedded workbook data didn't write back to the cache
    //  3. Re-export reference PNGs from the patched PPTX
    private static void GenerateCharts(dynamic app, string pptxPath, string refDir)
    {
        dynamic? comPres = null;
        try
        {
            // Create a new visible presentation for chart COM operations
            comPres = app.Presentations.Add(MsoTrue);
            try { app.WindowState = 2; } catch { } // ppWindowMinimized=2

            AddChartSlideViaCom(comPres, "Clustered Column Chart",
                51,  // xlColumnClustered (2D)
                new[] { "Q1", "Q2", "Q3", "Q4" },
                new[] { ("Sales", new double[] { 120, 200, 150, 180 }), ("Budget", new double[] { 130, 170, 160, 190 }) });

            AddChartSlideViaCom(comPres, "Line with Markers",
                65,  // xlLineMarkers
                new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" },
                new[] { ("Revenue", new double[] { 50, 80, 65, 90, 75, 110 }), ("Forecast", new double[] { 55, 75, 70, 85, 80, 105 }) });

            AddChartSlideViaCom(comPres, "Pie Chart",
                5,  // xlPie
                new[] { "Alpha", "Beta", "Gamma", "Delta" },
                new[] { ("Share", new double[] { 40, 30, 20, 10 }) });

            AddChartSlideViaCom(comPres, "Clustered Bar Chart",
                57,  // xlBarClustered (horizontal clustered 2D bar)
                new[] { "North", "South", "East", "West" },
                new[] { ("2023", new double[] { 80, 100, 60, 90 }), ("2024", new double[] { 90, 110, 70, 100 }) });

            // Save the PPTX
            if (File.Exists(pptxPath)) File.Delete(pptxPath);
            comPres.SaveAs(pptxPath, 24, MsoFalse); // ppSaveAsOpenXMLPresentation = 24
            comPres.Close();
            comPres = null;

            // Patch charts with hardcoded cached data (COM API may not persist to XML cache).
            // PatchChartDataInZip uses XmlDocument (not LINQ to XML) to avoid namespace
            // declaration reformatting that breaks PowerPoint's parser.
            PatchChartDataInZip(pptxPath);

            // Export reference PNGs from the patched PPTX
            dynamic? exportPres = null;
            try
            {
                exportPres = app.Presentations.Open(pptxPath, MsoTrue, MsoFalse, MsoFalse);
                var slideCount = (int)exportPres.Slides.Count;
                for (int i = 1; i <= slideCount; i++)
                {
                    var pngPath = Path.Combine(refDir, $"slide-{i:D2}.png");
                    dynamic slide = exportPres.Slides.Item(i);
                    slide.Export(pngPath, "PNG", ExportWidth, ExportHeight);
                }
                exportPres.Close();
                exportPres = null;
            }
            finally
            {
                if (exportPres is not null)
                    try { exportPres.Close(); } catch { }
            }
        }
        finally
        {
            if (comPres is not null)
            {
                try { comPres.Close(); } catch { }
                if (Marshal.IsComObject(comPres))
                    try { Marshal.FinalReleaseComObject(comPres); } catch { }
            }
        }
    }

    private static void AddChartSlideViaCom(dynamic pres, string title, int xlChartType,
        string[] cats, (string name, double[] vals)[] series)
    {
        dynamic slide = pres.Slides.Add(pres.Slides.Count + 1, PpLayoutBlank);

        // Title textbox
        dynamic tb = slide.Shapes.AddTextbox(1, 20f, 10f, 920f, 30f);
        tb.TextFrame.TextRange.Text = title;
        tb.TextFrame.TextRange.Font.Size = 18;
        tb.TextFrame.TextRange.Font.Bold = MsoTrue;

        // Add chart — AddChart is the classic API; if it fails try AddChart2
        dynamic chartShape;
        try { chartShape = slide.Shapes.AddChart(xlChartType, 60f, 55f, 860f, 460f); }
        catch  { chartShape = slide.Shapes.AddChart2(-1, xlChartType, 60f, 55f, 860f, 460f, false); }

        dynamic chart = chartShape.Chart;
        try { chart.ChartType = xlChartType; } catch { }

        // Try to populate via series API; ignore failures (we patch the cache afterwards)
        try
        {
            dynamic sc = chart.SeriesCollection();
            while ((int)sc.Count > 0) { try { sc.Item(1).Delete(); } catch { break; } }
            foreach (var (sName, vals) in series)
            {
                dynamic ser = sc.NewSeries();
                ser.Name = sName;
                ser.XValues = cats.Cast<object>().ToArray();
                ser.Values  = vals.Cast<object>().ToArray();
            }
        }
        catch { /* will be fixed by patch */ }
    }

    /// <summary>
    /// Post-processes the PPTX zip to inject deterministic cached data into all chart parts.
    /// This ensures charts have proper numCache/strCache entries regardless of whether
    /// the COM API managed to write them.
    /// </summary>
    private static void PatchChartDataInZip(string pptxPath)
    {
        // Define the expected data per chart index (0-based = slide 1, 2, 3, 4)
        var chartData = new[]
        {
            // Chart 0 (slide 1): Clustered column
            new ChartPatchData(
                Cats: new[] { "Q1", "Q2", "Q3", "Q4" },
                Series: new[]
                {
                    ("Sales",  new double[] { 120, 200, 150, 180 }),
                    ("Budget", new double[] { 130, 170, 160, 190 })
                }),
            // Chart 1 (slide 2): Line with markers
            new ChartPatchData(
                Cats: new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" },
                Series: new[]
                {
                    ("Revenue",  new double[] { 50, 80, 65, 90, 75, 110 }),
                    ("Forecast", new double[] { 55, 75, 70, 85, 80, 105 })
                }),
            // Chart 2 (slide 3): Pie
            new ChartPatchData(
                Cats: new[] { "Alpha", "Beta", "Gamma", "Delta" },
                Series: new[]
                {
                    ("Share", new double[] { 40, 30, 20, 10 })
                }),
            // Chart 3 (slide 4): Stacked bar
            new ChartPatchData(
                Cats: new[] { "North", "South", "East", "West" },
                Series: new[]
                {
                    ("2023", new double[] { 80, 100, 60, 90 }),
                    ("2024", new double[] { 90, 110, 70, 100 })
                }),
        };

        // Read all zip entries into memory, patch chart parts, rewrite
        var patchedPath = pptxPath + ".patched";
        using var srcZip  = ZipFile.OpenRead(pptxPath);
        using var destZip = ZipFile.Open(patchedPath, ZipArchiveMode.Create);

        // Index charts by number order
        var chartEntries = srcZip.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"ppt/charts/chart\d+\.xml$"))
            .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+").Value))
            .ToList();

        int chartIdx = 0;
        foreach (var entry in srcZip.Entries)
        {
            if (chartIdx < chartData.Length && chartEntries.Contains(entry))
            {
                // Patch this chart's XML using XmlDocument (not LINQ to XML) to avoid
                // namespace declaration reformatting that breaks PowerPoint.
                string xmlText;
                using (var s = entry.Open())
                using (var reader = new StreamReader(s, Encoding.UTF8))
                    xmlText = reader.ReadToEnd();

                var patched = PatchChartXmlViaXmlDocument(xmlText, chartData[chartIdx]);
                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using (var s = destEntry.Open())
                {
                    var settings = new System.Xml.XmlWriterSettings
                        { Encoding = utf8NoBom, Indent = false, CloseOutput = false };
                    using (var xw = System.Xml.XmlWriter.Create(s, settings))
                        patched.Save(xw);
                }
                chartIdx++;
            }
            else
            {
                // Copy as-is
                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var src  = entry.Open();
                using var dest = destEntry.Open();
                src.CopyTo(dest);
            }
        }

        srcZip.Dispose();
        destZip.Dispose();

        // Replace original
        File.Delete(pptxPath);
        File.Move(patchedPath, pptxPath);
    }

    private record ChartPatchData(string[] Cats, (string Name, double[] Vals)[] Series);

    private const string ChartNsUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// Patches chart series data using XmlDocument (not LINQ to XML) so that namespace
    /// declarations are preserved in-place and PowerPoint's parser is not broken.
    /// </summary>
    private static System.Xml.XmlDocument PatchChartXmlViaXmlDocument(
        string xmlText, ChartPatchData data)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xmlText);

        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("c", ChartNsUri);

        var serNodes = doc.SelectNodes("//c:plotArea/*/c:ser", nsMgr);
        if (serNodes is null) return doc;

        for (int si = 0; si < serNodes.Count && si < data.Series.Length; si++)
        {
            var ser = serNodes[si]!;
            var (serName, vals) = data.Series[si];

            // ---- c:tx series name: update <c:v> if present ----
            var txV = ser.SelectSingleNode("c:tx//c:v", nsMgr);
            if (txV is not null) txV.InnerText = serName;

            // ---- c:cat categories: inject strCache ----
            var cat = ser.SelectSingleNode("c:cat", nsMgr);
            if (cat is null)
            {
                cat = doc.CreateElement("c", "cat", ChartNsUri);
                ser.AppendChild(cat);
            }
            var catStrRef = cat.SelectSingleNode("c:strRef", nsMgr);
            var catNumRef = cat.SelectSingleNode("c:numRef", nsMgr);
            if (catStrRef is not null)
            {
                // Remove old cache, add fresh one
                var oldCache = catStrRef.SelectSingleNode("c:strCache", nsMgr);
                if (oldCache is not null) catStrRef.RemoveChild(oldCache);
                catStrRef.AppendChild(CreateStrCache(doc, nsMgr, data.Cats));
            }
            else if (catNumRef is not null)
            {
                // Replace numRef with strRef (string categories)
                var fEl = catNumRef.SelectSingleNode("c:f", nsMgr);
                var newStrRef = doc.CreateElement("c", "strRef", ChartNsUri);
                var newF = doc.CreateElement("c", "f", ChartNsUri);
                if (fEl is not null) newF.InnerText = fEl.InnerText;
                newStrRef.AppendChild(newF);
                newStrRef.AppendChild(CreateStrCache(doc, nsMgr, data.Cats));
                cat.RemoveAll();
                cat.AppendChild(newStrRef);
            }
            else
            {
                // No inner element: add strLit
                cat.RemoveAll();
                var strLit = doc.CreateElement("c", "strLit", ChartNsUri);
                foreach (var child in CreateStrCache(doc, nsMgr, data.Cats).ChildNodes.Cast<System.Xml.XmlNode>().ToList())
                    strLit.AppendChild(child.CloneNode(true));
                cat.AppendChild(strLit);
            }

            // ---- c:val values: inject numCache ----
            var val = ser.SelectSingleNode("c:val", nsMgr);
            if (val is null)
            {
                val = doc.CreateElement("c", "val", ChartNsUri);
                ser.AppendChild(val);
            }
            var valNumRef = val.SelectSingleNode("c:numRef", nsMgr);
            var valNumLit = val.SelectSingleNode("c:numLit", nsMgr);
            if (valNumRef is not null)
            {
                var oldCache = valNumRef.SelectSingleNode("c:numCache", nsMgr);
                if (oldCache is not null) valNumRef.RemoveChild(oldCache);
                valNumRef.AppendChild(CreateNumCache(doc, nsMgr, vals));
            }
            else if (valNumLit is not null)
            {
                // Replace numLit content in-place
                val.RemoveChild(valNumLit);
                var newLit = doc.CreateElement("c", "numLit", ChartNsUri);
                foreach (var child in CreateNumCache(doc, nsMgr, vals, "numLit").ChildNodes.Cast<System.Xml.XmlNode>().ToList())
                    newLit.AppendChild(child.CloneNode(true));
                val.AppendChild(newLit);
            }
            else
            {
                val.RemoveAll();
                var numLit = doc.CreateElement("c", "numLit", ChartNsUri);
                foreach (var child in CreateNumCache(doc, nsMgr, vals, "numLit").ChildNodes.Cast<System.Xml.XmlNode>().ToList())
                    numLit.AppendChild(child.CloneNode(true));
                val.AppendChild(numLit);
            }
        }
        return doc;
    }

    private static System.Xml.XmlElement CreateStrCache(
        System.Xml.XmlDocument doc, System.Xml.XmlNamespaceManager nsMgr, string[] values)
    {
        var cache = doc.CreateElement("c", "strCache", ChartNsUri);
        var ptCount = doc.CreateElement("c", "ptCount", ChartNsUri);
        ptCount.SetAttribute("val", values.Length.ToString());
        cache.AppendChild(ptCount);
        for (int i = 0; i < values.Length; i++)
        {
            var pt = doc.CreateElement("c", "pt", ChartNsUri);
            pt.SetAttribute("idx", i.ToString());
            var v = doc.CreateElement("c", "v", ChartNsUri);
            v.InnerText = values[i];
            pt.AppendChild(v);
            cache.AppendChild(pt);
        }
        return cache;
    }

    private static System.Xml.XmlElement CreateNumCache(
        System.Xml.XmlDocument doc, System.Xml.XmlNamespaceManager nsMgr,
        double[] values, string elementName = "numCache")
    {
        var cache = doc.CreateElement("c", elementName, ChartNsUri);
        var fmtCode = doc.CreateElement("c", "formatCode", ChartNsUri);
        fmtCode.InnerText = "General";
        cache.AppendChild(fmtCode);
        var ptCount = doc.CreateElement("c", "ptCount", ChartNsUri);
        ptCount.SetAttribute("val", values.Length.ToString());
        cache.AppendChild(ptCount);
        for (int i = 0; i < values.Length; i++)
        {
            var pt = doc.CreateElement("c", "pt", ChartNsUri);
            pt.SetAttribute("idx", i.ToString());
            var v = doc.CreateElement("c", "v", ChartNsUri);
            v.InnerText = values[i].ToString();
            pt.AppendChild(v);
            cache.AppendChild(pt);
        }
        return cache;
    }



    // -----------------------------------------------------------------------
    // Deck 18: Doughnut, Scatter, Radar, Bubble chart types
    // -----------------------------------------------------------------------
    // XlChartType constants:
    //   xlDoughnut    = -4120
    //   xlXYScatter   = -4169  (scatter with markers only)
    //   xlRadar       = -4151
    //   xlBubble      = 15
    private static void GenerateChartTypes(dynamic app, string pptxPath, string refDir)
    {
        dynamic? comPres = null;
        try
        {
            comPres = app.Presentations.Add(MsoTrue);
            try { app.WindowState = 2; } catch { } // ppWindowMinimized=2

            // Slide 1: Doughnut
            AddChartSlideViaCom(comPres, "Doughnut Chart",
                -4120, // xlDoughnut
                new[] { "Alpha", "Beta", "Gamma", "Delta" },
                new[] { ("Share", new double[] { 40, 30, 20, 10 }) });

            // Slide 2: XY Scatter
            AddChartSlideViaCom(comPres, "XY Scatter",
                -4169, // xlXYScatter
                new[] { "1", "2", "3", "4", "5" },
                new[] { ("Series1", new double[] { 10, 30, 15, 40, 25 }) });

            // Slide 3: Radar
            AddChartSlideViaCom(comPres, "Radar Chart",
                -4151, // xlRadar
                new[] { "Speed", "Power", "Agility", "Stamina", "Tech" },
                new[] {
                    ("Alpha", new double[] { 80, 60, 90, 70, 50 }),
                    ("Beta",  new double[] { 50, 80, 60, 90, 75 })
                });

            // Slide 4: Bubble
            AddChartSlideViaCom(comPres, "Bubble Chart",
                15, // xlBubble
                new[] { "1", "2", "3" },
                new[] { ("Bubbles", new double[] { 2, 4, 1 }) });

            if (File.Exists(pptxPath)) File.Delete(pptxPath);
            comPres.SaveAs(pptxPath, 24, MsoFalse);
            comPres.Close();
            comPres = null;

            // Patch chart XML with correct cached data
            PatchChartTypes18InZip(pptxPath);

            // Export reference PNGs
            dynamic? exportPres = null;
            try
            {
                exportPres = app.Presentations.Open(pptxPath, MsoTrue, MsoFalse, MsoFalse);
                int slideCount = (int)exportPres.Slides.Count;
                for (int i = 1; i <= slideCount; i++)
                {
                    var pngPath = Path.Combine(refDir, $"slide-{i:D2}.png");
                    dynamic slide = exportPres.Slides.Item(i);
                    slide.Export(pngPath, "PNG", ExportWidth, ExportHeight);
                }
                exportPres.Close();
                exportPres = null;
            }
            finally
            {
                if (exportPres is not null)
                    try { exportPres.Close(); } catch { }
            }
        }
        finally
        {
            if (comPres is not null)
            {
                try { comPres.Close(); } catch { }
                if (Marshal.IsComObject(comPres))
                    try { Marshal.FinalReleaseComObject(comPres); } catch { }
            }
        }
    }

    /// <summary>
    /// Patches chart XML for the 18-chart-types deck:
    ///   chart1 — doughnut  (c:cat / c:val)
    ///   chart2 — scatter   (c:xVal / c:yVal)
    ///   chart3 — radar     (c:cat / c:val)
    ///   chart4 — bubble    (c:xVal / c:yVal / c:bubbleSize)
    /// </summary>
    private static void PatchChartTypes18InZip(string pptxPath)
    {
        var patchedPath = pptxPath + ".patched";
        using var srcZip  = ZipFile.OpenRead(pptxPath);
        using var destZip = ZipFile.Open(patchedPath, ZipArchiveMode.Create);

        var chartEntries = srcZip.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"ppt/charts/chart\d+\.xml$"))
            .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+").Value))
            .ToList();

        int chartIdx = 0;
        foreach (var entry in srcZip.Entries)
        {
            if (chartIdx < chartEntries.Count && chartEntries.Contains(entry))
            {
                string xmlText;
                using (var s = entry.Open())
                using (var reader = new StreamReader(s, Encoding.UTF8))
                    xmlText = reader.ReadToEnd();

                System.Xml.XmlDocument patched;
                switch (chartIdx)
                {
                    case 0: // doughnut
                        patched = PatchChartXmlViaXmlDocument(xmlText, new ChartPatchData(
                            Cats: new[] { "Alpha", "Beta", "Gamma", "Delta" },
                            Series: new[] { ("Share", new double[] { 40, 30, 20, 10 }) }));
                        break;
                    case 1: // scatter
                        patched = PatchScatterChartXml(xmlText,
                            xVals:  new double[] { 1, 2, 3, 4, 5 },
                            yVals:  new double[] { 10, 30, 15, 40, 25 },
                            serName: "Series1");
                        break;
                    case 2: // radar
                        patched = PatchChartXmlViaXmlDocument(xmlText, new ChartPatchData(
                            Cats: new[] { "Speed", "Power", "Agility", "Stamina", "Tech" },
                            Series: new[]
                            {
                                ("Alpha", new double[] { 80, 60, 90, 70, 50 }),
                                ("Beta",  new double[] { 50, 80, 60, 90, 75 })
                            }));
                        break;
                    case 3: // bubble
                        patched = PatchBubbleChartXml(xmlText,
                            xVals:       new double[] { 1, 3, 5 },
                            yVals:       new double[] { 2, 4, 1 },
                            bubbleSizes: new double[] { 5, 15, 10 },
                            serName: "Bubbles");
                        break;
                    default:
                        patched = null!;
                        break;
                }

                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using (var s = destEntry.Open())
                {
                    var settings = new System.Xml.XmlWriterSettings
                        { Encoding = utf8NoBom, Indent = false, CloseOutput = false };
                    using (var xw = System.Xml.XmlWriter.Create(s, settings))
                        patched.Save(xw);
                }
                chartIdx++;
            }
            else
            {
                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var src  = entry.Open();
                using var dest = destEntry.Open();
                src.CopyTo(dest);
            }
        }

        srcZip.Dispose();
        destZip.Dispose();

        File.Delete(pptxPath);
        File.Move(patchedPath, pptxPath);
    }

    /// <summary>Patches a scatter chart's series to use c:xVal / c:yVal caches.</summary>
    private static System.Xml.XmlDocument PatchScatterChartXml(
        string xmlText, double[] xVals, double[] yVals, string serName)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xmlText);
        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("c", ChartNsUri);

        var serNode = doc.SelectSingleNode("//c:plotArea/*/c:ser", nsMgr);
        if (serNode is null) return doc;

        // Series name
        var txV = serNode.SelectSingleNode("c:tx//c:v", nsMgr);
        if (txV is not null) txV.InnerText = serName;

        // Ensure c:xVal with numCache
        EnsureXValYVal(doc, nsMgr, serNode, xVals, yVals);
        return doc;
    }

    /// <summary>Patches a bubble chart's series to use c:xVal / c:yVal / c:bubbleSize caches.</summary>
    private static System.Xml.XmlDocument PatchBubbleChartXml(
        string xmlText, double[] xVals, double[] yVals, double[] bubbleSizes, string serName)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xmlText);
        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("c", ChartNsUri);

        var serNode = doc.SelectSingleNode("//c:plotArea/*/c:ser", nsMgr);
        if (serNode is null) return doc;

        var txV = serNode.SelectSingleNode("c:tx//c:v", nsMgr);
        if (txV is not null) txV.InnerText = serName;

        EnsureXValYVal(doc, nsMgr, serNode, xVals, yVals);

        // c:bubbleSize
        var bsNode = serNode.SelectSingleNode("c:bubbleSize", nsMgr)
                     ?? AppendElement(doc, serNode, "c", "bubbleSize", ChartNsUri);
        bsNode.RemoveAll();
        var bsNumRef = doc.CreateElement("c", "numRef", ChartNsUri);
        bsNumRef.AppendChild(CreateNumCache(doc, nsMgr, bubbleSizes));
        bsNode.AppendChild(bsNumRef);

        return doc;
    }

    private static void EnsureXValYVal(
        System.Xml.XmlDocument doc, System.Xml.XmlNamespaceManager nsMgr,
        System.Xml.XmlNode serNode, double[] xVals, double[] yVals)
    {
        // c:xVal
        var xNode = serNode.SelectSingleNode("c:xVal", nsMgr)
                    ?? AppendElement(doc, serNode, "c", "xVal", ChartNsUri);
        xNode.RemoveAll();
        var xNumRef = doc.CreateElement("c", "numRef", ChartNsUri);
        xNumRef.AppendChild(CreateNumCache(doc, nsMgr, xVals));
        xNode.AppendChild(xNumRef);

        // c:yVal
        var yNode = serNode.SelectSingleNode("c:yVal", nsMgr)
                    ?? AppendElement(doc, serNode, "c", "yVal", ChartNsUri);
        yNode.RemoveAll();
        var yNumRef = doc.CreateElement("c", "numRef", ChartNsUri);
        yNumRef.AppendChild(CreateNumCache(doc, nsMgr, yVals));
        yNode.AppendChild(yNumRef);
    }

    private static System.Xml.XmlElement AppendElement(
        System.Xml.XmlDocument doc, System.Xml.XmlNode parent,
        string prefix, string localName, string ns)
    {
        var el = doc.CreateElement(prefix, localName, ns);
        parent.AppendChild(el);
        return el;
    }

    // -----------------------------------------------------------------------
    // Deck 04: Slide with a picture (we embed a small generated PNG)
    // -----------------------------------------------------------------------
    private static void GeneratePicture(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        string? tempPng = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Generate a small deterministic PNG to embed (a coloured gradient block)
            tempPng = Path.Combine(Path.GetTempPath(), $"freep-corpus-pic-{Guid.NewGuid():N}.png");
            WriteTestPng(tempPng, 320, 200);

            // AddPicture(FileName, LinkToFile, SaveWithDocument, Left, Top, Width, Height)
            dynamic pic = slide.Shapes.AddPicture(
                tempPng,
                MsoFalse,  // LinkToFile = false (embed)
                MsoTrue,   // SaveWithDocument = true
                100f, 80f, 320f, 200f);

            pic.Name = "TestPicture";

            // Add a caption textbox below the image
            dynamic tb = slide.Shapes.AddTextbox(1, 100f, 295f, 320f, 30f);
            tb.TextFrame.TextRange.Text = "Embedded test image (corpus deck 04)";
            tb.TextFrame.TextRange.Font.Size = 12;

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
            if (tempPng != null)
                TryDeleteFile(tempPng);
        }
    }

    /// <summary>Write a small 8-banded rainbow PNG using raw WPF BitmapSource so no extra dependency is needed.</summary>
    private static void WriteTestPng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Simple hue-sweep based on x position
                var hue = (double)x / width * 360.0;
                var (r, g, b) = HsvToRgb(hue, 0.8, 0.9);
                var off = (y * width + x) * 4;
                pixels[off + 0] = b;
                pixels[off + 1] = g;
                pixels[off + 2] = r;
                pixels[off + 3] = 255;
            }
        }

        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32,
            null, pixels, width * 4);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        var hi = (int)(h / 60.0) % 6;
        var f  = h / 60.0 - Math.Floor(h / 60.0);
        var p  = v * (1 - s);
        var q  = v * (1 - f * s);
        var t  = v * (1 - (1 - f) * s);

        var (r, g, b) = hi switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };

        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // -----------------------------------------------------------------------
    // Deck 07: Custom geometry — freeform triangle + curved arrow via freeform
    // -----------------------------------------------------------------------
    private static void GenerateCustomGeom(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Freeform #1: isosceles triangle (3 vertices)
            // BuildFreeform(msoEditingAuto, startX, startY)
            dynamic ff1 = slide.Shapes.BuildFreeform(0 /*msoEditingAuto*/, 100f, 280f);
            ff1.AddNodes(0 /*msoSegmentLine*/, 0 /*msoEditingAuto*/, 190f, 80f);
            ff1.AddNodes(0, 0, 280f, 280f);
            dynamic shape1 = ff1.ConvertToShape();
            shape1.Name = "Triangle";
            shape1.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent1;
            shape1.Fill.Solid();
            shape1.Line.Weight = 2.0f;
            shape1.Line.ForeColor.ObjectThemeColor = 1; // Dark1

            // Freeform #2: simple curved arrow path with cubicBezTo equivalent via smooth nodes
            dynamic ff2 = slide.Shapes.BuildFreeform(0, 400f, 200f);
            ff2.AddNodes(1 /*msoSegmentCurve*/, 1 /*msoEditingSmooth*/, 460f, 100f);
            ff2.AddNodes(1, 1, 560f, 300f);
            ff2.AddNodes(1, 1, 620f, 200f);
            dynamic shape2 = ff2.ConvertToShape();
            shape2.Name = "CurvedLine";
            shape2.Line.Weight = 3.0f;
            shape2.Line.ForeColor.ObjectThemeColor = MsoThemeColorAccent3;
            shape2.Fill.Visible = MsoFalse;

            // Freeform #3: closed diamond
            dynamic ff3 = slide.Shapes.BuildFreeform(0, 750f, 150f);
            ff3.AddNodes(0, 0, 850f, 80f);
            ff3.AddNodes(0, 0, 950f, 150f);
            ff3.AddNodes(0, 0, 850f, 220f);
            dynamic shape3 = ff3.ConvertToShape();
            shape3.Name = "Diamond";
            shape3.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent2;
            shape3.Fill.Solid();
            shape3.Line.Weight = 1.5f;

            // Title label
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 10f, 900f, 35f);
            tb.TextFrame.TextRange.Text = "Custom Geometry (a:custGeom): freeform paths";
            tb.TextFrame.TextRange.Font.Size = 18;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 08: Shape effects — drop shadow + glow
    // -----------------------------------------------------------------------
    private static void GenerateEffects(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Shape 1: Rectangle with outer drop shadow
            dynamic sh1 = slide.Shapes.AddShape(MsoShapeRectangle, 80f, 80f, 260f, 160f);
            sh1.Name = "ShadowRect";
            sh1.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent1;
            sh1.Fill.Solid();
            sh1.TextFrame.TextRange.Text = "Outer Shadow";
            sh1.TextFrame.TextRange.Font.Size = 16;
            sh1.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            // Apply outer shadow via ShadowFormat
            try
            {
                sh1.Shadow.Visible  = MsoTrue;
                sh1.Shadow.OffsetX  = 6f;
                sh1.Shadow.OffsetY  = 6f;
                sh1.Shadow.Blur     = 6f;
                sh1.Shadow.ForeColor.RGB = 0x404040;
                sh1.Shadow.Transparency = 0.4f;
            }
            catch { /* older PPTX shadow API may vary */ }

            // Shape 2: Ellipse with glow
            dynamic sh2 = slide.Shapes.AddShape(MsoShapeOval, 430f, 80f, 240f, 160f);
            sh2.Name = "GlowEllipse";
            sh2.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent3;
            sh2.Fill.Solid();
            sh2.TextFrame.TextRange.Text = "Glow";
            sh2.TextFrame.TextRange.Font.Size = 16;
            sh2.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh2.Glow.Radius = 12f;
                sh2.Glow.Color.ObjectThemeColor = MsoThemeColorAccent3;
                sh2.Glow.Transparency = 0.4f;
            }
            catch { /* glow API may vary */ }

            // Shape 3: Rounded rectangle with both shadow + soft edges
            dynamic sh3 = slide.Shapes.AddShape(MsoShapeRoundedRectangle, 720f, 80f, 200f, 160f);
            sh3.Name = "SoftRect";
            sh3.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent4;
            sh3.Fill.Solid();
            sh3.TextFrame.TextRange.Text = "Soft Edge";
            sh3.TextFrame.TextRange.Font.Size = 16;
            sh3.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh3.SoftEdge.Radius = 8f;
            }
            catch { /* soft edge API may vary */ }

            // Title label
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 10f, 900f, 35f);
            tb.TextFrame.TextRange.Text = "Shape Effects: outer shadow, glow, soft edge";
            tb.TextFrame.TextRange.Font.Size = 18;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 09: SmartArt — Basic Process diagram with text nodes
    // -----------------------------------------------------------------------
    private static void GenerateSmartArt(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Title label
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
            tb.TextFrame.TextRange.Text = "SmartArt — Basic Process";
            tb.TextFrame.TextRange.Font.Size = 20;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            // Try to insert a SmartArt via Shapes.AddSmartArt if available (PPTX 2013+).
            // SmartArtLayout index 1 = Basic Block List; we want a process / hierarchy.
            // The SmartArt layouts are accessed via Application.SmartArtLayouts collection.
            // We wrap in try/catch so older versions fall back to regular shapes.
            bool smartArtInserted = false;
            try
            {
                // ppLayoutBlank slide dimensions in points: 960 wide x 540 tall (16:9)
                // Position: left=60, top=60, width=840, height=360
                dynamic layouts = app.SmartArtLayouts;
                dynamic? targetLayout = null;

                // Walk layouts to find "Basic Process" (or any process-family layout)
                for (int li = 1; li <= (int)layouts.Count; li++)
                {
                    dynamic layout = layouts.Item(li);
                    string name = (string)layout.Name;
                    if (name.Contains("Process", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                    {
                        targetLayout = layout;
                        break;
                    }
                }

                targetLayout ??= layouts.Item(1); // fallback to first

                dynamic saShape = slide.Shapes.AddSmartArt(targetLayout, 60f, 60f, 840f, 360f);

                // Populate nodes; SmartArt AllNodes collection
                dynamic nodes = saShape.SmartArt.AllNodes;
                string[] texts = ["Plan", "Design", "Build", "Test", "Deploy"];
                for (int ni = 1; ni <= Math.Min((int)nodes.Count, texts.Length); ni++)
                {
                    try { nodes.Item(ni).TextFrame2.TextRange.Text = texts[ni - 1]; }
                    catch { /* node may not accept text */ }
                }

                smartArtInserted = true;
            }
            catch (Exception ex)
            {
                Console.Write($"(SmartArtLayouts not available: {ex.Message}; using placeholder shapes) ");
            }

            if (!smartArtInserted)
            {
                // Fallback: draw a row of 5 process rectangles manually to simulate SmartArt
                int accentBase = MsoThemeColorAccent1;
                string[] steps = ["Plan", "Design", "Build", "Test", "Deploy"];
                for (int i = 0; i < steps.Length; i++)
                {
                    dynamic sh = slide.Shapes.AddShape(MsoShapeRectangle,
                        60f + i * 168f, 80f, 150f, 80f);
                    sh.Name = $"Step{i + 1}";
                    sh.Fill.ForeColor.ObjectThemeColor = accentBase + (i % 6);
                    sh.Fill.Solid();
                    sh.TextFrame.TextRange.Text = steps[i];
                    sh.TextFrame.TextRange.Font.Size = 14;
                    sh.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
                }
            }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 11: Bevel / 3-D shape effects — a:sp3d / a:bevelT / a:scene3d
    // -----------------------------------------------------------------------
    private static void GenerateBevel3d(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Title
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
            tb.TextFrame.TextRange.Text = "Bevel / 3-D Shape Effects";
            tb.TextFrame.TextRange.Font.Size = 20;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            // Shape 1: Rectangle with Circle bevel (Angle preset)
            dynamic sh1 = slide.Shapes.AddShape(MsoShapeRectangle, 60f, 60f, 200f, 140f);
            sh1.Name = "BevelCircle";
            sh1.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent1;
            sh1.Fill.Solid();
            sh1.TextFrame.TextRange.Text = "Circle Bevel";
            sh1.TextFrame.TextRange.Font.Size = 14;
            sh1.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh1.ThreeD.BevelTopType   = 1;     // msoBevelCircle = 1
                sh1.ThreeD.BevelTopInset  = 8f;
                sh1.ThreeD.BevelTopDepth  = 6f;
                sh1.ThreeD.Depth          = 0f;
            }
            catch { /* ThreeD API may vary across PP versions */ }

            // Shape 2: Rounded rectangle with Relaxed Inset bevel + depth
            dynamic sh2 = slide.Shapes.AddShape(MsoShapeRoundedRectangle, 310f, 60f, 200f, 140f);
            sh2.Name = "BevelRelaxed";
            sh2.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent2;
            sh2.Fill.Solid();
            sh2.TextFrame.TextRange.Text = "Relaxed Inset";
            sh2.TextFrame.TextRange.Font.Size = 14;
            sh2.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh2.ThreeD.BevelTopType   = 2;     // msoBevelRelaxedInset = 2
                sh2.ThreeD.BevelTopInset  = 10f;
                sh2.ThreeD.BevelTopDepth  = 10f;
                sh2.ThreeD.Depth          = 20f;
            }
            catch { }

            // Shape 3: Ellipse with Angle bevel + extrusion + material
            dynamic sh3 = slide.Shapes.AddShape(MsoShapeOval, 560f, 60f, 200f, 140f);
            sh3.Name = "BevelAngle";
            sh3.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent3;
            sh3.Fill.Solid();
            sh3.TextFrame.TextRange.Text = "Angle + Extrusion";
            sh3.TextFrame.TextRange.Font.Size = 13;
            sh3.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh3.ThreeD.BevelTopType   = 5;     // msoBevelAngle = 5
                sh3.ThreeD.BevelTopInset  = 12f;
                sh3.ThreeD.BevelTopDepth  = 8f;
                sh3.ThreeD.Depth          = 40f;
                sh3.ThreeD.ExtrusionColor.RGB = 0x7030A0;
            }
            catch { }

            // Shape 4: Rectangle with Cross bevel + scene camera
            dynamic sh4 = slide.Shapes.AddShape(MsoShapeRectangle, 60f, 240f, 200f, 140f);
            sh4.Name = "BevelCross";
            sh4.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent4;
            sh4.Fill.Solid();
            sh4.TextFrame.TextRange.Text = "Cross + Scene3D";
            sh4.TextFrame.TextRange.Font.Size = 14;
            sh4.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh4.ThreeD.BevelTopType   = 7;     // msoBevelCross = 7
                sh4.ThreeD.BevelTopInset  = 6f;
                sh4.ThreeD.BevelTopDepth  = 6f;
                // Scene 3D via PresetCamera
                sh4.ThreeD.SetPresetCamera(20);    // msoCameraOrthographicFront ≈ 20
            }
            catch { }

            // Shape 5: Rectangle with contour (no bevel)
            dynamic sh5 = slide.Shapes.AddShape(MsoShapeRectangle, 310f, 240f, 200f, 140f);
            sh5.Name = "ContourOnly";
            sh5.Fill.ForeColor.ObjectThemeColor = 5; // MsoThemeColorAccent1
            sh5.Fill.Solid();
            sh5.TextFrame.TextRange.Text = "Contour + Depth";
            sh5.TextFrame.TextRange.Font.Size = 14;
            sh5.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh5.ThreeD.Depth          = 60f;
                sh5.ThreeD.ContourWidth   = 4f;
                sh5.ThreeD.ContourColor.RGB = 0xC55A11;
            }
            catch { }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Deck 12: Fill depth — multi-stop gradients, picture fill, pattern fill
    // -----------------------------------------------------------------------
    private static void GenerateFills(dynamic app, string pptxPath, string refDir)
    {
        // PowerPoint COM fill constants
        const int MsoGradientHorizontal           = 1;   // msoGradientHorizontal (linear)
        const int MsoGradientFromCenter           = 7;   // msoGradientFromCenter (radial)
        const int MsoPatternLightDownwardDiagonal = 21;  // msoPatternLightDownwardDiagonal -> ltDnDiag
        const int MsoPatternCross                 = 51;  // msoPatternCross

        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Title
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
            tb.TextFrame.TextRange.Text = "Fill Depth — Gradients / Picture / Pattern";
            tb.TextFrame.TextRange.Font.Size = 18;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            // Shape 1: 3-stop linear gradient (custom stops)
            dynamic sh1 = slide.Shapes.AddShape(MsoShapeRectangle, 40f, 55f, 200f, 140f);
            sh1.Name = "Grad3Stop";
            sh1.TextFrame.TextRange.Text = "3-Stop Linear";
            sh1.TextFrame.TextRange.Font.Size = 13;
            sh1.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            try
            {
                sh1.Fill.TwoColorGradient(MsoGradientHorizontal, 1);
                sh1.Fill.GradientStops.Insert(0x0000FF, 0.0f, 1);  // stop 1: red at 0%
                sh1.Fill.GradientStops[1].Color.RGB = 0xFF0000;
                sh1.Fill.GradientStops[1].Position = 0.0f;
                // Try multi-stop: insert middle stop
                try { sh1.Fill.GradientStops.Insert(0x00FF00, 0.5f, 2); } catch { }
            }
            catch
            {
                // Fallback: simple 2-color gradient
                try
                {
                    sh1.Fill.TwoColorGradient(MsoGradientHorizontal, 1);
                    sh1.Fill.ForeColor.RGB = 0xFF0000;
                    sh1.Fill.BackColor.RGB = 0x0000FF;
                }
                catch { }
            }

            // Shape 2: Radial gradient (from center)
            dynamic sh2 = slide.Shapes.AddShape(MsoShapeOval, 270f, 55f, 200f, 140f);
            sh2.Name = "GradRadial";
            sh2.TextFrame.TextRange.Text = "Radial";
            sh2.TextFrame.TextRange.Font.Size = 13;
            try
            {
                sh2.Fill.TwoColorGradient(MsoGradientFromCenter, 1);
                sh2.Fill.ForeColor.RGB = 0xFFFFFF;
                sh2.Fill.BackColor.RGB = 0x0070C0;
            }
            catch { }

            // Shape 3: Pattern fill — diagonal stripe
            dynamic sh3 = slide.Shapes.AddShape(MsoShapeRectangle, 500f, 55f, 200f, 140f);
            sh3.Name = "PatternDiag";
            sh3.TextFrame.TextRange.Text = "Diag Pattern";
            sh3.TextFrame.TextRange.Font.Size = 13;
            try
            {
                sh3.Fill.Patterned(MsoPatternLightDownwardDiagonal);
                sh3.Fill.ForeColor.RGB = 0x0000FF;
                sh3.Fill.BackColor.RGB = 0xFFFFFF;
            }
            catch { }

            // Shape 4: Pattern fill — cross
            dynamic sh4 = slide.Shapes.AddShape(MsoShapeRectangle, 730f, 55f, 200f, 140f);
            sh4.Name = "PatternCross";
            sh4.TextFrame.TextRange.Text = "Cross Pattern";
            sh4.TextFrame.TextRange.Font.Size = 13;
            try
            {
                sh4.Fill.Patterned(MsoPatternCross);
                sh4.Fill.ForeColor.RGB = 0xFF0000;
                sh4.Fill.BackColor.RGB = 0xFFFF00;
            }
            catch { }

            // Shape 5 & 6 on row 2 — more gradient presets
            dynamic sh5 = slide.Shapes.AddShape(MsoShapeRoundedRectangle, 40f, 230f, 200f, 140f);
            sh5.Name = "GradPreset";
            sh5.TextFrame.TextRange.Text = "Preset Gradient";
            sh5.TextFrame.TextRange.Font.Size = 13;
            try
            {
                // msoGradientPresetColors=3, msoGradientSunrise=11
                sh5.Fill.PresetGradient(MsoGradientHorizontal, 1, 11);
            }
            catch
            {
                try
                {
                    sh5.Fill.TwoColorGradient(MsoGradientHorizontal, 1);
                    sh5.Fill.ForeColor.ObjectThemeColor = MsoThemeColorAccent1;
                    sh5.Fill.BackColor.ObjectThemeColor = MsoThemeColorAccent3;
                }
                catch { }
            }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // 13-wordart: gradient-filled + outlined + shadowed text + warp presets
    // -----------------------------------------------------------------------

    private static void GenerateWordArt(dynamic app, string pptxPath, string refDir)
    {
        const int MsoGradientHorizontal = 1;

        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);
            dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

            // Title label
            dynamic tb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 32f);
            tb.TextFrame.TextRange.Text = "WordArt / Text Effects";
            tb.TextFrame.TextRange.Font.Size = 18;
            tb.TextFrame.TextRange.Font.Bold = MsoTrue;

            // ── Shape 1: gradient-filled text ─────────────────────────────
            // A textbox with large bold text whose fill is a two-color linear gradient.
            dynamic sh1 = slide.Shapes.AddTextbox(1, 40f, 50f, 380f, 100f);
            sh1.Name = "GradText";
            var r1 = sh1.TextFrame.TextRange;
            r1.Text = "Gradient Fill";
            r1.Font.Size = 44;
            r1.Font.Bold = MsoTrue;
            try
            {
                // Apply gradient fill to the text characters via TextEffectFormat is not
                // COM-accessible; instead apply a shape fill which PowerPoint will store
                // as a:solidFill on the run via format-as-picture path.
                // Alternatively, just set font color and add a shape gradient fill so the
                // exported PPTX round-trips the gradient correctly via shape-level effects.
                sh1.Fill.TwoColorGradient(MsoGradientHorizontal, 1);
                sh1.Fill.ForeColor.RGB = 0xFF6600;   // orange
                sh1.Fill.BackColor.RGB = 0xCC0000;   // deep red
                sh1.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF; // white outline text
            }
            catch { }

            // ── Shape 2: text with shadow ──────────────────────────────────
            dynamic sh2 = slide.Shapes.AddTextbox(1, 450f, 50f, 480f, 100f);
            sh2.Name = "ShadowText";
            var r2 = sh2.TextFrame.TextRange;
            r2.Text = "Text Shadow";
            r2.Font.Size = 40;
            r2.Font.Bold = MsoTrue;
            r2.Font.Color.RGB = 0x0070C0; // blue
            try
            {
                sh2.Shadow.Visible = MsoTrue;
                sh2.Shadow.OffsetX = 4f;
                sh2.Shadow.OffsetY = 4f;
                sh2.Shadow.Blur = 5f;
                sh2.Shadow.ForeColor.RGB = 0x404040;
                sh2.Shadow.Transparency = 0.3f;
            }
            catch { }

            // ── Shape 3: text with outline ────────────────────────────────
            dynamic sh3 = slide.Shapes.AddTextbox(1, 40f, 170f, 380f, 100f);
            sh3.Name = "OutlineText";
            var r3 = sh3.TextFrame.TextRange;
            r3.Text = "Text Outline";
            r3.Font.Size = 44;
            r3.Font.Bold = MsoTrue;
            r3.Font.Color.RGB = 0x00B050; // green fill
            try
            {
                // pp line / text outline via Font
                r3.Font.Size = 44;
                // TextEffectFormat is not easily COM-addressable; at minimum set shape outline
                sh3.Line.Visible = MsoTrue;
                sh3.Line.ForeColor.RGB = 0x004000;
                sh3.Line.Weight = 1.5f;
            }
            catch { }

            // ── Shape 4: WordArt warp — textArchUp ───────────────────────
            // Use WordArt style via AddTextEffect
            dynamic sh4;
            try
            {
                // WordArt preset 29 = textArchUp in most PowerPoint versions
                sh4 = slide.Shapes.AddTextEffect(
                    /*PresetTextEffect*/ 29,
                    "Arch Up Text", "Arial Black", 36f, MsoFalse, MsoFalse,
                    450f, 170f);
                sh4.Name = "WarpArchUp";
                sh4.Width  = 460f;
                sh4.Height = 100f;
                sh4.TextEffect.FontName = "Arial Black";
                sh4.TextEffect.FontSize = 32f;
                sh4.TextEffect.FontBold = MsoTrue;
                try
                {
                    sh4.Fill.ForeColor.RGB = 0x7030A0; // purple
                    sh4.Fill.Solid();
                }
                catch { }
            }
            catch
            {
                // Fallback: plain text
                sh4 = slide.Shapes.AddTextbox(1, 450f, 170f, 460f, 100f);
                sh4.Name = "WarpArchUpFallback";
                sh4.TextFrame.TextRange.Text = "Arch Up Text";
                sh4.TextFrame.TextRange.Font.Size = 36;
                sh4.TextFrame.TextRange.Font.Color.RGB = 0x7030A0;
            }

            // ── Shape 5: WordArt warp — textWave ─────────────────────────
            dynamic sh5;
            try
            {
                // WordArt preset 34 ≈ textWave
                sh5 = slide.Shapes.AddTextEffect(
                    34,
                    "Wave Text", "Arial Black", 32f, MsoFalse, MsoFalse,
                    40f, 290f);
                sh5.Name = "WarpWave";
                sh5.Width  = 860f;
                sh5.Height = 100f;
                sh5.TextEffect.FontBold = MsoTrue;
                try
                {
                    sh5.Fill.ForeColor.RGB = 0xC00000;
                    sh5.Fill.Solid();
                }
                catch { }
            }
            catch
            {
                sh5 = slide.Shapes.AddTextbox(1, 40f, 290f, 860f, 100f);
                sh5.Name = "WarpWaveFallback";
                sh5.TextFrame.TextRange.Text = "Wave Text — warp best-effort";
                sh5.TextFrame.TextRange.Font.Size = 32;
                sh5.TextFrame.TextRange.Font.Color.RGB = 0xC00000;
            }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    private static void SaveAndExport(dynamic pres, string pptxPath, string refDir)
    {
        if (File.Exists(pptxPath))
            File.Delete(pptxPath);

        // ppSaveAsOpenXMLPresentation = 24
        pres.SaveAs(pptxPath, 24);

        // Export all slides as PNG
        var slideCount = (int)pres.Slides.Count;
        for (var i = 1; i <= slideCount; i++)
        {
            var pngPath = Path.Combine(refDir, $"slide-{i:D2}.png");
            dynamic slide = pres.Slides.Item(i);
            slide.Export(pngPath, "PNG", ExportWidth, ExportHeight);
        }

        pres.Close();
    }

    private static dynamic CreatePowerPointApplication()
    {
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new InvalidOperationException("PowerPoint.Application COM ProgID not found.");

        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("PowerPoint.Application activation returned null.");

        dynamic app = instance;
        app.DisplayAlerts = PpAlertsNone;
        // Note: app.Visible = false is not allowed after activation; window visibility
        // is controlled per-presentation via WithWindow=msoFalse in Presentations.Add/Open.
        return app;
    }

    private static void TryClosePresentation(ref dynamic? pres)
    {
        if (pres is null)
            return;

        try   { pres.Close(); }
        catch { /* best effort */ }
        finally
        {
            if (Marshal.IsComObject(pres))
                try { Marshal.FinalReleaseComObject(pres); } catch { }
            pres = null;
        }
    }

    private static void QuitApplication(ref dynamic? app)
    {
        if (app is null)
            return;

        try   { app.Quit(); }
        catch { /* best effort */ }
        finally
        {
            if (Marshal.IsComObject(app))
                try { Marshal.FinalReleaseComObject(app); } catch { }
            app = null;
        }
    }

    private static void FinishApplication(ref dynamic? app, bool ownsApplication)
    {
        if (ownsApplication)
        {
            QuitApplication(ref app);
            return;
        }

        if (app is null)
            return;

        if (Marshal.IsComObject(app))
            try { Marshal.FinalReleaseComObject(app); } catch { }
        app = null;
    }

    private static HashSet<int> GetPowerPointProcessIds() =>
        Process.GetProcessesByName("POWERPNT").Select(p => p.Id).ToHashSet();

    private static void WaitForPowerPointToExit(HashSet<int> pids, int timeoutMs)
    {
        if (pids.Count == 0) return;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!Process.GetProcessesByName("POWERPNT").Any(p => pids.Contains(p.Id)))
                return;
            Thread.Sleep(300);
        }
    }

    // -----------------------------------------------------------------------
    // 14-smartart-live: Process + Hierarchy/OrgChart + Cycle + List SmartArt
    //   — four slides, one family per slide, with node text
    //   — used for live-layout parity measurement (Theme 17)
    // -----------------------------------------------------------------------
    private static void GenerateSmartArtLive(dynamic app, string pptxPath, string refDir)
    {
        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);

            // Helper: find a layout by family keyword, fallback to index 1
            dynamic FindLayout(string keyword)
            {
                dynamic layouts = app.SmartArtLayouts;
                for (int li = 1; li <= (int)layouts.Count; li++)
                {
                    dynamic layout = layouts.Item(li);
                    string name = (string)layout.Name;
                    if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return layout;
                }
                return layouts.Item(1);
            }

            // ── Slide 1: Process ──────────────────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(1, PpLayoutBlank);
                dynamic tb = slide.Shapes.AddTextbox(1, 20f, 6f, 920f, 30f);
                tb.TextFrame.TextRange.Text = "SmartArt Live — Process";
                tb.TextFrame.TextRange.Font.Size = 18; tb.TextFrame.TextRange.Font.Bold = MsoTrue;

                try
                {
                    dynamic layout = FindLayout("Process");
                    dynamic sa = slide.Shapes.AddSmartArt(layout, 50f, 50f, 860f, 380f);
                    dynamic nodes = sa.SmartArt.AllNodes;
                    string[] texts = ["Plan", "Design", "Build", "Test", "Deploy"];
                    for (int ni = 1; ni <= Math.Min((int)nodes.Count, texts.Length); ni++)
                        try { nodes.Item(ni).TextFrame2.TextRange.Text = texts[ni - 1]; } catch { }
                }
                catch (Exception ex) { Console.Write($"(Process SmartArt: {ex.Message}) "); }
            }

            // ── Slide 2: Hierarchy / OrgChart ─────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(2, PpLayoutBlank);
                dynamic tb = slide.Shapes.AddTextbox(1, 20f, 6f, 920f, 30f);
                tb.TextFrame.TextRange.Text = "SmartArt Live — Hierarchy";
                tb.TextFrame.TextRange.Font.Size = 18; tb.TextFrame.TextRange.Font.Bold = MsoTrue;

                try
                {
                    dynamic layout = FindLayout("Org");
                    if ((string)layout.Name == (string)app.SmartArtLayouts.Item(1).Name)
                        layout = FindLayout("Hierarch");

                    dynamic sa = slide.Shapes.AddSmartArt(layout, 50f, 50f, 860f, 380f);
                    dynamic nodes = sa.SmartArt.AllNodes;
                    // Populate root + children
                    string[] texts = ["CEO", "VP Sales", "VP Engineering", "VP Marketing"];
                    for (int ni = 1; ni <= Math.Min((int)nodes.Count, texts.Length); ni++)
                        try { nodes.Item(ni).TextFrame2.TextRange.Text = texts[ni - 1]; } catch { }
                }
                catch (Exception ex) { Console.Write($"(Hierarchy SmartArt: {ex.Message}) "); }
            }

            // ── Slide 3: Cycle ─────────────────────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(3, PpLayoutBlank);
                dynamic tb = slide.Shapes.AddTextbox(1, 20f, 6f, 920f, 30f);
                tb.TextFrame.TextRange.Text = "SmartArt Live — Cycle";
                tb.TextFrame.TextRange.Font.Size = 18; tb.TextFrame.TextRange.Font.Bold = MsoTrue;

                try
                {
                    dynamic layout = FindLayout("Cycle");
                    dynamic sa = slide.Shapes.AddSmartArt(layout, 50f, 50f, 860f, 380f);
                    dynamic nodes = sa.SmartArt.AllNodes;
                    string[] texts = ["Idea", "Plan", "Execute", "Review", "Improve"];
                    for (int ni = 1; ni <= Math.Min((int)nodes.Count, texts.Length); ni++)
                        try { nodes.Item(ni).TextFrame2.TextRange.Text = texts[ni - 1]; } catch { }
                }
                catch (Exception ex) { Console.Write($"(Cycle SmartArt: {ex.Message}) "); }
            }

            // ── Slide 4: List ──────────────────────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(4, PpLayoutBlank);
                dynamic tb = slide.Shapes.AddTextbox(1, 20f, 6f, 920f, 30f);
                tb.TextFrame.TextRange.Text = "SmartArt Live — List";
                tb.TextFrame.TextRange.Font.Size = 18; tb.TextFrame.TextRange.Font.Bold = MsoTrue;

                try
                {
                    dynamic layout = FindLayout("List");
                    dynamic sa = slide.Shapes.AddSmartArt(layout, 50f, 50f, 860f, 380f);
                    dynamic nodes = sa.SmartArt.AllNodes;
                    string[] texts = ["Requirement 1", "Requirement 2", "Requirement 3", "Requirement 4"];
                    for (int ni = 1; ni <= Math.Min((int)nodes.Count, texts.Length); ni++)
                        try { nodes.Item(ni).TextFrame2.TextRange.Text = texts[ni - 1]; } catch { }
                }
                catch (Exception ex) { Console.Write($"(List SmartArt: {ex.Message}) "); }
            }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    // -----------------------------------------------------------------------
    // 16-bg-tabs-vtext: slide gradient background + tab stops + vertical text
    // -----------------------------------------------------------------------

    private static void GenerateBgTabsVtext(dynamic app, string pptxPath, string refDir)
    {
        // PowerPoint COM constants
        const int PpTabStopLeft       = 1;  // ppTabStopLeft
        const int PpTabStopRight      = 3;  // ppTabStopRight
        const int PpOrientationUpward = 2;  // ppUpward  (90° vertical text)
        const int PpOrientationDownward = 3;  // ppDownward (270°)

        dynamic? pres = null;
        try
        {
            pres = app.Presentations.Add(MsoFalse);

            // ── Slide 1: gradient slide background ──────────────────────────
            {
                dynamic slide = pres.Slides.Add(1, PpLayoutBlank);

                // Set a two-color gradient background.
                // Must unlink from master first (FollowMasterBackground = MsoFalse).
                try
                {
                    slide.FollowMasterBackground = MsoFalse;
                    slide.Background.Fill.TwoColorGradient(1, 1);  // msoGradientHorizontal, variant 1
                    slide.Background.Fill.ForeColor.RGB = 0xFFD0A0;  // peach / light orange
                    slide.Background.Fill.BackColor.RGB = 0x0070C0;  // blue
                }
                catch (Exception ex) { Console.Write($"(bg gradient: {ex.Message}) "); }

                // Title label
                dynamic tb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
                tb.TextFrame.TextRange.Text = "Slide 1 — Gradient Background";
                tb.TextFrame.TextRange.Font.Size = 20;
                tb.TextFrame.TextRange.Font.Bold = MsoTrue;
                tb.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
            }

            // ── Slide 2: tab stops ───────────────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(2, PpLayoutBlank);

                // Title
                dynamic lblTb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
                lblTb.TextFrame.TextRange.Text = "Slide 2 — Tab Stops";
                lblTb.TextFrame.TextRange.Font.Size = 18;
                lblTb.TextFrame.TextRange.Font.Bold = MsoTrue;

                // Textbox with tab-delimited columns
                dynamic tb = slide.Shapes.AddTextbox(1, 40f, 60f, 880f, 300f);
                tb.Name = "TabDemo";
                tb.TextFrame.WordWrap = MsoTrue;

                // Three tab stops using TextFrame2.TextRange API.
                try
                {
                    dynamic tf2 = tb.TextFrame2;
                    dynamic tr = tf2.TextRange;
                    tr.Text = "Name\tDept\tSalary\rAlice\tEngineering\t$95,000\rBob\tMarketing\t$72,000\rCarol\tFinance\t$88,000";
                    // TabStops via TextFrame2.TextRange.ParagraphFormat
                    dynamic pf2 = tr.ParagraphFormat;
                    try
                    {
                        pf2.TabStops.Add(200f * 12700, PpTabStopLeft);   // EMU: 200pt * 12700
                        pf2.TabStops.Add(450f * 12700, PpTabStopLeft);
                        pf2.TabStops.Add(680f * 12700, PpTabStopRight);
                    }
                    catch
                    {
                        // Fallback: Tab stops via points using TextRange1.ParagraphFormat
                        try
                        {
                            dynamic pf1 = tb.TextFrame.TextRange.ParagraphFormat;
                            pf1.TabStops.Add(200f, PpTabStopLeft);
                            pf1.TabStops.Add(450f, PpTabStopLeft);
                            pf1.TabStops.Add(680f, PpTabStopRight);
                        }
                        catch { }
                    }
                    tr.Font.Size = 16;
                }
                catch (Exception ex) { Console.Write($"(tabs: {ex.Message}) "); }
            }

            // ── Slide 3: vertical text ───────────────────────────────────────
            {
                dynamic slide = pres.Slides.Add(3, PpLayoutBlank);

                // Title
                dynamic lblTb = slide.Shapes.AddTextbox(1, 20f, 8f, 900f, 35f);
                lblTb.TextFrame.TextRange.Text = "Slide 3 — Vertical Text";
                lblTb.TextFrame.TextRange.Font.Size = 18;
                lblTb.TextFrame.TextRange.Font.Bold = MsoTrue;

                // Vertical-up textbox (90°)
                try
                {
                    dynamic sh1 = slide.Shapes.AddShape(MsoShapeRectangle, 60f, 60f, 80f, 300f);
                    sh1.Name = "VertUp";
                    sh1.Fill.ForeColor.RGB = 0x4472C4;
                    sh1.Fill.Solid();
                    sh1.TextFrame.TextRange.Text = "Vertical Upward Text";
                    sh1.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
                    sh1.TextFrame.TextRange.Font.Size = 14;
                    sh1.TextFrame.Orientation = PpOrientationUpward;
                }
                catch (Exception ex) { Console.Write($"(vert-up: {ex.Message}) "); }

                // Vertical-down textbox (270°)
                try
                {
                    dynamic sh2 = slide.Shapes.AddShape(MsoShapeRectangle, 200f, 60f, 80f, 300f);
                    sh2.Name = "VertDown";
                    sh2.Fill.ForeColor.RGB = 0xED7D31;
                    sh2.Fill.Solid();
                    sh2.TextFrame.TextRange.Text = "Vertical Downward Text";
                    sh2.TextFrame.TextRange.Font.Color.RGB = 0xFFFFFF;
                    sh2.TextFrame.TextRange.Font.Size = 14;
                    sh2.TextFrame.Orientation = PpOrientationDownward;
                }
                catch (Exception ex) { Console.Write($"(vert-down: {ex.Message}) "); }

                // Regular horizontal comparison text
                dynamic cmpTb = slide.Shapes.AddTextbox(1, 340f, 150f, 500f, 60f);
                cmpTb.TextFrame.TextRange.Text = "← Compare with horizontal text here";
                cmpTb.TextFrame.TextRange.Font.Size = 14;
            }

            SaveAndExport(pres, pptxPath, refDir);
        }
        finally
        {
            TryClosePresentation(ref pres);
        }
    }

    private static void KillPowerPointProcesses(HashSet<int> pids)
    {
        foreach (var p in Process.GetProcessesByName("POWERPNT"))
        {
            if (!pids.Contains(p.Id)) continue;
            try { p.Kill(entireProcessTree: true); p.WaitForExit(5_000); }
            catch { }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    // -----------------------------------------------------------------------
    // Deck 19: Chart data labels + secondary axis
    //   Slide 1 — Clustered column with value labels (OutsideEnd)
    //   Slide 2 — Pie with percent labels
    //   Slide 3 — Combo: columns (revenue) + line (units) on secondary axis,
    //             value labels on the column series
    // -----------------------------------------------------------------------
    private static void GenerateChartLabels19(dynamic app, string pptxPath, string refDir)
    {
        dynamic? comPres = null;
        try
        {
            comPres = app.Presentations.Add(MsoTrue);
            try { app.WindowState = 2; } catch { }

            // Slide 1: column + value labels
            AddChartSlideViaCom(comPres, "Column Chart — Value Data Labels",
                51, // xlColumnClustered
                new[] { "Q1", "Q2", "Q3", "Q4" },
                new[] {
                    ("North", new double[] { 120, 195, 165, 240 }),
                    ("South", new double[] {  80, 145, 220, 185 })
                });

            // Slide 2: pie + percent labels
            AddChartSlideViaCom(comPres, "Pie Chart — Percent Labels",
                5, // xlPie
                new[] { "Product A", "Product B", "Product C", "Product D" },
                new[] { ("Share", new double[] { 45, 30, 15, 10 }) });

            // Slide 3: combo (column primary + line secondary axis)
            AddChartSlideViaCom(comPres, "Combo — Revenue (columns) + Units (line, secondary axis)",
                51, // xlColumnClustered — will patch one series to line+secondary in XML
                new[] { "Jan", "Feb", "Mar", "Apr" },
                new[] {
                    ("Revenue $K", new double[] { 120, 145, 98, 175 }),
                    ("Units",      new double[] { 5200, 6100, 4800, 7400 })
                });

            if (File.Exists(pptxPath)) File.Delete(pptxPath);
            comPres.SaveAs(pptxPath, 24, MsoFalse);
            comPres.Close();
            comPres = null;

            // Patch all 3 charts to inject data labels + secondary-axis wiring
            PatchChartLabels19InZip(pptxPath);

            // Export reference PNGs
            dynamic? exportPres = null;
            try
            {
                exportPres = app.Presentations.Open(pptxPath, MsoTrue, MsoFalse, MsoFalse);
                int slideCount = (int)exportPres.Slides.Count;
                for (int i = 1; i <= slideCount; i++)
                {
                    var pngPath = Path.Combine(refDir, $"slide-{i:D2}.png");
                    dynamic slide = exportPres.Slides.Item(i);
                    slide.Export(pngPath, "PNG", ExportWidth, ExportHeight);
                }
                exportPres.Close();
                exportPres = null;
            }
            finally
            {
                if (exportPres is not null)
                    try { exportPres.Close(); } catch { }
            }
        }
        finally
        {
            if (comPres is not null)
            {
                try { comPres.Close(); } catch { }
                if (Marshal.IsComObject(comPres))
                    try { Marshal.FinalReleaseComObject(comPres); } catch { }
            }
        }
    }

    /// <summary>Public entry-point so Program.cs can call PatchChartLabels19InZip standalone.</summary>
    internal static void PatchChartLabels19(string pptxPath) => PatchChartLabels19InZip(pptxPath);

    /// <summary>
    /// Patches chart XML for the 19-chart-labels deck:
    ///   chart1 (slide 1) — column chart: patch data + inject c:dLbls showVal OutsideEnd
    ///   chart2 (slide 2) — pie chart:    patch data + inject c:dLbls showPercent
    ///   chart3 (slide 3) — combo chart:  patch data + inject c:dLbls on ser[0] + add secondary valAx + change ser[1] to line on secondary
    /// </summary>
    private static void PatchChartLabels19InZip(string pptxPath)
    {
        var patchedPath = pptxPath + ".patched";
        using var srcZip  = ZipFile.OpenRead(pptxPath);
        using var destZip = ZipFile.Open(patchedPath, ZipArchiveMode.Create);

        var chartEntries = srcZip.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"ppt/charts/chart\d+\.xml$"))
            .OrderBy(e => int.Parse(System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+").Value))
            .ToList();

        foreach (var entry in srcZip.Entries)
        {
            // Use IndexOf so the correct patch is applied regardless of zip-entry order
            var entryChartIdx = chartEntries.IndexOf(entry);
            if (entryChartIdx >= 0)
            {
                string xmlText;
                using (var s = entry.Open())
                using (var reader = new StreamReader(s, Encoding.UTF8))
                    xmlText = reader.ReadToEnd();

                System.Xml.XmlDocument patched;
                switch (entryChartIdx)
                {
                    case 0: // slide 1: column + value labels OutsideEnd
                        patched = PatchChartXmlViaXmlDocument(xmlText, new ChartPatchData(
                            Cats: new[] { "Q1", "Q2", "Q3", "Q4" },
                            Series: new[] {
                                ("North", new double[] { 120, 195, 165, 240 }),
                                ("South", new double[] {  80, 145, 220, 185 })
                            }));
                        // Inject chart-level dLbls: showVal + dLblPos outEnd
                        InjectChartLevelDLbls(patched, showVal: true, showPct: false, position: "outEnd");
                        break;

                    case 1: // slide 2: pie + percent labels
                        patched = PatchChartXmlViaXmlDocument(xmlText, new ChartPatchData(
                            Cats: new[] { "Product A", "Product B", "Product C", "Product D" },
                            Series: new[] { ("Share", new double[] { 45, 30, 15, 10 }) }));
                        // Inject chart-level dLbls: showPercent (no value)
                        InjectChartLevelDLbls(patched, showVal: false, showPct: true, position: "bestFit");
                        break;

                    case 2: // slide 3: combo — columns + line on secondary axis
                        patched = PatchComboChartXml(xmlText,
                            cats: new[] { "Jan", "Feb", "Mar", "Apr" },
                            primarySeries: ("Revenue $K", new double[] { 120, 145, 98, 175 }),
                            secondarySeries: ("Units",    new double[] { 5200, 6100, 4800, 7400 }));
                        break;

                    default:
                        patched = new System.Xml.XmlDocument();
                        patched.LoadXml(xmlText);
                        break;
                }

                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using (var s = destEntry.Open())
                {
                    var settings = new System.Xml.XmlWriterSettings
                        { Encoding = utf8NoBom, Indent = false, CloseOutput = false };
                    using (var xw = System.Xml.XmlWriter.Create(s, settings))
                        patched.Save(xw);
                }
            }
            else
            {
                var destEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var src  = entry.Open();
                using var dest = destEntry.Open();
                src.CopyTo(dest);
            }
        }

        srcZip.Dispose();
        destZip.Dispose();

        File.Delete(pptxPath);
        File.Move(patchedPath, pptxPath);
    }

    /// <summary>
    /// Injects a c:dLbls element into the first chart-type element of a chart's plotArea.
    /// </summary>
    private static void InjectChartLevelDLbls(
        System.Xml.XmlDocument doc,
        bool showVal, bool showPct, string position)
    {
        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("c", ChartNsUri);

        // Find the first chart-type node inside plotArea (e.g. c:barChart, c:pieChart, etc.)
        var plotArea = doc.SelectSingleNode("//c:plotArea", nsMgr);
        if (plotArea is null) return;

        // Find first child that looks like a chart type (ends with "Chart" and has c:ser children)
        System.Xml.XmlNode? chartTypeNode = null;
        foreach (System.Xml.XmlNode child in plotArea.ChildNodes)
        {
            if (child.LocalName.EndsWith("Chart", StringComparison.OrdinalIgnoreCase))
            {
                chartTypeNode = child;
                break;
            }
        }
        if (chartTypeNode is null) return;

        // Remove existing dLbls if any
        var existing = chartTypeNode.SelectSingleNode("c:dLbls", nsMgr);
        if (existing is not null)
            chartTypeNode.RemoveChild(existing);

        // Build dLbls element
        var dLbls = doc.CreateElement("c", "dLbls", ChartNsUri);

        if (showVal)
        {
            var sv = doc.CreateElement("c", "showVal", ChartNsUri);
            sv.SetAttribute("val", "1");
            dLbls.AppendChild(sv);
        }
        if (showPct)
        {
            var sp = doc.CreateElement("c", "showPercent", ChartNsUri);
            sp.SetAttribute("val", "1");
            dLbls.AppendChild(sp);
        }
        // Always explicitly set the others to 0
        foreach (var flag in new[] { "showLegendKey", "showSerName", "showCatName" })
        {
            if (showVal && flag == "showVal") continue;
            if (showPct && flag == "showPercent") continue;
            var el = doc.CreateElement("c", flag, ChartNsUri);
            el.SetAttribute("val", "0");
            dLbls.AppendChild(el);
        }
        if (!string.IsNullOrEmpty(position))
        {
            var pos = doc.CreateElement("c", "dLblPos", ChartNsUri);
            pos.SetAttribute("val", position);
            dLbls.AppendChild(pos);
        }

        // Insert dLbls before the first c:ser node
        var firstSer = chartTypeNode.SelectSingleNode("c:ser", nsMgr);
        if (firstSer is not null)
            chartTypeNode.InsertBefore(dLbls, firstSer);
        else
            chartTypeNode.AppendChild(dLbls);
    }

    /// <summary>
    /// Patches the combo chart (slide 3) to create a barChart+lineChart combo with secondary valAx.
    /// Strategy: patch data in the barChart as usual, then move ser[1] into a new lineChart,
    /// and add a secondary valAx on the right. The lineChart references the secondary valAx,
    /// satisfying PowerPoint's axis consistency requirement.
    /// externalData is kept (autoUpdate=0) so PowerPoint uses cached XML data.
    /// </summary>
    private static System.Xml.XmlDocument PatchComboChartXml(
        string xmlText,
        string[] cats,
        (string name, double[] vals) primarySeries,
        (string name, double[] vals) secondarySeries)
    {
        // Use the standard patcher to set the data
        var patchData = new ChartPatchData(
            Cats: cats,
            Series: new[]
            {
                (primarySeries.name,   primarySeries.vals),
                (secondarySeries.name, secondarySeries.vals)
            });
        var doc = PatchChartXmlViaXmlDocument(xmlText, patchData);
        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("c", ChartNsUri);

        var plotArea = doc.SelectSingleNode("//c:plotArea", nsMgr) as System.Xml.XmlElement;
        if (plotArea is null) return doc;

        // ── Find the primary barChart element ─────────────────────────────────
        var barChart = plotArea.SelectSingleNode("c:barChart", nsMgr) as System.Xml.XmlElement;
        if (barChart is null) return doc;

        // ── Get catAx ID and primary valAx ID from the barChart's axId list ───
        var barAxIdNodes = barChart.SelectNodes("c:axId", nsMgr);
        var barAxIds = barAxIdNodes is null
            ? Array.Empty<string>()
            : barAxIdNodes.Cast<System.Xml.XmlElement>().Select(e => e.GetAttribute("val")).ToArray();
        // barAxIds[0] = catAx id, barAxIds[1] = primary valAx id
        string catAxId = barAxIds.Length > 0 ? barAxIds[0] : "1";
        string primaryValAxId = barAxIds.Length > 1 ? barAxIds[1] : "2";

        // ── Extract ser[1] (the secondary series) from barChart ───────────────
        var serNodes = barChart.SelectNodes("c:ser", nsMgr);
        var allSeries = serNodes is null
            ? new List<System.Xml.XmlNode>()
            : serNodes.Cast<System.Xml.XmlNode>().ToList();
        System.Xml.XmlNode? ser1 = null;
        if (allSeries.Count >= 2)
        {
            ser1 = allSeries[1];
            barChart.RemoveChild(ser1);
        }

        // ── Add dLbls to the barChart (primary series) ────────────────────────
        InjectChartLevelDLbls(doc, showVal: true, showPct: false, position: "outEnd");

        // ── Build the secondary valAx (id=3, pos=r) ───────────────────────────
        const string SecondaryAxId = "3";
        var secValAx = BuildValAxEl(doc, SecondaryAxId, "r", catAxId);
        // crosses=max positions it on the right
        var crossesEl = secValAx.SelectSingleNode("c:crosses", nsMgr) as System.Xml.XmlElement;
        if (crossesEl is not null) crossesEl.SetAttribute("val", "max");

        // ── Build lineChart wrapping ser1 + axIds [catAxId, secondaryAxId] ─────
        var lineChart = doc.CreateElement("c", "lineChart", ChartNsUri);
        AppendSimpleAttrEl(doc, lineChart, "grouping", "standard");
        AppendSimpleAttrEl(doc, lineChart, "varyColors", "0");
        if (ser1 is not null)
        {
            // Re-index as ser idx=1, order=1
            var serIdxEl = ser1.SelectSingleNode("c:idx", nsMgr) as System.Xml.XmlElement;
            if (serIdxEl is not null) serIdxEl.SetAttribute("val", "1");
            var serOrderEl = ser1.SelectSingleNode("c:order", nsMgr) as System.Xml.XmlElement;
            if (serOrderEl is not null) serOrderEl.SetAttribute("val", "1");
            lineChart.AppendChild(ser1);
        }
        AppendAxId(doc, lineChart, catAxId);
        AppendAxId(doc, lineChart, SecondaryAxId);

        // ── Insert lineChart right after barChart in plotArea ─────────────────
        var barChartNext = barChart.NextSibling;
        if (barChartNext is not null)
            plotArea.InsertBefore(lineChart, barChartNext);
        else
            plotArea.AppendChild(lineChart);

        // ── Append secondary valAx at end of plotArea (after all axes) ────────
        plotArea.AppendChild(secValAx);

        // ── Ensure externalData has autoUpdate=0 ─────────────────────────────
        var nsMgrR = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgrR.AddNamespace("c", ChartNsUri);
        nsMgrR.AddNamespace("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var extData = doc.SelectSingleNode("//c:externalData", nsMgrR) as System.Xml.XmlElement;
        if (extData is not null)
        {
            var autoUpdate = extData.SelectSingleNode("c:autoUpdate", nsMgrR) as System.Xml.XmlElement;
            if (autoUpdate is null)
            {
                autoUpdate = doc.CreateElement("c", "autoUpdate", ChartNsUri);
                extData.AppendChild(autoUpdate);
            }
            autoUpdate.SetAttribute("val", "0");
        }

        return doc;
    }

    private static System.Xml.XmlElement BuildSerElForCombo(
        System.Xml.XmlDocument doc, System.Xml.XmlNamespaceManager nsMgr,
        int idx, string serName, string[] cats, double[] vals)
    {
        var ser = doc.CreateElement("c", "ser", ChartNsUri);

        var idxEl = doc.CreateElement("c", "idx", ChartNsUri);
        idxEl.SetAttribute("val", idx.ToString());
        ser.AppendChild(idxEl);

        var orderEl = doc.CreateElement("c", "order", ChartNsUri);
        orderEl.SetAttribute("val", idx.ToString());
        ser.AppendChild(orderEl);

        // c:tx > c:strRef > c:strCache
        var tx = doc.CreateElement("c", "tx", ChartNsUri);
        var strRef = doc.CreateElement("c", "strRef", ChartNsUri);
        var fEl = doc.CreateElement("c", "f", ChartNsUri);
        fEl.InnerText = "Sheet1!$A$1";
        strRef.AppendChild(fEl);
        var sc = CreateStrCache(doc, nsMgr, new[] { serName });
        strRef.AppendChild(sc);
        tx.AppendChild(strRef);
        ser.AppendChild(tx);

        // c:cat
        var cat = doc.CreateElement("c", "cat", ChartNsUri);
        var catStrRef = doc.CreateElement("c", "strRef", ChartNsUri);
        var catF = doc.CreateElement("c", "f", ChartNsUri);
        catF.InnerText = "Sheet1!$A$2:$A$5";
        catStrRef.AppendChild(catF);
        catStrRef.AppendChild(CreateStrCache(doc, nsMgr, cats));
        cat.AppendChild(catStrRef);
        ser.AppendChild(cat);

        // c:val
        var val = doc.CreateElement("c", "val", ChartNsUri);
        var numRef = doc.CreateElement("c", "numRef", ChartNsUri);
        var valF = doc.CreateElement("c", "f", ChartNsUri);
        valF.InnerText = $"Sheet1!$B$2:$B$5";
        numRef.AppendChild(valF);
        numRef.AppendChild(CreateNumCache(doc, nsMgr, vals));
        val.AppendChild(numRef);
        ser.AppendChild(val);

        return ser;
    }

    private static void AppendSimpleAttrEl(System.Xml.XmlDocument doc, System.Xml.XmlNode parent, string localName, string attrVal)
    {
        var el = doc.CreateElement("c", localName, ChartNsUri);
        el.SetAttribute("val", attrVal);
        parent.AppendChild(el);
    }

    private static void AppendAxId(System.Xml.XmlDocument doc, System.Xml.XmlNode parent, string id)
    {
        var el = doc.CreateElement("c", "axId", ChartNsUri);
        el.SetAttribute("val", id);
        parent.AppendChild(el);
    }

    private static System.Xml.XmlElement BuildCatAxEl(System.Xml.XmlDocument doc, string axId, string crossAxId)
    {
        var catAx = doc.CreateElement("c", "catAx", ChartNsUri);
        AppendSimpleAttrEl(doc, catAx, "axId",     axId);
        var scaling = doc.CreateElement("c", "scaling", ChartNsUri);
        AppendSimpleAttrEl(doc, scaling, "orientation", "minMax");
        catAx.AppendChild(scaling);
        AppendSimpleAttrEl(doc, catAx, "delete",   "0");
        AppendSimpleAttrEl(doc, catAx, "axPos",    "b");
        AppendSimpleAttrEl(doc, catAx, "crossAx",  crossAxId);
        return catAx;
    }

    private static System.Xml.XmlElement BuildValAxEl(System.Xml.XmlDocument doc, string axId, string axPos, string crossAxId)
    {
        var valAx = doc.CreateElement("c", "valAx", ChartNsUri);
        AppendSimpleAttrEl(doc, valAx, "axId",    axId);
        var scaling = doc.CreateElement("c", "scaling", ChartNsUri);
        AppendSimpleAttrEl(doc, scaling, "orientation", "minMax");
        valAx.AppendChild(scaling);
        AppendSimpleAttrEl(doc, valAx, "delete",  "0");
        AppendSimpleAttrEl(doc, valAx, "axPos",   axPos);
        var crosses = doc.CreateElement("c", "crosses", ChartNsUri);
        crosses.SetAttribute("val", "autoZero");
        valAx.AppendChild(crosses);
        AppendSimpleAttrEl(doc, valAx, "crossAx", crossAxId);
        return valAx;
    }
}
