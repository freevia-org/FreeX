using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// CT_CatAx and CT_ValAx are sequences, not bags: c:crossAx precedes c:crosses, the tick block sits
/// between c:numFmt and c:crossAx, and each axis kind owns a different tail (c:crossBetween and the
/// unit elements on CT_ValAx; c:auto/c:lblAlgn/c:lblOffset/c:noMultiLvlLbl on CT_CatAx). Emitting a
/// correct-looking element in the wrong slot — or on the wrong axis kind — makes PowerPoint repair
/// the deck just as surely as an unknown element does.
/// </summary>
public sealed class ChartAxisSchemaOrderTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ValueAxis_EmitsCrossAxBeforeCrossesAndTicksBeforeCrossAx()
    {
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.ValueAxis.MajorTickMark = ChartTickMark.Out;
        chart.ValueAxis.MinorTickMark = ChartTickMark.In;
        chart.ValueAxis.TickLabelPosition = ChartTickLabelPosition.NextTo;
        chart.ValueAxis.NumberFormatCode = "$#,##0";
        chart.ValueAxis.Crosses = ChartAxisCrossing.AutoZero;
        chart.ValueAxis.CrossBetween = ChartCrossBetween.MidCat;
        chart.ValueAxis.MajorUnit = 10;
        chart.ValueAxis.MinorUnit = 5;
        chart.ValueAxis.DisplayUnit = ChartAxisDisplayUnit.Thousands;

        var bytes = WriteDeck(chart);

        ChildNames(Axis(bytes, "valAx")).Should().Equal(
            "axId", "scaling", "delete", "axPos", "majorGridlines", "numFmt",
            "majorTickMark", "minorTickMark", "tickLblPos",
            "crossAx", "crosses", "crossBetween", "majorUnit", "minorUnit", "dispUnits");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void CategoryAxis_EmitsItsOwnTailAfterCrossAxAndCrosses()
    {
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.CategoryAxis.MajorTickMark = ChartTickMark.Out;
        chart.CategoryAxis.TickLabelPosition = ChartTickLabelPosition.NextTo;
        chart.CategoryAxis.Crosses = ChartAxisCrossing.AutoZero;
        chart.CategoryAxis.AutoCrossing = true;
        chart.CategoryAxis.LabelAlignment = ChartLabelAlignment.Center;
        chart.CategoryAxis.LabelOffsetPercent = 100;
        chart.CategoryAxis.NoMultiLevelLabels = true;

        var bytes = WriteDeck(chart);

        ChildNames(Axis(bytes, "catAx")).Should().Equal(
            "axId", "scaling", "delete", "axPos", "majorGridlines",
            "majorTickMark", "tickLblPos",
            "crossAx", "crosses", "auto", "lblAlgn", "lblOffset", "noMultiLvlLbl");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void ValueAxis_DoesNotEmitCategoryOnlyElements()
    {
        // The model can hold these on any axis; only CT_CatAx can express them.
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.ValueAxis.AutoCrossing = false;
        chart.ValueAxis.LabelAlignment = ChartLabelAlignment.Right;
        chart.ValueAxis.LabelOffsetPercent = 35;
        chart.ValueAxis.NoMultiLevelLabels = true;

        var bytes = WriteDeck(chart);

        ChildNames(Axis(bytes, "valAx")).Should()
            .NotContain(new[] { "auto", "lblAlgn", "lblOffset", "noMultiLvlLbl" });
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void CategoryAxis_DoesNotEmitValueOnlyCrossBetween()
    {
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.CategoryAxis.CrossBetween = ChartCrossBetween.Between;

        var bytes = WriteDeck(chart);

        ChildNames(Axis(bytes, "catAx")).Should().NotContain("crossBetween");
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Fact]
    public void SecondaryValueAxis_IsSchemaValid()
    {
        // The secondary valAx is the one that forced c:crosses ("max"), which used to land
        // before c:crossAx and invalidate every combo chart the writer produced.
        var chart = BuildChart(ChartType.ColumnClustered);
        chart.SecondaryValueAxis = new ChartAxis();
        var secondary = new ChartSeries { Name = "Margin", OnSecondaryAxis = true };
        secondary.Values.AddRange(new double?[] { 1, 2, 3, 4 });
        chart.Series.Add(secondary);

        var bytes = WriteDeck(chart);

        ValidateSchema(bytes).Should().BeEmpty();
    }

    private static ChartShape BuildChart(ChartType chartType)
    {
        var chart = new ChartShape { ChartType = chartType, RegenerateWorkbookOnSave = true };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange(new double?[] { 10, 12, 14, 15 });
        chart.Series.Add(series);
        return chart;
    }

    private static XElement Axis(byte[] bytes, string axisName)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.Entries.Single(item =>
            item.FullName.StartsWith("ppt/charts/chart", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream).Root!
            .Element(C + "chart")!
            .Element(C + "plotArea")!
            .Elements(C + axisName)
            .First();
    }

    private static string[] ChildNames(XElement element) =>
        element.Elements().Select(child => child.Name.LocalName).ToArray();

    private static byte[] WriteDeck(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            ExtentCxEmu = 5_000_000,
            ExtentCyEmu = 3_000_000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static string[] ValidateSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var package = PresentationDocument.Open(stream, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }
}
