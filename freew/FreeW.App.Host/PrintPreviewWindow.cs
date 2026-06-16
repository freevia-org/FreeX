using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modeless, read-only print-preview window. It paginates the editor's current content into
/// discrete pages at the model's <see cref="PageSettings"/> size and margins, so the user sees the
/// real page boundaries that printing will produce.
///
/// Pagination is delegated to WPF's <see cref="FlowDocumentPageViewer"/>: by setting the previewed
/// <see cref="FlowDocument"/>'s <see cref="FlowDocument.PageWidth"/>/<see cref="FlowDocument.PageHeight"/>
/// (and a single-column layout) to the page geometry computed by <see cref="PageLayout"/>, the
/// viewer's internal <see cref="DocumentPaginator"/> breaks the flow into page-sized pieces. This
/// window never edits the model; it works on a display-only copy of the editor's FlowDocument so the
/// concurrent model/FlowDocument mapping in <see cref="DocumentView"/> is untouched.
/// </summary>
public sealed class PrintPreviewWindow : Window
{
    public PrintPreviewWindow(DocumentView editor)
    {
        Title = "Print Preview — FreeW";
        Width = 900;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        Owner = Window.GetWindow(editor);

        var viewer = new FlowDocumentPageViewer
        {
            Document = PrintLayout.BuildPaginatedDocument(editor)
        };

        Content = viewer;
    }
}

/// <summary>
/// Shared layout/printing helper. Builds a page-settings-aware <see cref="FlowDocument"/> from the
/// editor's current content (used by both the print-preview window and <see cref="MainWindow.Print"/>),
/// converting the model's point-based <see cref="PageSettings"/> into DIP via <see cref="PageLayout"/>.
/// </summary>
internal static class PrintLayout
{
    /// <summary>
    /// Produces a fresh <see cref="FlowDocument"/> whose page size and margins match the model's
    /// <see cref="PageSettings"/>, carrying a display-only clone of the editor's content. The clone is
    /// taken via XAML round-tripping over the editor's FlowDocument so this path never reaches into
    /// the model&lt;-&gt;FlowDocument mapping owned by <see cref="DocumentView"/>.
    /// </summary>
    public static FlowDocument BuildPaginatedDocument(DocumentView editor)
    {
        var page = editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        var (left, top, right, bottom) = PageLayout.MarginsDip(page);

        var flow = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(left, top, right, bottom),
            ColumnWidth = double.PositiveInfinity, // single column spanning the content area
            ColumnGap = 0,
            FontFamily = editor.Document.FontFamily,
            FontSize = editor.Document.FontSize
        };

        foreach (var block in CloneBlocks(editor.Document))
            flow.Blocks.Add(block);

        return flow;
    }

    /// <summary>
    /// Deep-clones the editor FlowDocument's blocks via XAML serialization. We clone (rather than
    /// re-host the live FlowDocument) because a FlowDocument may belong to only one container at a
    /// time, and the editor keeps its own; cloning leaves the editable surface untouched.
    /// </summary>
    private static IEnumerable<Block> CloneBlocks(FlowDocument source)
    {
        var clone = (FlowDocument)CloneElement(source);
        // Detach blocks from the clone so they can be re-parented into the target FlowDocument.
        var blocks = clone.Blocks.ToList();
        clone.Blocks.Clear();
        return blocks;
    }

    private static object CloneElement(object element)
    {
        var xaml = XamlWriter.Save(element);
        using var reader = new StringReader(xaml);
        using var xmlReader = System.Xml.XmlReader.Create(reader);
        return XamlReader.Load(xmlReader);
    }
}
