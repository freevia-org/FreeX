using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PivotUiPlannerTests
{
    [Fact]
    public void FieldCaption_FallsBackToOneBasedColumnNameWhenHeaderMissing()
    {
        PivotUiPlanner.FieldCaption(["Region", "Amount"], 1).Should().Be("Amount");
        PivotUiPlanner.FieldCaption(["Region"], 2).Should().Be("Column 3");
    }

    [Fact]
    public void FindFieldIndexes_SearchesHeadersAndDataFieldsCaseInsensitively()
    {
        var pivot = CreatePivot();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotUiPlanner.FindSourceFieldIndex(["Region", "Quarter", "Amount"], "quarter").Should().Be(1);
        PivotUiPlanner.FindDataFieldIndex(pivot, "sum OF amount").Should().Be(0);
        PivotUiPlanner.FindFieldSourceIndex(["Region"], pivot, "Sum of Amount").Should().Be(2);
    }

    [Fact]
    public void ResolvePivotChartFieldButtonCaption_UsesValuesAxisPageOrDataFallback()
    {
        var pivot = CreatePivot();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.PageFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        var headers = new[] { "Region", "Quarter", "Channel", "Amount" };

        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Values").Should().Be("Sum of Amount");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Axis Fields").Should().Be("Region");
        PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivot, headers, "Legend").Should().Be("Channel");
    }

    [Fact]
    public void FindPivotTableForSelection_PrefersContainingPivotAndFallsBackToFirst()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var first = CreatePivot("First", 2, sheet.Id);
        var second = CreatePivot("Second", 10, sheet.Id);
        sheet.PivotTables.Add(first);
        sheet.PivotTables.Add(second);

        PivotUiPlanner.FindPivotTableForSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 2)))
            .Should()
            .BeSameAs(second);

        PivotUiPlanner.FindPivotTableForSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 100, 2), new CellAddress(sheet.Id, 100, 2)))
            .Should()
            .BeSameAs(first);
    }

    [Fact]
    public void FindPivotTableContainingSelection_ReturnsOnlyIntersectingPivot()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var first = CreatePivot("First", 2, sheet.Id);
        var second = CreatePivot("Second", 10, sheet.Id);
        sheet.PivotTables.Add(first);
        sheet.PivotTables.Add(second);

        PivotUiPlanner.FindPivotTableContainingSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 2)))
            .Should()
            .BeSameAs(second);

        PivotUiPlanner.FindPivotTableContainingSelection(
                sheet,
                new GridRange(new CellAddress(sheet.Id, 100, 2), new CellAddress(sheet.Id, 100, 2)))
            .Should()
            .BeNull("Excel hides contextual PivotTable tabs when selection leaves the PivotTable body");
    }

    [Fact]
    public void ChooseDefaultDataField_UsesFirstNumericOrDateColumnAfterHeader()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new DateTimeValue(46161));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(12));

        PivotUiPlanner.ChooseDefaultDataField(sheet, range).Should().Be(1);
    }

    [Fact]
    public void ChooseDefaultDataField_FallsBackToSecondColumnWhenNoNumericDataExists()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));

        PivotUiPlanner.ChooseDefaultDataField(sheet, range).Should().Be(1);
    }

    [Fact]
    public void DefaultTargetRange_PlacesPivotTwoColumnsAfterSourceAndClampsToSheetEdges()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(new CellAddress(sheet.Id, 10, 16382), new CellAddress(sheet.Id, 20, 16384));

        var target = PivotUiPlanner.DefaultTargetRange(sheet, source);

        target.Start.Should().Be(new CellAddress(sheet.Id, 10, 16384));
        target.End.Should().Be(new CellAddress(sheet.Id, 23, 16384));
    }

    [Fact]
    public void GenerateUniquePivotTableName_SkipsExistingNamesCaseInsensitively()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("PivotTable1", sheetId: sheet.Id));
        sheet.PivotTables.Add(CreatePivot("pivottable2", sheetId: sheet.Id));

        PivotUiPlanner.GenerateUniquePivotTableName(sheet).Should().Be("PivotTable3");
    }

    [Theory]
    [InlineData("Sheet1", "Sheet1")]
    [InlineData("'Sales Q1'", "Sales Q1")]
    [InlineData("'Bob''s Sheet'", "Bob's Sheet")]
    public void UnquoteSheetName_RemovesExcelQuotes(string input, string expected)
    {
        PivotUiPlanner.UnquoteSheetName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Sheet1", "Sheet1")]
    [InlineData("Sales_Q1", "Sales_Q1")]
    [InlineData("Sales Q1", "'Sales Q1'")]
    [InlineData("Bob's Sheet", "'Bob''s Sheet'")]
    public void QuoteSheetNameForReference_QuotesOnlyWhenNeeded(string input, string expected)
    {
        PivotUiPlanner.QuoteSheetNameForReference(input).Should().Be(expected);
    }

    [Fact]
    public void CreateDefaultDataField_UsesSumForNumericSourceAndCountForTextSource()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var pivot = CreatePivot(sheetId: sheet.Id);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        PivotUiPlanner.CreateDefaultDataField(sheet, pivot, ["Region", "Amount"], 1)
            .Should()
            .Be(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        PivotUiPlanner.CreateDefaultDataField(sheet, pivot, ["Region", "Amount"], 0)
            .Should()
            .Be(new PivotDataFieldModel(0, "Count of Region", "count"));
    }

    [Theory]
    [InlineData("contains:East", PivotLabelFilterKind.Contains, "East")]
    [InlineData("begins:Q", PivotLabelFilterKind.BeginsWith, "Q")]
    [InlineData("<>West", PivotLabelFilterKind.DoesNotEqual, "West")]
    public void TryParseLabelFilter_ParsesExcelStyleFilterText(string input, PivotLabelFilterKind expectedKind, string expectedValue)
    {
        PivotUiPlanner.TryParseLabelFilter(input, 2, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(2);
        filter.Kind.Should().Be(expectedKind);
        filter.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(">10", PivotValueFilterKind.GreaterThan, 10)]
    [InlineData("<=5.5", PivotValueFilterKind.LessThanOrEqual, 5.5)]
    [InlineData("<>0", PivotValueFilterKind.DoesNotEqual, 0)]
    public void TryParseValueFilter_ParsesComparisonOperators(string input, PivotValueFilterKind expectedKind, double expectedValue)
    {
        PivotUiPlanner.TryParseValueFilter(input, 3, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(3);
        filter.Kind.Should().Be(expectedKind);
        filter.ComparisonValue.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("top:10", PivotValueFilterKind.Top, 10)]
    [InlineData("bottom:3", PivotValueFilterKind.Bottom, 3)]
    public void TryParseValueFilter_ParsesTopBottomFilters(string input, PivotValueFilterKind expectedKind, int expectedCount)
    {
        PivotUiPlanner.TryParseValueFilter(input, 4, out var filter).Should().BeTrue();
        filter.SourceFieldIndex.Should().Be(4);
        filter.Kind.Should().Be(expectedKind);
        filter.Count.Should().Be(expectedCount);
    }

    [Fact]
    public void SetFieldSelectedItems_UpdatesMatchingFieldOnly()
    {
        var fields = new[]
        {
            new PivotFieldModel(0, SelectedItems: ["East"]),
            new PivotFieldModel(1)
        };

        var updated = PivotUiPlanner.SetFieldSelectedItems(fields, 1, ["Q1"]);

        updated[0].SelectedItems.Should().Equal("East");
        updated[1].SelectedItem.Should().Be("Q1");
        updated[1].SelectedItems.Should().Equal("Q1");
    }

    [Fact]
    public void GetFieldListCaption_ReadsStringOrFieldListItemAndIgnoresBlankCaptions()
    {
        PivotUiPlanner.GetFieldListCaption("Region").Should().Be("Region");
        PivotUiPlanner.GetFieldListCaption(new PivotFieldListItem("Amount", true)).Should().Be("Amount");
        PivotUiPlanner.GetFieldListCaption(new PivotFieldListItem("  ", false)).Should().BeNull();
        PivotUiPlanner.GetFieldListCaption(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FilterPivotFieldListItems_ReturnsAllFieldsForBlankSearch(string? searchText)
    {
        var fields = new[]
        {
            new PivotFieldListItem("Region", true),
            new PivotFieldListItem("Amount", false)
        };

        var filtered = PivotUiPlanner.FilterPivotFieldListItems(fields, searchText);

        filtered.Should().Equal(fields);
    }

    [Fact]
    public void FilterPivotFieldListItems_MatchesCaptionsCaseInsensitivelyAndPreservesCheckedState()
    {
        var fields = new[]
        {
            new PivotFieldListItem("Region", true),
            new PivotFieldListItem("Sales Amount", false),
            new PivotFieldListItem("Cost", true)
        };

        var filtered = PivotUiPlanner.FilterPivotFieldListItems(fields, "amount");

        filtered.Should().Equal(new PivotFieldListItem("Sales Amount", false));
    }

    [Fact]
    public void PendingPivotLayoutUpdate_CapturesDeferredLayoutIntent()
    {
        var pending = new PendingPivotLayoutUpdate(
            IsDeferred: true,
            AvailableFieldsSearchText: "sales",
            Fields: [new PivotFieldListItem("Sales Amount", true)]);

        pending.IsDeferred.Should().BeTrue();
        pending.AvailableFieldsSearchText.Should().Be("sales");
        pending.Fields.Should().Equal(new PivotFieldListItem("Sales Amount", true));
    }

    [Theory]
    [InlineData(-1, new[] { "A", "B", "X" })]
    [InlineData(1, new[] { "A", "X", "B" })]
    [InlineData(3, new[] { "A", "B", "X" })]
    public void InsertOrAppend_InsertsOnlyInsideExistingListBounds(int index, string[] expected)
    {
        var items = new List<string> { "A", "B" };

        PivotUiPlanner.InsertOrAppend(items, "X", index);

        items.Should().Equal(expected);
    }

    private static PivotTableModel CreatePivot(string name = "Pivot", uint targetRow = 5, SheetId? sheetId = null)
    {
        sheetId ??= SheetId.New();
        return new PivotTableModel
        {
            Name = name,
            SourceRange = new GridRange(new CellAddress(sheetId.Value, 1, 1), new CellAddress(sheetId.Value, 4, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId.Value, targetRow, 1), new CellAddress(sheetId.Value, targetRow + 4, 4))
        };
    }
}
