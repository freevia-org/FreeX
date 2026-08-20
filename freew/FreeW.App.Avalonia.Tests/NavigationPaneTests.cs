using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Headless tests for <see cref="NavigationPane"/>: verifies that the pane builds its heading list
/// from a sample document's <see cref="DocumentOutline"/> and that scroll-to-block works via
/// <see cref="DocumentView.GetBlockTop"/>.
/// </summary>
public sealed class NavigationPaneTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    [Fact]
    public async Task NavigationPane_builds_heading_list_from_document_outline()
    {
        // Build a document with known headings and verify the nav pane produces the right item count.
        int itemCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildDocumentWithHeadings();
            var editor = new DocumentView();
            editor.LoadDocument(doc);

            var pane = new NavigationPane(editor);
            pane.Refresh();

            itemCount = pane.HeadingItemCount;
        });

        if (!ran)
            return;

        // The sample doc has 4 heading paragraphs: Title, Heading1 "Introduction",
        // Heading1 "Section One", Heading2 "Section Two".
        itemCount.Should().Be(4);
    }

    [Fact]
    public async Task NavigationPane_filters_headings_on_search()
    {
        int filteredCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildDocumentWithHeadings();
            var editor = new DocumentView();
            editor.LoadDocument(doc);

            var pane = new NavigationPane(editor);
            // Filter to only entries matching "Section"
            filteredCount = pane.CountHeadingsMatching("Section", doc);
        });

        if (!ran)
            return;

        // "Section One" and "Section Two" match directly; the ancestor "Title" is also
        // included (it is a shallower ancestor of the first matching "Section One").
        // "Introduction" (level 1, depth-peer of "Section One") is NOT an ancestor — correct.
        filteredCount.Should().Be(3);
    }

    [Fact]
    public async Task DocumentView_GetBlockTop_returns_positive_Y_for_laid_out_block()
    {
        double blockTop = -2;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildDocumentWithHeadings();
            var editor = new DocumentView();
            editor.LoadDocument(doc);
            editor.Measure(new Size(800, 4000));

            // Block 0 is the Title paragraph — should have a positive Y after layout.
            blockTop = editor.GetBlockTop(0);
        });

        if (!ran)
            return;

        blockTop.Should().BeGreaterThanOrEqualTo(0, "laid-out block 0 should have a non-negative Y coordinate");
    }

    [Fact]
    public async Task DocumentView_GetBlockTop_returns_negative_for_out_of_range_block()
    {
        double blockTop = 0;
        var ran = await OnUiThread(() =>
        {
            var editor = new DocumentView();
            editor.LoadDocument(BuildDocumentWithHeadings());
            editor.Measure(new Size(800, 4000));

            blockTop = editor.GetBlockTop(9999);
        });

        if (!ran)
            return;

        blockTop.Should().Be(-1, "out-of-range block index should return -1");
    }

    [Fact]
    public async Task MainWindow_toggle_shows_and_hides_nav_pane()
    {
        bool visibleAfterToggleOn = false;
        bool visibleAfterToggleOff = false;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var pane = window.NavPane;

            pane.IsVisible.Should().BeFalse("nav pane is hidden by default");
            window.ToggleNavigationPane();
            visibleAfterToggleOn = pane.IsVisible;
            window.ToggleNavigationPane();
            visibleAfterToggleOff = pane.IsVisible;
        });

        if (!ran)
            return;

        visibleAfterToggleOn.Should().BeTrue();
        visibleAfterToggleOff.Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TextDocument BuildDocumentWithHeadings()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Title
        var title = new Paragraph { StyleId = "Title" };
        title.Runs.Add(new Run("My Document"));
        doc.Blocks.Add(title);

        // Body paragraph before first heading
        var body1 = new Paragraph();
        body1.Runs.Add(new Run("Some introductory text."));
        doc.Blocks.Add(body1);

        // Heading 1 — "Introduction"
        var h1a = new Paragraph { StyleId = "Heading1" };
        h1a.Runs.Add(new Run("Introduction"));
        doc.Blocks.Add(h1a);

        // Body under h1a
        var body2 = new Paragraph();
        body2.Runs.Add(new Run("This is the introduction."));
        doc.Blocks.Add(body2);

        // Heading 1 — "Section One"
        var h1b = new Paragraph { StyleId = "Heading1" };
        h1b.Runs.Add(new Run("Section One"));
        doc.Blocks.Add(h1b);

        // Heading 2 — "Section Two"
        var h2 = new Paragraph { StyleId = "Heading2" };
        h2.Runs.Add(new Run("Section Two"));
        doc.Blocks.Add(h2);

        return doc;
    }
}
