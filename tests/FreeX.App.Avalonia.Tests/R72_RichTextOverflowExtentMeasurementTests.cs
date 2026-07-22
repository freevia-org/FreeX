using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R72-meta-3: the r71 rich-text overflow fix (<c>AddCellTextOverflowOverlayToGrid</c> /
/// <c>AddCellTextOverflowExtension</c>) let a rich-text cell spill into an empty neighbor, but measured
/// how far it may spill (the clip extent, <c>clipRight - clipLeft</c>) from a single uniform
/// <c>FormattedText</c> built over the whole <c>DisplayText</c> using the CELL's overall style (e.g.
/// its base, un-overridden font size/weight) — ignoring any per-run bold/italic/size override from
/// <see cref="Sheet.RichTextRuns"/>. A run that overrides its own (larger) font size or weight renders
/// wider than that uniform estimate, and because the host <c>Canvas</c> sets
/// <c>Width = clipRight - clipLeft</c> with <c>ClipToBounds = true</c>, its tail was visually clipped
/// even though the neighbor cell is empty. The fix sums each resolved run's own per-run
/// <c>FormattedText</c> width (<c>MainWindow.MeasureRichRunsWidth</c>, mirroring the exact font/weight/
/// style/size <see cref="CellRichTextInlinesBuilder.Build"/> already applies when rendering) instead of
/// reusing the uniform estimate.
/// </summary>
/// <remarks>
/// The bold run in these fixtures documents the real-world scenario from the finding, but Avalonia's
/// headless text layout (used by this test session) measures <c>FormattedText</c> width purely from
/// text length and font SIZE — it does not vary advance widths by font weight/family at all (verified:
/// a bold and a normal <c>FormattedText</c> over identical text/size measure byte-for-byte identical in
/// this harness). To get a deterministic, headless-observable width difference that still exercises the
/// exact same per-run measurement code path (<c>ResolvedCellTextRun.RenderedFontSize</c>), the plain run
/// additionally overrides its own font SIZE well above the cell's base style size — precisely the other
/// per-run property (alongside bold/italic) the finding calls out as ignored by the old uniform
/// estimate.
/// </remarks>
[Collection("AvaloniaHeadless")]
public sealed class R72_RichTextOverflowExtentMeasurementTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private const string BoldRunText = "Hello ";
    private const string PlainRunText = "World that runs far past the default column width for overflow testing";
    private const double OverriddenRunFontSize = 28;

    [Fact]
    public async Task RichTextCell_PerRunOverrideWiderThanUniformEstimate_OverlayExtentCoversFullRichWidth()
    {
        await Session.Dispatch(() =>
        {
            // Rich scenario: "Hello " is bold (default size), the remaining run is plain but
            // overrides its own font size to well above the cell's base style size (11pt default) --
            // so a uniform estimate built from the cell's own (un-overridden, non-bold) style over the
            // WHOLE string would completely miss this run's true, much larger rendered width.
            var richWindow = CreateCleanWindow(out var richSheet);
            var richAddress = new CellAddress(richSheet.Id, 1, 1);
            richSheet.SetCell(richAddress, new TextValue(BoldRunText + PlainRunText));
            richSheet.RichTextRuns[richAddress] =
            [
                new CellTextRun(BoldRunText, Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                new CellTextRun(PlainRunText, Bold: false, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: OverriddenRunFontSize, FontColor: null),
            ];
            RefreshViewport(richWindow);
            var richHost = GetOverflowHostCanvas(richWindow.RebuildSheetGridForTest());

            // Plain scenario: identical concatenated text, but with NO RichTextRuns entry at all, so
            // the whole string renders (and, before this fix, is ALSO what the rich cell's clip extent
            // was measured from) using one uniform FormattedText at the cell's base style size.
            var plainWindow = CreateCleanWindow(out var plainSheet);
            var plainAddress = new CellAddress(plainSheet.Id, 1, 1);
            plainSheet.SetCell(plainAddress, new TextValue(BoldRunText + PlainRunText));
            RefreshViewport(plainWindow);
            var plainHost = GetOverflowHostCanvas(plainWindow.RebuildSheetGridForTest());

            // Before the fix, richHost.Width and plainHost.Width were computed identically (both from
            // one uniform FormattedText over the same characters at the cell's base style size),
            // clipping the rich cell's actually-larger run short. After the fix, the rich cell's
            // extent must be measured from its own (wider) per-run total and so must exceed the plain
            // estimate.
            richHost.Width.Should().BeGreaterThan(plainHost.Width,
                "a run that overrides its own (larger) font size renders wider than the uniform base-style estimate, so the rich cell's overflow extent must be wider than the plain cell's identical-text estimate, not clipped to match it");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: a uniform (non-rich) cell's overflow extent is unaffected ────────────

    [Fact]
    public async Task UniformTextCell_WithEmptyNeighbor_StillOverflowsUsingUniformEstimate()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue(BoldRunText + PlainRunText));
            // No RichTextRuns entry -- must keep using the plain, uniform FormattedText width path
            // (overflowExtentWidth falls back to textWidth) exactly as before this fix.
            RefreshViewport(window);

            var host = GetOverflowHostCanvas(window.RebuildSheetGridForTest());

            host.Width.Should().BeGreaterThan(0, "a uniform cell whose text overruns the column must still produce a non-empty overflow extent");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: a rich cell with an occupied neighbor still does not overflow ────────

    [Fact]
    public async Task RichTextCell_WithNonEmptyNeighbor_StillDoesNotOverflow()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            var neighbor = new CellAddress(sheet.Id, 1, 2);
            sheet.SetCell(address, new TextValue(BoldRunText + PlainRunText));
            sheet.RichTextRuns[address] =
            [
                new CellTextRun(BoldRunText, Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                new CellTextRun(PlainRunText, Bold: false, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: OverriddenRunFontSize, FontColor: null),
            ];
            sheet.SetCell(neighbor, new TextValue("X"));
            RefreshViewport(window);

            var grid = window.RebuildSheetGridForTest();
            var overlays = FindDescendantsAndSelf(grid).OfType<Canvas>()
                .Where(control => AutomationProperties.GetAutomationId(control) == "WorksheetCellTextOverflowOverlay")
                .ToList();
            overlays.Should().BeEmpty("a rich-text cell with an occupied right neighbor must not spill over it, regardless of how the extent is measured");
        }, CancellationToken.None);
    }

    // ── Helpers (mirrors R71_RichTextCellOverflowOverlayTests' private fixture helpers) ──────────────

    private static MainWindow CreateCleanWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("RichTextOverflowExtentFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }

    private static void RefreshViewport(MainWindow window) =>
        window.Session.UpdateViewportSize(881, 1440);

    private static Canvas GetOverflowHostCanvas(Control grid)
    {
        var overlay = FindDescendantsAndSelf(grid).OfType<Canvas>()
            .Single(control => AutomationProperties.GetAutomationId(control) == "WorksheetCellTextOverflowOverlay");
        overlay.Children.Should().NotBeEmpty("the overlay must contain at least one overflow host Canvas");
        return overlay.Children.OfType<Canvas>().First();
    }

    private static IEnumerable<Control> FindDescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in FindDescendants(root))
            yield return descendant;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Decorator { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var childControl in panel.Children)
            {
                yield return childControl;
                foreach (var descendant in FindDescendants(childControl))
                    yield return descendant;
            }
        }
        else if (root is ContentControl { Content: Control content })
        {
            yield return content;
            foreach (var descendant in FindDescendants(content))
                yield return descendant;
        }
    }
}
