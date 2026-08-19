using System.Linq;
using System.Reflection;

using Avalonia.Controls;
using Avalonia.Media;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// rtl-localization F2: the Avalonia renderer for a Linked Picture / Camera cell-range snapshot
/// (<c>MainWindow.CreateDrawingCellRangeSnapshotVisual</c>, <see cref="PictureKind.CellRangeSnapshot"/>,
/// created in-app by Paste Special &gt; Linked Picture / PasteRangeAsPictureCommand) hardcoded
/// <c>isEffectivelyRightToLeft: false</c> and never set <c>TextBlock.FlowDirection</c>, so a snapshot
/// taken from -- or displayed on -- a right-to-left sheet always rendered as if the sheet were
/// left-to-right, unlike the WPF host's <c>GridView.DrawPictureCellText</c> (fixed for the same case
/// in R88-render-rtl-bidi-5-2, see tests/FreeX.App.UI.Tests/R88_PictureRtlSnapshotTests.cs). The fix
/// threads the sheet's effective RTL flag from <c>CreateSelectableDrawingObjectVisual</c> (which has
/// <c>_session.ActiveSheet.IsRightToLeft</c>) through <c>CreateDrawingObjectVisual</c> into
/// <c>CreateDrawingCellRangeSnapshotVisual</c>, which now resolves the per-cell reading order via
/// <see cref="FreeX.Core.Calc.CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft"/> the
/// same way the live grid does, and sets both TextAlignment and FlowDirection from it.
/// </summary>
public sealed class R146_PictureCellRangeSnapshotRtlTests
{
    private static readonly MethodInfo CreateDrawingCellRangeSnapshotVisualMethod = typeof(MainWindow).GetMethod(
        "CreateDrawingCellRangeSnapshotVisual", BindingFlags.Static | BindingFlags.NonPublic)!;

    // A single source cell (RowCount = ColumnCount = 1) so the snapshot's one cell spans the whole
    // picture rect -- simplifies locating the rendered TextBlock.
    private static DrawingObjectRenderPlan BuildNumericGeneralCellSnapshotPlan()
    {
        var bounds = new DrawingObjectBounds(
            Kind: SelectionPaneObjectKind.Picture,
            Id: System.Guid.NewGuid(),
            DisplayName: "Picture 1",
            AnchorRow: 1,
            AnchorCol: 1,
            Left: 0,
            Top: 0,
            Width: 100,
            Height: 60,
            PictureKind: PictureKind.CellRangeSnapshot);

        var cell = new PictureCellSnapshot(
            RowOffset: 0,
            ColumnOffset: 0,
            Text: "12345",
            Style: null,
            IsNumericOrDate: true);

        var grid = new DrawingPictureGrid(RowCount: 1, ColumnCount: 1, Cells: [cell]);

        return new DrawingObjectRenderPlan(bounds, DrawingObjectRenderPrimitiveKind.CellRangeSnapshot, PictureGrid: grid);
    }

    private static TextBlock FindSnapshotTextBlock(Control visual)
    {
        var outer = (Border)visual;
        var canvas = (Canvas)outer.Child!;
        var textHost = canvas.Children.OfType<Border>().Single(b => b.Child is TextBlock);
        return (TextBlock)textHost.Child!;
    }

    [Fact]
    public void CreateDrawingCellRangeSnapshotVisual_NumericGeneralCell_OnRightToLeftSheet_MirrorsAlignmentAndFlow()
    {
        var plan = BuildNumericGeneralCellSnapshotPlan();

        var visual = (Control)CreateDrawingCellRangeSnapshotVisualMethod.Invoke(
            null, [plan, 100.0, 60.0, WorkbookTheme.Office, true])!;
        var textBlock = FindSnapshotTextBlock(visual);

        // Pre-fix bug: isEffectivelyRightToLeft was hardcoded to false, so this stayed
        // TextAlignment.Right / FlowDirection.LeftToRight regardless of the sheet's reading order --
        // identical to the LTR case below.
        textBlock.TextAlignment.Should().Be(TextAlignment.Left,
            "a numeric General-aligned cell must mirror to the LEFT on a right-to-left sheet, matching " +
            "the live grid and the WPF host's DrawPictureCellText fix");
        textBlock.FlowDirection.Should().Be(FlowDirection.RightToLeft,
            "the snapshot's text flow must follow the sheet's reading order instead of always defaulting " +
            "to LeftToRight");
    }

    // ── Sibling no-regression: the ordinary left-to-right sheet keeps its pre-fix rendering ───────

    [Fact]
    public void CreateDrawingCellRangeSnapshotVisual_NumericGeneralCell_OnLeftToRightSheet_KeepsRightFlushLtrFlow()
    {
        var plan = BuildNumericGeneralCellSnapshotPlan();

        var visual = (Control)CreateDrawingCellRangeSnapshotVisualMethod.Invoke(
            null, [plan, 100.0, 60.0, WorkbookTheme.Office, false])!;
        var textBlock = FindSnapshotTextBlock(visual);

        textBlock.TextAlignment.Should().Be(TextAlignment.Right,
            "General alignment on a numeric cell must keep flushing to the RIGHT on an ordinary " +
            "left-to-right sheet, unchanged from before the fix");
        textBlock.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [Fact]
    public void CreateDrawingCellRangeSnapshotVisual_DefaultParameter_StillDefaultsToLeftToRight()
    {
        // The new isSheetRightToLeft parameter defaults to false so any caller that doesn't pass it
        // (there are none left in MainWindow.cs, but the parameter itself still carries the safe
        // default) behaves exactly as before the fix.
        var parameters = CreateDrawingCellRangeSnapshotVisualMethod.GetParameters();
        var rtlParameter = parameters.Single(p => p.Name == "isSheetRightToLeft");
        rtlParameter.HasDefaultValue.Should().BeTrue();
        rtlParameter.DefaultValue.Should().Be(false);
    }
}
