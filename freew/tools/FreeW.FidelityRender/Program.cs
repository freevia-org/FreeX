using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using FreeW.Core.IO;

// FreeW.FidelityRender — renders FreeW's view of one or more .docx files to PNG (one image per page),
// using the real editor render path (DocumentView -> FlowDocument -> page rasterization). This is the
// "FreeW side" of a visual fidelity comparison; the ground-truth side (MS Word / LibreOffice) and the
// image diff are produced by freew-fidelity-corpus/tools/Run-VisualFidelity.ps1.
//
// Usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc]
//   - input is a single .docx or a directory (all *.docx are rendered)
//   - output PNGs are named <docname>_pN.png (N = 1-based page index)

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FreeW.FidelityRender <input.docx | inputDir> <outputDir> [maxPagesPerDoc]");
    return 2;
}

string input = args[0];
string outDir = args[1];
int maxPages = args.Length > 2 && int.TryParse(args[2], out var mp) ? Math.Max(1, mp) : 3;

int exit = 0;
var sta = new Thread(() => exit = Run(input, outDir, maxPages));
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
return exit;

static int Run(string input, string outDir, int maxPages)
{
    Directory.CreateDirectory(outDir);

    List<string> files;
    if (Directory.Exists(input))
        files = Directory.GetFiles(input, "*.docx").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    else if (File.Exists(input))
        files = [input];
    else
    {
        Console.Error.WriteLine($"input not found: {input}");
        return 2;
    }

    if (files.Count == 0)
    {
        Console.Error.WriteLine($"no .docx files under {input}");
        return 2;
    }

    const double pageW = 816;   // 8.5in @ 96dpi
    const double pageH = 1056;  // 11in  @ 96dpi
    int failures = 0;

    foreach (var file in files)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        try
        {
            var doc = DocxReader.Read(file);

            var view = new DocumentView { Width = pageW };
            view.LoadModel(doc);

            // RichTextBox.Document is the rendered FlowDocument; detach it so we can paginate it ourselves.
            FlowDocument flow = view.Document;
            view.Document = new FlowDocument();

            flow.PageWidth = pageW;
            flow.PageHeight = pageH;
            flow.PagePadding = new Thickness(64);
            flow.ColumnWidth = pageW;
            flow.ColumnGap = 0;

            var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            paginator.PageSize = new Size(pageW, pageH);
            paginator.ComputePageCount();
            int pages = Math.Min(Math.Max(1, paginator.PageCount), maxPages);

            for (int i = 0; i < pages; i++)
            {
                DocumentPage page = paginator.GetPage(i);
                var bmp = new RenderTargetBitmap((int)pageW, (int)pageH, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
                    dc.DrawRectangle(new VisualBrush(page.Visual), null, new Rect(0, 0, pageW, pageH));
                }
                bmp.Render(dv);

                string outPath = Path.Combine(outDir, $"{name}_p{i + 1}.png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using FileStream fs = File.Create(outPath);
                enc.Save(fs);
                Console.WriteLine($"ok    {Path.GetFileName(outPath)} ({paginator.PageCount} pages)");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"rendered {files.Count - failures}/{files.Count} docs into {outDir}");
    return 0;
}
