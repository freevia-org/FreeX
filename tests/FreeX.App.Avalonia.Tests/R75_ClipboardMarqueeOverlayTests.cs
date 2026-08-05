using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R75-render-selection-marquee-4-2: the Avalonia (Linux/macOS) shell rendered NO Copy/Cut
/// marching-ants marquee at all -- unlike the WPF host's <c>GridView.ClipboardRange</c>/
/// <c>ClipboardIsCut</c> (<c>GridView.Overlays.cs</c> <c>RenderMarchingAnts</c>), so a user had no
/// visual reminder of which range a subsequent Paste would actually use. This adds a minimal,
/// non-animated port: <c>MainWindow._clipboardMarqueeRange</c>/<c>_clipboardMarqueeIsCut</c>, set by
/// <c>CopySelectedRangeToClipboardAsync</c>/<c>CutSelectedRangeToClipboardAsync</c>, rendered by
/// <c>AddClipboardMarqueeOverlayToGrid</c> as a static dashed rectangle, and cleared by every Paste*
/// method, Escape, and committing an ordinary cell edit.
///
/// Avalonia's <c>IClipboard</c> is <c>[NotClientImplementable]</c> (see
/// <see cref="R66_ClipboardHtmlReadPasteTests"/>'s doc comment for the established rationale in this
/// project), so the real async Copy/Cut/Paste methods cannot be driven end-to-end in a headless test.
/// These tests instead exercise the actual overlay-rendering code (via the real
/// <c>RebuildSheetGridForTest()</c> seam against the actual private marquee state, set through the
/// test-only <c>SetClipboardMarqueeForTest</c> seam that calls the exact same private
/// <c>SetClipboardMarquee</c> helper Copy/Cut/Paste/Escape/edit-commit call) plus source-level
/// assertions proving each of those call sites is actually wired to it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R75_ClipboardMarqueeOverlayTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Overlay rendering: the marquee must appear after "Copy" and disappear after "Paste/Escape" ──

    [Fact]
    public async Task AfterCopy_SourceRangeHasADashedMarqueeOverlay()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));

            // Simulates what CopySelectedRangeToClipboardAsync does to the marquee state after a
            // successful OS-clipboard write (SetClipboardMarquee(_session.SelectedRange, isCut: false)).
            window.SetClipboardMarqueeForTest(range, isCut: false);

            var built = window.RebuildSheetGridForTest();
            var marquee = FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCopyMarquee");

            marquee.Should().NotBeNull(
                "after a Copy, the source range must render a marquee overlay -- previously the Avalonia " +
                "shell drew nothing at all for an active Copy/Cut, unlike the WPF host's marching ants");
            marquee!.StrokeDashArray.Should().NotBeNullOrEmpty("the marquee must be a DASHED outline, not a solid border");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AfterCut_SourceRangeHasACutMarqueeOverlay_WithItsOwnAutomationId()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            var range = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5));

            window.SetClipboardMarqueeForTest(range, isCut: true);

            var built = window.RebuildSheetGridForTest();
            FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCutMarquee").Should().NotBeNull(
                "a Cut must render its own distinctly-tagged marquee overlay");
            FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCopyMarquee").Should().BeNull(
                "a Cut's marquee must not also carry the Copy automation id");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AfterClearingTheMarquee_OverlayIsGone_SimulatingPasteOrEscape()
    {
        // Sibling: proves the overlay actually reacts to the clear side (what every Paste* method,
        // Escape, and an ordinary edit commit all now do: SetClipboardMarquee(null, isCut: false)).
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 3));

            window.SetClipboardMarqueeForTest(range, isCut: false);
            FindByAutomationId<AvaloniaRectangle>(window.RebuildSheetGridForTest(), "WorksheetClipboardCopyMarquee")
                .Should().NotBeNull("sanity check: the marquee must render before it is cleared");

            window.SetClipboardMarqueeForTest(null);
            var builtAfterClear = window.RebuildSheetGridForTest();

            FindByAutomationId<AvaloniaRectangle>(builtAfterClear, "WorksheetClipboardCopyMarquee").Should().BeNull(
                "clearing the marquee (Paste/Escape/edit-commit) must remove the overlay from the next rebuild");
            FindByAutomationId<AvaloniaRectangle>(builtAfterClear, "WorksheetClipboardCutMarquee").Should().BeNull();

            window.ClipboardMarqueeRangeForTest.Should().BeNull();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MarqueeOnADifferentSheet_DoesNotRender_MatchingWpfHostSheetAffinityGuard()
    {
        // Sibling no-regression: the WPF host's RenderMarchingAnts hides the marquee while any sheet
        // OTHER than the one it was copied from is active (ClipboardRange isn't cleared on a sheet
        // switch). The Avalonia port must not draw a marquee for a same-numbered range on an unrelated
        // sheet just because it happens to be active.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var originalSheet = window.Session.ActiveSheet;
            var otherSheet = window.Session.Workbook.AddSheet("Elsewhere");

            var range = new GridRange(new CellAddress(originalSheet.Id, 2, 2), new CellAddress(originalSheet.Id, 3, 3));
            window.SetClipboardMarqueeForTest(range, isCut: false);

            window.Session.SelectSheet(otherSheet.Id);
            var built = window.RebuildSheetGridForTest();

            FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCopyMarquee").Should().BeNull(
                "the marquee belongs only to the sheet it was copied from, not every sheet with a same-numbered range");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NoRegression_NormalSelectionOverlayStillRenders_WithNoMarqueeActive()
    {
        // Sibling no-regression: adding the new overlay pass must not disturb the pre-existing
        // selection outline / active-cell box overlays when no Copy/Cut is pending (the overwhelmingly
        // common case).
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var built = window.RebuildSheetGridForTest();

            FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCopyMarquee").Should().BeNull();
            FindByAutomationId<AvaloniaRectangle>(built, "WorksheetClipboardCutMarquee").Should().BeNull();
            FindByAutomationId<Border>(built, "WorksheetActiveCellBox").Should().NotBeNull(
                "the pre-existing active-cell box overlay must still render unaffected");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Source-level wiring: Copy/Cut/every Paste*/Escape/edit-commit must actually call the setter ──

    [Fact]
    public void CopyAndCut_BothCallSetClipboardMarquee_WithTheCorrectIsCutFlag()
    {
        var source = MainWindowSource();

        source.Should().Contain(
            "SetClipboardMarquee(_session.SelectedRange, isCut: false);\n        RefreshShell(UiText.Format(\"MainLoc_CopiedX\", rangeReference));",
            "CopySelectedRangeToClipboardAsync must arm the marquee (isCut: false) right before its success status message");
        source.Should().Contain(
            "SetClipboardMarquee(_session.SelectedRange, isCut: true);\n        RefreshShell(UiText.Format(\"MainLoc_CutX\", rangeReference));",
            "CutSelectedRangeToClipboardAsync must arm the marquee (isCut: true) right before its success status message");
    }

    [Theory]
    [InlineData("MainLoc_PastedAt", FormatCellReferenceCall)]
    [InlineData("MainLoc_PastedPictureAt", FormatCellReferenceCall)]
    public void EveryPasteSuccessPath_ClearsTheMarquee_BeforeItsStatusMessage(string statusKey, string cellRefExpr)
    {
        var source = MainWindowSource();
        source.Should().Contain(
            $"SetClipboardMarquee(null, isCut: false);\n        RefreshShell(UiText.Format(\"{statusKey}\", {cellRefExpr}));",
            $"the paste path reporting '{statusKey}' must clear the marquee immediately before its success message");
    }

    private const string FormatCellReferenceCall = "FormatCellReference(destination)";

    [Fact]
    public void AllLabelledPasteVariants_ClearTheMarquee_BeforeTheSharedPastedLabelAtMessage()
    {
        var source = MainWindowSource();

        // PasteSpecialClipboardTextAsync / PasteColumnWidthsFromClipboardAsync /
        // PasteCommentsFromClipboardAsync / PasteDataValidationFromClipboardAsync /
        // PasteLinkFromClipboardAsync / PasteSpecialExternalTextFromClipboardAsync /
        // PastePictureFromClipboardAsync all end with the identical shared status message -- every
        // occurrence must be immediately preceded by the marquee clear.
        const string sharedStatusLine =
            "RefreshShell(UiText.Format(\"MainLoc_PastedLabelAt\", label, FormatCellReference(destination)));";
        const string clearThenSharedStatus =
            "SetClipboardMarquee(null, isCut: false);\n        " + sharedStatusLine;

        var totalOccurrences = CountOccurrences(source, sharedStatusLine);
        var clearedOccurrences = CountOccurrences(source, clearThenSharedStatus);

        totalOccurrences.Should().Be(7, "sanity check: 7 labelled paste variants share this status message");
        clearedOccurrences.Should().Be(totalOccurrences,
            "every labelled paste variant (PasteSpecialClipboardTextAsync, PasteColumnWidthsFromClipboardAsync, " +
            "PasteCommentsFromClipboardAsync, PasteDataValidationFromClipboardAsync, PasteLinkFromClipboardAsync, " +
            "PasteSpecialExternalTextFromClipboardAsync, PastePictureFromClipboardAsync) must clear the marquee " +
            "right before reporting success -- a stale marquee would keep pointing at a range a later Paste no " +
            "longer uses");
    }

    [Fact]
    public void Escape_ClearsTheMarquee_WhenOneIsActive()
    {
        var source = MainWindowSource();
        const string escapeClipboardGuard =
            "if (e.Key == Key.Escape &&\n" +
            "                (_clipboardMarqueeRange is not null || _internalObjectClipboard is not null))";

        source.Should().Contain(
            escapeClipboardGuard,
            "Escape must check for either active clipboard visual state, preserving the WPF host's " +
            "ClearClipboardVisualState-on-Escape behavior while also clearing copied drawing objects");

        var guardIndex = source.IndexOf(escapeClipboardGuard, StringComparison.Ordinal);
        var body = source.Substring(guardIndex, 300);
        body.Should().Contain("SetClipboardMarquee(null, isCut: false);");
        body.Should().Contain("_internalObjectClipboard = null;");
    }

    [Fact]
    public void CommittingAnOrdinaryCellEdit_ClearsTheMarquee_BothCommitPaths()
    {
        var source = MainWindowSource();

        var commitFormulaBoxBody = ExtractMethodBody(source, "private bool CommitFormulaBox()");
        commitFormulaBoxBody.Should().Contain("SetClipboardMarquee(null, isCut: false);",
            "committing a normal formula-bar/inline cell edit must cancel an active Copy/Cut marquee " +
            "(mirroring the WPF host's TryExecuteEditCells), so a later Paste cannot silently use a " +
            "source range the user has since overwritten");

        var commitAcrossSelectionBody = ExtractMethodBody(
            source, "private bool CommitEditAcrossSelection(CellAddress current, string text)");
        commitAcrossSelectionBody.Should().Contain("SetClipboardMarquee(null, isCut: false);",
            "the Ctrl+Enter fill-selection commit path must cancel the marquee identically to the single-cell commit");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static T? FindByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T own && AutomationProperties.GetAutomationId(own) == automationId)
            return own;

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"MainWindow.cs should declare a method with the exact signature '{signature}'");

        var braceOpenIndex = source.IndexOf('{', startIndex);
        braceOpenIndex.Should().BeGreaterThan(startIndex);

        var depth = 0;
        var index = braceOpenIndex;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }

        index.Should().BeLessThan(source.Length, "the method's closing brace should be found");
        return source[braceOpenIndex..(index + 1)];
    }

    private static string MainWindowSource() =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
