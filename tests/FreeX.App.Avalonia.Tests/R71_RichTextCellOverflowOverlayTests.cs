using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R71-render-text-overflow-4-2: a rich-text (per-run formatted) cell must be allowed to overflow
/// into an empty neighbor the same way a plain-text cell does — <c>AddCellTextOverflowOverlayToGrid</c>
/// previously skipped every cell present in <see cref="Sheet.RichTextRuns"/> outright, clipping its
/// text to its own column even when the neighbor was blank (Excel spills rich-text cells too).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R71_RichTextCellOverflowOverlayTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private const string BoldRunText = "Hello ";
    private const string PlainRunText = "World that runs far past the default column width for overflow testing";

    [Fact]
    public async Task RichTextCell_WithEmptyNeighbor_OverflowsAndRendersRichRuns()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue(BoldRunText + PlainRunText));
            sheet.RichTextRuns[address] =
            [
                new CellTextRun(BoldRunText, Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                new CellTextRun(PlainRunText, Bold: false, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
            ];
            RefreshViewport(window);

            var grid = window.RebuildSheetGridForTest();

            // Before the fix, RichTextRuns.ContainsKey(address) short-circuited the loop for every
            // rich-text cell, so no overlay Canvas was ever attached to the grid — FindByAutomationId
            // would throw (Single() over zero matches) instead of finding this overlay.
            var overlay = FindByAutomationId<Canvas>(grid, "WorksheetCellTextOverflowOverlay");
            overlay.Children.Should().NotBeEmpty("a rich-text cell with a blank right neighbor should spill into it, just like a plain-text cell");

            var richBlock = FindDescendants(overlay).OfType<TextBlock>()
                .FirstOrDefault(block => block.Inlines is { Count: 2 });
            richBlock.Should().NotBeNull("the overlay should render the cell's rich runs, not just a plain-text fallback");

            var runs = richBlock!.Inlines!.OfType<Run>().ToList();
            runs.Should().HaveCount(2);
            runs[0].Text.Should().Be(BoldRunText);
            runs[0].FontWeight.Should().Be(FontWeight.Bold);
            runs[1].Text.Should().Be(PlainRunText);
            runs[1].FontWeight.Should().Be(FontWeight.Normal);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RichTextCell_WithNonEmptyNeighbor_DoesNotOverflow()
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
                new CellTextRun(PlainRunText, Bold: false, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
            ];
            sheet.SetCell(neighbor, new TextValue("X"));
            RefreshViewport(window);

            var grid = window.RebuildSheetGridForTest();

            // No cell overflows (the neighbor is occupied), so the overlay layer is never attached
            // to the grid at all (AddCellTextOverflowOverlayToGrid returns early when it stays empty).
            var overlays = FindDescendantsAndSelf(grid).OfType<Canvas>()
                .Where(control => AutomationProperties.GetAutomationId(control) == "WorksheetCellTextOverflowOverlay")
                .ToList();
            overlays.Should().BeEmpty("a rich-text cell with an occupied right neighbor must not spill over it");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: a plain-text cell's overflow behavior is unaffected ──────────────────

    [Fact]
    public async Task PlainTextCell_WithEmptyNeighbor_StillOverflowsWithoutRichRuns()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateCleanWindow(out var sheet);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue(BoldRunText + PlainRunText));
            // No RichTextRuns entry for this address — plain single-format cell.
            RefreshViewport(window);

            var grid = window.RebuildSheetGridForTest();
            var overlay = FindByAutomationId<Canvas>(grid, "WorksheetCellTextOverflowOverlay");
            overlay.Children.Should().NotBeEmpty();

            var textBlock = FindDescendants(overlay).OfType<TextBlock>()
                .First(block => block.Text == BoldRunText + PlainRunText);
            textBlock.Text.Should().Be(BoldRunText + PlainRunText);
            (textBlock.Inlines is null or { Count: 0 }).Should().BeTrue("a plain-text cell should keep using the Text fallback, not Inlines");
        }, CancellationToken.None);
    }

    // ── Helpers (mirrors LinuxWorksheetEditingParityTests' private fixture helpers) ──────────────────

    private static MainWindow CreateCleanWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("RichTextOverflowFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }

    private static void RefreshViewport(MainWindow window) =>
        window.Session.UpdateViewportSize(881, 1440);

    private static T FindByAutomationId<T>(Control root, string automationId)
        where T : Control =>
        FindDescendantsAndSelf(root)
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

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
