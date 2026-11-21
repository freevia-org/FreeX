using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using DrawingGroup = System.Windows.Media.DrawingGroup;
using Visual = System.Windows.Media.Visual;

namespace FreeW.App.Host.Tests;

/// <summary>
/// An embedded OLE object's cached presentation (a WMF/EMF metafile in Word's icon form) must reach the
/// printed page on both print layouts: the plain flow and the per-page footnote (section-aware) one.
/// </summary>
public sealed class EmbeddedObjectIconPrintTests
{
    private static byte[] BuildEmfIcon()
    {
        using var stream = new MemoryStream();
        using (var reference = Graphics.FromHwnd(IntPtr.Zero))
        {
            var hdc = reference.GetHdc();
            try
            {
                using var metafile = new Metafile(stream, hdc, EmfType.EmfPlusDual);
                using var graphics = Graphics.FromImage(metafile);
                graphics.FillRectangle(System.Drawing.Brushes.Blue, 0, 0, 60, 60);
            }
            finally
            {
                reference.ReleaseHdc(hdc);
            }
        }

        return stream.ToArray();
    }

    private static TextDocument BuildDocument(bool withFootnote)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var objectParagraph = new Paragraph();
        objectParagraph.Runs.Add(new Run(string.Empty)
        {
            EmbeddedObject = new EmbeddedObject([1, 2, 3], "Word.Document.12")
            {
                Icon = new InlineImage(BuildEmfIcon(), 77, 50, FreeW.Core.Model.ImageFormat.Emf),
                WidthPt = 77,
                HeightPt = 50,
            },
        });
        doc.Blocks.Add(new Paragraph("Heading line"));
        doc.Blocks.Add(objectParagraph);
        if (withFootnote)
        {
            doc.Footnotes[1] = new Footnote(1, "Note text");
            var referencing = new Paragraph();
            referencing.Runs.Add(new Run("Body with a note"));
            referencing.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(referencing);
        }

        return doc;
    }

    private static int CountImageDrawings(Visual visual)
    {
        var count = 0;
        void Walk(Drawing? drawing)
        {
            switch (drawing)
            {
                case ImageDrawing:
                    count++;
                    break;
                case DrawingGroup group:
                    foreach (var child in group.Children)
                        Walk(child);
                    break;
            }
        }

        Walk(VisualTreeHelper.GetDrawing(visual));
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                count += CountImageDrawings(child);
        return count;
    }

    [StaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrintedPage_DrawsTheEmbeddedObjectsMetafileIcon(bool withFootnote)
    {
        var editor = new DocumentView();
        editor.LoadModel(BuildDocument(withFootnote));
        editor.CommitToModel();

        var paginator = PrintLayout.BuildPaginator(editor);
        paginator.ComputePageCount();

        CountImageDrawings((Visual)paginator.GetPage(0).Visual).Should().BeGreaterThan(0,
            "the object's icon metafile must be painted, not just laid out as empty space");
    }
}
