using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

#if DEBUG

/// <summary>
/// One physical page slot in the <see cref="PaginatedEditorPanel"/>.  Hosts a read-only header strip
/// at the top, a body <see cref="RichTextBox"/> that the user edits, and a read-only footer strip at
/// the bottom.  The body is fixed to the page content area so each box represents exactly one page.
///
/// <para>
/// The body RichTextBox wraps a freshly created <see cref="FlowDocument"/> whose blocks have been
/// moved directly from the source FlowDocument produced by <see cref="PaginatedEditorPanel.Build"/>.
/// Moving — rather than serialising and re-parsing — is the critical Tag-preservation strategy: every
/// WPF <see cref="System.Windows.Documents.Block"/> element carries its original <c>Tag</c> payload
/// (<c>ParagraphTag</c>, <c>RunMarkers</c>, <c>FootnoteMarker</c>, etc.) intact, so
/// <see cref="PaginatedCommitCoordinator"/> can read them back with the same logic that
/// <see cref="DocumentView.CommitToModel"/> uses for the continuous editor.
/// </para>
/// </summary>
internal sealed class PageBox : Border
{
    // ── geometry constants ────────────────────────────────────────────────────────────────────────
    private const double PageGapDip = 20;        // vertical gap rendered above each page box
    private const double HeaderHeightDip = 24;   // placeholder header strip height
    private const double FooterHeightDip = 24;   // placeholder footer strip height

    // ── public surface ────────────────────────────────────────────────────────────────────────────
    /// <summary>The editable body RichTextBox for this page.</summary>
    internal RichTextBox Body { get; }

    /// <summary>1-based page number (informational; shown in header strip).</summary>
    internal int PageNumber { get; }

    // ── construction ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a page box for <paramref name="pageNumber"/> using geometry from <paramref name="page"/>.
    /// <paramref name="pageBlocks"/> are the WPF Block elements (already detached from their previous
    /// parent) that belong on this page; they are added directly to the body FlowDocument so Tags are
    /// preserved.
    /// </summary>
    internal PageBox(int pageNumber, PageSettings page, IReadOnlyList<System.Windows.Documents.Block> pageBlocks)
    {
        PageNumber = pageNumber;

        var (pageWidth, _) = PageLayout.PageSizeDip(page);
        var (marginLeft, marginTop, marginRight, marginBottom) = PageLayout.MarginsDip(page);
        var (contentWidth, contentHeight) = PageLayout.ContentAreaDip(page);

        // ── page-chrome border (the white page "sheet") ───────────────────────────────────────────
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        BorderThickness = new Thickness(1);
        Margin = new Thickness(0, PageGapDip, 0, 0);
        Width = pageWidth;

        // ── outer stack: header + body + footer ───────────────────────────────────────────────────
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeightDip) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeightDip) });

        // header strip (read-only, page number label)
        var headerStrip = BuildStrip($"— Page {pageNumber} —", marginLeft, marginRight);
        Grid.SetRow(headerStrip, 0);
        stack.Children.Add(headerStrip);

        // body RichTextBox
        var bodyFlow = new FlowDocument { PagePadding = new Thickness(0) };
        if (contentWidth > 0)
            bodyFlow.PageWidth = contentWidth;

        // Move the pre-rendered blocks into the body FlowDocument.  Moving preserves Tags because
        // the block objects themselves are not recreated — only their parent pointer changes.
        foreach (var block in pageBlocks)
            bodyFlow.Blocks.Add(block);

        Body = new RichTextBox
        {
            Document = bodyFlow,
            IsDocumentEnabled = true,
            AcceptsTab = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(marginLeft, marginTop, marginRight, marginBottom),
            // Fix height to the full page content height so the box has a definite page size.
            MinHeight = contentHeight + marginTop + marginBottom,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Grid.SetRow(Body, 1);
        stack.Children.Add(Body);

        // footer strip
        var footerStrip = BuildStrip(string.Empty, marginLeft, marginRight);
        Grid.SetRow(footerStrip, 2);
        stack.Children.Add(footerStrip);

        Child = stack;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static Border BuildStrip(string text, double padLeft, double padRight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            Padding = new Thickness(padLeft, 2, padRight, 2),
            Child = label
        };
    }
}

#endif
