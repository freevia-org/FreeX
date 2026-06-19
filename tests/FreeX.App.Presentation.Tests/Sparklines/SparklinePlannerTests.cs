using FluentAssertions;
using FreeX.App.Presentation.SparklineUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklinePlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    [Fact]
    public void Catalog_ExposesAllKindsAndToggles()
    {
        SparklinePlanner.Kinds.Should().Equal(SparklineKind.Line, SparklineKind.Column, SparklineKind.WinLoss);
        SparklinePlanner.PointToggles.Should().HaveCount(Enum.GetValues<SparklinePointToggle>().Length);
    }

    [Theory]
    [InlineData(SparklinePointToggle.Markers, SparklineKind.Line, true)]
    [InlineData(SparklinePointToggle.Markers, SparklineKind.Column, false)]
    [InlineData(SparklinePointToggle.NegativePoints, SparklineKind.Line, false)]
    [InlineData(SparklinePointToggle.NegativePoints, SparklineKind.WinLoss, true)]
    [InlineData(SparklinePointToggle.HighPoint, SparklineKind.Column, true)]
    public void IsToggleApplicable_GatesMarkersAndNegativesByKind(
        SparklinePointToggle toggle, SparklineKind kind, bool expected)
    {
        SparklinePlanner.IsToggleApplicable(toggle, kind).Should().Be(expected);
    }

    [Fact]
    public void ValidateInsert_AcceptsValidRangeAndLocation()
    {
        var result = SparklinePlanner.ValidateInsert("A1:E1", "F1", Sheet, out var range, out var location);

        result.Should().Be(SparklineInputValidation.Valid);
        range.CellCount.Should().Be(5);
        location.Should().Be(CellAddress.Parse("F1", Sheet));
    }

    [Fact]
    public void ValidateInsert_RejectsBadDataRangeThenBadLocation()
    {
        SparklinePlanner.ValidateInsert("not-a-range", "F1", Sheet, out _, out _)
            .Should().Be(SparklineInputValidation.InvalidDataRange);

        SparklinePlanner.ValidateInsert("A1:E1", "not-a-cell", Sheet, out _, out _)
            .Should().Be(SparklineInputValidation.InvalidLocation);
    }

    [Fact]
    public void TryParseDataRange_RejectsOversizedRange()
    {
        var oversized = $"A1:A{SparklineRangeLimits.MaxDataCellCount + 1}";
        SparklinePlanner.TryParseDataRange(oversized, Sheet, out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSettings_ClearsFlagsNotApplicableToKind()
    {
        var column = SparklinePlanner.BuildSettings(
            SparklineKind.Column,
            showMarkers: true,
            showHighPoint: true,
            showLowPoint: false,
            showFirstPoint: false,
            showLastPoint: false,
            showNegativePoints: true,
            seriesColor: new CellColor(1, 2, 3));
        column.ShowMarkers.Should().BeFalse("markers apply only to line sparklines");
        column.ShowNegativePoints.Should().BeTrue();
        column.SeriesColor.Should().Be(new CellColor(1, 2, 3));

        var line = SparklinePlanner.BuildSettings(
            SparklineKind.Line,
            showMarkers: true,
            showHighPoint: false,
            showLowPoint: false,
            showFirstPoint: false,
            showLastPoint: false,
            showNegativePoints: true,
            seriesColor: null);
        line.ShowMarkers.Should().BeTrue();
        line.ShowNegativePoints.Should().BeFalse("negative-point emphasis applies only to column/win-loss");
    }

    [Fact]
    public void GetToggle_ReflectsSettingsSnapshot()
    {
        var settings = new SparklineSettings(
            SparklineKind.Line, ShowMarkers: true, ShowHighPoint: false, ShowLowPoint: true,
            ShowFirstPoint: false, ShowLastPoint: true, ShowNegativePoints: false, SeriesColor: null);

        SparklinePlanner.GetToggle(settings, SparklinePointToggle.Markers).Should().BeTrue();
        SparklinePlanner.GetToggle(settings, SparklinePointToggle.HighPoint).Should().BeFalse();
        SparklinePlanner.GetToggle(settings, SparklinePointToggle.LastPoint).Should().BeTrue();
    }
}
