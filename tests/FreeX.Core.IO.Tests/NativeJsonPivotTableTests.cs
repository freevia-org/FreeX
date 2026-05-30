using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonPivotTableTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrip_PivotTableMetadata()
    {
        var workbook = new Workbook("PivotNativeJson");
        var dataSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        SeedSourceData(dataSheet);
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:D4",
            RefreshOnLoad = false,
            SaveData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 0,
            RecordCount = 3,
            CreatedVersion = 6,
            MinRefreshableVersion = 3,
            RefreshedVersion = 8,
            RefreshedBy = "FreeX",
            RefreshedDateIso = "2026-05-30T20:00:00Z"
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["East", "West"]));
        cache.Fields.Add(new PivotCacheFieldModel(
            "Quarter",
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["Q1", "Q2"]));
        cache.Fields.Add(new PivotCacheFieldModel(
            "Channel",
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["Retail", "Online"]));
        cache.Fields.Add(new PivotCacheFieldModel(
            "Amount",
            NumberFormatId: 4,
            SharedItemCount: 3,
            ContainsNumber: true,
            MinValue: 10,
            MaxValue: 25,
            SharedItems: ["10", "15", "25"]));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(dataSheet, "A1", "D4"),
            TargetRange = Range(pivotSheet, "B3", "H12"),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            CreatedVersion = 6,
            UpdatedVersion = 8,
            MinRefreshableVersion = 3,
            DataOnRows = false,
            FirstHeaderRow = 2,
            FirstDataRow = 3,
            FirstDataColumn = 2,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            ReportLayout = PivotReportLayout.Outline,
            CompactRowLabelIndent = 3,
            StyleName = "PivotStyleMedium9",
            ShowRowHeaders = false,
            ShowColumnHeaders = true,
            ShowRowStripes = true,
            ShowColumnStripes = true,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            ShowItemsWithNoDataOnRows = true,
            ShowItemsWithNoDataOnColumns = true,
            PageOverThenDown = true,
            PageWrap = 2,
            EmptyValueText = "(empty)",
            ApplyNumberFormats = false,
            ApplyBorderFormats = false,
            ApplyFontFormats = false,
            ApplyPatternFormats = false,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowExpandCollapseButtons = false,
            EnableDrill = false,
            AsteriskTotals = true,
            MultipleFieldFilters = false,
            EnableFieldDialog = false,
            EnableFieldProperties = false,
            EnableDataValueEditing = true,
            PrintTitles = true,
            PrintExpandCollapseButtons = true,
            AltTextTitle = "Pivot title",
            AltTextDescription = "Pivot description",
            DataCaption = "Values",
            GrandTotalCaption = "Total",
            MissingCaption = "(missing)",
            ErrorCaption = "(error)"
        };
        pivot.RowFields.Add(new PivotFieldModel(
            0,
            SelectedItems: ["East"],
            ShowAll: true,
            IncludeNewItemsInFilter: true,
            MultipleItemSelectionAllowed: true,
            DragToRow: true,
            DragToColumn: false,
            DragToPage: true,
            DragToData: false,
            ShowDropDowns: false));
        pivot.ColumnFields.Add(new PivotFieldModel(1, Grouping: PivotFieldGrouping.Month, GroupStart: 1, GroupEnd: 12, GroupInterval: 1));
        pivot.PageFields.Add(new PivotFieldModel(2, SelectedItem: "Retail", MultipleItemSelectionAllowed: false));
        pivot.DataFields.Add(new PivotDataFieldModel(
            3,
            "Average Amount",
            "average",
            NumberFormatId: 4,
            CalculatedFieldName: null,
            ShowValuesAs: PivotShowValuesAs.PercentOfRowTotal,
            BaseFieldIndex: null,
            BaseItem: null,
            NumberFormatCode: "#,##0.00"));
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*2"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "Ea"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 12, SourceFieldIndex: 0));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 0));
        pivotSheet.PivotTables.Add(pivot);

        var adapter = new NativeJsonAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);

        var loadedCache = loaded.PivotCaches.Should().ContainSingle().Subject;
        loadedCache.Should().Match<PivotCacheModel>(item =>
            item.CacheId == 7 &&
            item.SourceSheetName == "Data" &&
            item.SourceReference == "A1:D4" &&
            !item.RefreshOnLoad &&
            !item.SaveData &&
            !item.EnableRefresh &&
            !item.PreserveSourceSortFilter &&
            item.MissingItemsLimit == 0 &&
            item.RecordCount == 3 &&
            item.RefreshedBy == "FreeX");
        loadedCache.Fields.Select(field => field.Name).Should().Equal("Region", "Quarter", "Channel", "Amount");
        loadedCache.Fields[0].SharedItems.Should().Equal("East", "West");
        loadedCache.Fields[3].NumberFormatId.Should().Be(4);
        loadedCache.Fields[3].MinValue.Should().Be(10);
        loadedCache.Fields[3].MaxValue.Should().Be(25);

        var loadedDataSheet = loaded.GetSheet("Data")!;
        var loadedPivotSheet = loaded.GetSheet("Pivot")!;
        var loadedPivot = loadedPivotSheet.PivotTables.Should().ContainSingle().Subject;
        loadedPivot.Name.Should().Be("PivotTable1");
        loadedPivot.CacheId.Should().Be(7);
        loadedPivot.SourceRange.Should().Be(Range(loadedDataSheet, "A1", "D4"));
        loadedPivot.TargetRange.Should().Be(Range(loadedPivotSheet, "B3", "H12"));
        loadedPivot.PackagePart.Should().Be("xl/pivotTables/pivotTable1.xml");
        loadedPivot.ShowSubtotals.Should().BeTrue();
        loadedPivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        loadedPivot.ShowRowGrandTotals.Should().BeFalse();
        loadedPivot.ShowColumnGrandTotals.Should().BeTrue();
        loadedPivot.ReportLayout.Should().Be(PivotReportLayout.Outline);
        loadedPivot.CompactRowLabelIndent.Should().Be(3);
        loadedPivot.ShowClassicLayout.Should().BeTrue();
        loadedPivot.EmptyValueText.Should().Be("(empty)");
        loadedPivot.EnableDrill.Should().BeFalse();
        loadedPivot.AltTextTitle.Should().Be("Pivot title");
        loadedPivot.ErrorCaption.Should().Be("(error)");
        loadedPivot.RowFields.Should().BeEquivalentTo(pivot.RowFields, options => options.WithStrictOrdering());
        loadedPivot.ColumnFields.Should().BeEquivalentTo(pivot.ColumnFields, options => options.WithStrictOrdering());
        loadedPivot.PageFields.Should().BeEquivalentTo(pivot.PageFields, options => options.WithStrictOrdering());
        loadedPivot.DataFields.Should().BeEquivalentTo(pivot.DataFields, options => options.WithStrictOrdering());
        loadedPivot.CalculatedFields.Should().BeEquivalentTo(pivot.CalculatedFields, options => options.WithStrictOrdering());
        loadedPivot.CalculatedItems.Should().BeEquivalentTo(pivot.CalculatedItems, options => options.WithStrictOrdering());
        loadedPivot.LabelFilters.Should().BeEquivalentTo(pivot.LabelFilters, options => options.WithStrictOrdering());
        loadedPivot.ValueFilters.Should().BeEquivalentTo(pivot.ValueFilters, options => options.WithStrictOrdering());
        loadedPivot.Sorts.Should().BeEquivalentTo(pivot.Sorts, options => options.WithStrictOrdering());
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsPivotTablesWithInvalidRanges()
    {
        const string json = """
            {
              "Name": "InvalidPivotRanges",
              "PivotCaches": [
                { "CacheId": 1, "SourceSheetName": "Data", "SourceReference": "A1:B3" }
              ],
              "Sheets": [
                {
                  "Name": "Data",
                  "PivotTables": [
                    { "Name": "BadPivot", "CacheId": 1, "SourceSheetName": "Data", "SourceRange": "NotARange", "TargetRange": "D3:E6" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(stream);

        loaded.PivotCaches.Should().ContainSingle();
        loaded.GetSheet("Data")!.PivotTables.Should().BeEmpty();
    }

    private static void SeedSourceData(Sheet sheet)
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
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Online"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(25));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
