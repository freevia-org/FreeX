using System.Linq;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word synthesizes small capitals (upper-case glyphs at 80% size) for any font. The page render does the
/// same, while the editor's live runs stay a single run so a commit reads the original text back.
/// </summary>
public sealed class SyntheticSmallCapsPrintTests
{
    [StaFact]
    public void PrintedSmallCaps_DrawsLowerCaseLettersAsUpperCaseGlyphsAtEightyPercent()
    {
        var view = MakeView(pageOnly: true);

        var runs = PageGlyphRuns(PrintLayout.BuildPaginator(view).GetPage(0).Visual);

        var text = string.Concat(runs.Select(run => new string(run.Characters.ToArray())));
        Assert.Contains("BUREAUDELIGUE", text.Replace(" ", string.Empty));
        var capital = runs.First(run => new string(run.Characters.ToArray()).Contains('B'));
        var small = runs.First(run => new string(run.Characters.ToArray()).Contains('U'));
        Assert.Equal(capital.FontRenderingEmSize * 0.8, small.FontRenderingEmSize, 2);
    }

    [StaFact]
    public void EditorSmallCaps_KeepsTheSingleOriginalRun()
    {
        var view = MakeView(pageOnly: false);

        var runs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<System.Windows.Documents.Run>())
            .Select(run => run.Text)
            .ToList();

        Assert.Contains("Bureau de Ligue", runs);
    }

    private static DocumentView MakeView(bool pageOnly)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bureau de Ligue")
        {
            Formatting = RunFormatting.Default with { SmallCaps = true, FontSizePt = 20 }
        });
        doc.Blocks.Add(paragraph);
        var view = new DocumentView { RenderPageBreakMarkers = !pageOnly };
        view.LoadModel(doc);
        return view;
    }

    private static System.Collections.Generic.List<GlyphRun> PageGlyphRuns(Visual root)
    {
        var result = new System.Collections.Generic.List<GlyphRun>();
        Walk(root);
        return result;

        void Walk(Visual visual)
        {
            Flatten(VisualTreeHelper.GetDrawing(visual));
            var count = VisualTreeHelper.GetChildrenCount(visual);
            for (var i = 0; i < count; i++)
                if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                    Walk(child);
        }

        void Flatten(Drawing? drawing)
        {
            switch (drawing)
            {
                case System.Windows.Media.DrawingGroup group:
                    foreach (var child in group.Children)
                        Flatten(child);
                    break;
                case GlyphRunDrawing glyphs:
                    result.Add(glyphs.GlyphRun);
                    break;
            }
        }
    }
}
