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
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_SuppressesPivotStyleFontComponentsWhenApplyFontFormatsIsFalse()
    {
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
        headerStyle.Bold.Should().BeFalse();
        headerStyle.FontColor.Should().Be(CellColor.Black);
        headerStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
    }

    [Fact]
    public void Refresh_SuppressesPivotStylePatternComponentsWhenApplyPatternFormatsIsFalse()
    {
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
        headerStyle.FillColor.Should().BeNull();
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.Thin);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId).FillColor.Should().BeNull();
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().BeNull();
    }

    [Fact]
    public void Refresh_SuppressesPivotStyleBorderComponentsWhenApplyBorderFormatsIsFalse()
    {
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
        headerStyle.BorderBottom.Style.Should().Be(BorderStyle.None);
        headerStyle.Bold.Should().BeTrue();
        headerStyle.FontColor.Should().Be(CellColor.White);
        headerStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
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

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E2"))!.StyleId)
            .FillColor.Should().Be(theme.ResolveColor(expectedSlot));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId)
            .FillColor.Should().Be(theme.ResolveColor(expectedSlot, 0.9));
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId)
            .FillColor.Should().Be(theme.ResolveColor(expectedSlot, 0.7));
    }

    [Theory]
    [InlineData("PivotStyleLight16", WorkbookThemeColorSlot.Accent1)]
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
        pageCaptionStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
        var pageValueStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "F2"))!.StyleId);
        pageValueStyle.Bold.Should().BeTrue();
        pageValueStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
        var bodyHeaderStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId);
        bodyHeaderStyle.Bold.Should().BeTrue();
        bodyHeaderStyle.FillColor.Should().Be(new CellColor(91, 155, 213));
    }

    private static WorkbookTheme CreateDistinctPivotStyleTheme() =>
        WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 80, 120))
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(120, 40, 20))
            .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(25, 130, 60))
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(40, 90, 180))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(150, 45, 140))
            .WithColor(WorkbookThemeColorSlot.Accent6, new CellColor(80, 145, 35));

}
