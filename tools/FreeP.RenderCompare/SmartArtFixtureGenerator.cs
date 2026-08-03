using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;

namespace FreeP.RenderCompare;

/// <summary>
/// Theme 17: Generates 14-smartart-live.pptx programmatically (pure XML, no COM required).
///
/// Creates a 6-slide deck:
///   Slide 1 — Process:   Plan → Design → Build → Test → Deploy
///   Slide 2 — Hierarchy: CEO with VP Sales / VP Engineering / VP Marketing children
///   Slide 3 — Hierarchy3: CEO with the same reports, template leaves, and an orthogonal cached drawing
///   Slide 4 — Cycle:     Idea → Plan → Execute → Review → Improve
///   Slide 5 — List:      Requirement 1 through 4
///   Slide 7 — Relationship1: Audience / Need / Offer overlapping ellipses
///
/// Each slide has a real dgm:dataModel (ptLst + parOf cxnLst) and a layout1.xml with
/// the correct uniqueId so the FreeP live layout engine classifies and renders it.
/// The hierarchy3 slide also carries the representative PowerPoint node-plus-edge
/// dsp:drawing that exercises the bounded imported-cache admission contract.
/// </summary>
internal static class SmartArtFixtureGenerator
{
    // Namespaces
    private static readonly XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P15 = "http://schemas.microsoft.com/office/powerpoint/2012/main";
    private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    private static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct  = "http://schemas.openxmlformats.org/package/2006/content-types";

    // Relationship types
    private const string SlideRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string LayoutRelType     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout";
    private const string MasterRelType     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster";
    private const string ThemeRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string PresRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string DmRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string LoRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    private const string QsRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    private const string CsRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
    private const string DgmDrawRelType    = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    private const string DiagramDataCT    = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
    private const string DiagramLayoutCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
    private const string DiagramQsCT      = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
    private const string DiagramColorsCT  = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
    private const string DiagramDrawingCT = "application/vnd.ms-office.drawingml.diagramDrawing+xml";

    public static void Generate(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (UsePowerPointGenerator())
        {
            GenerateWithPowerPoint(outputPath);
            EnsurePresentationGuideList(outputPath);
            return;
        }

        // Define the 6 slides
        var slides = new[]
        {
            new SlideSpec
            {
                Title     = "SmartArt Live — Process",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/process1",
                Nodes     = [("n1","Plan"), ("n2","Design"), ("n3","Build"), ("n4","Test"), ("n5","Deploy")],
                Connections = [("n1","n2"), ("n2","n3"), ("n3","n4"), ("n4","n5")]
            },
            new SlideSpec
            {
                Title     = "SmartArt Live — Hierarchy",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
                Nodes     = [("r","CEO"), ("c1","VP Sales"), ("c2","VP Engineering"), ("c3","VP Marketing")],
                Connections = [("r","c1"), ("r","c2"), ("r","c3")]
            },
            new SlideSpec
            {
                Title     = "SmartArt Live — Hierarchy3",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3",
                Nodes     = [("r","CEO"), ("c1","VP Sales"), ("c2","VP Engineering"), ("c3","VP Marketing")],
                Connections = [("r","c1"), ("r","c2"), ("r","c3")],
                HasHierarchy3CachedDrawing = true
            },
            new SlideSpec
            {
                Title     = "SmartArt Live — Cycle",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/cycle1",
                Nodes     = [("a","Idea"), ("b","Plan"), ("c","Execute"), ("d","Review"), ("e","Improve")],
                Connections = []
            },
            new SlideSpec
            {
                Title     = "SmartArt Live — List",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/list1",
                Nodes     = [("i1","Requirement 1"), ("i2","Requirement 2"), ("i3","Requirement 3"), ("i4","Requirement 4")],
                Connections = []
            },
            new SlideSpec
            {
                Title     = "SmartArt Live - Grouped List",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/groupedList",
                Nodes     = [("g1","Plan"), ("g1a","Scope"), ("g1b","Schedule"), ("g2","Build"), ("g2a","Implement"), ("g2b","Verify")],
                Connections = [("g1","g1a"), ("g1","g1b"), ("g2","g2a"), ("g2","g2b")],
                HasGroupedListCachedDrawing = true
            },
            new SlideSpec
            {
                Title     = "SmartArt Live - Relationship1",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/relationship1",
                Nodes     = [("rel1","Audience"), ("rel2","Need"), ("rel3","Offer")],
                Connections = [],
                HasBasicRelationshipCachedDrawing = true
            },
            new SlideSpec
            {
                Title     = "SmartArt Live - Grid Matrix",
                LayoutUid = "urn:microsoft.com/office/officeart/2005/8/layout/gridMatrix",
                Nodes     = [("grid1","Axis"), ("grid2","Speed"), ("grid3","Quality"), ("grid4","Cost")],
                Connections = [],
                HasGridMatrixCachedDrawing = true
            }
        };

        using (var zipStream = File.Create(outputPath))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
        {

        void WriteEntry(string entryPath, byte[] bytes)
        {
            var e = archive.CreateEntry(entryPath, CompressionLevel.Fastest);
            using var s = e.Open();
            s.Write(bytes);
        }
        void WriteXml(string entryPath, XDocument doc)
        {
            using var ms = new MemoryStream();
            doc.Save(ms);
            WriteEntry(entryPath, ms.ToArray());
        }

        // ── [Content_Types].xml ────────────────────────────────────────────────
        var ctDoc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Ct + "Types",
                new XElement(Ct + "Default", new XAttribute("Extension", "rels"),  new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Ct + "Default", new XAttribute("Extension", "xml"),   new XAttribute("ContentType", "application/xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/presentation.xml"),              new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/theme/theme1.xml"),               new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/slideMasters/slideMaster1.xml"),  new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/ppt/slideLayouts/slideLayout1.xml"),  new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml")),
                slides.SelectMany((_, i) => new[]
                {
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/slides/slide{i+1}.xml"),         new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")),
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/diagrams/data{i+1}.xml"),        new XAttribute("ContentType", DiagramDataCT)),
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/diagrams/layout{i+1}.xml"),      new XAttribute("ContentType", DiagramLayoutCT)),
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/diagrams/quickStyle{i+1}.xml"),  new XAttribute("ContentType", DiagramQsCT)),
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/diagrams/colors{i+1}.xml"),      new XAttribute("ContentType", DiagramColorsCT)),
                    new XElement(Ct + "Override", new XAttribute("PartName", $"/ppt/diagrams/drawing{i+1}.xml"),     new XAttribute("ContentType", DiagramDrawingCT))
                })
            ));
        WriteXml("[Content_Types].xml", ctDoc);

        // ── Root rels ─────────────────────────────────────────────────────────
        WriteXml("_rels/.rels", MakeRels(("rId1", PresRelType, "ppt/presentation.xml")));

        // ── Presentation ──────────────────────────────────────────────────────
        var sldIds = slides.Select((_, i) =>
            (XObject)new XElement(P + "sldId",
                new XAttribute("id", 256 + i),
                new XAttribute(R + "id", $"rIdSlide{i+1}"))).ToArray();

        WriteXml("ppt/presentation.xml", new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(P + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "sldMasterIdLst",
                    new XElement(P + "sldMasterId", new XAttribute("id", 2147483648u), new XAttribute(R + "id", "rIdMaster1"))),
                new XElement(P + "sldIdLst", sldIds),
                new XElement(P + "sldSz", new XAttribute("cx", 9144000), new XAttribute("cy", 6858000)),
                new XElement(P + "notesSz", new XAttribute("cx", 6858000), new XAttribute("cy", 9144000)))));

        var presRels = slides.Select((_, i) => ($"rIdSlide{i+1}", SlideRelType, $"slides/slide{i+1}.xml"))
            .Prepend(("rIdMaster1", MasterRelType, "slideMasters/slideMaster1.xml"))
            .ToArray();
        WriteXml("ppt/_rels/presentation.xml.rels", MakeRels(presRels));

        // ── Theme (minimal accent colors) ─────────────────────────────────────
        WriteXml("ppt/theme/theme1.xml", BuildTheme());

        // ── Slide Master ──────────────────────────────────────────────────────
        WriteXml("ppt/slideMasters/slideMaster1.xml", BuildMinimalMaster());
        WriteXml("ppt/slideMasters/_rels/slideMaster1.xml.rels", MakeRels(
            ("rId1", ThemeRelType, "../theme/theme1.xml"),
            ("rId2", LayoutRelType, "../slideLayouts/slideLayout1.xml")));

        // ── Slide Layout ──────────────────────────────────────────────────────
        WriteXml("ppt/slideLayouts/slideLayout1.xml", BuildMinimalLayout());
        WriteXml("ppt/slideLayouts/_rels/slideLayout1.xml.rels", MakeRels(
            ("rId1", MasterRelType, "../slideMasters/slideMaster1.xml")));

        // ── Slides + diagram parts ─────────────────────────────────────────────
        for (int i = 0; i < slides.Length; i++)
        {
            var spec = slides[i];
            int si   = i + 1;

            // Slide XML
            WriteXml($"ppt/slides/slide{si}.xml", BuildSlide(spec.Title, si));

            // Slide rels
            WriteXml($"ppt/slides/_rels/slide{si}.xml.rels", MakeRels(
                ("rId1",   LayoutRelType, "../slideLayouts/slideLayout1.xml"),
                ("rIdDm1", DmRelType,     $"../diagrams/data{si}.xml"),
                ("rIdLo1", LoRelType,     $"../diagrams/layout{si}.xml"),
                ("rIdQs1", QsRelType,     $"../diagrams/quickStyle{si}.xml"),
                ("rIdCs1", CsRelType,     $"../diagrams/colors{si}.xml")));

            // data#.xml (ptLst + cxnLst)
            WriteXml($"ppt/diagrams/data{si}.xml",  BuildDataXml(spec));
            WriteXml($"ppt/diagrams/layout{si}.xml", BuildLayoutXml(spec.LayoutUid));
            WriteXml($"ppt/diagrams/quickStyle{si}.xml", MakeSimpleDoc(Dgm + "styleDef"));
            WriteXml($"ppt/diagrams/colors{si}.xml",     MakeSimpleDoc(Dgm + "colorsDef"));
            WriteXml($"ppt/diagrams/drawing{si}.xml",    BuildDrawingXml(spec));

            // data rels (points to drawing for dsp:drawing lookup)
            WriteXml($"ppt/diagrams/_rels/data{si}.xml.rels", MakeRels(
                ("rIdDraw1", DgmDrawRelType, $"drawing{si}.xml")));
        }
        }

        EnsurePresentationGuideList(outputPath);
        Console.WriteLine($"  Written: {outputPath}");
    }

    private static void EnsurePresentationGuideList(string outputPath)
    {
        var tempPath = outputPath + ".guide.tmp";
        var entries = new List<(string Name, byte[] Bytes)>();
        XDocument presentation;
        using (var archive = ZipFile.OpenRead(outputPath))
        {
            var entry = archive.GetEntry("ppt/presentation.xml")
                ?? throw new InvalidDataException("SmartArt fixture is missing ppt/presentation.xml.");
            using var stream = entry.Open();
            presentation = XDocument.Load(stream);
            foreach (var sourceEntry in archive.Entries)
            {
                using var sourceStream = sourceEntry.Open();
                using var bytes = new MemoryStream();
                sourceStream.CopyTo(bytes);
                entries.Add((sourceEntry.FullName, bytes.ToArray()));
            }
        }

        var root = presentation.Root
            ?? throw new InvalidDataException("SmartArt fixture has no presentation root.");
        if (root.Element(P + "extLst")?.Elements(P + "ext")
                .Any(ext => (string?)ext.Attribute("uri") == "{EFAFB233-063F-42B5-8137-9DF3F51BA10A}") == true)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "p15", P15.NamespaceName);
        root.Add(new XElement(P + "extLst",
            new XElement(P + "ext",
                new XAttribute("uri", "{EFAFB233-063F-42B5-8137-9DF3F51BA10A}"),
                new XElement(P15 + "sldGuideLst"))));

        using var presentationBytes = new MemoryStream();
        presentation.Save(presentationBytes);
        var rewrittenPresentation = presentationBytes.ToArray();
        var tempEntry = entries.FindIndex(item => item.Name == "ppt/presentation.xml");
        entries[tempEntry] = ("ppt/presentation.xml", rewrittenPresentation);

        try
        {
            using (var rebuilt = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var (name, bytes) in entries)
                {
                    var replacement = rebuilt.CreateEntry(name, CompressionLevel.Optimal);
                    using var output = replacement.Open();
                    output.Write(bytes, 0, bytes.Length);
                }
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static bool UsePowerPointGenerator()
    {
        try
        {
            return Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false) is not null;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void GenerateWithPowerPoint(string outputPath)
    {
        var beforePids = Process.GetProcessesByName("POWERPNT").Select(process => process.Id).ToHashSet();
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new InvalidOperationException("PowerPoint.Application COM is not registered.");

        dynamic? app = null;
        dynamic? presentation = null;
        try
        {
            app = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("PowerPoint.Application COM activation returned null.");

            app.DisplayAlerts = 2; // ppAlertsNone
            presentation = RetryCom(() => app.Presentations.Add(-1)); // msoTrue: keep a window so SmartArt materializes like PowerPoint-authored files.

            AddSmartArtSlide(app, presentation, 1, "SmartArt Live - Process", "Process",
                new[] { "Plan", "Design", "Build", "Test", "Deploy" });
            AddSmartArtSlide(app, presentation, 2, "SmartArt Live - Hierarchy", "Hierarch",
                new[] { "CEO", "VP Sales", "VP Engineering", "VP Marketing" });
            AddSmartArtSlide(app, presentation, 3, "SmartArt Live - Hierarchy3", "Hierarch",
                new[] { "CEO", "VP Sales", "VP Engineering", "VP Marketing" });
            AddSmartArtSlide(app, presentation, 4, "SmartArt Live - Cycle", "Cycle",
                new[] { "Idea", "Plan", "Execute", "Review", "Improve" });
            AddSmartArtSlide(app, presentation, 5, "SmartArt Live - List", "List",
                new[] { "Requirement 1", "Requirement 2", "Requirement 3", "Requirement 4" });
            AddSmartArtSlide(app, presentation, 6, "SmartArt Live - Relationship1", "Relationship",
                new[] { "Audience", "Need", "Offer" });

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            RetryCom(() => presentation.SaveAs(outputPath));
            PatchPowerPointHierarchy3Identity(outputPath);
            Console.WriteLine($"  Written: {outputPath}");
        }
        finally
        {
            ClosePresentation(ref presentation);

            var afterPids = Process.GetProcessesByName("POWERPNT").Select(process => process.Id).ToHashSet();
            var startedPowerPoint = afterPids.Any(pid => !beforePids.Contains(pid));
            if (startedPowerPoint)
                QuitApplication(ref app);
            else
                ReleaseComObject(ref app);
        }
    }

    private static void AddSmartArtSlide(
        dynamic app,
        dynamic presentation,
        int slideIndex,
        string title,
        string layoutKeyword,
        string[] nodeTexts)
    {
        const int ppLayoutBlank = 12;
        const int msoTextOrientationHorizontal = 1;
        const int msoTrue = -1;

        dynamic slide = RetryCom(() => presentation.Slides.Add(slideIndex, ppLayoutBlank));
        dynamic titleBox = RetryCom(() => slide.Shapes.AddTextbox(msoTextOrientationHorizontal, 20f, 6f, 920f, 30f));
        RetryCom(() => titleBox.TextFrame.TextRange.Text = title);
        RetryCom(() => titleBox.TextFrame.TextRange.Font.Size = 18);
        RetryCom(() => titleBox.TextFrame.TextRange.Font.Bold = msoTrue);

        dynamic layout = FindSmartArtLayout(app, layoutKeyword);
        dynamic smartArtShape = RetryCom(() => slide.Shapes.AddSmartArt(layout, 50f, 50f, 860f, 380f));
        dynamic nodes = RetryCom(() => smartArtShape.SmartArt.AllNodes);
        var count = RetryCom(() => (int)nodes.Count);
        for (var i = 1; i <= Math.Min(count, nodeTexts.Length); i++)
        {
            var nodeIndex = i;
            RetryCom(() => nodes.Item(nodeIndex).TextFrame2.TextRange.Text = nodeTexts[nodeIndex - 1]);
        }
    }

    private static void PatchPowerPointHierarchy3Identity(string outputPath)
    {
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string diagramLayoutRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update);
        var relsEntry = archive.GetEntry("ppt/slides/_rels/slide3.xml.rels")
            ?? throw new InvalidDataException("PowerPoint SmartArt fixture is missing slide 3 relationships.");
        XDocument rels;
        using (var stream = relsEntry.Open())
            rels = XDocument.Load(stream);

        var target = rels.Root?.Elements(XNamespace.Get(relationshipsNamespace) + "Relationship")
            .Where(element => (string?)element.Attribute("Type") == diagramLayoutRelationship)
            .Select(element => (string?)element.Attribute("Target"))
            .FirstOrDefault(targetValue => !string.IsNullOrWhiteSpace(targetValue));
        if (target is null)
            throw new InvalidDataException("PowerPoint SmartArt fixture slide 3 has no diagram layout relationship.");

        var layoutPath = "ppt/slides/" + target.Replace("../", string.Empty, StringComparison.Ordinal)
            .Replace('/', Path.DirectorySeparatorChar);
        layoutPath = layoutPath.Replace("ppt/slides/diagrams", "ppt/diagrams", StringComparison.OrdinalIgnoreCase);
        var layoutEntry = archive.GetEntry(layoutPath)
            ?? throw new InvalidDataException($"PowerPoint SmartArt fixture is missing {layoutPath}.");
        XDocument layout;
        using (var stream = layoutEntry.Open())
            layout = XDocument.Load(stream);
        if (layout.Root is not null)
        {
            layout.Root.SetAttributeValue("uniqueId",
                "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy3");
        }

        using var layoutBytes = new MemoryStream();
        layout.Save(layoutBytes);
        layoutEntry.Delete();
        var replacement = archive.CreateEntry(layoutPath, CompressionLevel.Optimal);
        using var output = replacement.Open();
        output.Write(layoutBytes.GetBuffer(), 0, checked((int)layoutBytes.Length));
    }

    private static dynamic FindSmartArtLayout(dynamic app, string keyword)
    {
        dynamic layouts = RetryCom(() => app.SmartArtLayouts);
        var count = RetryCom(() => (int)layouts.Count);
        for (var i = 1; i <= count; i++)
        {
            var index = i;
            dynamic layout = RetryCom(() => layouts.Item(index));
            string name = RetryCom(() => (string)layout.Name);
            if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return layout;
        }

        return RetryCom(() => layouts.Item(1));
    }

    private static T RetryCom<T>(Func<T> action)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsRpcRejected(ex) && attempt < 15)
            {
                lastException = ex;
                Thread.Sleep(500);
            }
        }

        throw lastException ?? new InvalidOperationException("PowerPoint COM operation failed.");
    }

    private static void RetryCom(Action action) =>
        RetryCom(() =>
        {
            action();
            return 0;
        });

    private static bool IsRpcRejected(Exception ex)
    {
        if (ex is COMException { HResult: unchecked((int)0x80010001) })
            return true;

        return ex.Message.Contains("0x80010001", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException is not null && IsRpcRejected(ex.InnerException);
    }

    private static void ClosePresentation(ref dynamic? presentation)
    {
        if (presentation is null)
            return;

        try { presentation.Close(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: presentation.Close() threw: {ex.Message}");
        }
        finally { ReleaseComObject(ref presentation); }
    }

    private static void QuitApplication(ref dynamic? app)
    {
        if (app is null)
            return;

        try { app.Quit(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: app.Quit() threw: {ex.Message}");
        }
        finally { ReleaseComObject(ref app); }
    }

    private static void ReleaseComObject(ref dynamic? instance)
    {
        if (instance is null)
            return;

        try
        {
            if (Marshal.IsComObject(instance))
                Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
            // Best-effort COM cleanup; callers still own any pre-existing PowerPoint instance.
        }
        finally
        {
            instance = null;
        }
    }

    // ── Slide builder ─────────────────────────────────────────────────────────

    private static XDocument BuildSlide(string title, int slideIndex)
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(P + "sld",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        // required group shape header
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id","1"), new XAttribute("name","")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "ext", new XAttribute("cx","0"), new XAttribute("cy","0")),
                                new XElement(A + "chOff", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "chExt", new XAttribute("cx","0"), new XAttribute("cy","0")))),
                        // Title textbox
                        new XElement(P + "sp",
                            new XElement(P + "nvSpPr",
                                new XElement(P + "cNvPr", new XAttribute("id","2"), new XAttribute("name","Title")),
                                new XElement(P + "cNvSpPr"),
                                new XElement(P + "nvPr")),
                            new XElement(P + "spPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x","457200"), new XAttribute("y","130000")),
                                    new XElement(A + "ext", new XAttribute("cx","8229600"), new XAttribute("cy","430000"))),
                                new XElement(A + "prstGeom", new XAttribute("prst","rect"), new XElement(A + "avLst")),
                                new XElement(A + "noFill")),
                            new XElement(P + "txBody",
                                new XElement(A + "bodyPr"),
                                new XElement(A + "lstStyle"),
                                new XElement(A + "p",
                                    new XElement(A + "r",
                                        new XElement(A + "rPr", new XAttribute("lang","en-US"), new XAttribute("sz","1800"), new XAttribute("b","1")),
                                        new XElement(A + "t", title))))),
                        // SmartArt graphicFrame
                        new XElement(P + "graphicFrame",
                            new XElement(P + "nvGraphicFramePr",
                                new XElement(P + "cNvPr", new XAttribute("id","3"), new XAttribute("name",$"SmartArt {slideIndex}")),
                                new XElement(P + "cNvGraphicFramePr"),
                                new XElement(P + "nvPr")),
                            new XElement(P + "xfrm",
                                new XElement(A + "off", new XAttribute("x","457200"), new XAttribute("y","680000")),
                                new XElement(A + "ext", new XAttribute("cx","8229600"), new XAttribute("cy","5744800"))),
                            new XElement(A + "graphic",
                                new XElement(A + "graphicData",
                                    new XAttribute("uri","http://schemas.openxmlformats.org/drawingml/2006/diagram"),
                                    new XElement(Dgm + "relIds",
                                        new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                                        new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                                        new XAttribute(R + "dm", "rIdDm1"),
                                        new XAttribute(R + "lo", "rIdLo1"),
                                        new XAttribute(R + "qs", "rIdQs1"),
                                        new XAttribute(R + "cs", "rIdCs1")))))))));
    }

    // ── data1.xml builder ─────────────────────────────────────────────────────

    private static XDocument BuildDataXml(SlideSpec spec)
    {
        var ptElems = spec.Nodes.Select(n =>
            new XElement(Dgm + "pt",
                new XAttribute("modelId", n.id),
                new XAttribute("type", "node"),
                new XElement(Dgm + "t",
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "rPr", new XAttribute("lang","en-US")),
                            new XElement(A + "t", n.text)))))).ToArray();

        var cxnElems = spec.Connections.Select(c =>
            new XElement(Dgm + "cxn",
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", c.src),
                new XAttribute("destId", c.dst))).ToArray();

        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dgm + "ptLst", ptElems),
                new XElement(Dgm + "cxnLst", cxnElems)));
    }

    // ── layout1.xml builder ───────────────────────────────────────────────────

    private static XDocument BuildLayoutXml(string uniqueId)
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(Dgm + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute("uniqueId", uniqueId)));
    }

    // ── Empty dsp:drawing ──────────────────────────────────────────────────────

    private static XDocument BuildEmptyDrawing()
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XElement(Dsp + "spTree")));
    }

    private static XDocument BuildDrawingXml(SlideSpec spec)
    {
        if (spec.HasGroupedListCachedDrawing)
            return BuildGroupedListDrawingXml(spec);

        if (spec.HasBasicRelationshipCachedDrawing)
            return BuildBasicRelationshipDrawingXml(spec);

        if (spec.HasGridMatrixCachedDrawing)
            return BuildGridMatrixDrawingXml(spec);

        if (!spec.HasHierarchy3CachedDrawing)
            return BuildEmptyDrawing();

        var nodePositions = new (long x, long y, long cx, long cy)[]
        {
            (2359189, 480, 2757165, 1378582),
            (2910622, 1723708, 2205732, 1378582),
            (2910622, 3446936, 2205732, 1378582),
            (5805645, 480, 2757165, 1378582)
        };
        var segmentPositions = new (long x, long y, long cx, long cy)[]
        {
            (2634905, 1379063, 275716, 1033936),
            (2634905, 1379063, 275716, 2757165),
            (6081362, 1379063, 275716, 1033936),
            (6081362, 1379063, 275716, 2757165)
        };
        var templatePositions = new (long x, long y, long cx, long cy)[]
        {
            (6357078, 1723708, 2205732, 1378582),
            (6357078, 3446936, 2205732, 1378582)
        };

        var nodeShapes = spec.Nodes.Select((node, index) =>
            BuildDspShape(
                id: (uint)(10 + index),
                name: $"Hierarchy3 node {index + 1}",
                text: node.text,
                preset: "roundRect",
                nodePositions[index]));
        var connectorShapes = segmentPositions.Select((bounds, index) =>
            BuildDspShape(
                id: (uint)(20 + index),
                name: $"Hierarchy3 connector {index + 1}",
                text: string.Empty,
                preset: null,
                bounds));
        var templateShapes = templatePositions.Select((bounds, index) =>
            BuildDspShape(
                id: (uint)(30 + index),
                name: $"Hierarchy3 template {index + 1}",
                text: string.Empty,
                preset: "roundRect",
                bounds));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dsp + "spTree", nodeShapes.Concat(templateShapes).Concat(connectorShapes))));
    }

    private static XDocument BuildGroupedListDrawingXml(SlideSpec spec)
    {
        const long frameCx = 8_229_600;
        const long frameCy = 5_744_800;
        var padX = (long)(frameCx * 0.04);
        var padY = (long)(frameCy * 0.04);
        var gapX = (long)(frameCx * 0.025);
        var gapY = Math.Max((long)(frameCy * 0.018), 1L);
        var groupWidth = Math.Max((frameCx - 2 * padX - gapX) / 2, 1L);
        var headerHeight = Math.Max((long)(frameCy * 0.22), 1L);
        var childStartY = padY + headerHeight + gapY;
        var childHeightArea = Math.Max(frameCy - 2 * padY - headerHeight - gapY, 1L);
        var childHeight = Math.Max((childHeightArea - gapY) / 2, 1L);
        var groups = new[]
        {
            ("g1", new[] { "g1a", "g1b" }),
            ("g2", new[] { "g2a", "g2b" })
        };
        var elements = new List<XElement>();
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var groupX = padX + groupIndex * (groupWidth + gapX);
            elements.Add(BuildDspShape(
                (uint)(40 + groupIndex),
                $"GroupedList band {groupIndex + 1}",
                string.Empty,
                "rect",
                (groupX, padY + headerHeight, groupWidth, Math.Max(frameCy - 2 * padY - headerHeight, 1L))));

            var header = spec.Nodes.Single(node => node.id == groups[groupIndex].Item1);
            elements.Add(BuildDspShape(
                (uint)(50 + groupIndex),
                $"GroupedList header {groupIndex + 1}",
                header.text,
                "roundRect",
                (groupX, padY, groupWidth, headerHeight)));

            for (var childIndex = 0; childIndex < groups[groupIndex].Item2.Length; childIndex++)
            {
                var child = spec.Nodes.Single(node => node.id == groups[groupIndex].Item2[childIndex]);
                elements.Add(BuildDspShape(
                    (uint)(60 + groupIndex * 2 + childIndex),
                    $"GroupedList child {groupIndex + 1}.{childIndex + 1}",
                    child.text,
                    "rect",
                    (groupX, childStartY + childIndex * (childHeight + gapY), groupWidth, childHeight)));
            }
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dsp + "spTree", elements)));
    }

    private static XDocument BuildBasicRelationshipDrawingXml(SlideSpec spec)
    {
        const long diameter = 2_400_000;
        const long step = 1_392_000;
        const long left = 1_522_800;
        const long top = 1_672_400;

        var elements = spec.Nodes.Select((node, index) => BuildDspShape(
            (uint)(70 + index),
            $"Relationship1 node {index + 1}",
            node.text,
            "ellipse",
            (left + index * step, top, diameter, diameter)));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(Dsp + "spTree", elements)));
    }

    private static XDocument BuildGridMatrixDrawingXml(SlideSpec spec)
    {
        const long frameCx = 8_229_600;
        const long frameCy = 5_744_800;
        var outerPad = (long)(Math.Min(frameCx, frameCy) * 0.04);
        var availableW = frameCx - 2 * outerPad;
        var availableH = frameCy - 2 * outerPad;
        var gridSize = Math.Min(availableW, availableH);
        var gap = (long)(gridSize * 0.025);
        var cellSize = (gridSize - gap) / 2;
        var gridX = (frameCx - gridSize) / 2;
        var gridY = (frameCy - gridSize) / 2;
        var positions = new[]
        {
            (gridX, gridY),
            (gridX + cellSize + gap, gridY),
            (gridX, gridY + cellSize + gap),
            (gridX + cellSize + gap, gridY + cellSize + gap)
        };
        var elements = spec.Nodes.Select((node, index) => BuildDspShape(
            (uint)(80 + index),
            $"GridMatrix cell {index + 1}",
            node.text,
            "rect",
            (positions[index].Item1, positions[index].Item2, cellSize, cellSize)));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(Dsp + "spTree", elements)));
    }

    private static XElement BuildDspShape(
        uint id,
        string name,
        string text,
        string? preset,
        (long x, long y, long cx, long cy) bounds)
    {
        var geometry = preset is null
            ? new XElement(A + "ln",
                new XElement(A + "solidFill",
                    new XElement(A + "srgbClr", new XAttribute("val", "0E4B66"))),
                new XElement(A + "prstDash", new XAttribute("val", "solid")))
            : new XElement(A + "prstGeom",
                new XAttribute("prst", preset),
                new XElement(A + "avLst"));

        var shapeProperties = new List<object>
        {
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", bounds.x), new XAttribute("y", bounds.y)),
                new XElement(A + "ext", new XAttribute("cx", bounds.cx), new XAttribute("cy", bounds.cy))),
            geometry
        };
        if (preset is not null)
        {
            shapeProperties.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", "4472C4"))));
            shapeProperties.Add(new XElement(A + "ln"));
        }

        var elements = new List<object>
        {
            new XElement(Dsp + "nvSpPr",
                new XElement(Dsp + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)),
                new XElement(Dsp + "cNvSpPr"),
                new XElement(Dsp + "nvPr")),
            new XElement(Dsp + "spPr", shapeProperties)
        };
        if (!string.IsNullOrEmpty(text))
        {
            elements.Add(new XElement(Dsp + "txBody",
                new XElement(A + "bodyPr"),
                new XElement(A + "lstStyle"),
                new XElement(A + "p",
                    new XElement(A + "r",
                        new XElement(A + "rPr", new XAttribute("lang", "en-US")),
                        new XElement(A + "t", text)))));
        }

        return new XElement(Dsp + "sp", elements);
    }

    // ── Theme with accent colors ──────────────────────────────────────────────

    private static XDocument BuildTheme()
    {
        // Minimal theme with the 6 Office accent colors
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(A + "theme",
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute("name", "Office Theme"),
                new XElement(A + "themeElements",
                    new XElement(A + "clrScheme", new XAttribute("name", "Office"),
                        new XElement(A + "dk1",  new XElement(A + "srgbClr", new XAttribute("val","000000"))),
                        new XElement(A + "lt1",  new XElement(A + "srgbClr", new XAttribute("val","FFFFFF"))),
                        new XElement(A + "dk2",  new XElement(A + "srgbClr", new XAttribute("val","44546A"))),
                        new XElement(A + "lt2",  new XElement(A + "srgbClr", new XAttribute("val","E7E6E6"))),
                        new XElement(A + "accent1", new XElement(A + "srgbClr", new XAttribute("val","4472C4"))),
                        new XElement(A + "accent2", new XElement(A + "srgbClr", new XAttribute("val","ED7D31"))),
                        new XElement(A + "accent3", new XElement(A + "srgbClr", new XAttribute("val","A9D18E"))),
                        new XElement(A + "accent4", new XElement(A + "srgbClr", new XAttribute("val","FFC000"))),
                        new XElement(A + "accent5", new XElement(A + "srgbClr", new XAttribute("val","5B9BD5"))),
                        new XElement(A + "accent6", new XElement(A + "srgbClr", new XAttribute("val","70AD47"))),
                        new XElement(A + "hlink", new XElement(A + "srgbClr", new XAttribute("val","0563C1"))),
                        new XElement(A + "folHlink", new XElement(A + "srgbClr", new XAttribute("val","954F72")))),
                    new XElement(A + "fontScheme", new XAttribute("name","Office"),
                        new XElement(A + "majorFont",
                            new XElement(A + "latin", new XAttribute("typeface","Calibri Light"))),
                        new XElement(A + "minorFont",
                            new XElement(A + "latin", new XAttribute("typeface","Calibri")))),
                    new XElement(A + "fmtScheme", new XAttribute("name","Office")))));
    }

    // ── Minimal slide master ───────────────────────────────────────────────────

    private static XDocument BuildMinimalMaster()
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(P + "sldMaster",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id","1"), new XAttribute("name","")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "ext", new XAttribute("cx","0"), new XAttribute("cy","0")),
                                new XElement(A + "chOff", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "chExt", new XAttribute("cx","0"), new XAttribute("cy","0")))))),
                new XElement(P + "clrMap",
                    new XAttribute("bg1","lt1"), new XAttribute("tx1","dk1"),
                    new XAttribute("bg2","lt2"), new XAttribute("tx2","dk2"),
                    new XAttribute("accent1","accent1"), new XAttribute("accent2","accent2"),
                    new XAttribute("accent3","accent3"), new XAttribute("accent4","accent4"),
                    new XAttribute("accent5","accent5"), new XAttribute("accent6","accent6"),
                    new XAttribute("hlink","hlink"), new XAttribute("folHlink","folHlink"))));
    }

    // ── Minimal slide layout ───────────────────────────────────────────────────

    private static XDocument BuildMinimalLayout()
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(P + "sldLayout",
                new XAttribute(XNamespace.Xmlns + "p", P.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                new XAttribute("type","blank"),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id","1"), new XAttribute("name","")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "ext", new XAttribute("cx","0"), new XAttribute("cy","0")),
                                new XElement(A + "chOff", new XAttribute("x","0"), new XAttribute("y","0")),
                                new XElement(A + "chExt", new XAttribute("cx","0"), new XAttribute("cy","0"))))))));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static XDocument MakeSimpleDoc(XName rootElement) =>
        new(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(rootElement,
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName)));

    private static XDocument MakeRels(params (string id, string type, string target)[] rels)
    {
        return new XDocument(new XDeclaration("1.0","UTF-8","yes"),
            new XElement(Pkg + "Relationships",
                rels.Select(r => new XElement(Pkg + "Relationship",
                    new XAttribute("Id", r.id),
                    new XAttribute("Type", r.type),
                    new XAttribute("Target", r.target)))));
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class SlideSpec
    {
        public string Title     { get; init; } = string.Empty;
        public string LayoutUid { get; init; } = string.Empty;
        public (string id, string text)[]   Nodes       { get; init; } = [];
        public (string src, string dst)[]   Connections { get; init; } = [];
        public bool HasHierarchy3CachedDrawing { get; init; }
        public bool HasGroupedListCachedDrawing { get; init; }
        public bool HasBasicRelationshipCachedDrawing { get; init; }
        public bool HasGridMatrixCachedDrawing { get; init; }
    }
}
