using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisSparklinePlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Fact]
    public void BuildCommands_BuildsOneCommandPerDataRow_PlacedRightOfSelection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 3));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: true, SparklineKind.Line);

        commands.Should().HaveCount(2);
    }

    [Fact]
    public void BuildCommands_ReturnsEmpty_ForSingleColumn()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 1));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: false, SparklineKind.Column);

        commands.Should().BeEmpty();
    }

    // freex-sparklines F2: Quick Analysis inserts one sparkline per data row. Excel's own Quick Analysis
    // Sparklines gesture (and FreeX's own multi-cell "Insert Sparklines" dialog path, SparklinePlanner.
    // BuildInsertCommand) assigns every sparkline in the selection one shared, nonzero GroupId so they
    // round-trip through XLSX as a single <x14:sparklineGroup> and stay linked for Group axis scaling /
    // "edit one, propagates to the group" behavior. Before the fix each row's command hard-used the
    // single-arg AddSparklineCommand constructor, which defaults GroupId to 0 -- "ungrouped, becomes its
    // own singleton on save" -- so every row silently became an independent group of one.
    [Fact]
    public void BuildCommands_MultiRowSelection_SharesOneNonzeroGroupIdAcrossAllRows()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 2));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheet.Id, range, hasHeaderRow: false, SparklineKind.Line, sheet.Sparklines);

        commands.Should().HaveCount(3);

        var ctx = new TestCommandContext(workbook);
        foreach (var command in commands)
            command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().HaveCount(3);
        sheet.Sparklines.Select(s => s.GroupId).Distinct().Should().ContainSingle(
            "every sparkline Quick Analysis inserts for one selection must belong to the same group");
        sheet.Sparklines[0].GroupId.Should().NotBe(0,
            "GroupId 0 means ungrouped/singleton on save -- the whole point of the group is a shared nonzero id");
    }

    // Sibling/no-regression: a lone sparkline (single data row, e.g. a 1-row-tall selection with a header)
    // must NOT be forced into a group of one -- that matches Excel and the existing BuildInsertCommand
    // single-member case (SparklinePlanner.cs), which also leaves a solitary sparkline's GroupId at 0.
    [Fact]
    public void BuildCommands_SingleDataRowSelection_LeavesGroupIdZero()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheet.Id, range, hasHeaderRow: true, SparklineKind.Line, sheet.Sparklines);

        commands.Should().HaveCount(1);

        var ctx = new TestCommandContext(workbook);
        commands[0].Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sheet.Sparklines[0].GroupId.Should().Be(0);
    }

    // Regression guard for the allocator wiring: a new Quick Analysis group must not collide with a
    // GroupId already in use on the sheet (e.g. from an earlier "Insert Sparklines" dialog group).
    [Fact]
    public void BuildCommands_MultiRowSelection_AllocatesGroupIdPastExistingGroups()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 10, 1),
                new CellAddress(sheet.Id, 10, 2)),
            Location = new CellAddress(sheet.Id, 10, 3),
            Kind = SparklineKind.Line,
            GroupId = 5
        });

        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheet.Id, range, hasHeaderRow: false, SparklineKind.Column, sheet.Sparklines);

        commands.Should().HaveCount(2);

        var ctx = new TestCommandContext(workbook);
        foreach (var command in commands)
            command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Where(s => s.Kind == SparklineKind.Column)
            .Select(s => s.GroupId)
            .Distinct()
            .Should().ContainSingle().Which.Should().Be(6);
    }
}
