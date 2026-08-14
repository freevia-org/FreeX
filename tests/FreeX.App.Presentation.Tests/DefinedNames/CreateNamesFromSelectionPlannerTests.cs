using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class CreateNamesFromSelectionPlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    // Builds a cellText accessor from a dense grid whose [0,0] cell sits at (startRow, startCol).
    private static Func<CellAddress, string?> Grid(uint startRow, uint startCol, string?[][] rows) =>
        addr =>
        {
            var r = (int)(addr.Row - startRow);
            var c = (int)(addr.Col - startCol);
            if (r < 0 || r >= rows.Length)
                return null;
            var row = rows[r];
            return c < 0 || c >= row.Length ? null : row[c];
        };

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(Sheet, r1, c1), new CellAddress(Sheet, r2, c2));

    [Fact]
    public void TryCreateOptions_RejectsNoSelectedEdges()
    {
        var ok = CreateNamesFromSelectionPlanner.TryCreateOptions(
            useTopRow: false,
            useLeftColumn: false,
            useBottomRow: false,
            useRightColumn: false,
            out var options,
            out var error);

        ok.Should().BeFalse();
        options.HasAnyEdge.Should().BeFalse();
        error.Should().Be(CreateNamesFromSelectionInputError.NoSelectedEdge);
    }

    [Fact]
    public void TryCreateOptions_ReturnsSelectedEdges()
    {
        var ok = CreateNamesFromSelectionPlanner.TryCreateOptions(
            useTopRow: false,
            useLeftColumn: true,
            useBottomRow: false,
            useRightColumn: true,
            out var options,
            out var error);

        ok.Should().BeTrue();
        error.Should().Be(CreateNamesFromSelectionInputError.None);
        options.Should().Be(new CreateNamesFromSelectionOptions(
            UseTopRow: false,
            UseLeftColumn: true,
            UseBottomRow: false,
            UseRightColumn: true));
    }

    [Fact]
    public void Plan_NoEdges_ReturnsEmpty()
    {
        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 3, 2),
            new CreateNamesFromSelectionOptions(false, false, false, false),
            _ => "Label");

        plan.Should().BeEmpty();
    }

    [Fact]
    public void Plan_TopRow_NamesEachColumnBelowItsHeader()
    {
        // Rows 1..3, Cols A..B. Top row = headers.
        var grid = Grid(1, 1,
        [
            ["Sales", "Cost"],
            ["10", "4"],
            ["20", "6"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 3, 2),
            new CreateNamesFromSelectionOptions(UseTopRow: true, false, false, false),
            grid);

        plan.Select(p => p.Name).Should().Equal("Sales", "Cost");
        plan.Should().OnlyContain(p => p.Edge == CreateNamesLabelEdge.TopRow);

        // "Sales" (col A) refers to rows below the header: A2:A3.
        var sales = plan.Single(p => p.Name == "Sales");
        sales.Range.Start.Should().Be(new CellAddress(Sheet, 2, 1));
        sales.Range.End.Should().Be(new CellAddress(Sheet, 3, 1));
    }

    [Fact]
    public void Plan_LeftColumn_NamesEachRowToTheRightOfItsLabel()
    {
        var grid = Grid(1, 1,
        [
            ["North", "10", "20"],
            ["South", "30", "40"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 2, 3),
            new CreateNamesFromSelectionOptions(false, UseLeftColumn: true, false, false),
            grid);

        plan.Select(p => p.Name).Should().Equal("North", "South");
        plan.Should().OnlyContain(p => p.Edge == CreateNamesLabelEdge.LeftColumn);

        // "North" (row 1) refers to the cells to the right of the label: B1:C1.
        var north = plan.Single(p => p.Name == "North");
        north.Range.Start.Should().Be(new CellAddress(Sheet, 1, 2));
        north.Range.End.Should().Be(new CellAddress(Sheet, 1, 3));
    }

    [Fact]
    public void Plan_TopRowAndLeftColumn_ProducesBothSets()
    {
        var grid = Grid(1, 1,
        [
            ["", "Spring", "Summer"],
            ["North", "10", "20"],
            ["South", "30", "40"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 3, 3),
            new CreateNamesFromSelectionOptions(UseTopRow: true, UseLeftColumn: true, false, false),
            grid);

        // Top row contributes Spring, Summer (the empty corner cell yields nothing);
        // left column contributes North, South (its corner cell is also empty).
        plan.Select(p => p.Name).Should().BeEquivalentTo(["Spring", "Summer", "North", "South"]);
    }

    [Fact]
    public void Plan_BottomRow_NamesColumnsAboveTheFooter()
    {
        var grid = Grid(1, 1,
        [
            ["10", "20"],
            ["30", "40"],
            ["Total1", "Total2"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 3, 2),
            new CreateNamesFromSelectionOptions(false, false, UseBottomRow: true, false),
            grid);

        plan.Select(p => p.Name).Should().Equal("Total1", "Total2");
        var total1 = plan.Single(p => p.Name == "Total1");
        total1.Range.Start.Should().Be(new CellAddress(Sheet, 1, 1));
        total1.Range.End.Should().Be(new CellAddress(Sheet, 2, 1));
    }

    [Fact]
    public void Plan_RightColumn_NamesRowsLeftOfTheLabel()
    {
        var grid = Grid(1, 1,
        [
            ["10", "20", "RowA"],
            ["30", "40", "RowB"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 2, 3),
            new CreateNamesFromSelectionOptions(false, false, false, UseRightColumn: true),
            grid);

        plan.Select(p => p.Name).Should().Equal("RowA", "RowB");
        var rowA = plan.Single(p => p.Name == "RowA");
        rowA.Range.Start.Should().Be(new CellAddress(Sheet, 1, 1));
        rowA.Range.End.Should().Be(new CellAddress(Sheet, 1, 2));
    }

    [Fact]
    public void Plan_SingleColumnSelection_LeftColumnYieldsNothing()
    {
        // ColCount == 1, so left/right column edges produce no names.
        var grid = Grid(1, 1, [["Only"], ["1"]]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 2, 1),
            new CreateNamesFromSelectionOptions(false, UseLeftColumn: true, false, false),
            grid);

        plan.Should().BeEmpty();
    }

    [Fact]
    public void Plan_BlankLabelsAreSkipped()
    {
        var grid = Grid(1, 1,
        [
            ["Sales", "", "Cost"],
            ["1", "2", "3"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 2, 3),
            new CreateNamesFromSelectionOptions(UseTopRow: true, false, false, false),
            grid);

        plan.Select(p => p.Name).Should().Equal("Sales", "Cost");
    }

    [Fact]
    public void Plan_SanitizesIllegalLabelCharacters()
    {
        var grid = Grid(1, 1,
        [
            ["Net Sales!", "1"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 1, 2),
            new CreateNamesFromSelectionOptions(false, UseLeftColumn: true, false, false),
            grid);

        // Spaces and '!' collapse to underscores, runs collapse, trailing underscore trimmed.
        plan.Single().Name.Should().Be("Net_Sales");
    }

    [Fact]
    public void Plan_NumericLabelGetsUnderscorePrefix()
    {
        var grid = Grid(1, 1,
        [
            ["2024", "1"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 1, 2),
            new CreateNamesFromSelectionOptions(false, UseLeftColumn: true, false, false),
            grid);

        plan.Single().Name.Should().Be("_2024");
    }

    [Fact]
    public void Plan_DeduplicatesRepeatedLabels()
    {
        var grid = Grid(1, 1,
        [
            ["Dup", "Dup"],
            ["1", "2"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 2, 2),
            new CreateNamesFromSelectionOptions(UseTopRow: true, false, false, false),
            grid);

        plan.Select(p => p.Name).Should().Equal("Dup", "Dup_2");
    }

    [Fact]
    public void Plan_AvoidsCollisionWithExistingNames()
    {
        var grid = Grid(1, 1,
        [
            ["Sales", "1"]
        ]);

        var plan = CreateNamesFromSelectionPlanner.Plan(
            Range(1, 1, 1, 2),
            new CreateNamesFromSelectionOptions(false, UseLeftColumn: true, false, false),
            grid,
            existingNames: ["Sales"]);

        plan.Single().Name.Should().Be("Sales_2");
    }
}
