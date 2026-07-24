using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for R84 findings render-text-wrap-rotate-5-1 / -5-2 (Avalonia rotated-cell
/// text layout):
///
///   5-1 — CreateOrientedCellContent computed CellTextOrientationLayoutPlanner.CalculateLayout
///         without ever forwarding the caller's isEffectivelyRightToLeft flag (it always defaulted to
///         false), so a rotated General-aligned text cell on a right-to-left sheet stayed
///         left-anchored instead of mirroring to the right edge like WPF/Excel. Fixed by adding an
///         isEffectivelyRightToLeft parameter to CreateOrientedCellContent and threading it into both
///         the CalculateLayout call and the BuildSheetGrid call site (matching the flowDirection ==
///         FlowDirection.RightToLeft already threaded into CreateDefaultCellContent's isRightToLeft).
///
///   5-2 — The wrap-measure-width for a rotated+wrapped cell was `Math.Max(1, cellWidth - 4)`, never
///         subtracting the cell's indent, unlike WPF's `rect.Width - 4 - indentPx` (applied
///         unconditionally of rotation). Fixed by extracting the computation into
///         ResolveOrientedWrapMeasureWidth, which now subtracts indentPixels too.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R84_AvaloniaRotatedTextLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── 5-1: isEffectivelyRightToLeft must be threaded into the rotated-cell layout ──────────

    [Fact]
    public async Task CreateOrientedCellContent_GeneralAlignment_RightAnchorsWhenEffectivelyRightToLeft()
    {
        await Session.Dispatch(() =>
        {
            var ltrContent = BuildOrientedContent(isEffectivelyRightToLeft: false);
            var rtlContent = BuildOrientedContent(isEffectivelyRightToLeft: true);

            var ltrLeft = Canvas.GetLeft(GetTextBlock(ltrContent));
            var rtlLeft = Canvas.GetLeft(GetTextBlock(rtlContent));

            // General-aligned TEXT content anchors at the "start" of the reading direction: the LEFT
            // edge in LTR, the RIGHT edge in RTL. With a 200px-wide cell and a short rotated string,
            // the RTL anchor must land measurably further right than the LTR anchor.
            rtlLeft.Should().BeGreaterThan(ltrLeft,
                "a rotated General-aligned text cell on a right-to-left sheet must right-anchor, matching WPF/Excel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CreateOrientedCellContent_ExplicitLeftAlignment_IgnoresEffectivelyRightToLeft()
    {
        // No-regression sibling: an EXPLICIT Left alignment (not General) must never mirror with
        // reading order — only General auto-mirrors in Excel.
        await Session.Dispatch(() =>
        {
            var ltrContent = BuildOrientedContent(isEffectivelyRightToLeft: false, hAlign: HorizontalAlignment.Left);
            var rtlContent = BuildOrientedContent(isEffectivelyRightToLeft: true, hAlign: HorizontalAlignment.Left);

            var ltrLeft = Canvas.GetLeft(GetTextBlock(ltrContent));
            var rtlLeft = Canvas.GetLeft(GetTextBlock(rtlContent));

            rtlLeft.Should().BeApproximately(ltrLeft, 0.01,
                "an explicit Left alignment must not mirror with the sheet's reading order");
        }, CancellationToken.None);
    }

    private static Grid BuildOrientedContent(bool isEffectivelyRightToLeft, HorizontalAlignment hAlign = HorizontalAlignment.General) =>
        MainWindow.CreateOrientedCellContentForTest(
            new TextBlock { Text = "AB" },
            cellWidth: 200,
            cellHeight: 100,
            horizontalAlignment: hAlign,
            verticalAlignment: null,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 45,
            textWrapping: TextWrapping.NoWrap,
            style: null,
            isEffectivelyRightToLeft: isEffectivelyRightToLeft);

    private static TextBlock GetTextBlock(Grid content)
    {
        var canvas = (Canvas)content.Children[0];
        return (TextBlock)canvas.Children[0];
    }

    // ── 5-2: the wrap-measure-width for a rotated+wrapped cell must subtract the indent ──────

    [Fact]
    public void ResolveOrientedWrapMeasureWidth_SubtractsIndent_WhenWrapping()
    {
        var width = MainWindow.ResolveOrientedWrapMeasureWidthForTest(cellWidth: 100, indentPixels: 20, TextWrapping.Wrap);

        width.Should().Be(76,
            "WPF subtracts the indent from the wrap width unconditionally of rotation (rect.Width - 4 - indentPx)");
    }

    [Fact]
    public void ResolveOrientedWrapMeasureWidth_IgnoresIndent_WhenNotWrapping()
    {
        // No-regression sibling: a non-wrapping rotated cell must still measure at infinite width
        // regardless of indent — only WrapText constrains the measure pass.
        var width = MainWindow.ResolveOrientedWrapMeasureWidthForTest(cellWidth: 100, indentPixels: 20, TextWrapping.NoWrap);

        width.Should().Be(double.PositiveInfinity);
    }
}
