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
        firstBodyStyle.FillColor.Should().Be(new CellColor(232, 239, 242));
        var secondBodyStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        secondBodyStyle.FillColor.Should().BeNull();
        var stripedValueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F4"))!.StyleId);
        stripedValueStyle.FillColor.Should().Be(new CellColor(232, 239, 242));
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
            StyleName = "PivotStyleMedium9"
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
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Units", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E5").Should().Be("East Total");
        sheet.GetCell(Addr(sheet, "F5"))!.Value.Should().Be(BlankValue.Instance);
        AssertPivotTotalStyle(workbook, sheet, "E5", new CellColor(208, 223, 230));
        AssertPivotTotalStyle(workbook, sheet, "F5", new CellColor(208, 223, 230));
        AssertPivotTotalStyle(workbook, sheet, "G5", new CellColor(208, 223, 230));
        AssertPivotTotalStyle(workbook, sheet, "H5", new CellColor(208, 223, 230));
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
            StyleName = "PivotStyleMedium9"
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
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId).FillColor.Should().Be(new CellColor(19, 80, 27));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G5"))!.StyleId).FillColor.Should().Be(new CellColor(209, 225, 211));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "G9"))!.StyleId).FillColor.Should().Be(new CellColor(209, 225, 211));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).FillColor.Should().Be(new CellColor(232, 240, 233));
    }

    [Theory]
    [InlineData("PivotStyleMedium2", 126, 53, 14, 247, 199, 172)]
    [InlineData("PivotStyleLight16", 207, 236, 247, 243, 250, 253)]
    [InlineData("PivotStyleMedium10", 233, 113, 50, 253, 241, 234)]
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
            .FillColor.Should().Be(new CellColor(120, 40, 20));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(242, 234, 232));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(214, 190, 184));
    }

    [Fact]
    public void Refresh_AppliesMedium2BodyFillAndDarkGrandTotalStyle()
    {
        var workbook = new Workbook("PivotStyleMedium2BodyRenderTest");
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
            .FillColor.Should().Be(new CellColor(126, 53, 14));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(247, 199, 172));
        var totalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        totalStyle.FillColor.Should().Be(new CellColor(126, 53, 14));
        totalStyle.FontColor.Should().Be(CellColor.White);
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
            .FillColor.Should().Be(new CellColor(126, 53, 14));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(new CellColor(247, 199, 172));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(126, 53, 14));
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
    public void ApplyLoadedPivotStyles_UsesFirstSharedCacheStyleForLoadedCache()
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

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId)
            .FillColor.Should().Be(new CellColor(126, 53, 14));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId)
            .FillColor.Should().Be(new CellColor(247, 199, 172));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E6"))!.StyleId).FontColor.Should().Be(CellColor.White);
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
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(theme.ResolveColor(expectedSlot, 0.9));
        var expectedGrandTotalFill = styleName.Equals("PivotStyleMedium5", StringComparison.OrdinalIgnoreCase) ||
                                     styleName.Equals("PivotStyleMedium6", StringComparison.OrdinalIgnoreCase) ||
                                     styleName.Equals("PivotStyleMedium7", StringComparison.OrdinalIgnoreCase)
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
