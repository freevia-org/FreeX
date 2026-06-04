using FluentAssertions;
using FreeX.Core.Model;
using System.Globalization;
using System.IO;

namespace FreeX.App.UI.Tests;

public sealed class ChartDataLabelFormatterTests
{
    [Fact]
    public void FormatDataLabel_CombinesSeriesCategoryAndFormattedValue()
    {
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Sales; Q1; $1,234.50");
    }

    [Fact]
    public void FormatDataLabel_UsesInvariantNumberCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var chart = new ChartModel { DataLabelNumberFormat = ChartDataLabelNumberFormat.Number };

            ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
                .Should().Be("1234.50");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void FormatDataLabel_OmitsValueWhenShowValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon
        };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Sales; Q1");
    }

    [Fact]
    public void FormatDataLabel_OmitsValueLeavingSingleCategoryName()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true
        };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("Q1");
    }

    [Fact]
    public void FormatDataLabel_FallsBackToValueWhenNoContentEnabled()
    {
        var chart = new ChartModel { ShowDataLabelValue = false };

        ChartDataLabelFormatter.FormatDataLabel(chart, "Sales", "Q1", 1234.5)
            .Should().Be("1234.5");
    }

    [Fact]
    public void GetPieLabelFormat_OmitsValuePlaceholderWhenShowValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be("{1}");
    }

    [Fact]
    public void GetPieLabelFormat_KeepsPercentageWhenValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabelValue = false,
            ShowDataLabelPercentage = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be($"{{1}}{Environment.NewLine}{{2:0%}}");
    }

    [Fact]
    public void ShouldUseNativeValueLabels_FalseWhenValueDisabled()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true
        };

        ChartDataLabelFormatter.ShouldUseNativeValueLabels(chart).Should().BeFalse();
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart).Should().BeTrue();
    }

    [Fact]
    public void FormatDataLabel_AssemblesAnnotationTextWithoutListOrJoin()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.FindWithFailureMessage(
            "Unable to locate workspace file",
            "src", "FreeX.App.UI", "ChartDataLabelFormatter.cs"));
        var formatDataLabel = source[
            source.IndexOf("public static string FormatDataLabel", StringComparison.Ordinal)..
            source.IndexOf("public static string GetPieLabelFormat", StringComparison.Ordinal)];

        formatDataLabel.Should().Contain("var valueText = hasValue ? FormatLabelValue(chart, value) : \"\";");
        formatDataLabel.Should().Contain("return (hasSeriesName, hasCategoryName, hasValue) switch");
        formatDataLabel.Should().NotContain("new List<string>");
        formatDataLabel.Should().NotContain("parts.Add(");
        formatDataLabel.Should().NotContain("string.Join(");
    }

    [Fact]
    public void GetNativeValueLabelFormat_ReturnsNullWhenAnnotationLabelsAreRequired()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number
        };

        ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1).Should().BeNull();
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart).Should().BeTrue();
    }

    [Fact]
    public void GetPieLabelFormat_UsesSeriesCategoryAndPercentagePlaceholder()
    {
        var chart = new ChartModel
        {
            ShowDataLabelSeriesName = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        ChartDataLabelFormatter.GetPieLabelFormat(chart, "Share")
            .Should().Be($"Share{Environment.NewLine}{{1}}{Environment.NewLine}{{2:0%}}");
    }
}
