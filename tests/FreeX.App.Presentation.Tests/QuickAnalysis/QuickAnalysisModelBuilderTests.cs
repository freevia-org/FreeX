using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisModelBuilderTests
{
    private static readonly SheetId Sheet = new(Guid.NewGuid());

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(Sheet, startRow, startCol), new CellAddress(Sheet, endRow, endCol));

    private static QuickAnalysisSelectionDescription Selection(
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        bool hasHeaderRow,
        params QuickAnalysisColumnKind[] columnKinds) =>
        new(Range(startRow, startCol, endRow, endCol), hasHeaderRow, columnKinds);

    private static QuickAnalysisColumnKind[] Numeric(int count) =>
        Enumerable.Repeat(QuickAnalysisColumnKind.Numeric, count).ToArray();

    // ── Degenerate selections ──────────────────────────────────────────────

    [Fact]
    public void SingleCell_ProducesNoSuggestions()
    {
        var selection = Selection(1, 1, 1, 1, hasHeaderRow: false, QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.IsEmpty.Should().BeTrue();
        model.Groups.Should().BeEmpty();
        model.AllSuggestions().Should().BeEmpty();
    }

    [Fact]
    public void EmptyRange_ProducesNoSuggestions()
    {
        // A zero-area description: same start/end but flagged with no column kinds and treated as empty
        // by the model when there are no columns to analyse.
        var selection = new QuickAnalysisSelectionDescription(
            Range(1, 1, 1, 1),
            HasHeaderRow: false,
            ColumnKinds: []);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.IsEmpty.Should().BeTrue();
    }

    // ── Numeric grid: full coverage ────────────────────────────────────────

    [Fact]
    public void NumericGrid_OffersFormattingChartsTotalsTablesAndSparklines()
    {
        var selection = Selection(
            1, 1, 5, 3,
            hasHeaderRow: true,
            Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.HasGroup(QuickAnalysisGroup.Formatting).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Charts).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Totals).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeTrue();
    }

    [Fact]
    public void NumericGrid_GroupsAreInDisplayOrder()
    {
        var selection = Selection(1, 1, 5, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.Groups.Select(g => g.Group).Should().ContainInOrder(
            QuickAnalysisGroup.Formatting,
            QuickAnalysisGroup.Charts,
            QuickAnalysisGroup.Totals,
            QuickAnalysisGroup.Tables,
            QuickAnalysisGroup.Sparklines);
    }

    [Fact]
    public void Formatting_MapsToConditionalFormatRuleTypes()
    {
        var selection = Selection(1, 1, 5, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var formatting = model.SuggestionsFor(QuickAnalysisGroup.Formatting);

        formatting.Should().OnlyContain(s => s.ActionKind == QuickAnalysisActionKind.ConditionalFormat);
        formatting.Select(s => s.ConditionalFormat!.RuleType).Should().Contain(new[]
        {
            CfRuleType.DataBar,
            CfRuleType.ColorScale,
            CfRuleType.IconSet,
            CfRuleType.CellValue,
            CfRuleType.Top10
        });
        formatting.Select(s => s.ConditionalFormat!.FormatKind).Should().Contain(QuickAnalysisFormatKind.DataBars);
    }

    [Fact]
    public void Charts_MapToChartTypes()
    {
        var selection = Selection(1, 1, 5, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var charts = model.SuggestionsFor(QuickAnalysisGroup.Charts);

        charts.Should().OnlyContain(s => s.ActionKind == QuickAnalysisActionKind.InsertChart);
        charts.Select(s => s.Chart!.ChartType).Should().Contain(new[]
        {
            ChartType.Column,
            ChartType.Line,
            ChartType.Bar
        });
    }

    [Fact]
    public void Totals_MapToInsertTotalsActions()
    {
        var selection = Selection(1, 1, 5, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var totals = model.SuggestionsFor(QuickAnalysisGroup.Totals);

        totals.Should().OnlyContain(s => s.ActionKind == QuickAnalysisActionKind.InsertTotals);
        totals.Select(s => s.Total!.Function).Should().Contain(new[]
        {
            QuickAnalysisTotalFunction.Sum,
            QuickAnalysisTotalFunction.Average,
            QuickAnalysisTotalFunction.Count,
            QuickAnalysisTotalFunction.PercentTotal,
            QuickAnalysisTotalFunction.RunningTotal
        });
    }

    [Fact]
    public void Sparklines_MapToLineColumnAndWinLoss()
    {
        var selection = Selection(1, 1, 5, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var sparklines = model.SuggestionsFor(QuickAnalysisGroup.Sparklines);

        sparklines.Should().OnlyContain(s => s.ActionKind == QuickAnalysisActionKind.InsertSparklines);
        sparklines.Select(s => s.Sparkline!.SparklineKind).Should().BeEquivalentTo(new[]
        {
            QuickAnalysisSparklineKind.Line,
            QuickAnalysisSparklineKind.Column,
            QuickAnalysisSparklineKind.WinLoss
        });
    }

    // ── Text-only selection ────────────────────────────────────────────────

    [Fact]
    public void TextOnlyGrid_OffersTablesButNotFormatting()
    {
        var selection = Selection(
            1, 1, 5, 3,
            hasHeaderRow: true,
            QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Formatting).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Charts).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Totals).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeFalse();
    }

    [Fact]
    public void TextOnlyGrid_OffersNoDataBars()
    {
        var selection = Selection(
            1, 1, 5, 3,
            hasHeaderRow: true,
            QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.AllSuggestions()
            .Where(s => s.ConditionalFormat is not null)
            .Should().BeEmpty();
    }

    // ── Single column vs grid ──────────────────────────────────────────────

    [Fact]
    public void SingleNumericColumn_OffersPieButNoSparklines()
    {
        var selection = Selection(
            1, 1, 6, 1,
            hasHeaderRow: false,
            QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);
        var charts = model.SuggestionsFor(QuickAnalysisGroup.Charts);

        charts.Select(s => s.Chart!.ChartType).Should().Contain(ChartType.Pie);
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeFalse();
    }

    [Fact]
    public void MultiNumericColumns_OmitPieAndOfferSparklines()
    {
        var selection = Selection(
            1, 1, 6, 3,
            hasHeaderRow: false,
            Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var charts = model.SuggestionsFor(QuickAnalysisGroup.Charts);

        charts.Select(s => s.Chart!.ChartType).Should().NotContain(ChartType.Pie);
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeTrue();
    }

    [Fact]
    public void SingleColumnWithoutHeader_OffersNoTablesGroup()
    {
        var selection = Selection(
            1, 1, 6, 1,
            hasHeaderRow: false,
            QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);

        // A single column without a header does not look tabular.
        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeFalse();
    }

    [Fact]
    public void SingleColumnWithHeader_LooksTabularButHasNoPivot()
    {
        var selection = Selection(
            1, 1, 6, 1,
            hasHeaderRow: true,
            QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);
        var tables = model.SuggestionsFor(QuickAnalysisGroup.Tables);

        tables.Select(s => s.Table!.TableKind).Should().Contain(QuickAnalysisTableKind.Table);
        tables.Select(s => s.Table!.TableKind).Should().Contain(QuickAnalysisTableKind.PivotTable);
    }

    [Fact]
    public void GridWithoutHeader_OffersTableButNoPivot()
    {
        var selection = Selection(
            1, 1, 6, 3,
            hasHeaderRow: false,
            Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var tables = model.SuggestionsFor(QuickAnalysisGroup.Tables);

        tables.Select(s => s.Table!.TableKind).Should().Contain(QuickAnalysisTableKind.Table);
        tables.Select(s => s.Table!.TableKind).Should().NotContain(QuickAnalysisTableKind.PivotTable);
    }

    [Fact]
    public void StructuredTableSelection_DoesNotOfferFormatAsTableAgain()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "Sales",
            DisplayName = "Sales",
            Range = range,
            HeaderRowCount = 1
        });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        var model = QuickAnalysisModelBuilder.Build(QuickAnalysisSelectionReader.Describe(sheet, range));

        model.AllSuggestions().Should().NotContain(s => s.Id == "table.table");
    }

    [Fact]
    public void PartialStructuredTableOverlap_DoesNotOfferFormatAsTable()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        var tableRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 6,
            Name = "Sales",
            DisplayName = "Sales",
            Range = tableRange,
            HeaderRowCount = 1
        });
        for (uint row = 1; row <= 6; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"R{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }
        var selection = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 6, 3));

        var description = QuickAnalysisSelectionReader.Describe(sheet, selection);
        var model = QuickAnalysisModelBuilder.Build(description);

        description.StructuredTableContext.Should().BeNull();
        description.OverlapsStructuredTable.Should().BeTrue();
        model.AllSuggestions().Should().NotContain(s => s.Id == "table.table");
    }

    [Fact]
    public void SelectionAtLastColumn_OmitsAdjacentTotalsAndSparklines()
    {
        var selection = Selection(
            1, CellAddress.MaxCol - 1, 5, CellAddress.MaxCol,
            hasHeaderRow: false,
            QuickAnalysisColumnKind.Numeric,
            QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.HasGroup(QuickAnalysisGroup.Totals).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Formatting).Should().BeTrue();
    }

    // ── Header detection effect ────────────────────────────────────────────

    [Fact]
    public void HeaderRowConsumingAllDataRows_SuppressesTotalsAndSparklines()
    {
        // Two rows, one of which is the header: only one data row remains. Totals/sparklines still need
        // a data row, but a header that leaves zero data rows must suppress them.
        var selection = Selection(
            1, 1, 1, 3,
            hasHeaderRow: true,
            Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);

        // Single header-only row leaves no data rows.
        model.HasGroup(QuickAnalysisGroup.Totals).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeFalse();
        // But it still looks tabular, so Tables is present.
        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeTrue();
    }

    [Fact]
    public void HeaderFlag_ReducesDataRowCount()
    {
        var withHeader = Selection(1, 1, 5, 2, hasHeaderRow: true, Numeric(2));
        var withoutHeader = Selection(1, 1, 5, 2, hasHeaderRow: false, Numeric(2));

        withHeader.DataRowCount.Should().Be(4);
        withoutHeader.DataRowCount.Should().Be(5);
    }

    [Fact]
    public void HeaderRow_EnablesPivotTableThatGridWithoutHeaderLacks()
    {
        var withHeader = Selection(1, 1, 6, 3, hasHeaderRow: true, Numeric(3));
        var withoutHeader = Selection(1, 1, 6, 3, hasHeaderRow: false, Numeric(3));

        var withHeaderModel = QuickAnalysisModelBuilder.Build(withHeader);
        var withoutHeaderModel = QuickAnalysisModelBuilder.Build(withoutHeader);

        withHeaderModel.SuggestionsFor(QuickAnalysisGroup.Tables)
            .Select(s => s.Table!.TableKind).Should().Contain(QuickAnalysisTableKind.PivotTable);
        withoutHeaderModel.SuggestionsFor(QuickAnalysisGroup.Tables)
            .Select(s => s.Table!.TableKind).Should().NotContain(QuickAnalysisTableKind.PivotTable);
    }

    // ── Mixed text + numeric ───────────────────────────────────────────────

    [Fact]
    public void MixedTextAndNumeric_OffersFormattingAndTotals()
    {
        var selection = Selection(
            1, 1, 6, 2,
            hasHeaderRow: true,
            QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.HasGroup(QuickAnalysisGroup.Formatting).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Totals).Should().BeTrue();
        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeTrue();
        // Only one numeric column → no sparklines, and pie chart is available.
        model.HasGroup(QuickAnalysisGroup.Sparklines).Should().BeFalse();
        model.SuggestionsFor(QuickAnalysisGroup.Charts)
            .Select(s => s.Chart!.ChartType).Should().Contain(ChartType.Pie);
    }

    [Fact]
    public void MultiNumeric_OffersRowOrientedTotals()
    {
        var selection = Selection(1, 1, 6, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var totals = model.SuggestionsFor(QuickAnalysisGroup.Totals);

        totals.Select(s => s.Total!.Orientation).Should().Contain(QuickAnalysisTotalOrientation.Row);
    }

    [Fact]
    public void SingleNumericColumn_OffersOnlyColumnOrientedTotals()
    {
        var selection = Selection(1, 1, 6, 1, hasHeaderRow: false, QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);
        var totals = model.SuggestionsFor(QuickAnalysisGroup.Totals);

        totals.Should().OnlyContain(s => s.Total!.Orientation == QuickAnalysisTotalOrientation.Column);
    }

    // ── Suggestion id stability and grouping ───────────────────────────────

    [Fact]
    public void SuggestionIds_AreUniqueAcrossTheModel()
    {
        var selection = Selection(1, 1, 6, 3, hasHeaderRow: true, Numeric(3));

        var model = QuickAnalysisModelBuilder.Build(selection);
        var ids = model.AllSuggestions().Select(s => s.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Contain("format.databars");
        ids.Should().Contain("chart.clusteredcolumn");
    }

    [Fact]
    public void ToDisplayModel_CarriesSuggestionLabelsRoutesAndPreviewMetadata()
    {
        var selection = Selection(1, 1, 6, 3, hasHeaderRow: true, Numeric(3));

        var displayModel = QuickAnalysisModelBuilder.Build(selection).ToDisplayModel();

        displayModel.Groups.Select(group => group.Group).Should().ContainInOrder(
            QuickAnalysisGroup.Formatting,
            QuickAnalysisGroup.Charts,
            QuickAnalysisGroup.Totals,
            QuickAnalysisGroup.Tables,
            QuickAnalysisGroup.Sparklines);

        var dataBars = displayModel.AllItems().Single(item => item.Id == "format.databars");
        dataBars.Label.Should().Be("Data Bars");
        dataBars.Route.Kind.Should().Be(QuickAnalysisCommandKind.ConditionalFormat);
        dataBars.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.DataBars);

        var rowSum = displayModel.AllItems().Single(item => item.Id == "total.sum.row");
        rowSum.Command.Should().BeNull();
        rowSum.Route.TotalFunction.Should().Be("SUM");
        rowSum.PreviewVisual.Kind.Should().Be(QuickAnalysisPreviewVisualKind.TotalFormula);
    }

    [Fact]
    public void EverySuggestion_PopulatesExactlyTheDescriptorForItsActionKind()
    {
        var selection = Selection(1, 1, 6, 1, hasHeaderRow: true, QuickAnalysisColumnKind.Numeric);

        var model = QuickAnalysisModelBuilder.Build(selection);

        foreach (var suggestion in model.AllSuggestions())
        {
            switch (suggestion.ActionKind)
            {
                case QuickAnalysisActionKind.ConditionalFormat:
                    suggestion.ConditionalFormat.Should().NotBeNull();
                    suggestion.Chart.Should().BeNull();
                    suggestion.Total.Should().BeNull();
                    break;
                case QuickAnalysisActionKind.InsertChart:
                    suggestion.Chart.Should().NotBeNull();
                    suggestion.ConditionalFormat.Should().BeNull();
                    break;
                case QuickAnalysisActionKind.InsertTotals:
                    suggestion.Total.Should().NotBeNull();
                    break;
                case QuickAnalysisActionKind.Table:
                    suggestion.Table.Should().NotBeNull();
                    break;
                case QuickAnalysisActionKind.InsertSparklines:
                    suggestion.Sparkline.Should().NotBeNull();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected action kind {suggestion.ActionKind}.");
            }
        }
    }

    [Fact]
    public void SuggestionsFor_AbsentGroup_ReturnsEmpty()
    {
        var selection = Selection(
            1, 1, 5, 3,
            hasHeaderRow: true,
            QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text, QuickAnalysisColumnKind.Text);

        var model = QuickAnalysisModelBuilder.Build(selection);

        model.SuggestionsFor(QuickAnalysisGroup.Sparklines).Should().BeEmpty();
    }

    [Fact]
    public void Build_NullSelection_Throws()
    {
        var act = () => QuickAnalysisModelBuilder.Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AllEmptyColumns_ProduceTablesOnlyWhenTabular()
    {
        var selection = Selection(
            1, 1, 5, 3,
            hasHeaderRow: false,
            QuickAnalysisColumnKind.Empty, QuickAnalysisColumnKind.Empty, QuickAnalysisColumnKind.Empty);

        var model = QuickAnalysisModelBuilder.Build(selection);

        // No numeric content → no Formatting/Charts/Totals/Sparklines; but a 3-column grid looks tabular.
        model.HasGroup(QuickAnalysisGroup.Formatting).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Charts).Should().BeFalse();
        model.HasGroup(QuickAnalysisGroup.Tables).Should().BeTrue();
    }
}
