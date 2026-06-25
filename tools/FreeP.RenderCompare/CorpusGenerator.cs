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
        var ownedPids = GetPowerPointProcessIds();

        dynamic? app = null;
        try
        {
            app = CreatePowerPointApplication();
            Console.WriteLine("  PowerPoint started for corpus generation.");

            var decks = new List<(string name, Action<dynamic, string, string> generate)>
            {
                ("01-title-slide",  GenerateTitleSlide),
                ("02-autoshapes",   GenerateAutoshapes),
                ("03-mixed-text",   GenerateMixedText),
                ("04-picture",      GeneratePicture),
                ("05-table",        GenerateTable),
                ("06-charts",       GenerateCharts),
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

            QuitApplication(ref app);
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
            QuitApplication(ref app);
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
                -4100,  // xlColumnClustered
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

            AddChartSlideViaCom(comPres, "Stacked Bar Chart",
                57,  // xlBarStacked
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
}
