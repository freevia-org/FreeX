using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-theme-border-color-F2: the Format Cells ▸ Border tab read <see cref="CellBorder.Color"/> —
/// the RGB baked in at load time — into both the per-edge color boxes (<c>PopulateBorder</c>) and the
/// shared Line Color box. For a theme-backed border on a workbook whose theme had since changed, the
/// dialog therefore showed a color that disagreed with what the grid was painting for that same edge.
/// </summary>
public sealed class R176_FormatCellsDialogThemeBorderColorTests
{
    // Deliberately unlike the stock Office Accent1 (21, 96, 130) so "followed the new theme" and
    // "kept the load-time baked color" can never be confused for one another.
    private static readonly CellColor SwappedAccent1 = new(7, 200, 111);

    [Fact]
    public void BorderTab_ThemeBackedBorder_ShowsSwappedThemeColor_NotTheBakedOne()
    {
        StaTestRunner.Run(() =>
        {
            var border = ThemedBorder();
            var bakedIn = border.Color;
            var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

            // Ground truth is the model's own resolver, not a hard-coded RGB.
            var expected = border.ResolveColor(theme);
            expected.Should().NotBe(bakedIn,
                "the swapped accent must actually differ from the baked color, or this test proves nothing");

            var dialog = ShowBorderDialog(border, theme);
            try
            {
                var expectedText = ColorInputParser.FormatRgbColor(expected);
                var bakedText = ColorInputParser.FormatRgbColor(bakedIn);

                BoxText(dialog, "DlgBorderBottomColorBox").Should().Be(expectedText,
                    "the per-edge color box must show the color the grid actually paints for that edge");
                BoxText(dialog, "DlgBorderLineColorBox").Should().Be(expectedText,
                    "the shared Line Color box seeds from the same edge and must agree with it");
                BoxText(dialog, "DlgBorderBottomColorBox").Should().NotBe(bakedText);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void BorderTab_ExplicitRgbBorder_ShowsItsOwnColorAcrossThemeChange()
    {
        StaTestRunner.Run(() =>
        {
            // No-regression sibling: a border with no ThemeColor is pinned to its authored RGB.
            var explicitColor = new CellColor(200, 0, 0);
            var theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

            var dialog = ShowBorderDialog(new CellBorder(BorderStyle.Thin, explicitColor), theme);
            try
            {
                BoxText(dialog, "DlgBorderBottomColorBox")
                    .Should().Be(ColorInputParser.FormatRgbColor(explicitColor),
                        "an explicitly colored border does not follow the theme and must keep its authored RGB");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static CellBorder ThemedBorder() =>
        new(
            BorderStyle.Thin,
            WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1),
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));

    private static FormatCellsDialog ShowBorderDialog(CellBorder border, WorkbookTheme theme)
    {
        var dialog = new FormatCellsDialog(
            new CellStyle { BorderBottom = border },
            theme,
            FormatCellsDialogTab.Border);
        dialog.Show();
        DispatcherTestPump.PumpDispatcher();
        return dialog;
    }

    private static string BoxText(FormatCellsDialog dialog, string name) =>
        DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, name).Text;
}
