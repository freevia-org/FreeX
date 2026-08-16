using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_EvaluatesCalculatedFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "I8"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Revenue", "sum", CalculatedFieldName: "Revenue"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Sum of Revenue");
        Text(sheet, "F3").Should().Be("East");
        // Excel semantics: calculated field evaluated once per group using SUM of each
        // constituent source field: SUM(Amount)*SUM(Units) = 25*5 = 125
        Number(sheet, "G3").Should().Be(125);
        Text(sheet, "F4").Should().Be("West");
        // SUM(Amount)*SUM(Units) = 45*6.2 = 279
        Number(sheet, "G4").Should().Be(279);
        Text(sheet, "F5").Should().Be("Grand Total");
        // SUM(Amount)*SUM(Units) = 70*11.2 = 784
        Number(sheet, "G5").Should().Be(784);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForRowField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("East + West");
        Number(sheet, "F5").Should().Be(70);
        Text(sheet, "E6").Should().Be("Grand Total");
        Number(sheet, "F6").Should().Be(140);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForColumnField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I4")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Q1");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "F2").Should().Be("Q2");
        Number(sheet, "F3").Should().Be(40);
        Text(sheet, "G2").Should().Be("Q1 + Q2");
        Number(sheet, "G3").Should().Be(70);
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "H3").Should().Be(140);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForSingleColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Q1 + Q2");
        Text(sheet, "I2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "H3").Should().Be(25);
        Number(sheet, "I3").Should().Be(50);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(20);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "H4").Should().Be(45);
        Number(sheet, "I4").Should().Be(90);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        Number(sheet, "H5").Should().Be(70);
        Number(sheet, "I5").Should().Be(140);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForNestedColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "M8"),
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(2, "Retail + Wholesale", "Retail+Wholesale"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I2").Should().Be("Q1");
        Text(sheet, "I3").Should().Be("Retail + Wholesale");
        Text(sheet, "J2").Should().Be("Q2");
        Text(sheet, "J3").Should().Be("Retail");
        Text(sheet, "K2").Should().Be("Q2");
        Text(sheet, "K3").Should().Be("Wholesale");
        Text(sheet, "L2").Should().Be("Q2");
        Text(sheet, "L3").Should().Be("Retail + Wholesale");
        Text(sheet, "M2").Should().Be("Grand Total");

        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10);
        Number(sheet, "H4").Should().Be(15);
        Number(sheet, "I4").Should().Be(25);
        Number(sheet, "J4").Should().Be(20);
        Number(sheet, "K4").Should().Be(25);
        Number(sheet, "L4").Should().Be(45);
        Number(sheet, "M4").Should().Be(140);

        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(35);
        Number(sheet, "I5").Should().Be(65);
        Number(sheet, "J5").Should().Be(40);
        Number(sheet, "K5").Should().Be(45);
        Number(sheet, "L5").Should().Be(85);
        Number(sheet, "M5").Should().Be(300);

        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);
        Number(sheet, "H6").Should().Be(50);
        Number(sheet, "I6").Should().Be(90);
        Number(sheet, "J6").Should().Be(60);
        Number(sheet, "K6").Should().Be(70);
        Number(sheet, "L6").Should().Be(130);
        Number(sheet, "M6").Should().Be(440);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForOuterNestedColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "M8"),
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I2").Should().Be("Q2");
        Text(sheet, "I3").Should().Be("Retail");
        Text(sheet, "J2").Should().Be("Q2");
        Text(sheet, "J3").Should().Be("Wholesale");
        Text(sheet, "K2").Should().Be("Q1 + Q2");
        Text(sheet, "K3").Should().Be("Retail");
        Text(sheet, "L2").Should().Be("Q1 + Q2");
        Text(sheet, "L3").Should().Be("Wholesale");
        Text(sheet, "M2").Should().Be("Grand Total");

        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10);
        Number(sheet, "H4").Should().Be(15);
        Number(sheet, "I4").Should().Be(20);
        Number(sheet, "J4").Should().Be(25);
        Number(sheet, "K4").Should().Be(30);
        Number(sheet, "L4").Should().Be(40);
        Number(sheet, "M4").Should().Be(140);

        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(35);
        Number(sheet, "I5").Should().Be(40);
        Number(sheet, "J5").Should().Be(45);
        Number(sheet, "K5").Should().Be(70);
        Number(sheet, "L5").Should().Be(80);
        Number(sheet, "M5").Should().Be(300);

        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);
        Number(sheet, "H6").Should().Be(50);
        Number(sheet, "I6").Should().Be(60);
        Number(sheet, "J6").Should().Be(70);
        Number(sheet, "K6").Should().Be(100);
        Number(sheet, "L6").Should().Be(120);
        Number(sheet, "M6").Should().Be(440);
    }

    [Fact]
    public void Refresh_EvaluatesCalculatedItemsForInnerRowField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H10"),
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Tabular/no-subtotal defaults this
            // 2-row-field layout test was written against.
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        Number(sheet, "G4").Should().Be(15);
        Text(sheet, "E5").Should().Be("East");
        Text(sheet, "F5").Should().Be("Q1 + Q2");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E6").Should().Be("West");
        Text(sheet, "F6").Should().Be("Q1");
        Number(sheet, "G6").Should().Be(20);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q2");
        Number(sheet, "G7").Should().Be(25);
        Text(sheet, "E8").Should().Be("West");
        Text(sheet, "F8").Should().Be("Q1 + Q2");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(140);
    }

    // Companion to Refresh_EvaluatesCalculatedItemsForInnerRowField above, which dodges
    // subtotal coverage entirely (ShowSubtotals = false). Here subtotals ARE on, using the
    // model's default Top placement. Excel includes a calculated item in every subtotal of
    // its enclosing field, so "East Total"/"West Total" must equal the real Q1+Q2 rows PLUS
    // the calculated item's own "Q1 + Q2" row (25+25=50, 45+45=90) - not just the raw rows
    // (25, 45), which would silently disagree with the Grand Total of 140 that already
    // included the calculated items.
    [Fact]
    public void Refresh_TopSubtotalsIncludeCalculatedItemContribution()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H14"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East Total");
        Number(sheet, "G3").Should().Be(50);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q1");
        Number(sheet, "G4").Should().Be(10);
        Text(sheet, "E5").Should().Be("East");
        Text(sheet, "F5").Should().Be("Q2");
        Number(sheet, "G5").Should().Be(15);
        Text(sheet, "E6").Should().Be("East");
        Text(sheet, "F6").Should().Be("Q1 + Q2");
        Number(sheet, "G6").Should().Be(25);
        Text(sheet, "E7").Should().Be("West Total");
        Number(sheet, "G7").Should().Be(90);
        Text(sheet, "E8").Should().Be("West");
        Text(sheet, "F8").Should().Be("Q1");
        Number(sheet, "G8").Should().Be(20);
        Text(sheet, "E9").Should().Be("West");
        Text(sheet, "F9").Should().Be("Q2");
        Number(sheet, "G9").Should().Be(25);
        Text(sheet, "E10").Should().Be("West");
        Text(sheet, "F10").Should().Be("Q1 + Q2");
        Number(sheet, "G10").Should().Be(45);
        Text(sheet, "E11").Should().Be("Grand Total");
        Number(sheet, "G11").Should().Be(140);
    }

    // Sibling of Refresh_TopSubtotalsIncludeCalculatedItemContribution for the other subtotal
    // placement mechanism (Bottom): the fix must apply to both write paths, since Top
    // subtotals are precomputed before their child rows are visited while Bottom subtotals
    // accumulate incrementally during the row loop - two different code paths that both
    // needed the calculated-item contribution added.
    [Fact]
    public void Refresh_BottomSubtotalsIncludeCalculatedItemContribution()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H14"),
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Bottom
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Q1 + Q2", "Q1+Q2"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        Number(sheet, "G4").Should().Be(15);
        Text(sheet, "E5").Should().Be("East");
        Text(sheet, "F5").Should().Be("Q1 + Q2");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E6").Should().Be("East Total");
        Number(sheet, "G6").Should().Be(50);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q1");
        Number(sheet, "G7").Should().Be(20);
        Text(sheet, "E8").Should().Be("West");
        Text(sheet, "F8").Should().Be("Q2");
        Number(sheet, "G8").Should().Be(25);
        Text(sheet, "E9").Should().Be("West");
        Text(sheet, "F9").Should().Be("Q1 + Q2");
        Number(sheet, "G9").Should().Be(45);
        Text(sheet, "E10").Should().Be("West Total");
        Number(sheet, "G10").Should().Be(90);
        Text(sheet, "E11").Should().Be("Grand Total");
        Number(sheet, "G11").Should().Be(140);
    }

    // A calculated field's formula is unbounded text — typed by the user, or read from the pivot
    // definition in an opened .xlsx. The expression parser descends one stack frame per nesting
    // level, so without a depth cap a deeply nested formula overflowed the stack, and
    // StackOverflowException is uncatchable: it kills the process instead of surfacing as a bad
    // formula. Verified to abort the test host when the cap is removed.
    [Fact]
    public void Refresh_CalculatedFieldWithDeeplyNestedParentheses_DoesNotOverflowTheStack()
    {
        var workbook = new Workbook("PivotDepthTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "I8"),
            ReportLayout = PivotReportLayout.Tabular
        };
        var formula = new string('(', 50_000) + "Amount" + new string(')', 50_000);
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Deep", formula));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Deep", "sum", CalculatedFieldName: "Deep"));

        var refresh = () => PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        refresh.Should().NotThrow();
    }

    [Fact]
    public void Refresh_CalculatedFieldWithOrdinaryParentheses_StillEvaluates()
    {
        // The cap must not reject formulas a real workbook would contain.
        var workbook = new Workbook("PivotDepthTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "F2", "I8"),
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "((Amount)*(Units))"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Revenue", "sum", CalculatedFieldName: "Revenue"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "G3").Should().Be(125);
    }
}
