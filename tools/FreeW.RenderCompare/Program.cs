using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.RenderCompare;

/// <summary>
/// Headless harness: render every page of each corpus .docx through FreeW's real print path
/// (<see cref="PrintLayout.BuildPaginator"/>) to a PNG, so its output can be diffed against MS Word's.
///
/// Usage: FreeW.RenderCompare &lt;corpusDir&gt; &lt;outDir&gt; [dpi]
///   corpusDir : folder of .docx files
///   outDir    : folder to write &lt;basename&gt;-p&lt;N&gt;.png and freew-render.csv
///   dpi       : raster DPI (default 150)
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: FreeW.RenderCompare <corpusDir> <outDir> [dpi]");
            return 2;
        }

        var corpusDir = args[0];
        var outDir = args[1];
        var dpi = args.Length >= 3 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 150.0;

        Directory.CreateDirectory(outDir);

        // A WPF Application context is needed so resource/font resolution behaves like the live app.
        _ = new Application();

        var docs = Directory.GetFiles(corpusDir, "*.docx").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var csv = new StringBuilder("file,pages,status,error\n");

        foreach (var path in docs)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            int pages = 0;
            var status = "ok";
            var error = "";
            try
            {
                pages = RenderDocument(path, outDir, name, dpi);
            }
            catch (Exception ex)
            {
                status = "fail";
                error = ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\r', ' ').Replace(',', ';');
            }

            Console.WriteLine($"[{status,-4}] {Path.GetFileName(path),-28} pages={pages}");
            csv.Append($"{Path.GetFileName(path)},{pages},{status},\"{error}\"\n");
        }

        File.WriteAllText(Path.Combine(outDir, "freew-render.csv"), csv.ToString());
        Console.WriteLine($"FreeW render -> {outDir}");
        return 0;
    }

    private static int RenderDocument(string docxPath, string outDir, string baseName, double dpi)
    {
        var model = DocxReader.Read(docxPath);

        var view = new DocumentView();
        view.LoadModel(model);

        // Paginate the editor's *live* FlowDocument directly rather than via PrintLayout.BuildPaginator.
        // BuildPaginator clones blocks with XamlWriter.Save, which throws on the editor's non-public
        // Tag types (ParagraphTag/HyperlinkInfo/Footnote/EndnoteMarker/TableCellTag) — the same crash the
        // app's Print/Print-Preview path hits. We detach the FlowDocument from the (never-shown)
        // RichTextBox so it is not "in use", set the model's page geometry on it exactly as
        // BuildPaginatedDocument does, then wrap with the real HeaderFooterPaginator so headers, footers,
        // watermark, page border and line numbers render identically to the app.
        var flow = view.Document;
        view.Document = new System.Windows.Documents.FlowDocument();

        var pageSettings = view.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(pageSettings);
        var (left, top, right, bottom) = PageLayout.MarginsDip(pageSettings);
        flow.PageWidth = pageWidth;
        flow.PageHeight = pageHeight;
        flow.PagePadding = new Thickness(left, top, right, bottom);
        DocumentView.ApplyColumnLayout(flow, pageSettings);
        // ApplyColumnLayout sets ColumnWidth = +Infinity for single-column and leans on PageWidth to
        // constrain it; for some header/multi-section docs that leaks through as an unconstrained
        // "paragraphWidth ('∞')" layout failure. Pin the single column to the finite content width
        // (visually identical — one column spanning the content area).
        if (double.IsInfinity(flow.ColumnWidth))
            flow.ColumnWidth = Math.Max(1, pageWidth - left - right);

        var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        inner.PageSize = new Size(pageWidth, pageHeight);
        var lineHeightDip = flow.FontSize * (4.0 / 3.0);
        var paginator = new HeaderFooterPaginator(inner, view.Model, pageSettings, lineHeightDip);
        paginator.ComputePageCount();
        var pageCount = paginator.PageCount;

        var scale = dpi / 96.0;

        for (var i = 0; i < pageCount; i++)
        {
            DocumentPage page;
            try
            {
                page = paginator.GetPage(i);
            }
            catch (Exception ex)
            {
                // One bad page (e.g. an undecodable embedded image) should not sink the whole document.
                Console.Error.WriteLine($"  page {i + 1}/{pageCount} of {baseName} failed: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var size = page.Size;
            var pxW = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
            var pxH = Math.Max(1, (int)Math.Ceiling(size.Height * scale));

            // Composite the page over an opaque white sheet so transparent areas read as paper, not black.
            var container = new ContainerVisual();
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, size.Width, size.Height));
            container.Children.Add(bg);
            container.Children.Add(page.Visual);

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(container);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var outPath = Path.Combine(outDir, $"{baseName}-p{i + 1}.png");
            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }

        return pageCount;
    }
}
