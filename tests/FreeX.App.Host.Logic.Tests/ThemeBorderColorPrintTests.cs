using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-theme-border-color-F1 (WPF print half): a cell border authored against a theme slot carries
/// only a live <see cref="CellBorder.ThemeColor"/> link plus whatever RGB was baked in at load time.
/// PrintRenderer's DrawPrintedBorderEdge must re-resolve it against the workbook's CURRENT theme, the
/// same way the surrounding print pass already resolves fills (DrawPrintedCellFill) and font colors
/// (ResolvePrintedTextBrush) — otherwise a theme change recolors every printed fill and font but leaves
/// the borders on the old palette.
/// </summary>
public sealed class ThemeBorderColorPrintTests
{
    private static readonly CellColor StaleBaked = new(0x01, 0x02, 0x03);

    private static CellBorder ThemeBackedBorder() =>
        new(
            BorderStyle.Thin,
            StaleBaked,
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));

    private static WorkbookTheme ThemeWithAccent1(CellColor accent1) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, accent1);

    /// <summary>
    /// Draws one edge through the private print helper and reads back the pen color it chose, by
    /// capturing the single DrawLine the Thin/solid path emits into a DrawingGroup.
    /// </summary>
    private static Color CapturePrintedBorderColor(CellBorder border, WorkbookTheme theme, bool blackAndWhite = false)
    {

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Direct call, not reflection: DrawPrintedBorderEdge is internal and this assembly
            // has InternalsVisibleTo, so a change to its signature is a build error right here
            // rather than a runtime TargetParameterCountException from a positional array.
            PrintRenderer.DrawPrintedBorderEdge(dc, border, new Point(0, 0), new Point(40, 0), theme, blackAndWhite);
        }

        var pen = FindFirstPen(visual.Drawing);
        pen.Should().NotBeNull("the Thin border style must emit a stroked line");
        return ((SolidColorBrush)pen!.Brush).Color;
    }

    private static Pen? FindFirstPen(DrawingGroup? group)
    {
        if (group is null)
            return null;

        foreach (var drawing in group.Children)
        {
            switch (drawing)
            {
                case GeometryDrawing { Pen: { } pen }:
                    return pen;
                case DrawingGroup nested when FindFirstPen(nested) is { } nestedPen:
                    return nestedPen;
            }
        }

        return null;
    }

    private static Color Expected(CellBorder border, WorkbookTheme theme)
    {
        var resolved = border.ResolveColor(theme);
        return Color.FromRgb(resolved.R, resolved.G, resolved.B);
    }

    [Fact]
    public void DrawPrintedBorderEdge_ResolvesThemeBackedBorderAgainstTheCurrentTheme()
    {
        var border = ThemeBackedBorder();
        var themeA = ThemeWithAccent1(new CellColor(200, 10, 20));
        var themeB = ThemeWithAccent1(new CellColor(20, 200, 10));

        var colorA = CapturePrintedBorderColor(border, themeA);
        var colorB = CapturePrintedBorderColor(border, themeB);

        colorA.Should().Be(Expected(border, themeA));
        colorB.Should().Be(Expected(border, themeB));
        colorA.Should().NotBe(colorB);
        // The RGB baked in at load time must never reach the printed pen.
        colorA.Should().NotBe(Color.FromRgb(StaleBaked.R, StaleBaked.G, StaleBaked.B));
    }

    [Fact]
    public void DrawPrintedBorderEdge_KeepsLiteralRgbBorderConstantAcrossThemeChanges()
    {
        var border = new CellBorder(BorderStyle.Thin, new CellColor(0, 112, 192));
        border.ThemeColor.Should().BeNull();

        CapturePrintedBorderColor(border, ThemeWithAccent1(new CellColor(200, 10, 20)))
            .Should().Be(Color.FromRgb(0, 112, 192));
        CapturePrintedBorderColor(border, ThemeWithAccent1(new CellColor(20, 200, 10)))
            .Should().Be(Color.FromRgb(0, 112, 192));
    }

    [Fact]
    public void BlackAndWhitePrinting_StillForcesThemeBackedBordersToBlack()
    {
        // Excel's "Black and white" print option overrides the authored color entirely; resolving the
        // theme must not sneak a colored pen past that override.
        var border = ThemeBackedBorder();
        var theme = ThemeWithAccent1(new CellColor(200, 10, 20));

        CapturePrintedBorderColor(border, theme, blackAndWhite: true)
            .Should().Be(Colors.Black);
    }
}
