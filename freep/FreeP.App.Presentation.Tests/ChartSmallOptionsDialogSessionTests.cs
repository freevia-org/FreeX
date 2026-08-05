using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartSmallOptionsDialogSessionTests
{
    [Fact]
    public void ThreeDViewSession_ProjectsStateAndRejectsInvalidInputWithoutDispatch()
    {
        var chart = new ChartShape
        {
            ThreeDStyle = ChartThreeDStyle.Column,
            BarGapDepthPercent = 150,
            Wireframe = true,
            WireframeSpecified = true,
            View3D = new Chart3DView
            {
                RotationX = 20,
                RotationY = 35,
                Perspective = 45,
                HeightPercent = 110,
                DepthPercent = 120,
                RightAngleAxes = true,
            },
        };
        var session = new Chart3DViewOptionsDialogSession(
            CreateEditor(chart),
            CultureInfo.InvariantCulture);

        session.State.Should().Be(new Chart3DViewOptionsDialogState(
            "20", "35", "45", "110", "120", "150", 1, 1, true));

        var invalid = session.Submit(new Chart3DViewOptionsDialogInput(
            "91", "35", "45", "110", "120", "150", 1, 1));

        invalid.ShouldClose.Should().BeFalse();
        invalid.ValidationMessage.Should().Be(
            "Elevation must be a whole number from -90 to 90, or blank.");
        chart.View3D!.RotationX.Should().Be(20);

        var accepted = session.Submit(new Chart3DViewOptionsDialogInput(
            "25", "40", "50", "115", "125", "175", 2, 0));

        accepted.ShouldClose.Should().BeTrue();
        accepted.Options.Should().Be(new Chart3DViewOptions(
            25, 40, 50, 115, 125, false, null, 175));
        chart.View3D!.RotationX.Should().Be(25);
        chart.View3D.RightAngleAxes.Should().BeFalse();
        chart.WireframeSpecified.Should().BeFalse();
        chart.BarGapDepthPercent.Should().Be(175);
    }

    [Fact]
    public void BubbleSession_OwnsValidationProjectionAndEditorDispatch()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Bubble,
            BubbleScalePercent = 100,
            BubbleSizeRepresents = BubbleSizeRepresentation.Area,
            ShowNegativeBubbles = false,
        };
        var session = new ChartBubbleOptionsDialogSession(
            CreateEditor(chart),
            CultureInfo.InvariantCulture);

        session.State.Should().Be(new ChartBubbleOptionsDialogState("100", 0, false));
        var invalid = session.Submit(new ChartBubbleOptionsDialogInput("301", 1, true));
        invalid.ShouldClose.Should().BeFalse();
        invalid.ValidationMessage.Should().Be(
            "Bubble scale must be a whole number from 0 to 300.");
        chart.BubbleScalePercent.Should().Be(100);
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Area);

        var accepted = session.Submit(new ChartBubbleOptionsDialogInput("225", 1, true));

        accepted.Options.Should().Be(new ChartBubbleOptions(
            225,
            BubbleSizeRepresentation.Width,
            true));
        chart.BubbleScalePercent.Should().Be(225);
        chart.BubbleSizeRepresents.Should().Be(BubbleSizeRepresentation.Width);
        chart.ShowNegativeBubbles.Should().BeTrue();
    }

    [Fact]
    public void PlotStyleSession_ProjectsEnablementAndAppliesSelectedStyles()
    {
        var chart = new ChartShape
        {
            ChartType = ChartType.Scatter,
            ScatterStyle = ScatterStyle.Marker,
            RadarStyle = RadarStyle.Standard,
        };
        var session = new ChartPlotStyleOptionsDialogSession(CreateEditor(chart));

        session.State.Should().Be(new ChartPlotStyleOptionsDialogState(0, 0, true, false));
        var result = session.Submit(new ChartPlotStyleOptionsDialogInput(
            session.FindScatterIndex(ScatterStyle.SmoothMarker),
            session.FindRadarIndex(RadarStyle.Filled)));

        result.Options.Should().Be(new ChartPlotStyleOptions(
            ScatterStyle.SmoothMarker,
            RadarStyle.Filled));
        chart.ScatterStyle.Should().Be(ScatterStyle.SmoothMarker);
        chart.RadarStyle.Should().Be(RadarStyle.Filled);
    }

    [Fact]
    public void ProtectionSession_ProjectsTriStateFlagsAndAppliesThemTogether()
    {
        var chart = new ChartShape();
        var editor = CreateEditor(chart);
        chart.ChartObjectProtected = true;
        chart.ChartDataProtected = false;
        chart.ChartFormattingProtected = null;
        chart.ChartSelectionProtected = true;
        var session = new ChartProtectionOptionsDialogSession(editor);

        session.State.Should().Be(new ChartProtectionOptionsDialogState(1, 2, 0, 1));
        var result = session.Submit(new ChartProtectionOptionsDialogInput(2, 0, 1, 2));

        result.Options.Should().Be(new ChartProtectionOptions(false, null, true, false));
        chart.ChartObjectProtected.Should().BeFalse();
        chart.ChartDataProtected.Should().BeNull();
        chart.ChartFormattingProtected.Should().BeTrue();
        chart.ChartSelectionProtected.Should().BeFalse();
    }

    [Fact]
    public void TextSession_UsesRequestedTargetAndCultureWithoutPartialInvalidDispatch()
    {
        var chart = new ChartShape
        {
            Title = "Revenue",
            TitleStyle = new ChartTextStyle
            {
                FontFamily = "Aptos",
                FontSizePt = 12,
                Bold = true,
                Italic = false,
                Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
            },
        };
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var session = new ChartTextOptionsDialogSession(
            CreateEditor(chart),
            ChartTextTarget.Title,
            french);

        session.State.FontFamilyText.Should().Be("Aptos");
        session.State.FontSizeText.Should().Be("12");
        session.Surface.Title.Should().Contain("Title");

        var invalid = session.Submit(new ChartTextOptionsDialogInput(
            "Calibri", "0,5", 2, 1, "#C00000"));

        invalid.ShouldClose.Should().BeFalse();
        invalid.ValidationMessage.Should().Contain("1 to 400");
        chart.TitleStyle!.FontFamily.Should().Be("Aptos");
        chart.TitleStyle.FontSizePt.Should().Be(12);

        var accepted = session.Submit(new ChartTextOptionsDialogInput(
            "Calibri", "14,5", 2, 1, "#C00000"));

        accepted.Options.Should().BeEquivalentTo(new ChartTextOptions(
            "Calibri",
            14.5,
            false,
            true,
            new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
            ChartTextTarget.Title));
        chart.TitleStyle!.FontFamily.Should().Be("Calibri");
        chart.TitleStyle.FontSizePt.Should().Be(14.5);
        chart.TitleStyle.Bold.Should().BeFalse();
        chart.TitleStyle.Italic.Should().BeTrue();
        chart.TitleStyle.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        chart.TextStyle.Should().BeNull();
    }

    private static EditingSession CreateEditor(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }
}
