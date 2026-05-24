using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MaterializesRowFieldSumAndGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_AppliesPivotStyleToHeadersAndGrandTotals()
    {
        var workbook = new Workbook("PivotStyleRenderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
        headerStyle.FontColor.Should().Be(CellColor.White);
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        totalStyle.Bold.Should().BeTrue();
        totalStyle.FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_AppliesPivotStyleRowAndColumnStripes()
    {
        var workbook = new Workbook("PivotStyleStripeRenderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ShowRowStripes = true,
            ShowColumnStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var firstBodyStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId);
        firstBodyStyle.FillColor.Should().Be(new CellColor(234, 243, 252));
        var secondBodyStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        secondBodyStyle.FillColor.Should().BeNull();
        var stripedValueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F4"))!.StyleId);
        stripedValueStyle.FillColor.Should().Be(new CellColor(234, 243, 252));
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId);
        totalStyle.FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_AppliesPivotStyleToMatrixGrandTotalColumn()
    {
        var workbook = new Workbook("PivotGrandTotalColumnStyleTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            StyleName = "PivotStyleMedium9"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "H2").Should().Be("Grand Total");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "H3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(221, 235, 247));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "H4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_AppliesPivotStyleHeaderFlagsToRowAndColumnHeadersSeparately()
    {
        var workbook = new Workbook("PivotStyleHeaderFlagRenderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ShowRowHeaders = false,
            ShowColumnHeaders = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().BeNull();
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F2"))!.StyleId).FillColor.Should().Be(new CellColor(91, 155, 213));
    }

    [Fact]
    public void Refresh_AppliesValueFieldNumberFormatToMaterializedValueCells()
    {
        var workbook = new Workbook("PivotValueNumberFormatTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", NumberFormatId: 4));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).NumberFormat.Should().Be("#,##0.00");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).NumberFormat.Should().Be("#,##0.00");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Theory]
    [InlineData(41, "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)")]
    [InlineData(42, "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)")]
    [InlineData(43, "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)")]
    [InlineData(44, "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)")]
    public void Refresh_MapsAccountingBuiltInValueFieldNumberFormats(int numberFormatId, string expectedFormat)
    {
        var workbook = new Workbook("PivotAccountingValueNumberFormatTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", NumberFormatId: numberFormatId));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).NumberFormat.Should().Be(expectedFormat);
    }

    [Fact]
    public void Refresh_AppliesCustomValueFieldNumberFormatCodeToMaterializedValueCells()
    {
        var workbook = new Workbook("PivotCustomValueNumberFormatTest");
        workbook.NumberFormatCatalog[165] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", NumberFormatId: 165));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).NumberFormat.Should().Be("#,##0.0 \"kg\"");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_AppliesNamedPivotStyleFamilyToSubtotalsAndGrandTotalsSeparately()
    {
        var workbook = new Workbook("PivotStyleFamilyRenderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            StyleName = "PivotStyleMedium4",
            ShowSubtotals = true,
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().Be(new CellColor(112, 173, 71));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G5"))!.StyleId).FillColor.Should().Be(new CellColor(226, 239, 218));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G9"))!.StyleId).FillColor.Should().Be(new CellColor(198, 224, 180));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).FillColor.Should().Be(new CellColor(235, 245, 230));
    }

    [Theory]
    [InlineData("PivotStyleMedium2", 31, 78, 121, 232, 240, 248)]
    [InlineData("PivotStyleLight16", 217, 225, 242, 242, 248, 238)]
    [InlineData("PivotStyleMedium10", 237, 125, 49, 253, 239, 230)]
    [InlineData("PivotStyleMedium17", 112, 48, 160, 243, 235, 250)]
    [InlineData("PivotStyleDark7", 31, 78, 121, 232, 240, 248)]
    public void Refresh_MapsAdditionalBuiltInPivotStyleFamilies(string styleName, byte headerR, byte headerG, byte headerB, byte stripeR, byte stripeG, byte stripeB)
    {
        var workbook = new Workbook("PivotStyleFamilyExpansionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = styleName,
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(new CellColor(headerR, headerG, headerB));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(stripeR, stripeG, stripeB));
    }

    [Fact]
    public void Refresh_ResolvesSupportedBuiltInPivotStyleFromWorkbookTheme()
    {
        var workbook = new Workbook("PivotStyleThemeRenderTest")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120))
        };
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium2",
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(new CellColor(10, 80, 120));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(230, 238, 242));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(182, 202, 214));
    }

    [Fact]
    public void Refresh_MaterializesColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "H3").Should().Be(25);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        Number(sheet, "H5").Should().Be(70);
    }

    [Fact]
    public void Refresh_MatrixUsesEmptyValueTextForMissingIntersections()
    {
        var workbook = new Workbook("PivotEmptyValueDisplayTest");
        var sheet = workbook.AddSheet("Data");
        SeedSparseSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            EmptyValueText = "N/A"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "G3").Should().Be("N/A");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "F4").Should().Be("N/A");
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "H3").Should().Be(10);
        Number(sheet, "H4").Should().Be(25);
    }

    [Fact]
    public void Refresh_MaterializesNestedColumnFieldMatrix()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "M10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "I2").Should().Be("Q2");
        Text(sheet, "J2").Should().Be("Q2");
        Text(sheet, "K2").Should().Be("Grand Total");
        Text(sheet, "G3").Should().Be("Retail");
        Text(sheet, "H3").Should().Be("Wholesale");
        Text(sheet, "I3").Should().Be("Retail");
        Text(sheet, "J3").Should().Be("Wholesale");
        Text(sheet, "F4").Should().Be("East");
        Number(sheet, "G4").Should().Be(10);
        Number(sheet, "H4").Should().Be(15);
        Number(sheet, "I4").Should().Be(20);
        Number(sheet, "J4").Should().Be(25);
        Number(sheet, "K4").Should().Be(70);
        Text(sheet, "F5").Should().Be("West");
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(35);
        Number(sheet, "I5").Should().Be(40);
        Number(sheet, "J5").Should().Be(45);
        Number(sheet, "K5").Should().Be(150);
        Text(sheet, "F6").Should().Be("Grand Total");
        Number(sheet, "G6").Should().Be(40);
        Number(sheet, "H6").Should().Be(50);
        Number(sheet, "I6").Should().Be(60);
        Number(sheet, "J6").Should().Be(70);
        Number(sheet, "K6").Should().Be(220);
    }

    [Fact]
    public void Refresh_MaterializesMultipleRowAndDataFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Quarter");
        Text(sheet, "G2").Should().Be("Sum of Amount");
        Text(sheet, "H2").Should().Be("Count of Amount");
        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "H3").Should().Be(1);
        Text(sheet, "E6").Should().Be("West");
        Text(sheet, "F6").Should().Be("Q2");
        Number(sheet, "G6").Should().Be(25);
        Number(sheet, "H6").Should().Be(1);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "G7").Should().Be(70);
        Number(sheet, "H7").Should().Be(4);
    }

    [Fact]
    public void Refresh_MaterializesValuesOnlyPivot()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H5")
        };
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Sum of Amount");
        Text(sheet, "F2").Should().Be("Count of Amount");
        Number(sheet, "E3").Should().Be(70);
        Number(sheet, "F3").Should().Be(4);
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MaterializesColumnOnlyPivot()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Q1");
        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "E3").Should().Be(30);
        Number(sheet, "F3").Should().Be(40);
        Number(sheet, "G3").Should().Be(70);
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixAppliesLabelFiltersToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(1, PivotLabelFilterKind.Equals, "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F4").Should().Be(20);
        Number(sheet, "G4").Should().Be(20);
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(30);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesComparisonAndBetweenLabelFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Between, "East", "West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesSelectedItemsToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["West"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("West");
        Number(sheet, "F3").Should().Be(45);
        Text(sheet, "E4").Should().Be("Grand Total");
        Number(sheet, "F4").Should().Be(45);
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesSelectedItemsToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItems: ["Q2"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "F4").Should().Be(25);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixAppliesValueFiltersToColumnFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 35, SourceFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(15);
        Number(sheet, "F4").Should().Be(25);
        Number(sheet, "G4").Should().Be(25);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesBetweenValueFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Between, ComparisonValue: 40, ComparisonValue2: 75, SourceFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Central");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesAboveAverageValueFiltersToRowFields()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Central");
        Text(sheet, "E4").Should().Be("West");
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixSortsColumnLabelsDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(30);
    }

    [Fact]
    public void Refresh_MatrixSortsColumnValuesDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q2");
        Text(sheet, "G2").Should().Be("Q1");
        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "F3").Should().Be(15);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
        Number(sheet, "G5").Should().Be(30);
    }

    [Fact]
    public void Refresh_EvaluatesCommonSummaryFunctions()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "L8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Average", "average"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Min", "min"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Max", "max"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Product", "product"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count Numbers", "countNums"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Average");
        Text(sheet, "J2").Should().Be("Count Numbers");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(12.5);
        Number(sheet, "G3").Should().Be(10);
        Number(sheet, "H3").Should().Be(15);
        Number(sheet, "I3").Should().Be(150);
        Number(sheet, "J3").Should().Be(2);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(17.5);
        Number(sheet, "J5").Should().Be(4);
    }

    [Fact]
    public void Refresh_EvaluatesStatisticalSummaryFunctions()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "L8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "StdDev", "stdDev"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "StdDevp", "stdDevP"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Var", "var"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Varp", "varP"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(Math.Sqrt(12.5d), 0.0000001);
        Number(sheet, "G3").Should().Be(2.5);
        Number(sheet, "H3").Should().Be(12.5);
        Number(sheet, "I3").Should().Be(6.25);
        Number(sheet, "F5").Should().BeApproximately(Math.Sqrt(125d / 3d), 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(Math.Sqrt(31.25d), 0.0000001);
        Number(sheet, "H5").Should().BeApproximately(125d / 3d, 0.0000001);
        Number(sheet, "I5").Should().Be(31.25);
    }

    [Fact]
    public void Refresh_CompactReportLayoutUsesSingleRowLabelColumn()
    {
        var workbook = new Workbook("PivotCompactLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East Q1");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "E4").Should().Be("East Q2");
        Number(sheet, "F4").Should().Be(15);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(70);
        sheet.GetCell(Addr(sheet, "G3")).Should().BeNull();
    }

    [Fact]
    public void Refresh_CompactReportLayoutAppliesConfiguredRowLabelIndent()
    {
        var workbook = new Workbook("PivotCompactIndentRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 3
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId).IndentLevel.Should().Be(3);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId).IndentLevel.Should().Be(3);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).IndentLevel.Should().Be(0);
    }

    [Fact]
    public void Refresh_CompactReportLayoutUsesSubtotaledFieldCaptionForNestedSubtotals()
    {
        var workbook = new Workbook("PivotCompactNestedSubtotalCaptionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "H20"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F5").Should().Be("Q1 Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "F8").Should().Be("Q2 Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "F11").Should().Be("Q1 Total");
        Number(sheet, "G11").Should().Be(65);
        Text(sheet, "F14").Should().Be("Q2 Total");
        Number(sheet, "G14").Should().Be(85);
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsMergesRepeatedOuterRowLabels()
    {
        var workbook = new Workbook("PivotMergeLabelsRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "E6")));
        Text(sheet, "E3").Should().Be("East");
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
        Text(sheet, "E5").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsMergesSuppressedRepeatedOuterRowLabels()
    {
        var workbook = new Workbook("PivotMergeSuppressedLabelsRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            RepeatItemLabels = false,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "E6")));
        Text(sheet, "E3").Should().Be("East");
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
        Text(sheet, "E5").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsRemovesStalePivotMergesWhenDisabled()
    {
        var workbook = new Workbook("PivotMergeLabelsRefreshDisableTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.AddMergedRegion(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            MergeAndCenterLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().BeEmpty();
        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("East");
    }

    [Fact]
    public void Refresh_CompactReportLayoutUsesSingleRowLabelColumnForMatrix()
    {
        var workbook = new Workbook("PivotCompactMatrixLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8"),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East Q1");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(0);
        Number(sheet, "H3").Should().Be(10);
        Text(sheet, "E6").Should().Be("West Q2");
        Number(sheet, "F6").Should().Be(0);
        Number(sheet, "G6").Should().Be(25);
        Number(sheet, "H6").Should().Be(25);
    }

    [Fact]
    public void Refresh_CanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(45d / 70d, 0.0000001);
        Number(sheet, "F5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 70d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 70d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F5").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfRowTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Row Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfRowTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 25d, 0.0000001);
        Number(sheet, "H3").Should().Be(1);
        Number(sheet, "F4").Should().BeApproximately(20d / 45d, 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d / 45d, 0.0000001);
        Number(sheet, "H4").Should().Be(1);
        Number(sheet, "F5").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "G5").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfColumnTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Column Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfColumnTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d / 40d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(25d / 70d, 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(20d / 30d, 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d / 40d, 0.0000001);
        Number(sheet, "H4").Should().BeApproximately(45d / 70d, 0.0000001);
        Number(sheet, "F5").Should().Be(1);
        Number(sheet, "G5").Should().Be(1);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_ColumnOnlyCanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "E3").Should().BeApproximately(30d / 70d, 0.0000001);
        Number(sheet, "F3").Should().BeApproximately(40d / 70d, 0.0000001);
        Number(sheet, "G3").Should().Be(1);
    }

    [Fact]
    public void Refresh_ValuesOnlyCanShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G5")
        };
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "E3").Should().Be(1);
    }

    [Fact]
    public void Refresh_CanShowValuesAsRunningTotalInBaseField()
    {
        var workbook = new Workbook("PivotRunningTotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Running Total",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RunningTotalIn,
            BaseFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("Q1");
        Number(sheet, "F3").Should().Be(30);
        Text(sheet, "E4").Should().Be("Q2");
        Number(sheet, "F4").Should().Be(70);
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_CanShowValuesAsDifferenceFromBaseItem()
    {
        var workbook = new Workbook("PivotDifferenceFromTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Difference From Q1",
            "sum",
            ShowValuesAs: PivotShowValuesAs.DifferenceFrom,
            BaseFieldIndex: 1,
            BaseItem: "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(0);
        Number(sheet, "F4").Should().Be(10);
        Number(sheet, "F5").Should().Be(40);
    }

    [Fact]
    public void Refresh_CanShowValuesAsPercentDifferenceFromBaseItem()
    {
        var workbook = new Workbook("PivotPercentDifferenceFromTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "% Difference From Q1",
            "sum",
            ShowValuesAs: PivotShowValuesAs.PercentDifferenceFrom,
            BaseFieldIndex: 1,
            BaseItem: "Q1"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(0);
        Number(sheet, "F4").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "F5").Should().BeApproximately(40d / 30d, 0.0000001);
    }

    [Fact]
    public void Refresh_CanShowValuesAsRankSmallestAndLargest()
    {
        var workbook = new Workbook("PivotRankTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H6")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Rank Smallest",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RankSmallest,
            BaseFieldIndex: 1));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Rank Largest",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RankLargest,
            BaseFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().Be(1);
        Number(sheet, "G3").Should().Be(2);
        Number(sheet, "F4").Should().Be(2);
        Number(sheet, "G4").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsIndex()
    {
        var workbook = new Workbook("PivotIndexTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Index", "sum", ShowValuesAs: PivotShowValuesAs.Index));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d * 70d / (25d * 30d), 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(15d * 70d / (25d * 40d), 0.0000001);
        Number(sheet, "F4").Should().BeApproximately(20d * 70d / (45d * 30d), 0.0000001);
        Number(sheet, "G4").Should().BeApproximately(25d * 70d / (45d * 40d), 0.0000001);
        Number(sheet, "H3").Should().Be(1);
        Number(sheet, "F5").Should().Be(1);
        Number(sheet, "H5").Should().Be(1);
    }

    [Fact]
    public void Refresh_MatrixCanShowValuesAsPercentOfParentTotals()
    {
        var workbook = new Workbook("PivotParentTotalsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "K7")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Row", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentRowTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Column", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentColumnTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% Parent Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfParentTotal));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Number(sheet, "F3").Should().BeApproximately(10d / 25d, 0.0000001);
        Number(sheet, "G3").Should().BeApproximately(10d / 30d, 0.0000001);
        Number(sheet, "H3").Should().BeApproximately(10d / 70d, 0.0000001);
    }

    [Fact]
    public void Refresh_AppliesPageFieldSelectedItemFilter()
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
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Quarter");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("Region");
        Text(sheet, "E5").Should().Be("East");
        Number(sheet, "F5").Should().Be(10);
        Text(sheet, "E6").Should().Be("West");
        Number(sheet, "F6").Should().Be(20);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(30);
    }

    [Fact]
    public void Refresh_AppliesPageFieldMultiSelectFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItems: ["East", "North"]));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("(Multiple Items)");
        Text(sheet, "E5").Should().Be("Q1");
        Number(sheet, "F5").Should().Be(60);
        Text(sheet, "E6").Should().Be("Q2");
        Number(sheet, "F6").Should().Be(15);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(75);
    }

    [Fact]
    public void Refresh_MaterializesPageFieldsUsingReportFilterLayout()
    {
        var workbook = new Workbook("PivotRefreshPageFieldLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8"),
            PageOverThenDown = true,
            PageWrap = 2
        };
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItems: ["East", "West"]));
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("(Multiple Items)");
        Text(sheet, "G2").Should().Be("Quarter");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("Region");
        Number(sheet, "F5").Should().Be(10);
    }

    [Fact]
    public void Refresh_StylesBodyHeadersBelowMaterializedPageFields()
    {
        var workbook = new Workbook("PivotRefreshPageFieldStyleTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            StyleName = "PivotStyleMedium9"
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().BeNull();
        var bodyHeaderStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        bodyHeaderStyle.Bold.Should().BeTrue();
        bodyHeaderStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
    }

    [Fact]
    public void Refresh_GroupsDateRowFieldByMonth()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedDatedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D2").Should().Be("Order Date");
        Text(sheet, "D3").Should().Be("2026-01");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "D4").Should().Be("2026-02");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("Grand Total");
        Number(sheet, "E5").Should().Be(100);
    }

    [Fact]
    public void Refresh_GroupsNumberRowFieldByInterval()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("0-9");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "D4").Should().Be("10-19");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("Grand Total");
        Number(sheet, "E5").Should().Be(100);
    }

    [Fact]
    public void Refresh_AppliesTopNValueFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 2));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(95);
    }

    [Fact]
    public void Refresh_AppliesLabelFilterContains()
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
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "st"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_AppliesValueGreaterThanFilter()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 45));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("Grand Total");
        Number(sheet, "F4").Should().Be(50);
    }

    [Fact]
    public void Refresh_SortsRowsByValueDescending()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(50));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C6"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("North");
        Number(sheet, "F3").Should().Be(50);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("East");
        Number(sheet, "F5").Should().Be(25);
    }

    [Fact]
    public void Refresh_WritesOuterRowFieldSubtotalsWhenEnabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E5").Should().Be("East Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E8").Should().Be("West Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

    [Fact]
    public void Refresh_CanPlaceOuterRowFieldSubtotalsAtTopOfGroup()
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
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East Total");
        Number(sheet, "G3").Should().Be(25);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q1");
        Text(sheet, "E6").Should().Be("West Total");
        Number(sheet, "G6").Should().Be(45);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q1");
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

    [Fact]
    public void Refresh_HidesGrandTotalWhenDisabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ShowGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideRowGrandTotalsWhileKeepingColumnGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideColumnGrandTotalsWhileKeepingRowGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = true,
            ShowColumnGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "H3").Should().Be(25);
        Number(sheet, "H4").Should().Be(45);
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_SuppressesRepeatedOuterLabelsWhenDisabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            RepeatItemLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("");
        Text(sheet, "F4").Should().Be("Q2");
        Text(sheet, "E5").Should().Be("West");
        Text(sheet, "E6").Should().Be("");
    }

    [Fact]
    public void Refresh_MatrixSuppressesRepeatedOuterLabelsWhenDisabled()
    {
        var workbook = new Workbook("PivotMatrixRepeatLabelsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J10"),
            RepeatItemLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("");
        Text(sheet, "F4").Should().Be("Q2");
        Text(sheet, "E5").Should().Be("West");
        Text(sheet, "E6").Should().Be("");
    }

    [Fact]
    public void Refresh_WritesBlankLineAfterOuterItemsWhenEnabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "G5")).Should().BeNull();
        Text(sheet, "E6").Should().Be("West");
    }

    [Fact]
    public void Refresh_MatrixWritesBlankLineAfterOuterItemsWhenEnabled()
    {
        var workbook = new Workbook("PivotMatrixBlankLineTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J12"),
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "G5")).Should().BeNull();
        Text(sheet, "E6").Should().Be("West");
    }

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
            TargetRange = Range(sheet, "F2", "I8")
        };
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*Units"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Revenue", "sum", CalculatedFieldName: "Revenue"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Region");
        Text(sheet, "G2").Should().Be("Sum of Revenue");
        Text(sheet, "F3").Should().Be("East");
        Number(sheet, "G3").Should().Be(65);
        Text(sheet, "F4").Should().Be("West");
        Number(sheet, "G4").Should().Be(135);
        Text(sheet, "F5").Should().Be("Grand Total");
        Number(sheet, "G5").Should().Be(200);
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
    public void ExtractDetailRows_ReturnsSourceRowsBehindPivotOutputRow()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q1", "10");
    }

    [Fact]
    public void ExtractDetailRows_ForRowLabelCell_ReturnsNoRows()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "E3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().BeEmpty();
    }

    [Fact]
    public void ExtractDetailRows_ForMatrixValueCell_FiltersByColumnField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q1", "10");
    }

    [Fact]
    public void ExtractDetailRows_ForGrandTotal_ReturnsAllFilteredSourceRows()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F5"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(4);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q1|10");
        rowTexts.Should().Contain("West|Q2|25");
    }

    [Fact]
    public void ExtractDetailRows_ForSubtotal_ReturnsSourceRowsInSubtotalGroup()
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
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G5"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(2);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q1|10");
        rowTexts.Should().Contain("East|Q2|15");
    }

    [Fact]
    public void ExtractDetailRows_WhenRepeatLabelsAreOff_UsesNearestVisibleOuterLabel()
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
            RepeatItemLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G4"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q2", "15");
    }

    [Fact]
    public void ExtractDetailRows_ForColumnOnlyPivot_FiltersByColumnItem()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(2);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q2|15");
        rowTexts.Should().Contain("West|Q2|25");
    }

    private static void SeedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
    }

    private static void SeedSparseSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
    }

    private static void SeedSalesChannelData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D6"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A7"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B7"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C7"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D7"), new NumberValue(35));
        sheet.SetCell(Addr(sheet, "A8"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B8"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C8"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D8"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "A9"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B9"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C9"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D9"), new NumberValue(45));
    }

    private static void SeedSalesWithUnitsData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(4));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(2.2));
    }

    private static void SeedDatedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Order Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 20)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 28)));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    private static void SeedPriceSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(7));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(12));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(17));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    private static string Text(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is NumberValue number ? number.Value : double.NaN;

    private static string PivotValueText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue date => date.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => ""
        };

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
