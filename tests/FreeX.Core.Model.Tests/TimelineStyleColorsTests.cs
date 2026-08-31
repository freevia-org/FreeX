using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Verifies <see cref="TimelineStyleColors.Resolve"/> maps the built-in timeline style family
/// (TimeSlicerStyleLight1..6) to theme-derived header/track/selection colors so timelines
/// theme like Excel. Excel's mapping is Light2→Accent2, …, Light6→Accent6 (a uniform +1 shift
/// from style number to slot). In the fixture workbook (slicer_timeline_001) the style is
/// TimeSlicerStyleLight2, which maps to Accent2 = orange RGB(233,113,50).
/// </summary>
public sealed class TimelineStyleColorsTests
{
    private static WorkbookTheme ThemeWith(
        CellColor accent1,
        CellColor accent2,
        CellColor accent5,
        CellColor accent6)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>
        {
            [WorkbookThemeColorSlot.Dark1] = new(0, 0, 0),
            [WorkbookThemeColorSlot.Light1] = CellColor.White,
            [WorkbookThemeColorSlot.Accent1] = accent1,
            [WorkbookThemeColorSlot.Accent2] = accent2,
            [WorkbookThemeColorSlot.Accent3] = new(2, 2, 2),
            [WorkbookThemeColorSlot.Accent4] = new(3, 3, 3),
            [WorkbookThemeColorSlot.Accent5] = accent5,
            [WorkbookThemeColorSlot.Accent6] = accent6,
        };
        return WorkbookTheme.Office with { Colors = colors };
    }

    private static WorkbookTheme SimpleTheme()
        => ThemeWith(
            accent1: new CellColor(0x15, 0x60, 0x82),  // teal (Accent1)
            accent2: new CellColor(0xE9, 0x71, 0x32),  // orange (Accent2) — matches fixture
            accent5: new CellColor(0xA0, 0x2B, 0x93),
            accent6: new CellColor(0x4E, 0xA7, 0x2E));

    [Fact]
    public void Resolve_NullOrLight1_UsesNeutralGrayHeader_NotAnAccent()
    {
        var theme = SimpleTheme();

        var defaultColors = TimelineStyleColors.Resolve(null, theme);
        var light1 = TimelineStyleColors.Resolve("TimeSlicerStyleLight1", theme);

        // Light1 (and the null/unknown default) is the neutral gray look, not an accent-colored band.
        defaultColors.Should().Be(light1);
        light1.Header.Should().Be(new CellColor(245, 245, 245));
        light1.HeaderText.Should().Be(new CellColor(64, 64, 64));
        // Selection band still uses accent1 tint so "selected" reads.
        light1.SelectionBand.Should().NotBe(CellColor.White);
    }

    [Fact]
    public void Resolve_Light2_BorderAndSelectionBandAreAccent2_HeaderIsWhite()
    {
        // Excel: TimeSlicerStyleLight2 → Accent2. The header background is WHITE with a dark caption;
        // the accent colour is used for the outer border and the selection band.
        var accent2 = new CellColor(0xE9, 0x71, 0x32);  // orange in the test theme (matches fixture)
        var theme = SimpleTheme();

        var colors = TimelineStyleColors.Resolve("TimeSlicerStyleLight2", theme);

        colors.Border.Should().Be(accent2, "Light2 outer border uses Accent2");
        colors.SelectionBand.Should().Be(accent2, "Light2 selection band uses Accent2 (accent, not a tint)");
        colors.Header.Should().Be(CellColor.White, "Light2 header background is white, not accent-filled");
        colors.HeaderText.Should().Be(new CellColor(64, 64, 64), "Light2 caption text is dark, not white");
        colors.Track.Should().Be(new CellColor(217, 217, 217), "Track is neutral light grey regardless of accent");
    }

    [Fact]
    public void Resolve_Light6_BorderIsAccent6_AndDiffersFromLight2()
    {
        var accent6 = new CellColor(0x4E, 0xA7, 0x2E);
        var theme = SimpleTheme();

        var light6 = TimelineStyleColors.Resolve("TimeSlicerStyleLight6", theme);
        var light2 = TimelineStyleColors.Resolve("TimeSlicerStyleLight2", theme);

        light6.Border.Should().Be(accent6, "Light6 maps to theme Accent6");
        light6.Border.Should().NotBe(light2.Border, "Light2 and Light6 must theme distinctly");
        // Both use white headers (accent-colored outer border only).
        light6.Header.Should().Be(CellColor.White);
    }

    [Fact]
    public void Resolve_UnknownStyle_FallsBackToNeutralDefault()
    {
        var theme = SimpleTheme();

        var unknown = TimelineStyleColors.Resolve("TimeSlicerStyleOther1", theme);

        unknown.Should().Be(TimelineStyleColors.Resolve("TimeSlicerStyleLight1", theme));
    }

    [Fact]
    public void Resolve_Light2_SummaryLabelIsAccent2()
    {
        // The summary date label (e.g. "Feb – Apr 2026") should be rendered in the accent colour
        // matching Excel's orange label for TimeSlicerStyleLight2.
        var accent2 = new CellColor(0xE9, 0x71, 0x32);
        var theme = SimpleTheme();

        var colors = TimelineStyleColors.Resolve("TimeSlicerStyleLight2", theme);

        colors.SummaryLabel.Should().Be(accent2, "Summary label uses the accent colour for visibility");
    }

    [Fact]
    public void Resolve_TrimsExactFamilyButRejectsSlicerFamilyAndWrongCase()
    {
        var theme = SimpleTheme();
        var light1 = TimelineStyleColors.Resolve("TimeSlicerStyleLight1", theme);

        TimelineStyleColors.Resolve(" \tTimeSlicerStyleLight2\r\n", theme).Border
            .Should().Be(theme.GetColor(WorkbookThemeColorSlot.Accent2));
        TimelineStyleColors.Resolve("SlicerStyleLight2", theme).Should().Be(light1);
        TimelineStyleColors.Resolve("timeSlicerStyleLight2", theme).Should().Be(light1);
    }

    [Fact]
    public void Resolve_NullTheme_ThrowsBeforeStyleResolution()
    {
        var act = () => TimelineStyleColors.Resolve("TimeSlicerStyleLight2", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
