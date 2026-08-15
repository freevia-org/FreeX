using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
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
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        headerStyle.FontColor.Should().Be(CellColor.White);
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        totalStyle.Bold.Should().BeTrue();
        totalStyle.FillColor.Should().BeNull();
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
        firstBodyStyle.FillColor.Should().Be(new CellColor(224, 242, 250));
        var secondBodyStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        secondBodyStyle.FillColor.Should().BeNull();
        var stripedValueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F4"))!.StyleId);
        stripedValueStyle.FillColor.Should().Be(new CellColor(224, 242, 250));
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId);
        totalStyle.FillColor.Should().BeNull();
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
            .FillColor.Should().BeNull();
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "H4"))!.StyleId)
            .FillColor.Should().BeNull();
    }

    [Fact]
    public void Refresh_MaterializesPivotStyleAcrossBlankMultiLevelHeaderFootprintCells()
    {
        var workbook = new Workbook("PivotHeaderFootprintMaterializationTest");
        var source = workbook.AddSheet("Data");
        var sheet = workbook.AddSheet("Pivot");
        source.SetCell(Addr(source, "A1"), new TextValue("Region"));
        source.SetCell(Addr(source, "B1"), new TextValue("Year"));
        source.SetCell(Addr(source, "C1"), new TextValue("Quarter"));
        source.SetCell(Addr(source, "D1"), new TextValue("Amount"));
        source.SetCell(Addr(source, "A2"), new TextValue("East"));
        source.SetCell(Addr(source, "B2"), new TextValue("2026"));
        source.SetCell(Addr(source, "C2"), new TextValue("Q1"));
        source.SetCell(Addr(source, "D2"), new NumberValue(100));
        source.SetCell(Addr(source, "A3"), new TextValue("East"));
        source.SetCell(Addr(source, "B3"), new TextValue("2026"));
        source.SetCell(Addr(source, "C3"), new TextValue("Q2"));
        source.SetCell(Addr(source, "D3"), new NumberValue(200));
        source.SetCell(Addr(source, "A4"), new TextValue("East"));
        source.SetCell(Addr(source, "B4"), new TextValue("2027"));
        source.SetCell(Addr(source, "C4"), new TextValue("Q1"));
        source.SetCell(Addr(source, "D4"), new NumberValue(300));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(source, "A1", "D4"),
            TargetRange = Range(sheet, "E2", "K12"),
            StyleName = "PivotStyleMedium9",
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        pivot.LastRenderedRange.Should().NotBeNull();
        var materialized = pivot.LastRenderedRange!.Value;
        var headerEndRow = pivot.TargetRange.Start.Row + (uint)pivot.ColumnFields.Count - 1;
        Cell? blankHeaderCell = null;
        for (var row = materialized.Start.Row; row <= headerEndRow && blankHeaderCell is null; row++)
        for (var col = materialized.Start.Col; col <= materialized.End.Col; col++)
        {
            var cell = sheet.GetCell(row, col);
            if (cell?.Value is BlankValue)
            {
                blankHeaderCell = cell;
                break;
            }
        }

        blankHeaderCell.Should().NotBeNull("the subtotal column leaves a blank lower-level header cell");
        workbook.GetStyle(blankHeaderCell!.StyleId).FillColor.Should().Be(new CellColor(21, 96, 130));
    }

    [Fact]
    public void Refresh_MaterializesPivotStyleAcrossBlankSpacerRows()
    {
        var workbook = new Workbook("PivotSpacerRowMaterializationTest");
        var source = workbook.AddSheet("Data");
        var sheet = workbook.AddSheet("Pivot");
        source.SetCell(Addr(source, "A1"), new TextValue("Region"));
        source.SetCell(Addr(source, "B1"), new TextValue("Category"));
        source.SetCell(Addr(source, "C1"), new TextValue("Amount"));
        source.SetCell(Addr(source, "A2"), new TextValue("East"));
        source.SetCell(Addr(source, "B2"), new TextValue("Hardware"));
        source.SetCell(Addr(source, "C2"), new NumberValue(100));
        source.SetCell(Addr(source, "A3"), new TextValue("East"));
        source.SetCell(Addr(source, "B3"), new TextValue("Services"));
        source.SetCell(Addr(source, "C3"), new NumberValue(200));
        source.SetCell(Addr(source, "A4"), new TextValue("West"));
        source.SetCell(Addr(source, "B4"), new TextValue("Hardware"));
        source.SetCell(Addr(source, "C4"), new NumberValue(300));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(source, "A1", "C4"),
            TargetRange = Range(sheet, "E2", "H10"),
            StyleName = "PivotStyleMedium9",
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false,
            BlankLineAfterItems = true,
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var spacerRowCell = sheet.GetCell(5, 5);
        spacerRowCell.Should().NotBeNull("the blank line between outer row-field groups is inside the pivot footprint");
        spacerRowCell!.Value.Should().BeOfType<BlankValue>();
        workbook.GetStyle(spacerRowCell.StyleId).FillColor.Should().Be(new CellColor(224, 242, 250));
    }

    [Fact]
    public void Refresh_AppliesGrandTotalRowStyleAcrossBlankRowFieldCells()
    {
        var workbook = new Workbook("PivotGrandTotalRowFootprintStyleTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "E2", "H8"),
            StyleName = "PivotStyleMedium9",
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Tabular/no-subtotal defaults this
            // 2-row-field footprint-style test was written against.
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Units", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E7").Should().Be("Grand Total");
        sheet.GetCell(Addr(sheet, "F7"))!.Value.Should().Be(BlankValue.Instance);
        AssertPivotTotalStyle(workbook, sheet, "E7", null);
        AssertPivotTotalStyle(workbook, sheet, "F7", null);
        AssertPivotTotalStyle(workbook, sheet, "G7", null);
        AssertPivotTotalStyle(workbook, sheet, "H7", null);
    }

    [Fact]
    public void Refresh_AppliesSubtotalRowStyleAcrossBlankRowFieldCells()
    {
        var workbook = new Workbook("PivotSubtotalRowFootprintStyleTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesWithUnitsData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "E2", "H10"),
            StyleName = "PivotStyleMedium9",
            ShowSubtotals = true,
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Bottom/Tabular defaults this
            // subtotal-row footprint-style test was written against.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Units", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E5").Should().Be("East Total");
        sheet.GetCell(Addr(sheet, "F5"))!.Value.Should().Be(BlankValue.Instance);
        AssertPivotTotalStyle(workbook, sheet, "E5", new CellColor(193, 229, 245));
        AssertPivotTotalStyle(workbook, sheet, "F5", new CellColor(193, 229, 245));
        AssertPivotTotalStyle(workbook, sheet, "G5", new CellColor(193, 229, 245));
        AssertPivotTotalStyle(workbook, sheet, "H5", new CellColor(193, 229, 245));
    }

    [Fact]
    public void Refresh_AppliesHeaderStyleAcrossBlankGrandTotalColumnHeaderCells()
    {
        var workbook = new Workbook("PivotGrandTotalColumnHeaderFootprintStyleTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "E2", "J8"),
            StyleName = "PivotStyleMedium9",
            // R90-render-pivot-layout-5-1: pin the (former) no-subtotal default -- this 2-column-field
            // test's Grand Total column position assumes no subtotal columns.
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "J2").Should().Be("Grand Total");
        sheet.GetCell(Addr(sheet, "J3"))!.Value.Should().Be(BlankValue.Instance);
        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "J3"))!.StyleId);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
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
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F2"))!.StyleId).FillColor.Should().Be(new CellColor(21, 96, 130));
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
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().BeNull();
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
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().BeNull();
    }

    [Fact]
    public void Refresh_SkipsValueFieldNumberFormatWhenApplyNumberFormatsIsFalse()
    {
        var workbook = new Workbook("PivotApplyNumberFormatsFalseTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ApplyNumberFormats = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", NumberFormatId: 4));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).NumberFormat.Should().Be(CellStyle.Default.NumberFormat);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).NumberFormat.Should().Be(CellStyle.Default.NumberFormat);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().BeNull();
    }

    [Fact]
    public void Refresh_AppliesPivotStyleFontEvenWhenApplyFontFormatsIsFalse()
    {
        // applyFontFormats / applyPatternFormats / applyBorderFormats are legacy autoFormatId flags;
        // the modern named pivot style (pivotTableStyleInfo) applies regardless of them, matching
        // Excel. Real-world files persist these as "0", so gating on them dropped all pivot styling
        // on load (Issue 123).
        var workbook = new Workbook("PivotApplyFontFormatsFalseTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ApplyFontFormats = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
    }

    [Fact]
    public void Refresh_AppliesPivotStylePatternEvenWhenApplyPatternFormatsIsFalse()
    {
        // The modern named pivot style applies its fills regardless of the legacy applyPatternFormats
        // autoFormatId flag (Issue 123).
        var workbook = new Workbook("PivotApplyPatternFormatsFalseTest");
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
            ApplyPatternFormats = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
    }

    [Fact]
    public void Refresh_AppliesPivotStyleBorderEvenWhenApplyBorderFormatsIsFalse()
    {
        // The modern named pivot style applies its borders regardless of the legacy applyBorderFormats
        // autoFormatId flag (Issue 123).
        var workbook = new Workbook("PivotApplyBorderFormatsFalseTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium9",
            ApplyBorderFormats = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
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
            ShowRowStripes = true,
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Bottom/Tabular defaults this
            // named-style-family test's cell coordinates assume.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().Be(new CellColor(19, 80, 27));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G5"))!.StyleId).FillColor.Should().Be(new CellColor(194, 241, 200));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G9"))!.StyleId).FillColor.Should().Be(new CellColor(194, 241, 200));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).FillColor.Should().Be(new CellColor(224, 248, 228));
    }

    [Theory]
    [InlineData("PivotStyleMedium2", 21, 96, 130, 224, 242, 250)]
    [InlineData("PivotStyleLight16", 202, 238, 251, 242, 251, 254)]
    [InlineData("PivotStyleMedium10", 233, 113, 50, 253, 241, 235)]
    [InlineData("PivotStyleMedium17", 112, 48, 160, 243, 235, 250)]
    [InlineData("PivotStyleDark7", 31, 78, 121, 232, 240, 248)]
    [InlineData("PivotStyleLight9", 193, 229, 245, 240, 248, 253)]
    [InlineData("PivotStyleLight14", 217, 242, 208, 246, 252, 243)]
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
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(120, 40, 20))
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
            .FillColor.Should().Be(new CellColor(220, 240, 252));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(150, 211, 246));
    }

    [Fact]
    public void Refresh_AppliesModernOfficeMedium2HeaderWithoutBodyOrGrandTotalFill()
    {
        var workbook = new Workbook("PivotStyleMedium2ModernOfficeRenderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            StyleName = "PivotStyleMedium2",
            ShowRowStripes = false
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().BeNull();
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        totalStyle.FillColor.Should().BeNull();
        totalStyle.FontColor.Should().Be(CellColor.Black);
    }

    [Fact]
    public void Refresh_UsesBuiltInMedium2PaletteForOfficeEquivalentTheme()
    {
        var workbook = new Workbook("PivotStyleMedium2OfficeEquivalentThemeTest")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Dark2, new CellColor(14, 40, 65))
                .WithColor(WorkbookThemeColorSlot.Light2, new CellColor(232, 232, 232))
                .WithColor(WorkbookThemeColorSlot.Hyperlink, new CellColor(70, 120, 134))
                .WithColor(WorkbookThemeColorSlot.FollowedHyperlink, new CellColor(150, 96, 125))
                .WithNativeColorSchemeXml("<a:clrScheme name=\"Office\" />")
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
            ShowRowStripes = false
        };
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().BeNull();
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId)
            .FillColor.Should().BeNull();
    }

    [Fact]
    public void ApplyLoadedPivotStyles_PreservesExistingExcelVisualStyles()
    {
        var workbook = new Workbook("LoadedPivotPreserveStyleTest");
        var sheet = workbook.AddSheet("Data");
        var headerStyleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(126, 53, 14),
            FontColor = CellColor.White
        });
        var bodyStyleId = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(247, 199, 172)
        });
        sheet.SetCell(Addr(sheet, "E2"), new Cell { Value = new TextValue("Row Labels"), StyleId = headerStyleId });
        sheet.SetCell(Addr(sheet, "F2"), new Cell { Value = new TextValue("Count of Sales"), StyleId = headerStyleId });
        sheet.SetCell(Addr(sheet, "E3"), new Cell { Value = new TextValue("Hardware"), StyleId = bodyStyleId });
        sheet.SetCell(Addr(sheet, "F3"), new Cell { Value = new NumberValue(4), StyleId = bodyStyleId });
        sheet.SetCell(Addr(sheet, "E4"), new Cell { Value = new TextValue("Grand Total"), StyleId = headerStyleId });
        sheet.SetCell(Addr(sheet, "F4"), new Cell { Value = new NumberValue(4), StyleId = headerStyleId });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotSharedCacheB",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "E2", "F4"),
            LastRenderedRange = Range(sheet, "E2", "F4"),
            StyleName = "PivotStyleDark3"
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().Be(new CellColor(126, 53, 14));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId).FillColor.Should().Be(new CellColor(247, 199, 172));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId).FontColor.Should().Be(CellColor.White);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_MaterializesWhiteBodySurfaceForLoadedNativePivotWithoutBodyFill()
    {
        var workbook = new Workbook("LoadedPivotWhiteBodySurfaceTest");
        var sheet = workbook.AddSheet("Pivot");
        var loadedThemeFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 10,
            FontScheme = CellFontScheme.Minor
        });
        sheet.SetCell(Addr(sheet, "A3"), new Cell { Value = new TextValue("Row Labels"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B3"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A4"), new Cell { Value = new TextValue("East"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C4"), new Cell { Value = new NumberValue(1250), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A5"), new Cell { Value = new TextValue("West"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B5"), new Cell { Value = new TextValue("Direct"), StyleId = loadedThemeFontStyle });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotSubtotalGrandTotals",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C2"),
            TargetRange = Range(sheet, "A3", "D6"),
            LastRenderedRange = Range(sheet, "A3", "D6"),
            StyleName = "PivotStyleMedium9",
            FirstHeaderRow = 1,
            FirstDataRow = 1,
            FirstDataColumn = 2,
            ShowRowStripes = false,
            ShowColumnStripes = false
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A4"))!.StyleId)
            .FillColor.Should().Be(CellColor.White, "Excel's dynamic PivotTable style layer hides sheet gridlines through unfilled body cells");
        sheet.GetCell(Addr(sheet, "B4"))!.Value.Should().Be(BlankValue.Instance);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B4"))!.StyleId)
            .FillColor.Should().Be(CellColor.White, "blank cells in the loaded native pivot footprint need the same body surface");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "C4"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B5"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        AssertLoadedPivotFontIdentity(workbook.GetStyle(sheet.GetCell(Addr(sheet, "A4"))!.StyleId));
    }

    [Fact]
    public void ApplyLoadedPivotStyles_StylesLoadedOutlineParentRowsAcrossBlankFootprintCells()
    {
        var workbook = new Workbook("LoadedPivotOutlineParentStyleTest");
        var sheet = workbook.AddSheet("Pivot");
        var loadedThemeFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 10,
            FontScheme = CellFontScheme.Minor
        });
        sheet.SetCell(Addr(sheet, "A3"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C3"), new Cell { Value = new TextValue("Category"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A4"), new Cell { Value = new TextValue("Region"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B4"), new Cell { Value = new TextValue("Channel"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C4"), new Cell { Value = new TextValue("Hardware"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A5"), new Cell { Value = new TextValue("East"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B6"), new Cell { Value = new TextValue("Direct"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C6"), new Cell { Value = new NumberValue(2360), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B7"), new Cell { Value = new TextValue("Partner"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "D7"), new Cell { Value = new NumberValue(980), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A8"), new Cell { Value = new TextValue("East Total"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C8"), new Cell { Value = new NumberValue(2360), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A9"), new Cell { Value = new TextValue("North"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "B10"), new Cell { Value = new TextValue("Direct"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "D10"), new Cell { Value = new NumberValue(2140), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "A11"), new Cell { Value = new TextValue("Grand Total"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "C11"), new Cell { Value = new NumberValue(2360), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "D11"), new Cell { Value = new NumberValue(3120), StyleId = loadedThemeFontStyle });
        var pivot = new PivotTableModel
        {
            Name = "NativePivotSubtotalGrandTotals",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D2"),
            TargetRange = Range(sheet, "A3", "E11"),
            LastRenderedRange = Range(sheet, "A3", "E11"),
            StyleName = "PivotStyleMedium9",
            ReportLayout = PivotReportLayout.Outline,
            FirstDataRow = 2,
            FirstDataColumn = 2,
            ShowRowStripes = false,
            ShowColumnStripes = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        var expectedGroupFill = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.8);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(expectedGroupFill);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .Bold.Should().BeTrue();
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FontColor.Should().Be(CellColor.Black);
        sheet.GetCell(Addr(sheet, "B5"))!.Value.Should().Be(BlankValue.Instance);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B5"))!.StyleId)
            .FillColor.Should().Be(expectedGroupFill);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId)
            .FillColor.Should().Be(expectedGroupFill);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B6"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A8"))!.StyleId)
            .FillColor.Should().Be(expectedGroupFill, "subtotal rows keep the same Medium9 fill but are detected separately from parent rows");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A9"))!.StyleId)
            .FillColor.Should().Be(expectedGroupFill);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_PreservesExistingFontIdentityWhenApplyingVisualStyle()
    {
        var workbook = new Workbook("LoadedPivotFontIdentityStyleTest");
        var sheet = workbook.AddSheet("Data");
        var loadedThemeFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 10,
            FontScheme = CellFontScheme.Minor,
            NumberFormat = "$#,##0.00"
        });
        sheet.SetCell(Addr(sheet, "E2"), new Cell { Value = new TextValue("Row Labels"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F2"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "E3"), new Cell { Value = new TextValue("Hardware"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F3"), new Cell { Value = new NumberValue(1250), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "E4"), new Cell { Value = new TextValue("Grand Total"), StyleId = loadedThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F4"), new Cell { Value = new NumberValue(1250), StyleId = loadedThemeFontStyle });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotThemeFont",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "E2", "F4"),
            LastRenderedRange = Range(sheet, "E2", "F4"),
            StyleName = "PivotStyleMedium9",
            ShowRowStripes = true
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        headerStyle.FontColor.Should().Be(CellColor.White);
        AssertLoadedPivotFontIdentity(headerStyle);

        var valueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId);
        valueStyle.FillColor.Should().Be(new CellColor(224, 242, 250));
        valueStyle.NumberFormat.Should().Be("$#,##0.00");
        AssertLoadedPivotFontIdentity(valueStyle);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_AppliesPivotFontLayerOverExistingLoadedFills()
    {
        var workbook = new Workbook("LoadedPivotFontLayerStyleTest");
        var sheet = workbook.AddSheet("Data");
        var loadedHeaderFillStyle = workbook.RegisterStyle(new CellStyle
        {
            FontName = "Calibri",
            FontSize = 10,
            FontScheme = CellFontScheme.Minor,
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FillColor = new CellColor(21, 96, 130)
        });
        sheet.SetCell(Addr(sheet, "E2"), new Cell { Value = new TextValue("Row Labels"), StyleId = loadedHeaderFillStyle });
        sheet.SetCell(Addr(sheet, "F2"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = loadedHeaderFillStyle });
        sheet.SetCell(Addr(sheet, "E3"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "F3"), new NumberValue(1250));
        sheet.SetCell(Addr(sheet, "E4"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "F4"), new NumberValue(1250));
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotLoadedHeaderFill",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "E2", "F4"),
            LastRenderedRange = Range(sheet, "E2", "F4"),
            StyleName = "PivotStyleMedium9"
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        var rowHeaderStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        rowHeaderStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        rowHeaderStyle.Bold.Should().BeTrue();
        rowHeaderStyle.FontColor.Should().Be(CellColor.White);
        rowHeaderStyle.FontThemeColor.Should().BeNull();
        AssertLoadedPivotFontIdentity(rowHeaderStyle);

        var columnHeaderStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F2"))!.StyleId);
        columnHeaderStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        columnHeaderStyle.Bold.Should().BeTrue();
        columnHeaderStyle.FontColor.Should().Be(CellColor.White);
        columnHeaderStyle.FontThemeColor.Should().BeNull();
        AssertLoadedPivotFontIdentity(columnHeaderStyle);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_UsesEachSharedCachePivotOwnStyle()
    {
        var workbook = new Workbook("LoadedPivotSharedCacheStyleTest");
        var sheet = workbook.AddSheet("Data");
        var defaultThemeFontStyle = workbook.RegisterStyle(new CellStyle
        {
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1)
        });
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(4));

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("Row Labels"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Count of Sales"));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(4));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "B6"), new NumberValue(4));
        sheet.SetCell(Addr(sheet, "E4"), new Cell { Value = new TextValue("Row Labels"), StyleId = defaultThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F4"), new Cell { Value = new TextValue("Count of Sales"), StyleId = defaultThemeFontStyle });
        sheet.SetCell(Addr(sheet, "E5"), new Cell { Value = new TextValue("Hardware"), StyleId = defaultThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F5"), new Cell { Value = new NumberValue(4), StyleId = defaultThemeFontStyle });
        sheet.SetCell(Addr(sheet, "E6"), new Cell { Value = new TextValue("Grand Total"), StyleId = defaultThemeFontStyle });
        sheet.SetCell(Addr(sheet, "F6"), new Cell { Value = new NumberValue(4), StyleId = defaultThemeFontStyle });

        var firstPivot = new PivotTableModel
        {
            Name = "SharedCacheA",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "A4", "B6"),
            LastRenderedRange = Range(sheet, "A4", "B6"),
            StyleName = "PivotStyleMedium2"
        };
        firstPivot.RowFields.Add(new PivotFieldModel(0));
        firstPivot.DataFields.Add(new PivotDataFieldModel(1, "Count of Sales", "count"));
        var secondPivot = new PivotTableModel
        {
            Name = "SharedCacheB",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "E4", "F6"),
            LastRenderedRange = Range(sheet, "E4", "F6"),
            StyleName = "PivotStyleDark3"
        };
        secondPivot.RowFields.Add(new PivotFieldModel(0));
        secondPivot.DataFields.Add(new PivotDataFieldModel(1, "Count of Sales", "count"));
        sheet.PivotTables.Add(secondPivot);
        sheet.PivotTables.Add(firstPivot);

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(126, 53, 14));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(247, 199, 172));
    }

    private static void AssertLoadedPivotFontIdentity(CellStyle style)
    {
        style.FontName.Should().Be("Calibri");
        style.FontSize.Should().Be(10);
        style.FontScheme.Should().Be(CellFontScheme.Minor);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_ExcludesCompactGroupHeadersFromRowStripeBanding()
    {
        var workbook = new Workbook("LoadedPivotCompactGroupStripeTest");
        var sheet = workbook.AddSheet("Pivot");
        var parentStyle = workbook.RegisterStyle(new CellStyle());
        var childStyle = workbook.RegisterStyle(new CellStyle { IndentLevel = 1 });
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Row Labels"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Sum of Sales"));
        sheet.SetCell(Addr(sheet, "A4"), new Cell { Value = new TextValue("2026"), StyleId = parentStyle });
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(28730));
        sheet.SetCell(Addr(sheet, "A5"), new Cell { Value = new TextValue("Jan"), StyleId = childStyle });
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(6550));
        sheet.SetCell(Addr(sheet, "A6"), new Cell { Value = new TextValue("Feb"), StyleId = childStyle });
        sheet.SetCell(Addr(sheet, "B6"), new NumberValue(7135));
        sheet.SetCell(Addr(sheet, "A7"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "B7"), new NumberValue(28730));

        var pivot = new PivotTableModel
        {
            Name = "NativePivotDateGrouping",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "A3", "B7"),
            LastRenderedRange = Range(sheet, "A3", "B7"),
            StyleName = "PivotStyleMedium9",
            ReportLayout = PivotReportLayout.Compact,
            FirstDataRow = 1,
            FirstDataColumn = 1,
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(193, 229, 245));
        // A5 is a label column (col < firstDataColumn); Medium9 has BodyFill=null so the
        // col-gate prevents stripe from bleeding onto label cells — label stays un-filled.
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(CellColor.White, "label col with null BodyFill must not receive stripe fill");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A6"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_UsesNativeLocationOffsetsForHeaderAndColumnStripeFootprints()
    {
        var workbook = new Workbook("LoadedPivotNativeLocationStyleTest");
        var sheet = workbook.AddSheet("Pivot");
        var plainThemeStyle = workbook.RegisterStyle(new CellStyle
        {
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1)
        });
        sheet.SetCell(Addr(sheet, "A3"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "D4"), new Cell { Value = new TextValue("Hardware"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "E4"), new Cell { Value = new TextValue("Services"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "A5"), new Cell { Value = new TextValue("East"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "B5"), new Cell { Value = new TextValue("Direct"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "D5"), new Cell { Value = new NumberValue(2360), StyleId = plainThemeStyle });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotLayoutOptions",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D2"),
            TargetRange = Range(sheet, "A3", "F6"),
            LastRenderedRange = Range(sheet, "A3", "F6"),
            StyleName = "PivotStyleMedium9",
            FirstHeaderRow = 1,
            FirstDataRow = 2,
            FirstDataColumn = 2,
            ShowFieldHeaders = false,
            ShowColumnStripes = true
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(BlankValue.Instance);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        sheet.GetCell(Addr(sheet, "C5"))!.Value.Should().Be(BlankValue.Instance);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B5"))!.StyleId)
            .FillColor.Should().Be(CellColor.White, "native firstDataCol keeps row-label columns out of column striping while the loaded body layer still hides sheet gridlines");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "C5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(224, 242, 250));
    }

    [Fact]
    public void ApplyLoadedPivotStyles_AppliesMedium13BodyFillBeforeNativeStripes()
    {
        var workbook = new Workbook("LoadedPivotMedium13BodyFillTest");
        var sheet = workbook.AddSheet("Pivot");
        var plainThemeStyle = workbook.RegisterStyle(new CellStyle
        {
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1)
        });
        sheet.SetCell(Addr(sheet, "A3"), new Cell { Value = new TextValue("Sum of Sales"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "C4"), new Cell { Value = new TextValue("Hardware"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "A5"), new Cell { Value = new TextValue("East"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "B5"), new Cell { Value = new TextValue("Direct"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "C5"), new Cell { Value = new NumberValue(2360), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "A6"), new Cell { Value = new TextValue("East"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "B6"), new Cell { Value = new TextValue("Partner"), StyleId = plainThemeStyle });
        sheet.SetCell(Addr(sheet, "D6"), new Cell { Value = new NumberValue(980), StyleId = plainThemeStyle });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "NativePivotLayoutOptions",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D2"),
            TargetRange = Range(sheet, "A3", "F7"),
            LastRenderedRange = Range(sheet, "A3", "F7"),
            StyleName = "PivotStyleMedium13",
            FirstHeaderRow = 1,
            FirstDataRow = 2,
            FirstDataColumn = 2,
            ShowFieldHeaders = false,
            ShowRowStripes = true,
            ShowColumnStripes = true
        });

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent5, 0.85));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A6"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent5, 0.95));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "C6"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent5, 0.85));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "D6"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent5, 0.95));
    }

    [Fact]
    public void ApplyLoadedPivotStyles_KeepsPageFieldCaptionRowOutOfDarkHeaderFootprint()
    {
        var workbook = new Workbook("LoadedPivotVisibleCaptionsStyleTest");
        var sheet = workbook.AddSheet("Pivot");
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("Sum of Sales"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Column Labels"));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Row Labels"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Jan-26"));
        sheet.SetCell(Addr(sheet, "A7"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "B7"), new NumberValue(1250));
        var pivot = new PivotTableModel
        {
            Name = "NativePivotReportFilters",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "A5", "C8"),
            LastRenderedRange = Range(sheet, "A5", "C8"),
            StyleName = "PivotStyleMedium9",
            FirstHeaderRow = 1,
            FirstDataRow = 2,
            FirstDataColumn = 1,
            ShowFieldHeaders = true,
            ShowRowStripes = true
        };
        pivot.PageFields.Add(new PivotFieldModel(0));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A6"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B6"))!.StyleId)
            .FillColor.Should().Be(CellColor.White);
    }

    [Fact]
    public void ApplyLoadedPivotStyles_UsesNativeLocationForPageFieldPivotHeaders()
    {
        var workbook = new Workbook("LoadedPivotNativePageFieldHeaderStyleTest");
        var sheet = workbook.AddSheet("Pivot");
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("(Multiple Items)"));
        sheet.SetCell(Addr(sheet, "E2"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "F2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("Sum of Sales"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Column Labels"));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("Row Labels"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Jan-26"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Apr-26"));
        sheet.SetCell(Addr(sheet, "D5"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "B6"), new NumberValue(1250));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(1310));
        sheet.SetCell(Addr(sheet, "D6"), new NumberValue(2560));
        sheet.SetCell(Addr(sheet, "A8"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "D8"), new NumberValue(4700));

        var pivot = new PivotTableModel
        {
            Name = "NativePivotReportFilters",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D2"),
            TargetRange = Range(sheet, "A4", "D8"),
            LastRenderedRange = Range(sheet, "A4", "D8"),
            StyleName = "PivotStyleMedium9",
            FirstDataRow = 2,
            FirstDataColumn = 1,
            PageOverThenDown = true,
            PageWrap = 2,
            ShowFieldHeaders = true,
            ShowRowStripes = true
        };
        pivot.PageFields.Add(new PivotFieldModel(0));
        pivot.PageFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.ColumnFields.Add(new PivotFieldModel(3));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(21, 96, 130));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A6"))!.StyleId)
            .FillColor.Should().NotBe(new CellColor(21, 96, 130));
    }

    [Fact]
    public void ApplyLoadedPivotStyles_UsesMedium12OutlineGroupSubtotalAndGrandTotalSurfaces()
    {
        var workbook = new Workbook("LoadedPivotMedium12OutlineStyleTest");
        var sheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotSubtotalGrandTotals",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "G12"),
            TargetRange = Range(sheet, "A3", "E22"),
            LastRenderedRange = Range(sheet, "A3", "E22"),
            ReportLayout = PivotReportLayout.Outline,
            FirstDataRow = 2,
            FirstDataColumn = 2,
            StyleName = "PivotStyleMedium12",
            ShowRowHeaders = true,
            ShowColumnHeaders = true,
            ShowRowStripes = false,
            ShowColumnStripes = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(6, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Sum of Sales"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Direct"));
        sheet.SetCell(Addr(sheet, "C6"), new NumberValue(2360));
        sheet.SetCell(Addr(sheet, "B7"), new TextValue("Partner"));
        sheet.SetCell(Addr(sheet, "D7"), new NumberValue(980));
        sheet.SetCell(Addr(sheet, "A8"), new TextValue("East Total"));
        sheet.SetCell(Addr(sheet, "C8"), new NumberValue(2360));
        sheet.SetCell(Addr(sheet, "A22"), new TextValue("Grand Total"));
        sheet.SetCell(Addr(sheet, "C22"), new NumberValue(8080));

        PivotTableRefreshService.ApplyLoadedPivotStyles(workbook).Should().BeTrue();

        var headerFill = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4);
        var groupFill = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4, 0.8);
        var subtotalFill = workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent4, 0.7);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A3"))!.StyleId)
            .FillColor.Should().Be(headerFill);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A5"))!.StyleId)
            .FillColor.Should().Be(groupFill, "Excel renders Medium12 outline parent rows lighter than subtotal rows");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B5"))!.StyleId)
            .FillColor.Should().Be(groupFill, "the loaded outline parent row surface must span blank row-field cells");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A8"))!.StyleId)
            .FillColor.Should().Be(subtotalFill);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B8"))!.StyleId)
            .FillColor.Should().Be(subtotalFill, "subtotal row surfaces span blank row-field cells");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A22"))!.StyleId)
            .FillColor.Should().BeNull("Medium12 grand totals keep Excel's white worksheet surface");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "B22"))!.StyleId)
            .FillColor.Should().BeNull("blank grand-total row-field cells stay visually white, not subtotal blue");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "A22"))!.StyleId)
            .Bold.Should().BeTrue();
    }

    [Theory]
    [InlineData("PivotStyleMedium3", WorkbookThemeColorSlot.Accent3)]
    [InlineData("PivotStyleMedium11", WorkbookThemeColorSlot.Accent3)]
    [InlineData("PivotStyleMedium5", WorkbookThemeColorSlot.Accent4)]
    [InlineData("PivotStyleMedium12", WorkbookThemeColorSlot.Accent4)]
    [InlineData("PivotStyleMedium6", WorkbookThemeColorSlot.Accent5)]
    [InlineData("PivotStyleMedium13", WorkbookThemeColorSlot.Accent5)]
    [InlineData("PivotStyleMedium7", WorkbookThemeColorSlot.Accent6)]
    [InlineData("pivotstylemedium14", WorkbookThemeColorSlot.Accent6)]
    public void Refresh_ResolvesAdditionalMediumPivotStylesFromWorkbookTheme(
        string styleName,
        WorkbookThemeColorSlot expectedSlot)
    {
        var theme = CreateDistinctPivotStyleTheme();
        var workbook = new Workbook("PivotStyleAdditionalMediumThemeRenderTest")
        {
            Theme = theme
        };
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

        var expectedHeaderFill = styleName.Equals("PivotStyleMedium5", StringComparison.OrdinalIgnoreCase) ||
                                 styleName.Equals("PivotStyleMedium6", StringComparison.OrdinalIgnoreCase) ||
                                 styleName.Equals("PivotStyleMedium7", StringComparison.OrdinalIgnoreCase)
            ? theme.ResolveColor(expectedSlot, -0.25)
            : theme.ResolveColor(expectedSlot);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(expectedHeaderFill);
        var expectedStripeFill = styleName.Equals("PivotStyleMedium13", StringComparison.OrdinalIgnoreCase)
            ? theme.ResolveColor(expectedSlot, 0.85)
            : theme.ResolveColor(expectedSlot, 0.9);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(expectedStripeFill);
        CellColor? expectedGrandTotalFill = styleName.Equals("PivotStyleMedium5", StringComparison.OrdinalIgnoreCase) ||
                                            styleName.Equals("PivotStyleMedium6", StringComparison.OrdinalIgnoreCase) ||
                                            styleName.Equals("PivotStyleMedium12", StringComparison.OrdinalIgnoreCase) ||
                                            styleName.Equals("PivotStyleMedium13", StringComparison.OrdinalIgnoreCase)
            ? null
            : styleName.Equals("PivotStyleMedium7", StringComparison.OrdinalIgnoreCase)
                ? theme.ResolveColor(expectedSlot, 0.8)
                : theme.ResolveColor(expectedSlot, 0.7);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(expectedGrandTotalFill);
    }

    [Theory]
    [InlineData("PivotStyleLight16", WorkbookThemeColorSlot.Accent4)]
    [InlineData("PivotStyleLight17", WorkbookThemeColorSlot.Accent2)]
    [InlineData("PivotStyleLight18", WorkbookThemeColorSlot.Accent3)]
    [InlineData("PivotStyleLight19", WorkbookThemeColorSlot.Accent4)]
    [InlineData("PivotStyleLight20", WorkbookThemeColorSlot.Accent5)]
    [InlineData("PivotStyleLight21", WorkbookThemeColorSlot.Accent6)]
    [InlineData("pivotstylelight21", WorkbookThemeColorSlot.Accent6)]
    public void Refresh_ResolvesSupportedLightPivotStyleFromWorkbookTheme(string styleName, WorkbookThemeColorSlot expectedSlot)
    {
        var workbook = new Workbook("PivotStyleLightThemeRenderTest")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(120, 40, 20))
                .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 130, 60))
                .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(40, 90, 180))
                .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(150, 45, 140))
                .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(80, 145, 35))
        };
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
            .FillColor.Should().Be(workbook.Theme.ResolveColor(expectedSlot, 0.8));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(expectedSlot, 0.95));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(workbook.Theme.ResolveColor(expectedSlot, 0.9));
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

        var pageCaptionStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId);
        pageCaptionStyle.Bold.Should().BeTrue();
        pageCaptionStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        var pageValueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F2"))!.StyleId);
        pageValueStyle.Bold.Should().BeTrue();
        pageValueStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
        var bodyHeaderStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        bodyHeaderStyle.Bold.Should().BeTrue();
        bodyHeaderStyle.FillColor.Should().Be(new CellColor(21, 96, 130));
    }

    private static WorkbookTheme CreateDistinctPivotStyleTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(120, 40, 20))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 130, 60))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(40, 90, 180))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(150, 45, 140))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(80, 145, 35));

    private static void AssertPivotTotalStyle(Workbook workbook, Sheet sheet, string a1, CellColor? expectedFill)
    {
        var cell = sheet.GetCell(Addr(sheet, a1));
        cell.Should().NotBeNull();
        var style = workbook.GetStyle(cell!.StyleId);
        style.Bold.Should().BeTrue();
        style.FillColor.Should().Be(expectedFill);
        style.BorderTop.Style.Should().Be(BorderStyle.Thin);
        style.BorderBottom.Style.Should().Be(BorderStyle.Thin);
    }
}
