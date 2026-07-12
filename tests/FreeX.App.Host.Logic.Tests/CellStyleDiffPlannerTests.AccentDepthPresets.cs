using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CellStyleDiffPlannerTests
{
    [Fact]
    public void CellStylePreset_AccentDepthPresets_DifferByFillColorAndUseReadableFontAndBorder()
    {
        var presets = AccentDepthPresetData()
            .Select(row => (CellStylePreset)row[0])
            .ToArray();

        var diffs = presets.Select(CellStyleDiffPlanner.GetCellStylePresetDiff).ToList();

        diffs.Select(diff => diff.FillThemeColor).Should().OnlyHaveUniqueItems();
        diffs.Should().AllSatisfy(diff =>
        {
            diff.FillColor.Should().BeNull();
            diff.FillThemeColor.Should().NotBeNull();
            diff.FontColor.Should().Be(CellColor.Black);
            diff.BorderBottom.Should().NotBeNull();
            diff.BorderBottom!.Value.Style.Should().Be(BorderStyle.Thin);
        });
    }

    [Theory]
    [MemberData(nameof(AccentDepthPresetData))]
    public void CellStylePreset_AccentDepthPresets_ResolveFromWorkbookTheme(
        CellStylePreset preset,
        WorkbookThemeColorSlot slot,
        double expectedTint)
    {
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(100, 150, 200))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(40, 80, 120))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(20, 60, 100))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(10, 50, 90))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(80, 20, 100))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(30, 120, 60));

        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(preset, theme);

        diff.FillColor.Should().BeNull();
        diff.FillThemeColor.Should().Be(new WorkbookThemeColorReference(slot, expectedTint));
        diff.FillThemeColor!.Value.Resolve(theme).Should().Be(theme.ResolveColor(slot, expectedTint));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, theme.GetColor(slot)));
        diff.FontColor.Should().Be(CellColor.Black);
    }

    public static IEnumerable<object[]> AccentDepthPresetData()
    {
        yield return [CellStylePreset.Accent1_20, WorkbookThemeColorSlot.Accent1, 0.8];
        yield return [CellStylePreset.Accent2_20, WorkbookThemeColorSlot.Accent2, 0.8];
        yield return [CellStylePreset.Accent3_20, WorkbookThemeColorSlot.Accent3, 0.8];
        yield return [CellStylePreset.Accent4_20, WorkbookThemeColorSlot.Accent4, 0.8];
        yield return [CellStylePreset.Accent5_20, WorkbookThemeColorSlot.Accent5, 0.8];
        yield return [CellStylePreset.Accent6_20, WorkbookThemeColorSlot.Accent6, 0.8];
        yield return [CellStylePreset.Accent1_40, WorkbookThemeColorSlot.Accent1, 0.6];
        yield return [CellStylePreset.Accent2_40, WorkbookThemeColorSlot.Accent2, 0.6];
        yield return [CellStylePreset.Accent3_40, WorkbookThemeColorSlot.Accent3, 0.6];
        yield return [CellStylePreset.Accent4_40, WorkbookThemeColorSlot.Accent4, 0.6];
        yield return [CellStylePreset.Accent5_40, WorkbookThemeColorSlot.Accent5, 0.6];
        yield return [CellStylePreset.Accent6_40, WorkbookThemeColorSlot.Accent6, 0.6];
        yield return [CellStylePreset.Accent1_60, WorkbookThemeColorSlot.Accent1, 0.4];
        yield return [CellStylePreset.Accent2_60, WorkbookThemeColorSlot.Accent2, 0.4];
        yield return [CellStylePreset.Accent3_60, WorkbookThemeColorSlot.Accent3, 0.4];
        yield return [CellStylePreset.Accent4_60, WorkbookThemeColorSlot.Accent4, 0.4];
        yield return [CellStylePreset.Accent5_60, WorkbookThemeColorSlot.Accent5, 0.4];
        yield return [CellStylePreset.Accent6_60, WorkbookThemeColorSlot.Accent6, 0.4];
    }
}
