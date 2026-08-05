using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideSizeDialogSessionTests
{
    [Fact]
    public void Constructor_ProjectsInitialPresetAndRendererSelectionIndex()
    {
        var session = new SlideSizeDialogSession(
            MakeEditor(12_192_000L, 6_858_000L),
            culture: CultureInfo.InvariantCulture);

        session.InitialState.Preset.Should().Be(SlideSizeDialogPreset.Widescreen169);
        session.InitialPresetIndex.Should().Be(1);
        session.InitialState.Display.WidthText.Should().Be("13.333");
        SlideSizeDialogSession.PresetNames.Should().Equal(
            "Standard (4:3)",
            "Widescreen (16:9)",
            "Custom");
    }

    [Theory]
    [InlineData(-1, SlideSizeDialogPreset.Standard43)]
    [InlineData(0, SlideSizeDialogPreset.Standard43)]
    [InlineData(1, SlideSizeDialogPreset.Widescreen169)]
    [InlineData(2, SlideSizeDialogPreset.Custom)]
    [InlineData(99, SlideSizeDialogPreset.Standard43)]
    public void PresetFromIndex_NormalizesRendererSelection(
        int selectedIndex,
        SlideSizeDialogPreset expected)
    {
        SlideSizeDialogSession.PresetFromIndex(selectedIndex).Should().Be(expected);
        SlideSizeDialogSession.PresetIndex(expected).Should().BeInRange(0, 2);
    }

    [Fact]
    public void SelectPresetAndChangeUnit_ShareDisplayTransitions()
    {
        var session = new SlideSizeDialogSession(
            MakeEditor(9_144_000L, 6_858_000L),
            culture: CultureInfo.InvariantCulture);

        var widescreen = session.SelectPreset(1);
        var centimeters = session.ChangeUnit(
            widescreen!.WidthText,
            widescreen.HeightText,
            SlideSizeDialogUnit.Centimeters);

        centimeters.WidthText.Should().Be("33.87");
        centimeters.HeightText.Should().Be("19.05");
        centimeters.UnitLabel.Should().Be("cm");
        session.Unit.Should().Be(SlideSizeDialogUnit.Centimeters);
        session.SelectPreset(2).Should().BeNull();
    }

    [Fact]
    public void TryApply_ExposesValidationThenAppliesValidResult()
    {
        var editor = MakeEditor(9_144_000L, 6_858_000L);
        var session = new SlideSizeDialogSession(
            editor,
            culture: CultureInfo.InvariantCulture);

        session.TryApply("0.25", "7.5").Should().BeFalse();
        session.LastResultPlan!.Validation!.Message
            .Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
        session.LastResultPlan.Validation.FocusField
            .Should().Be(SlideSizeDialogField.Width);
        editor.Presentation.SlideSizeCxEmu.Should().Be(9_144_000L);

        session.TryApply("11", "6.25").Should().BeTrue();
        session.LastResultPlan!.ShouldApply.Should().BeTrue();
        editor.Presentation.SlideSizeCxEmu.Should().Be(10_058_400L);
        editor.Presentation.SlideSizeCyEmu.Should().Be(5_715_000L);
    }

    [Fact]
    public void SetInputUnit_ChangesUnitWithoutConvertingTestOrBoundInput()
    {
        var session = new SlideSizeDialogSession(
            MakeEditor(9_144_000L, 6_858_000L),
            culture: CultureInfo.InvariantCulture);

        var display = session.SetInputUnit(
            "25.4",
            "19.05",
            SlideSizeDialogUnit.Centimeters);

        display.Should().Be(new SlideSizeDialogDisplayState("25.4", "19.05", "cm"));
        session.TryParse(display.WidthText, display.HeightText).CxEmu.Should().Be(9_144_000L);
    }

    private static EditingSession MakeEditor(long cxEmu, long cyEmu)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.SlideSizeCxEmu = cxEmu;
        presentation.SlideSizeCyEmu = cyEmu;
        return new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
    }
}
