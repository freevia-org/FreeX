using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartAxisDisplayOptionsDialogSessionTests
{
    [Fact]
    public void AxisSession_ProjectsOptionsAndDispatchesCultureAwareEditAsOneUndoStep()
    {
        var chart = new ChartShape();
        chart.ValueAxis.Title = "Original";
        chart.ValueAxis.Min = 1;
        var editor = CreateEditor(chart);
        var session = new ChartAxisOptionsDialogSession(
            editor,
            ChartAxisKind.Value,
            CultureInfo.GetCultureInfo("fr-FR"));

        session.AxisOptions.Should().Equal("Category axis", "Value axis", "Secondary value axis");
        session.State.AxisIndex.Should().Be(1);
        var result = session.Submit(AxisInput(session.State) with
        {
            Title = "Revenue",
            Minimum = "10,5",
            Maximum = "90,5",
            MajorUnit = "5,5",
            LabelOffset = "35",
            MajorTickMarkIndex = session.FindTickMarkIndex(ChartTickMark.Out),
            CrossingIndex = session.FindCrossingIndex(ChartAxisCrossing.Min),
        });

        result.ShouldClose.Should().BeTrue();
        result.Options!.Minimum.Should().Be(10.5);
        chart.ValueAxis.Title.Should().Be("Revenue");
        chart.ValueAxis.Min.Should().Be(10.5);
        chart.ValueAxis.Max.Should().Be(90.5);
        chart.ValueAxis.MajorUnit.Should().Be(5.5);
        chart.ValueAxis.LabelOffsetPercent.Should().Be(35);
        chart.ValueAxis.MajorTickMark.Should().Be(ChartTickMark.Out);

        editor.Undo();

        chart.ValueAxis.Title.Should().Be("Original");
        chart.ValueAxis.Min.Should().Be(1);
    }

    [Fact]
    public void AxisSession_InvalidNumberReturnsEstablishedMessageWithoutDispatch()
    {
        var chart = new ChartShape();
        chart.ValueAxis.Min = 12;
        var session = new ChartAxisOptionsDialogSession(CreateEditor(chart), culture: CultureInfo.InvariantCulture);

        var result = session.Submit(AxisInput(session.State) with { Minimum = "NaN" });

        result.ShouldClose.Should().BeFalse();
        result.ValidationMessage.Should().Be("Minimum must be a finite number or blank.");
        chart.ValueAxis.Min.Should().Be(12);
    }

    [Fact]
    public void AxisSession_UnchangedProjectionPreservesImportedTokens()
    {
        var importedTitleColor = new ThemeAwareColor(
            SrgbColor.FromRgb(0xC0504D),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2, RoleName = "accent2" });
        var chart = new ChartShape();
        chart.ValueAxis.TitleStyle = new ChartTextStyle { Color = importedTitleColor };
        chart.ValueAxis.RawMajorTickMarkToken = "futureMajor";
        chart.ValueAxis.RawMinorTickMarkToken = "futureMinor";
        chart.ValueAxis.RawTickLabelPositionToken = "futureLabels";
        chart.ValueAxis.RawCrossesToken = "futureCrossing";
        chart.ValueAxis.RawCrossBetweenToken = "futureBetween";
        chart.ValueAxis.RawLabelAlignmentToken = "futureAlignment";
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Unsupported;
        chart.ValueAxis.RawDisplayUnitToken = "futureUnit";
        var session = new ChartAxisOptionsDialogSession(CreateEditor(chart), culture: CultureInfo.InvariantCulture);

        var plan = session.BuildCommitPlan(AxisInput(session.State));

        plan.RawMajorTickMarkToken.Should().Be("futureMajor");
        plan.RawMinorTickMarkToken.Should().Be("futureMinor");
        plan.RawTickLabelPositionToken.Should().Be("futureLabels");
        plan.RawCrossesToken.Should().Be("futureCrossing");
        plan.RawCrossBetweenToken.Should().Be("futureBetween");
        plan.RawLabelAlignmentToken.Should().Be("futureAlignment");
        plan.DisplayUnit.Should().Be(ChartAxisDisplayUnit.Unsupported);
        plan.RawDisplayUnitToken.Should().Be("futureUnit");
        plan.TitleStyle!.Color.Should().BeSameAs(importedTitleColor);
    }

    [Fact]
    public void DisplaySession_ProjectsImportedStyleAndChartSubtypeWithoutMaterializingNullableState()
    {
        var importedLabelColor = new ThemeAwareColor(
            SrgbColor.FromRgb(0x9BBB59),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent3, RoleName = "accent3" });
        var chart = new ChartShape
        {
            ChartType = ChartType.Waterfall,
            StyleId = 777,
            ShowWaterfallConnectorLines = true,
            TitleOverlay = null,
            PlotVisibleOnly = null,
            RoundedCorners = null,
            DataLabels = new ChartDataLabels
            {
                TextStyle = new ChartTextStyle { Color = importedLabelColor },
            },
        };
        var session = new ChartDisplayOptionsDialogSession(
            CreateEditor(chart),
            CultureInfo.InvariantCulture);

        session.StyleOptions[session.State.StyleIndex].Should().Be("Style 777 (imported)");
        session.State.SupportsWaterfallConnectorLines.Should().BeTrue();
        session.State.SupportsHighLowLines.Should().BeFalse();
        session.State.SupportsDropLines.Should().BeFalse();

        var plan = session.BuildCommitPlan(DisplayInput(session.State));

        plan.StyleId.Should().Be(777);
        plan.TitleOverlay.Should().BeNull();
        plan.PlotVisibleOnly.Should().BeNull();
        plan.RoundedCorners.Should().BeNull();
        plan.ShowWaterfallConnectorLines.Should().BeTrue();
        plan.LabelTextStyle!.Color.Should().BeSameAs(importedLabelColor);
    }

    [Fact]
    public void DisplaySession_ParsesAndDispatchesAllSharedInputsWithUndo()
    {
        var chart = new ChartShape { Title = "Original" };
        var editor = CreateEditor(chart);
        var session = new ChartDisplayOptionsDialogSession(
            editor,
            CultureInfo.GetCultureInfo("fr-FR"));

        var result = session.Submit(DisplayInput(session.State) with
        {
            Title = "Revenue",
            LegendIndex = session.FindLegendIndex(LegendPosition.Bottom),
            ShowValueLabels = true,
            LabelPositionIndex = session.FindLabelPositionIndex(DataLabelPosition.InsideEnd),
            LabelFontFamily = "Aptos",
            LabelFontSize = "9,5",
            LabelBold = true,
            LabelColor = "#2F5496",
            BarGapWidth = "125",
            BarOverlap = "-25",
            DisplayBlanksIndex = session.FindDisplayBlanksIndex(ChartDisplayBlanksAs.Zero),
            VaryColors = true,
        });

        result.ShouldClose.Should().BeTrue();
        chart.Title.Should().Be("Revenue");
        chart.Legend.Should().Be(LegendPosition.Bottom);
        chart.DataLabels!.ShowValue.Should().BeTrue();
        chart.DataLabels.Position.Should().Be(DataLabelPosition.InsideEnd);
        chart.DataLabels.TextStyle!.FontSizePt.Should().Be(9.5);
        chart.BarGapWidthPercent.Should().Be(125);
        chart.BarOverlapPercent.Should().Be(-25);
        chart.DisplayBlanksAs.Should().Be(ChartDisplayBlanksAs.Zero);
        chart.VaryColors.Should().BeTrue();

        editor.Undo();

        chart.Title.Should().Be("Original");
        chart.DataLabels.Should().BeNull();
    }

    [Fact]
    public void DisplaySession_InvalidPercentReturnsEstablishedMessageWithoutDispatch()
    {
        var chart = new ChartShape { BarGapWidthPercent = 80 };
        var session = new ChartDisplayOptionsDialogSession(
            CreateEditor(chart),
            CultureInfo.InvariantCulture);

        var result = session.Submit(DisplayInput(session.State) with { BarGapWidth = "501" });

        result.ShouldClose.Should().BeFalse();
        result.ValidationMessage.Should().Be(
            "Bar gap width must be a whole number from 0 to 500, or blank.");
        chart.BarGapWidthPercent.Should().Be(80);
    }

    private static ChartAxisOptionsDialogInput AxisInput(ChartAxisOptionsDialogState state) => new(
        state.AxisIndex,
        state.Title,
        state.TitleFontFamily,
        state.TitleFontSizeText,
        state.TitleColor,
        state.TitleBold,
        state.TitleItalic,
        state.ShowAxis,
        state.MinimumText,
        state.MaximumText,
        state.MajorUnitText,
        state.MinorUnitText,
        state.NumberFormatCode,
        state.DisplayUnitIndex,
        state.CustomDisplayUnitText,
        state.MajorGridlines,
        state.MinorGridlines,
        state.MajorTickMarkIndex,
        state.MinorTickMarkIndex,
        state.TickLabelPositionIndex,
        state.CrossingIndex,
        state.CrossesAtText,
        state.CrossBetweenIndex,
        state.LabelAlignmentIndex,
        state.LabelOffsetText,
        state.MultiLevelLabelsIndex,
        state.AutoCrossingIndex,
        state.ReverseOrder);

    private static ChartDisplayOptionsDialogInput DisplayInput(ChartDisplayOptionsDialogState state) => new(
        state.Title,
        state.TitleOverlay,
        state.PlotVisibleOnly,
        state.RoundedCorners,
        state.StyleIndex,
        state.LegendIndex,
        state.ShowValueLabels,
        state.ShowPercentLabels,
        state.ShowCategoryLabels,
        state.ShowSeriesLabels,
        state.ShowLegendKeys,
        state.ShowBubbleSize,
        state.ShowLeaderLines,
        state.LabelNumberFormat,
        state.LabelSeparator,
        state.LabelFontFamily,
        state.LabelFontSizeText,
        state.LabelBold,
        state.LabelItalic,
        state.LabelColor,
        state.LabelPositionIndex,
        state.CategoryGridlines,
        state.ValueGridlines,
        state.BarGapWidthText,
        state.BarOverlapText,
        state.DisplayBlanksIndex,
        state.ShowDataLabelsOverMaximum,
        state.VaryColors,
        state.LegendOverlay,
        state.HighLowLines,
        state.WaterfallConnectorLines,
        state.DropLines,
        state.UpDownBars,
        state.SeriesLines);

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
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }
}
