using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class TableFormulaDialogPlannerTests
{
    [Fact]
    public void Catalogs_ExposeWordFormulaDialogChoicesInDisplayOrder()
    {
        TableFormulaDialogPlanner.Functions.Should()
            .Equal("SUM", "AVERAGE", "COUNT", "PRODUCT", "MIN", "MAX");
        TableFormulaDialogPlanner.NumberFormats.Should()
            .Equal("", "0", "0.00", "#,##0", "#,##0.00", "0%", "$#,##0.00;($#,##0.00)");
    }

    [Fact]
    public void BuildInitialState_UsesSumAboveWhenNumericCellsAreAbove()
    {
        var table = Grid(["10"], ["20"], [""]);

        var state = TableFormulaDialogPlanner.BuildInitialState(table, rowIndex: 2, columnIndex: 0);

        state.FormulaText.Should().Be(TableFormulaDialogPlanner.SumAboveFormula);
        state.NumberFormatIndex.Should().Be(0);
    }

    [Fact]
    public void BuildInitialState_UsesSumLeftWhenOnlyLeftCellsAreNumeric()
    {
        var table = Grid(["10", "20", ""]);

        var state = TableFormulaDialogPlanner.BuildInitialState(table, rowIndex: 0, columnIndex: 2);

        state.FormulaText.Should().Be(TableFormulaDialogPlanner.SumLeftFormula);
    }

    [Fact]
    public void BuildInitialState_PrefersSumAboveWhenBothDirectionsContainNumbers()
    {
        var table = Grid(["", "5"], ["3", ""]);

        var state = TableFormulaDialogPlanner.BuildInitialState(table, rowIndex: 1, columnIndex: 1);

        state.FormulaText.Should().Be(TableFormulaDialogPlanner.SumAboveFormula);
    }

    [Fact]
    public void BuildInitialState_FallsBackToSumAboveWhenNoNeighborNumbersExist()
    {
        var table = Grid(["Header"], [""]);

        var state = TableFormulaDialogPlanner.BuildInitialState(table, rowIndex: 1, columnIndex: 0);

        state.FormulaText.Should().Be(TableFormulaDialogPlanner.SumAboveFormula);
    }

    [Fact]
    public void PasteFunction_AddsEqualsAndParksCaretInsideParentheses()
    {
        var result = TableFormulaDialogPlanner.PasteFunction("1+", "average");

        result.Text.Should().Be("=1+AVERAGE()");
        result.CaretIndex.Should().Be(result.Text.Length - 1);
    }

    [Fact]
    public void TryBuildResult_TrimsFormulaAndBlankFormatToModelField()
    {
        var input = new TableFormulaDialogInput(" =SUM(LEFT) ", "  ");

        TableFormulaDialogPlanner.TryBuildResult(input, out var result, out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Expression.Should().Be("=SUM(LEFT)");
        result.NumberFormat.Should().BeNull();
    }

    [Fact]
    public void TryBuildResult_TrimsCustomNumberFormat()
    {
        var input = new TableFormulaDialogInput("=SUM(ABOVE)", " #,##0.00 ");

        TableFormulaDialogPlanner.TryBuildResult(input, out var result, out _)
            .Should().BeTrue();

        result!.NumberFormat.Should().Be("#,##0.00");
    }

    [Fact]
    public void TryBuildResult_RejectsEmptyFormulaWithPreservedMessage()
    {
        var input = new TableFormulaDialogInput("   ", "#,##0");

        TableFormulaDialogPlanner.TryBuildResult(input, out var result, out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(TableFormulaDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void Session_OwnsCatalogMutationAndAcceptance()
    {
        var session = new TableFormulaDialogSession(
            new TableFormulaDialogInitialState("=SUM(ABOVE)", 3));

        session.InitialState.NumberFormatIndex.Should().Be(3);
        session.Functions.Should().Equal(TableFormulaDialogPlanner.Functions);
        session.NumberFormats.Should().Equal(TableFormulaDialogPlanner.NumberFormats);
        session.PasteFunction("1+", "average").Should().Be(
            new TableFormulaPasteResult("=1+AVERAGE()", 11));
        session.PlanAcceptance(new TableFormulaDialogInput("   ", "0"))
            .ValidationMessage.Should().Be(TableFormulaDialogPlanner.ValidationMessage);
        session.PlanAcceptance(new TableFormulaDialogInput(" =SUM(LEFT) ", " #,##0 "))
            .Result.Should().Be(new TableFormulaField("=SUM(LEFT)", "#,##0"));
    }

    private static Table Grid(params string[][] rows)
    {
        var table = new Table();
        foreach (var cells in rows)
        {
            var row = new TableRow();
            foreach (var text in cells)
                row.Cells.Add(new TableCell(text));
            table.Rows.Add(row);
        }

        return table;
    }
}

public sealed class TableFormulaDialogSessionOwnershipTests
{
    [Theory]
    [InlineData("FreeW.App.Host")]
    [InlineData("FreeW.App.Avalonia")]
    public void RenderersDelegateFormulaLifetimeToSession(string project)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", project,
            project.EndsWith("Host", StringComparison.Ordinal) ? "TableFormulaDialog.cs" : "TableDialogs.cs"));

        source.Should().Contain("TableFormulaDialogSession");
        source.Should().Contain("_session.NumberFormats");
        source.Should().Contain("_session.Functions");
        source.Should().Contain("_session.PasteFunction(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("TableFormulaDialogPlanner.TryBuildResult(");
        source.Should().NotContain("TableFormulaDialogPlanner.PasteFunction(");
    }
}
