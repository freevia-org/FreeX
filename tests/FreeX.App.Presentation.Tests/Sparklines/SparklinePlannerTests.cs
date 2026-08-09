using FluentAssertions;
using FreeX.App.Presentation.SparklineUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklinePlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    [Fact]
    public void SparklineDialogPlanning_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostDialogPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "SparklineDialog.cs");

        File.Exists(Path.Combine(presentationRoot, "SparklineUI", "SparklinePlanner.cs"))
            .Should()
            .BeTrue("sparkline dialog result and validation planning should be shared by renderers");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "SparklineDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared SparklinePlanner instead of carrying a renderer-local facade");
        // Round-8 finding N7: the host now validates through the group-aware entry point so a
        // multi-cell Location Range (Excel's "Insert Sparklines" multi-series dialog) expands into
        // one sparkline per row/column instead of being rejected as a single-cell-only location.
        File.ReadAllText(hostDialogPath)
            .Should()
            .Contain("SparklinePlanner.CreateDialogResult")
            .And
            .Contain("SparklinePlanner.InsertDialogCaptureWidth")
            .And
            .Contain("SparklinePlanner.InsertDialogCaptureHeight")
            .And
            .Contain("SparklinePlanner.CreateRangeSelectionRequest")
            .And
            .Contain("SparklinePlanner.ValidateInsertGroup")
            .And
            .NotContain("public sealed record SparklineDialogResult")
            .And
            .NotContain("public enum SparklineRangeSelectionTarget");
    }

    [Fact]
    public void InsertDialogCaptureSize_MatchesSharedVisualEvidenceContract()
    {
        SparklinePlanner.InsertDialogCaptureWidth.Should().Be(380);
        SparklinePlanner.InsertDialogCaptureHeight.Should().Be(280);
    }

    [Fact]
    public void Catalog_ExposesAllKindsAndToggles()
    {
        SparklinePlanner.Kinds.Should().Equal(SparklineKind.Line, SparklineKind.Column, SparklineKind.WinLoss);
        SparklinePlanner.PointToggles.Should().HaveCount(Enum.GetValues<SparklinePointToggle>().Length);
    }

    [Fact]
    public void CreateDialogResult_TrimsRangeAndLocation()
    {
        SparklinePlanner.CreateDialogResult(" A1:E1 ", " F1 ", SparklineKind.Column)
            .Should()
            .Be(new SparklineDialogResult("A1:E1", "F1", SparklineKind.Column));
    }

    [Fact]
    public void CreateRangeSelectionRequest_TrimsCurrentTextAndRequestsCollapse()
    {
        SparklinePlanner.CreateRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, " A1:E1 ")
            .Should()
            .Be(new SparklineRangeSelectionRequest(SparklineRangeSelectionTarget.DataRange, "A1:E1", CollapseDialog: true));
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
    public void ValidateDialogInputs_UsesSharedInsertParser()
    {
        SparklinePlanner.ValidateDialogInputs("A1:E1", "F1", Sheet)
            .Should().Be(SparklineInputValidation.Valid);
        SparklinePlanner.ValidateDialogInputs("A1:E1", "F1:G1", Sheet)
            .Should().Be(SparklineInputValidation.InvalidLocation);
        SparklinePlanner.ValidateDialogInputs("A1", "F1", Sheet)
            .Should().Be(SparklineInputValidation.InvalidDataRange);
        SparklinePlanner.ValidateDialogInputs("A1:A4097", "F1", Sheet)
            .Should().Be(SparklineInputValidation.InvalidDataRange);
    }

    [Theory]
    [InlineData("$F$1", 1, 6)]
    [InlineData("F$1", 1, 6)]
    [InlineData("$F1", 1, 6)]
    [InlineData("R1C6", 1, 6)]
    public void ValidateInsert_AcceptsSharedCellReferenceForms(string locationText, uint row, uint column)
    {
        SparklinePlanner.ValidateInsert("A1:E1", locationText, Sheet, out _, out var location)
            .Should().Be(SparklineInputValidation.Valid);
        location.Should().Be(new CellAddress(Sheet, row, column));
    }

    [Theory]
    [InlineData("column", SparklineKind.Column)]
    [InlineData("winloss", SparklineKind.WinLoss)]
    [InlineData("line", SparklineKind.Line)]
    [InlineData("anything", SparklineKind.Line)]
    public void ParseKind_MapsToolbarKindText(string input, SparklineKind expected)
    {
        SparklinePlanner.ParseKind(input).Should().Be(expected);
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

    [Fact]
    public void BuildInsertCommand_SingleMemberUsesOptionalRepeatLocationWithoutGrouping()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var member = new SparklineGroupMember(
            Range(sheet, "A1:C1"),
            CellAddress.Parse("D1", sheet.Id));
        var repeatLocation = CellAddress.Parse("E2", sheet.Id);

        var command = SparklinePlanner.BuildInsertCommand(
            sheet.Id,
            [member],
            SparklineKind.Line,
            sheet.Sparklines,
            repeatLocation);

        command.Should().BeOfType<AddSparklineCommand>();
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.Sparklines.Should().ContainSingle();
        sheet.Sparklines[0].Location.Should().Be(repeatLocation);
        sheet.Sparklines[0].GroupId.Should().Be(0);
    }

    [Fact]
    public void BuildInsertCommand_GroupAllocatesOneSharedNonzeroGroupId()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, "A1:C1"),
            Location = CellAddress.Parse("D1", sheet.Id),
            Kind = SparklineKind.Line,
            GroupId = 4
        });
        var members = new[]
        {
            new SparklineGroupMember(Range(sheet, "A2:C2"), CellAddress.Parse("D2", sheet.Id)),
            new SparklineGroupMember(Range(sheet, "A3:C3"), CellAddress.Parse("D3", sheet.Id))
        };

        var command = SparklinePlanner.BuildInsertCommand(
            sheet.Id,
            members,
            SparklineKind.Column,
            sheet.Sparklines);

        command.Should().BeOfType<CompositeWorkbookCommand>()
            .Which.Commands.Should().HaveCount(2);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var inserted = sheet.Sparklines.Skip(1).ToArray();
        inserted.Should().HaveCount(2);
        inserted[0].GroupId.Should().NotBe(0);
        inserted.Select(sparkline => sparkline.GroupId).Should().OnlyContain(groupId => groupId == inserted[0].GroupId);
        inserted.Select(sparkline => sparkline.Location).Should().Equal(members.Select(member => member.Location));
    }

    private static GridRange Range(FreeX.Core.Model.Sheet sheet, string reference)
    {
        var parts = reference.Split(':');
        return new GridRange(
            CellAddress.Parse(parts[0], sheet.Id),
            CellAddress.Parse(parts[^1], sheet.Id));
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public FreeX.Core.Model.Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
