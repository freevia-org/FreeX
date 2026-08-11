using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for printing/exporting documents that contain embedded pictures.
/// <see cref="PrintLayout.BuildPaginator"/> deep-clones the editor FlowDocument through
/// <c>XamlWriter.Save</c>/<c>XamlReader.Load</c>, and WPF's decoded bitmap types are not
/// XAML-round-trippable: a decoded frame is the internal <c>BitmapFrameDecode</c> (non-public, so
/// <c>XamlWriter.Save</c> throws) and the undecodable-image placeholder is a <c>RenderTargetBitmap</c>
/// (no parameterless constructor, so <c>XamlReader.Load</c> throws). That crashed Print, Print Preview,
/// Export to PDF and Export to XPS for any document containing a picture.
/// </summary>
public sealed class PrintLayoutImageCloneTests
{
    [StaFact]
    public void BuildPaginator_DocumentWithDecodedImage_DoesNotThrow_AndKeepsImage()
    {
        var view = BuildViewWithImage(OnePixelPng(), ImageFormat.Png);

        var paginator = PrintLayout.BuildPaginator(view);

        Assert.NotEqual(DocumentPage.Missing, paginator.GetPage(0));
        Assert.Equal(1, CountSourcedImages(paginator));
    }

    [StaFact]
    public void BuildPaginator_DocumentWithUndecodableImage_DoesNotThrow_AndKeepsPlaceholder()
    {
        var view = BuildViewWithImage(new byte[] { 1, 2, 3, 4 }, ImageFormat.Wmf);

        var paginator = PrintLayout.BuildPaginator(view);

        Assert.NotEqual(DocumentPage.Missing, paginator.GetPage(0));
        Assert.Equal(1, CountSourcedImages(paginator));
    }

    [StaFact]
    public void PdfExport_DocumentWithImage_ProducesValidPdf()
    {
        var view = BuildViewWithImage(OnePixelPng(), ImageFormat.Png);

        var bytes = PdfExport.RenderToBytes(PrintLayout.BuildPaginator(view), "Picture");

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Exported PDF should not be empty.");
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [StaFact]
    public void PdfExport_DocumentWithUndecodableImage_ProducesValidPdf()
    {
        var view = BuildViewWithImage(new byte[] { 1, 2, 3, 4 }, ImageFormat.Wmf);

        var bytes = PdfExport.RenderToBytes(PrintLayout.BuildPaginator(view), "Picture");

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "Exported PDF should not be empty.");
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [StaFact]
    public void XpsExport_DocumentWithImage_ProducesNonEmptyArtifact()
    {
        var view = BuildViewWithImage(OnePixelPng(), ImageFormat.Png);

        var bytes = XpsExport.RenderToBytes(PrintLayout.BuildPaginator(view));

        Assert.NotEmpty(bytes);
    }

    [StaFact]
    public void BuildPaginator_DocumentWithImage_ClonePreservesTheDecodedBitmap()
    {
        // Fidelity, not just survival: the printed page must carry the very bitmap the editor decoded,
        // so a picture cannot silently print blank.
        var view = BuildViewWithImage(OnePixelPng(), ImageFormat.Png);
        var editorSources = DescendantImages(view.Document)
            .Select(image => image.Source)
            .ToList();
        Assert.Single(editorSources);

        var paginator = PrintLayout.BuildPaginator(view);
        _ = paginator.GetPage(0);

        var flow = Assert.IsAssignableFrom<FlowDocument>(paginator.Source);
        var clonedSources = DescendantImages(flow).Select(image => image.Source).ToList();

        Assert.Single(clonedSources);
        Assert.Same(editorSources[0], clonedSources[0]);
        // And the editor's own document is left untouched by the clone's strip/restore cycle.
        Assert.NotNull(DescendantImages(view.Document).Single().Source);
    }

    [StaFact]
    public void BuildPaginatedSource_DocumentWithImage_DoesNotThrow()
    {
        // The Print Preview window binds an IDocumentPaginatorSource built from the same clone path.
        var view = BuildViewWithImage(OnePixelPng(), ImageFormat.Png);

        var source = PrintLayout.BuildPaginatedSource(view);

        Assert.NotEqual(DocumentPage.Missing, source.DocumentPaginator.GetPage(0));
    }

    /// <summary>Counts <see cref="Image"/> elements with a live source in the paginated (cloned) document.</summary>
    private static int CountSourcedImages(DocumentPaginator paginator)
    {
        // Realise the first page so the flow document lays out, then walk the cloned logical tree.
        _ = paginator.GetPage(0);
        var flow = paginator.Source as FlowDocument;
        Assert.NotNull(flow);
        return DescendantImages(flow!).Count(image => image.Source is not null);
    }

    private static IEnumerable<Image> DescendantImages(DependencyObject node)
    {
        if (node is Image image)
            yield return image;
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d)
                foreach (var found in DescendantImages(d))
                    yield return found;
    }

    private static DocumentView BuildViewWithImage(byte[] bytes, ImageFormat format)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new FreeW.Core.Model.Paragraph();
        para.Runs.Add(new Run("Before "));
        para.Runs.Add(Run.FromImage(new InlineImage(bytes, 50, 30, format)));
        para.Runs.Add(new Run(" After"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
