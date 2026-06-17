using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidatePlannerTests
{
    private static ConsolidateCellValue Num(double value) => ConsolidateCellValue.FromNumber(value);

    private static ConsolidateCellValue Label(string text) => ConsolidateCellValue.FromLabel(text);

    private static ConsolidateCellValue Blank => ConsolidateCellValue.Blank;

    private static ConsolidateSource Source(params ConsolidateCellValue[][] rows) =>
        new(rows.Select(r => (IReadOnlyList<ConsolidateCellValue>)r).ToList());

    private static double NumberAt(ConsolidateResult result, int row, int col) =>
        result.Cells.Single(c => c.Row == row && c.Column == col && c.IsNumber).Number!.Value;

    private static string LabelAt(ConsolidateResult result, int row, int col) =>
        result.Cells.Single(c => c.Row == row && c.Column == col && c.IsLabel).Text!;

    // ---- By position ----

    [Fact]
    public void ByPosition_Sum_AddsElementWiseAcrossSources()
    {
        var a = Source(
            [Num(1), Num(2)],
            [Num(3), Num(4)]);
        var b = Source(
            [Num(10), Num(20)],
            [Num(30), Num(40)]);
        var c = Source(
            [Num(100), Num(200)],
            [Num(300), Num(400)]);

        var result = ConsolidatePlanner.Plan([a, b, c], new ConsolidateOptions { Function = ConsolidateFunction.Sum });

        result.RowCount.Should().Be(2);
        result.ColumnCount.Should().Be(2);
        NumberAt(result, 0, 0).Should().Be(111);
        NumberAt(result, 0, 1).Should().Be(222);
        NumberAt(result, 1, 0).Should().Be(333);
        NumberAt(result, 1, 1).Should().Be(444);
        result.Cells.Should().OnlyContain(cell => cell.IsNumber);
    }

    [Fact]
    public void ByPosition_Average_AveragesNumericCellsOnly()
    {
        var a = Source([Num(2), Num(4)]);
        var b = Source([Num(4), Blank]);
        var c = Source([Num(6), Num(8)]);

        var result = ConsolidatePlanner.Plan([a, b, c], new ConsolidateOptions { Function = ConsolidateFunction.Average });

        NumberAt(result, 0, 0).Should().Be(4); // (2+4+6)/3
        NumberAt(result, 0, 1).Should().Be(6); // (4+8)/2 — blank excluded
    }

    [Fact]
    public void ByPosition_Max_TakesMaximum()
    {
        var a = Source([Num(5), Num(-3)]);
        var b = Source([Num(2), Num(-1)]);

        var result = ConsolidatePlanner.Plan([a, b], new ConsolidateOptions { Function = ConsolidateFunction.Max });

        NumberAt(result, 0, 0).Should().Be(5);
        NumberAt(result, 0, 1).Should().Be(-1);
    }

    [Fact]
    public void ByPosition_TwoSources_AlignedByIndex()
    {
        var a = Source([Num(1)], [Num(2)], [Num(3)]);
        var b = Source([Num(10)], [Num(20)], [Num(30)]);

        var result = ConsolidatePlanner.Plan([a, b], new ConsolidateOptions { Function = ConsolidateFunction.Sum });

        result.RowCount.Should().Be(3);
        result.ColumnCount.Should().Be(1);
        NumberAt(result, 0, 0).Should().Be(11);
        NumberAt(result, 1, 0).Should().Be(22);
        NumberAt(result, 2, 0).Should().Be(33);
    }

    // ---- Each function (by position, single cell) ----

    [Theory]
    [InlineData(ConsolidateFunction.Sum, 30)]
    [InlineData(ConsolidateFunction.Average, 10)]
    [InlineData(ConsolidateFunction.Max, 15)]
    [InlineData(ConsolidateFunction.Min, 5)]
    [InlineData(ConsolidateFunction.Product, 750)]
    [InlineData(ConsolidateFunction.Count, 3)]
    [InlineData(ConsolidateFunction.CountNumbers, 3)]
    public void EachFunction_OverThreeNumbers_MatchesCoreSemantics(ConsolidateFunction function, double expected)
    {
        // 5, 10, 15
        var a = Source([Num(5)]);
        var b = Source([Num(10)]);
        var c = Source([Num(15)]);

        var result = ConsolidatePlanner.Plan([a, b, c], new ConsolidateOptions { Function = function });

        NumberAt(result, 0, 0).Should().Be(expected);
    }

    [Fact]
    public void StdDev_And_Var_MatchSampleAndPopulationFormulas()
    {
        // values 2, 4, 4, 4, 5, 5, 7, 9 across eight single-cell sources
        var values = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
        var sources = values.Select(v => Source([Num(v)])).Cast<ConsolidateSource>().ToList();

        var varp = ConsolidatePlanner.Plan(sources, new ConsolidateOptions { Function = ConsolidateFunction.Varp });
        var var = ConsolidatePlanner.Plan(sources, new ConsolidateOptions { Function = ConsolidateFunction.Var });
        var stdevp = ConsolidatePlanner.Plan(sources, new ConsolidateOptions { Function = ConsolidateFunction.StdDevp });
        var stdev = ConsolidatePlanner.Plan(sources, new ConsolidateOptions { Function = ConsolidateFunction.StdDev });

        NumberAt(varp, 0, 0).Should().BeApproximately(4.0, 1e-9);        // population variance
        NumberAt(stdevp, 0, 0).Should().BeApproximately(2.0, 1e-9);     // population stddev
        NumberAt(var, 0, 0).Should().BeApproximately(32.0 / 7.0, 1e-9); // sample variance
        NumberAt(stdev, 0, 0).Should().BeApproximately(Math.Sqrt(32.0 / 7.0), 1e-9);
    }

    [Fact]
    public void Count_CountsNonEmpty_Including_Labels_While_CountNumbers_CountsOnlyNumbers()
    {
        var a = Source([Num(1)]);
        var b = Source([Label("note")]);
        var c = Source([Blank]);

        var count = ConsolidatePlanner.Plan([a, b, c], new ConsolidateOptions { Function = ConsolidateFunction.Count });
        var countNumbers = ConsolidatePlanner.Plan([a, b, c], new ConsolidateOptions { Function = ConsolidateFunction.CountNumbers });

        NumberAt(count, 0, 0).Should().Be(2);        // number + label, blank excluded
        NumberAt(countNumbers, 0, 0).Should().Be(1); // only the number
    }

    [Theory]
    [InlineData(ConsolidateFunction.Sum)]
    [InlineData(ConsolidateFunction.Average)]
    [InlineData(ConsolidateFunction.Max)]
    [InlineData(ConsolidateFunction.Min)]
    [InlineData(ConsolidateFunction.Product)]
    [InlineData(ConsolidateFunction.CountNumbers)]
    [InlineData(ConsolidateFunction.StdDev)]
    [InlineData(ConsolidateFunction.StdDevp)]
    [InlineData(ConsolidateFunction.Var)]
    [InlineData(ConsolidateFunction.Varp)]
    public void NoNumericValues_YieldsZero(ConsolidateFunction function)
    {
        var a = Source([Blank]);
        var b = Source([Label("x")]);

        var result = ConsolidatePlanner.Plan([a, b], new ConsolidateOptions { Function = function });

        NumberAt(result, 0, 0).Should().Be(0);
    }

    [Fact]
    public void SampleVariance_WithSingleNumber_ReturnsZero()
    {
        var a = Source([Num(42)]);

        var var = ConsolidatePlanner.Plan([a], new ConsolidateOptions { Function = ConsolidateFunction.Var });
        var stdev = ConsolidatePlanner.Plan([a], new ConsolidateOptions { Function = ConsolidateFunction.StdDev });

        NumberAt(var, 0, 0).Should().Be(0);   // n-1 == 0 -> guarded to 0
        NumberAt(stdev, 0, 0).Should().Be(0);
    }

    [Fact]
    public void ByPosition_BlankAndNonNumeric_AreSkippedInSum()
    {
        var a = Source([Num(10), Label("hi")]);
        var b = Source([Blank, Num(5)]);

        var result = ConsolidatePlanner.Plan([a, b], new ConsolidateOptions { Function = ConsolidateFunction.Sum });

        NumberAt(result, 0, 0).Should().Be(10); // blank skipped
        NumberAt(result, 0, 1).Should().Be(5);  // label skipped
    }

    [Fact]
    public void SingleSource_ByPosition_ReturnsAggregateOfThatSource()
    {
        var a = Source(
            [Num(1), Num(2)],
            [Num(3), Num(4)]);

        var result = ConsolidatePlanner.Plan([a], new ConsolidateOptions { Function = ConsolidateFunction.Sum });

        NumberAt(result, 0, 0).Should().Be(1);
        NumberAt(result, 1, 1).Should().Be(4);
    }

    // ---- Empty / edge cases ----

    [Fact]
    public void NoSources_ReturnsEmpty()
    {
        var result = ConsolidatePlanner.Plan([], new ConsolidateOptions());

        result.Should().BeSameAs(ConsolidateResult.Empty);
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void EmptySources_ByPosition_ReturnsEmpty()
    {
        var empty = new ConsolidateSource([]);

        var result = ConsolidatePlanner.Plan([empty], new ConsolidateOptions());

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Plan_NullArguments_Throw()
    {
        var act1 = () => ConsolidatePlanner.Plan(null!, new ConsolidateOptions());
        var act2 = () => ConsolidatePlanner.Plan([], null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    // ---- By labels ----

    [Fact]
    public void ByLabels_UnionOfRowAndColumnLabels_InFirstAppearanceOrder()
    {
        // Source A: rows {North, South} x cols {Q1, Q2}
        var a = Source(
            [Blank,         Label("Q1"), Label("Q2")],
            [Label("North"), Num(1),      Num(2)],
            [Label("South"), Num(3),      Num(4)]);

        // Source B: rows {South, East} x cols {Q2, Q3} — overlaps South/Q2, adds East and Q3
        var b = Source(
            [Blank,          Label("Q2"), Label("Q3")],
            [Label("South"), Num(40),     Num(50)],
            [Label("East"),  Num(60),     Num(70)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        // Column labels: Q1 (from A), Q2 (from A), Q3 (from B)
        LabelAt(result, 0, 1).Should().Be("Q1");
        LabelAt(result, 0, 2).Should().Be("Q2");
        LabelAt(result, 0, 3).Should().Be("Q3");
        // Row labels: North, South (from A), East (from B)
        LabelAt(result, 1, 0).Should().Be("North");
        LabelAt(result, 2, 0).Should().Be("South");
        LabelAt(result, 3, 0).Should().Be("East");

        // Corner blank
        result.Cells.Should().Contain(c => c.Row == 0 && c.Column == 0 && c.IsBlank);

        // North/Q1 = 1 (only A), North/Q2 = 2, North/Q3 = 0 (absent)
        NumberAt(result, 1, 1).Should().Be(1);
        NumberAt(result, 1, 2).Should().Be(2);
        NumberAt(result, 1, 3).Should().Be(0);
        // South/Q1 = 3, South/Q2 = 4 + 40 = 44, South/Q3 = 50
        NumberAt(result, 2, 1).Should().Be(3);
        NumberAt(result, 2, 2).Should().Be(44);
        NumberAt(result, 2, 3).Should().Be(50);
        // East/Q1 = 0, East/Q2 = 60, East/Q3 = 70
        NumberAt(result, 3, 1).Should().Be(0);
        NumberAt(result, 3, 2).Should().Be(60);
        NumberAt(result, 3, 3).Should().Be(70);
    }

    [Fact]
    public void ByLabels_LeftColumnOnly_UsesPositionColumnLabels()
    {
        var a = Source(
            [Label("Alpha"), Num(1), Num(2)],
            [Label("Beta"),  Num(3), Num(4)]);
        var b = Source(
            [Label("Beta"),  Num(30), Num(40)],
            [Label("Gamma"), Num(5),  Num(6)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseLeftColumnLabels = true
            });

        // No top-row labels -> rows offset by 0, single left label column.
        result.ColumnCount.Should().Be(3); // 1 label col + 2 position cols
        LabelAt(result, 0, 0).Should().Be("Alpha");
        LabelAt(result, 1, 0).Should().Be("Beta");
        LabelAt(result, 2, 0).Should().Be("Gamma");

        // Alpha: 1,2 ; Beta: (3+30),(4+40) ; Gamma: 5,6
        NumberAt(result, 0, 1).Should().Be(1);
        NumberAt(result, 0, 2).Should().Be(2);
        NumberAt(result, 1, 1).Should().Be(33);
        NumberAt(result, 1, 2).Should().Be(44);
        NumberAt(result, 2, 1).Should().Be(5);
        NumberAt(result, 2, 2).Should().Be(6);
    }

    [Fact]
    public void ByLabels_TopRowOnly_UsesPositionRowLabels()
    {
        var a = Source(
            [Label("X"), Label("Y")],
            [Num(1),     Num(2)],
            [Num(3),     Num(4)]);
        var b = Source(
            [Label("Y"), Label("Z")],
            [Num(20),    Num(30)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true
            });

        // Columns union: X, Y, Z
        LabelAt(result, 0, 0).Should().Be("X");
        LabelAt(result, 0, 1).Should().Be("Y");
        LabelAt(result, 0, 2).Should().Be("Z");

        // Position rows: "Row 1" from A body row 0 and B body row 0; A also has "Row 2".
        // Row 1: X=1 (A), Y=2 (A)+20 (B), Z=30 (B)
        NumberAt(result, 1, 0).Should().Be(1);
        NumberAt(result, 1, 1).Should().Be(22);
        NumberAt(result, 1, 2).Should().Be(30);
        // Row 2: only A -> X=3, Y=4, Z=0
        NumberAt(result, 2, 0).Should().Be(3);
        NumberAt(result, 2, 1).Should().Be(4);
        NumberAt(result, 2, 2).Should().Be(0);
    }

    [Fact]
    public void ByLabels_HeaderDedupIsCaseInsensitive_FirstAppearanceCasingWins()
    {
        // Faithful to the desktop hosts: header de-duplication folds case (so "Total"/"TOTAL" and
        // "Apple"/"apple" each collapse to a single header in first-appearance casing), but the bucket that
        // backs a cell is keyed by the exact label text. A differently-cased label therefore lands in its
        // own bucket and is not surfaced by the single rendered cell.
        var a = Source(
            [Blank,          Label("Total")],
            [Label("Apple"), Num(10)]);
        var b = Source(
            [Blank,          Label("TOTAL")],
            [Label("apple"), Num(5)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        // Headers collapsed to one row and one column, keeping the first source's casing.
        result.RowCount.Should().Be(2);
        result.ColumnCount.Should().Be(2);
        LabelAt(result, 0, 1).Should().Be("Total");
        LabelAt(result, 1, 0).Should().Be("Apple");
        // The rendered cell reflects only the ("Apple","Total") bucket — matching host behavior.
        NumberAt(result, 1, 1).Should().Be(10);
    }

    [Fact]
    public void ByLabels_SameCasing_AggregatesAcrossSources()
    {
        var a = Source(
            [Blank,          Label("Total")],
            [Label("Apple"), Num(10)]);
        var b = Source(
            [Blank,          Label("Total")],
            [Label("Apple"), Num(5)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        NumberAt(result, 1, 1).Should().Be(15);
    }

    [Fact]
    public void ByLabels_Average_AggregatesMatchingCellsAcrossSources()
    {
        var a = Source(
            [Blank,        Label("V")],
            [Label("Key"), Num(10)]);
        var b = Source(
            [Blank,        Label("V")],
            [Label("Key"), Num(20)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Average,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        NumberAt(result, 1, 1).Should().Be(15); // (10+20)/2
    }

    [Fact]
    public void ByLabels_NumericLabel_MatchedByDisplayText()
    {
        // Row labels are numbers (e.g. year identifiers); they should match by their text.
        var a = Source(
            [Blank,                                        Label("Sales")],
            [ConsolidateCellValue.FromNumber(2024, "2024"), Num(100)]);
        var b = Source(
            [Blank,                                        Label("Sales")],
            [ConsolidateCellValue.FromNumber(2024, "2024"), Num(50)]);

        var result = ConsolidatePlanner.Plan(
            [a, b],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        result.RowCount.Should().Be(2); // single merged row label
        LabelAt(result, 1, 0).Should().Be("2024");
        NumberAt(result, 1, 1).Should().Be(150);
    }

    [Fact]
    public void ByLabels_SingleSource_ProducesItsOwnLabelledGrid()
    {
        var a = Source(
            [Blank,          Label("Jan"), Label("Feb")],
            [Label("North"), Num(1),       Num(2)],
            [Label("South"), Num(3),       Num(4)]);

        var result = ConsolidatePlanner.Plan(
            [a],
            new ConsolidateOptions
            {
                Function = ConsolidateFunction.Sum,
                UseTopRowLabels = true,
                UseLeftColumnLabels = true
            });

        result.RowCount.Should().Be(3);
        result.ColumnCount.Should().Be(3);
        NumberAt(result, 1, 1).Should().Be(1);
        NumberAt(result, 2, 2).Should().Be(4);
    }
}
