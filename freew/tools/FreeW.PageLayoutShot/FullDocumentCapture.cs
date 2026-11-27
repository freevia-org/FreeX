using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Free.Shared.Pdf.Skia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.IO;
using SkiaSharp;

/// <summary>
/// Diagnostic capture of one complete, unchanged imported document. Unlike the legacy section
/// surface mode, pagination and header/footer ownership come from the retained production editor.
/// No expected page count is accepted, no page is rebuilt, and no raster placeholder is permitted.
/// </summary>
internal static class FullDocumentCapture
{
    private const double Dpi = 96;
    private const double PointsPerDip = 72 / Dpi;
    private const int MaximumPageDimension = 16384;
    private const long MaximumPagePixels = 64_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(string outputDirectory, string inputPath, string? sourceRoot)
    {
        var output = Path.GetFullPath(outputDirectory);
        // Do not overwrite historical evidence or mix new pages with an older, longer capture.
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Full-document capture requires an empty output directory.");
        Directory.CreateDirectory(output);
        var manifest = new Dictionary<string, object?>
        {
            ["schema"] = "freex.freew.full-document-capture.v1",
            ["status"] = "capturing",
            ["capturedUtc"] = DateTimeOffset.UtcNow,
            ["inputFileName"] = Path.GetFileName(inputPath),
            ["inputSha256"] = HashFile(inputPath),
            ["renderer"] = "production Avalonia DocumentView / native Skia RenderTargetBitmap",
            ["retainsFullDocument"] = true,
            ["expectedPageCountSupplied"] = false,
            ["pageRasterDpi"] = Dpi,
            ["pageRasterResized"] = false,
            ["platform"] = RuntimeInformation.OSDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["assemblies"] = new[]
            {
                AssemblyIdentity(typeof(FullDocumentCapture).Assembly),
                AssemblyIdentity(typeof(DocumentView).Assembly),
                AssemblyIdentity(typeof(DocxReader).Assembly),
                AssemblyIdentity(typeof(FreeWAvaloniaPdfExport).Assembly),
                AssemblyIdentity(typeof(Application).Assembly),
                AssemblyIdentity(typeof(SKBitmap).Assembly),
            }.DistinctBy(identity => identity.Name).ToArray(),
        };

        try
        {
            manifest["sourceObservation"] = ObserveSource(sourceRoot);
            manifest["fontRequests"] = FontRequests(inputPath);
            var source = DocxReader.Read(inputPath);
            var view = new DocumentView { ViewMode = DocumentViewMode.PrintLayout };
            view.LoadDocument(source);
            if (!ReferenceEquals(source, view.Document))
                throw new InvalidOperationException("The editor replaced the imported document instance.");

            // Width controls desk centering; production page geometry still owns body wrapping.
            const double viewportWidth = 960;
            view.Width = viewportWidth;
            view.Measure(new Size(viewportWidth, double.PositiveInfinity));
            view.Height = Math.Max(1, view.DesiredSize.Height);
            view.Arrange(new Rect(0, 0, viewportWidth, view.Height));
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            // MeasureOverride intentionally swallows layout exceptions for interactive resilience.
            // Diagnostics must fail instead of accepting a partly built page as a valid capture.
            RelayoutStrictly(view, viewportWidth);

            var surface = LiveSurface(view);
            var pageCount = view.PageCount;
            if (pageCount < 1 || !surface.IsPrintLayout || surface.UsesProjectedPageFlow)
                throw new InvalidOperationException("The editor did not expose an ordinary paginated print surface.");
            var pixelWidth = PixelDimension(surface.PageWidthDip);
            var pixelHeight = PixelDimension(surface.PageHeightDip);
            if ((long)pixelWidth * pixelHeight > MaximumPagePixels)
                throw new InvalidOperationException("The live page exceeds the diagnostic raster pixel limit.");

            manifest["observedViewPageCount"] = pageCount;
            manifest["liveSurface"] = surface;
            manifest["physicalPageGeometrySource"] = "retained DocumentView._surfacePlan, not reconstructed section settings";
            // Presently the production editor draws one uniform sheet size for its whole stack.
            // Record that actual behavior, including any discrepancy with authored section sizes.
            manifest["authoredSectionGeometryPt"] = source.Sections.Select(section => new
            {
                section.Page.WidthPt,
                section.Page.HeightPt,
            }).ToArray();

            view.Height = Math.Max(view.Height, surface.ScrollableHeightForPages(pageCount));
            var frame = new Canvas
            {
                Width = surface.PageWidthDip,
                Height = surface.PageHeightDip,
                Background = Brushes.White,
                ClipToBounds = true,
            };
            frame.Children.Add(view);
            var pages = new List<object>();
            manifest["pages"] = pages;
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var left = surface.RenderedPageLeftDip(pageIndex);
                var top = surface.RenderedPageTopDip(pageIndex);
                Canvas.SetLeft(view, -left);
                Canvas.SetTop(view, -top);
                frame.Measure(new Size(surface.PageWidthDip, surface.PageHeightDip));
                frame.Arrange(new Rect(0, 0, surface.PageWidthDip, surface.PageHeightDip));
                frame.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                RelayoutStrictly(view, viewportWidth);
                RequireStableView(view, source, surface, pageCount);

                var fileName = $"page-{pageIndex + 1:D3}.png";
                using (var bitmap = new RenderTargetBitmap(
                    new PixelSize(pixelWidth, pixelHeight), new Vector(Dpi, Dpi)))
                {
                    bitmap.Render(frame);
                    using var buffer = new MemoryStream();
                    bitmap.Save(buffer);
                    var bytes = buffer.ToArray();
                    using var decoded = bytes.Length == 0 ? null : SKBitmap.Decode(bytes);
                    if (decoded is null || decoded.Width != pixelWidth || decoded.Height != pixelHeight)
                        throw new InvalidOperationException($"Page {pageIndex + 1}: RenderTargetBitmap did not produce a valid page raster.");
                    // Blank pages are legitimate. Content similarity is assessed against Word,
                    // never by inventing a minimum ink count or substituting a placeholder.
                    File.WriteAllBytes(Path.Combine(output, fileName), bytes);
                }
                RequireStableView(view, source, surface, pageCount);
                pages.Add(new
                {
                    pageNumber = pageIndex + 1,
                    widthPoints = surface.PageWidthDip * PointsPerDip,
                    heightPoints = surface.PageHeightDip * PointsPerDip,
                    pixelWidth,
                    pixelHeight,
                    rasterDpi = Dpi,
                    sourceRectDip = new { x = left, y = top, width = surface.PageWidthDip, height = surface.PageHeightDip },
                    fileName,
                    sha256 = HashFile(Path.Combine(output, fileName)),
                    captureSource = "retained-full-document-real-skia",
                });
            }

            // Use exactly the production export entry point on the same complete editor instance.
            var pdfPath = Path.Combine(output, "full-document.pdf");
            FreeWAvaloniaPdfExportResult pdfResult;
            using (var stream = File.Create(pdfPath))
                pdfResult = FreeWAvaloniaPdfExport.Save(view, stream);
            var pdfContent = view.BuildPdfContent();
            RequireStableView(view, source, surface, pageCount);
            var pdfGeometry = pdfContent.Pages.Select((page, index) => new
            {
                pageNumber = index + 1,
                widthPoints = page.WidthPoints,
                heightPoints = page.HeightPoints,
            }).ToArray();
            manifest["pdf"] = new
            {
                fileName = Path.GetFileName(pdfPath),
                sha256 = HashFile(pdfPath),
                bytes = new FileInfo(pdfPath).Length,
                backend = pdfResult.Backend.ToString(),
                exportedPageCount = pdfResult.PageCount,
                contentPageCount = pdfContent.Pages.Count,
                imageDiagnostics = pdfResult.ImageDiagnostics,
                pages = pdfGeometry,
            };
            var problems = new List<string>();
            if (pdfResult.Backend != PdfExportBackend.Skia)
                problems.Add("Production export fell back from Skia; portable WinAnsi PDF is not Unicode fidelity evidence.");
            if (new FileInfo(pdfPath).Length == 0)
                problems.Add("Production export returned an empty PDF.");
            if (pdfResult.ImageDiagnostics.Count > 0)
                problems.Add("Production export reported image decode diagnostics.");
            if (pdfResult.PageCount != pageCount || pdfContent.Pages.Count != pageCount)
                problems.Add("Production PDF and live editor page counts disagree.");
            if (pdfContent.Pages.Any(page =>
                Math.Abs(page.WidthPoints - surface.PageWidthDip * PointsPerDip) > 0.01
                || Math.Abs(page.HeightPoints - surface.PageHeightDip * PointsPerDip) > 0.01))
                problems.Add("Production PDF and live editor page geometry disagree.");
            manifest["captureProblems"] = problems;
            manifest["status"] = problems.Count == 0 ? "captured" : "captured-with-disagreement";
            manifest["wordParityAssessed"] = false;
            WriteManifest(output, manifest);
            Console.WriteLine($"[FullDocumentCapture] {pageCount} actual editor pages; PDF {pdfResult.PageCount} pages/{pdfResult.Backend}; {output}");
            foreach (var problem in problems)
                Console.Error.WriteLine($"[FullDocumentCapture] {problem}");
            return problems.Count == 0 ? 0 : 3;
        }
        catch (Exception exception)
        {
            manifest["status"] = "failed";
            manifest["failure"] = new { type = exception.GetType().FullName, exception.Message };
            WriteManifest(output, manifest);
            Console.Error.WriteLine($"[FullDocumentCapture] Failed: {exception.Message}");
            return 2;
        }
    }

    private static void RequireStableView(DocumentView view, object source, DocumentViewSurfacePlan surface, int pageCount)
    {
        if (!ReferenceEquals(source, view.Document) || view.PageCount != pageCount || LiveSurface(view) != surface)
            throw new InvalidOperationException("The retained document, live surface or observed pagination changed during capture.");
    }

    private static DocumentViewSurfacePlan LiveSurface(DocumentView view) =>
        typeof(DocumentView).GetField("_surfacePlan", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(view) as DocumentViewSurfacePlan
        ?? throw new InvalidOperationException("The production editor's live page surface is unavailable; refusing an inferred capture.");

    private static void RelayoutStrictly(DocumentView view, double width)
    {
        var method = typeof(DocumentView).GetMethod("Relayout", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(double)], modifiers: null)
            ?? throw new InvalidOperationException("The production editor's strict layout method is unavailable.");
        try
        {
            method.Invoke(view, [width]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static int PixelDimension(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > MaximumPageDimension)
            throw new InvalidOperationException("The live page has invalid or unsupported raster dimensions.");
        return (int)Math.Ceiling(value);
    }

    private static void WriteManifest(string output, Dictionary<string, object?> manifest) =>
        File.WriteAllText(Path.Combine(output, "capture.json"), JsonSerializer.Serialize(manifest, JsonOptions));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record LoadedAssembly(string? Name, string? Version, string? InformationalVersion,
        Guid ModuleVersionId, string? FileName, string? Sha256);

    private static LoadedAssembly AssemblyIdentity(Assembly assembly) => new(
        assembly.GetName().Name,
        assembly.GetName().Version?.ToString(),
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        assembly.ManifestModule.ModuleVersionId,
        string.IsNullOrEmpty(assembly.Location) ? null : Path.GetFileName(assembly.Location),
        string.IsNullOrEmpty(assembly.Location) ? null : HashFile(assembly.Location));

    private static object ObserveSource(string? sourceRoot)
    {
        var root = sourceRoot ?? FindSourceRoot(Directory.GetCurrentDirectory());
        if (root is null)
            return new { available = false, note = "Use --source-root to record the observed working tree; loaded assembly identities remain authoritative." };
        root = Path.GetFullPath(root);
        var paths = new[]
        {
            "freew/tools/FreeW.PageLayoutShot/Program.cs",
            "freew/tools/FreeW.PageLayoutShot/FullDocumentCapture.cs",
            "freew/FreeW.App.Avalonia/Editing/DocumentView.cs",
            "freew/FreeW.App.Avalonia/Pdf/FreeWAvaloniaPdfExport.cs",
        };
        if (paths.Any(path => !File.Exists(Path.Combine(root, path))))
            throw new ArgumentException("The supplied source root does not contain the capture tool and production editor.");
        return new
        {
            available = true,
            gitHead = Git(root, "rev-parse", "HEAD"),
            gitStatus = Git(root, "status", "--porcelain=v1", "--untracked-files=normal"),
            files = paths.Select(path => new { path, sha256 = HashFile(Path.Combine(root, path)) }).ToArray(),
            observationOnly = true,
            note = "Working-tree observation at capture time; not proof these files produced the loaded assemblies. Assembly hashes/informational versions are recorded separately.",
        };
    }

    private static string? FindSourceRoot(string directory)
    {
        for (var candidate = new DirectoryInfo(directory); candidate is not null; candidate = candidate.Parent)
            if (File.Exists(Path.Combine(candidate.FullName, "FreeX.slnx")))
                return candidate.FullName;
        return null;
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start source identity query.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true); // This diagnostic's own git process only.
            process.WaitForExit();
            throw new InvalidOperationException("Source identity query timed out.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Source identity query failed: {stderr.GetAwaiter().GetResult().Trim()}");
        return stdout.GetAwaiter().GetResult().Trim();
    }

    private static object FontRequests(string inputPath)
    {
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(inputPath);
        foreach (var entry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith("word/", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            foreach (var fonts in xml.Descendants(word + "rFonts"))
                foreach (var name in new[] { "ascii", "hAnsi", "eastAsia", "cs" })
                    if ((string?)fonts.Attribute(word + name) is { Length: > 0 } family)
                        families.Add(family);
        }
        var manager = FontManager.Current;
        return new
        {
            defaultFamily = manager.DefaultFontFamily.Name,
            resolutionScope = "Explicit DOCX rFonts requests; face probes do not establish per-glyph fallback or shaping, and theme-only requests are not expanded here.",
            families = families.Select(family => new
            {
                authoredFamily = family,
                installed = manager.SystemFonts.Any(font => string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)),
                faces = new[]
                {
                    ProbeFace(family, FontStyle.Normal, FontWeight.Normal),
                    ProbeFace(family, FontStyle.Normal, FontWeight.Bold),
                    ProbeFace(family, FontStyle.Italic, FontWeight.Normal),
                    ProbeFace(family, FontStyle.Italic, FontWeight.Bold),
                },
            }).ToArray(),
        };
    }

    private static object ProbeFace(string family, FontStyle style, FontWeight weight)
    {
        var resolved = DocumentFontFamilyResolver.Resolve(family);
        var found = FontManager.Current.TryGetGlyphTypeface(new Typeface(resolved, style, weight), out var face);
        return new
        {
            requestedStyle = style.ToString(),
            requestedWeight = weight.ToString(),
            resolvedFamilyRequest = resolved.Name,
            found,
            actualFamily = face?.FamilyName,
            actualStyle = face?.Style.ToString(),
            actualWeight = face?.Weight.ToString(),
            simulations = face?.FontSimulations.ToString(),
        };
    }
}
