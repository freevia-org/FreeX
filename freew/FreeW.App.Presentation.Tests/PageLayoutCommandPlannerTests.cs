using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PageLayoutCommandPlannerTests
{
    [Theory]
    [InlineData(PageColumnPreset.One, 1)]
    [InlineData(PageColumnPreset.Two, 2)]
    [InlineData(PageColumnPreset.Three, 3)]
    [InlineData(PageColumnPreset.Left, 2)]
    [InlineData(PageColumnPreset.Right, 2)]
    public void Column_presets_apply_and_report_checked_state(PageColumnPreset preset, int count)
    {
        var page = new PageSettings();

        PageLayoutCommandPlanner.ApplyColumnPreset(page, preset);

        page.ColumnCount.Should().Be(count);
        PageLayoutCommandPlanner.IsColumnPresetChecked(page, preset).Should().BeTrue();
    }

    [Fact]
    public void Page_quick_actions_preserve_orientation_and_expected_geometry()
    {
        var page = new PageSettings();

        PageLayoutCommandPlanner.ToggleOrientation(page);
        PageLayoutCommandPlanner.ApplyPaperSize(page, PagePaperSizePreset.A4);
        PageLayoutCommandPlanner.ApplyMarginPreset(page, PageMarginPreset.Wide);

        page.Landscape.Should().BeTrue();
        page.WidthPt.Should().BeGreaterThan(page.HeightPt);
        page.MarginLeftPt.Should().Be(108);
        page.MarginTopPt.Should().Be(72);
    }

    [Fact]
    public void Line_number_and_hyphenation_results_apply_all_backed_fields()
    {
        var page = new PageSettings();

        PageLayoutCommandPlanner.ApplyLineNumberOptions(
            page,
            new LineNumberOptionsDialogResult(7, 3, LineNumberMode.RestartEachPage));
        PageLayoutCommandPlanner.ApplyHyphenationOptions(
            page,
            new HyphenationOptionsDialogResult(true, 18, 2, false));

        page.LineNumberStartAt.Should().Be(7);
        page.LineNumberCountBy.Should().Be(3);
        page.LineNumberMode.Should().Be(LineNumberMode.RestartEachPage);
        page.AutoHyphenation.Should().BeTrue();
        page.HyphenationZonePt.Should().Be(18);
        page.ConsecutiveHyphenLimit.Should().Be(2);
        page.DoNotHyphenateCaps.Should().BeTrue();
    }

    [Fact]
    public void Line_number_quick_action_cycles_through_each_section_mode()
    {
        var page = new PageSettings();

        PageLayoutCommandPlanner.CycleLineNumberMode(page);
        page.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        PageLayoutCommandPlanner.CycleLineNumberMode(page);
        page.LineNumberMode.Should().Be(LineNumberMode.RestartEachPage);
        PageLayoutCommandPlanner.CycleLineNumberMode(page);
        page.LineNumberMode.Should().Be(LineNumberMode.RestartEachSection);
        PageLayoutCommandPlanner.CycleLineNumberMode(page);
        page.LineNumberMode.Should().Be(LineNumberMode.None);
    }

    [Fact]
    public void Drop_cap_result_clamps_WPF_authoritative_values()
    {
        var result = DropCapOptionsDialogPlanner.BuildResult(
            new DropCapOptionsDialogInput(
                (int)DropCapDialogPosition.InMargin,
                "Georgia",
                "99",
                "-4"),
            System.Globalization.CultureInfo.InvariantCulture);

        result.Position.Should().Be(DropCapDialogPosition.InMargin);
        result.LinesToDrop.Should().Be(10);
        result.DistanceFromTextPt.Should().Be(0);
        result.SizePt.Should().Be(144);
    }
}
