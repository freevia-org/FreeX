using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesEnableDrillAndUndoRestores()
    {
        var workbook = new Workbook("PivotEnableDrillOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            EnableDrill = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            enableDrill: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EnableDrill.Should().BeFalse();

        command.Revert(ctx);

        pivot.EnableDrill.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_ReplacesLayoutOptionsRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "H8"),
            ShowSubtotals = false,
            RepeatItemLabels = true,
            BlankLineAfterItems = false,
            StyleName = "PivotStyleLight16",
            ReportLayout = PivotReportLayout.Tabular,
            ShowRowHeaders = true,
            ShowColumnHeaders = true,
            ShowRowStripes = false,
            ShowColumnStripes = false,
            AltTextTitle = "Old title",
            AltTextDescription = "Old description"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: false,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "PivotStyleMedium9",
            reportLayout: PivotReportLayout.Compact,
            showRowHeaders: false,
            showColumnHeaders: false,
            showRowStripes: true,
            showColumnStripes: true,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "Sales pivot",
            altTextDescription: "Quarterly sales summary");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ShowRowGrandTotals.Should().BeFalse();
        pivot.ShowColumnGrandTotals.Should().BeFalse();
        pivot.ShowSubtotals.Should().BeTrue();
        pivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        pivot.RepeatItemLabels.Should().BeFalse();
        pivot.BlankLineAfterItems.Should().BeTrue();
        pivot.StyleName.Should().Be("PivotStyleMedium9");
        pivot.ReportLayout.Should().Be(PivotReportLayout.Compact);
        pivot.ShowRowHeaders.Should().BeFalse();
        pivot.ShowColumnHeaders.Should().BeFalse();
        pivot.ShowRowStripes.Should().BeTrue();
        pivot.ShowColumnStripes.Should().BeTrue();
        pivot.PrintTitles.Should().BeTrue();
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.AltTextTitle.Should().Be("Sales pivot");
        pivot.AltTextDescription.Should().Be("Quarterly sales summary");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A Total"));

        command.Revert(ctx);

        pivot.ShowRowGrandTotals.Should().BeTrue();
        pivot.ShowColumnGrandTotals.Should().BeTrue();
        pivot.ShowSubtotals.Should().BeFalse();
        pivot.RepeatItemLabels.Should().BeTrue();
        pivot.BlankLineAfterItems.Should().BeFalse();
        pivot.StyleName.Should().Be("PivotStyleLight16");
        pivot.ReportLayout.Should().Be(PivotReportLayout.Tabular);
        pivot.ShowRowHeaders.Should().BeTrue();
        pivot.ShowColumnHeaders.Should().BeTrue();
        pivot.ShowRowStripes.Should().BeFalse();
        pivot.ShowColumnStripes.Should().BeFalse();
        pivot.PrintTitles.Should().BeFalse();
        pivot.PrintExpandCollapseButtons.Should().BeFalse();
        pivot.AltTextTitle.Should().Be("Old title");
        pivot.AltTextDescription.Should().Be("Old description");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "E6"))!.Value.Should().Be(new TextValue("Grand Total"));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotOptionsCommandTest");
        sheet.IsProtected = true;

        var outcome = CreateBasicPivotOptionsCommand(sheet.Id, pivot.Name, showRowGrandTotals: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        pivot.ShowRowGrandTotals.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_AllowsProtectedSheetWithUsePivotReportsPermission()
    {
        var (sheet, ctx, pivot) = CreateBasicPivotReport("ProtectedPivotOptionsCommandTest");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.UsePivotTableReports);

        var outcome = CreateBasicPivotOptionsCommand(sheet.Id, pivot.Name, showRowGrandTotals: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        pivot.ShowRowGrandTotals.Should().BeFalse();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesEmptyValueTextRefreshesAndUndoRestores()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "N/A",
            updateEmptyValueText: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("N/A");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("N/A"));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new TextValue("N/A"));

        command.Revert(ctx);

        pivot.EmptyValueText.Should().BeNull();
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new NumberValue(0));
        sheet.GetCell(Addr(sheet, "F4"))!.Value.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesEmptyValueTextWhenCallerOmitsIt()
    {
        var workbook = new Workbook("PivotEmptyValueOptionsCompatibilityTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E2", "I7"),
            StyleName = "PivotStyleLight16",
            EmptyValueText = "-"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: false,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16");

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.EmptyValueText.Should().Be("-");
        sheet.GetCell(Addr(sheet, "G3"))!.Value.Should().Be(new TextValue("-"));
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesErrorCaptionAndUndoRestores()
    {
        var workbook = new Workbook("PivotErrorCaptionOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            StyleName = "PivotStyleLight16",
            ErrorCaption = "(old error)"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            errorCaption: "  #VALUE!  ",
            updateErrorCaption: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ErrorCaption.Should().Be("#VALUE!");

        command.Revert(ctx);

        pivot.ErrorCaption.Should().Be("(old error)");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesOldOptionalArgumentOrder()
    {
        var workbook = new Workbook("PivotOptionsArgumentOrderTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            StyleName = "PivotStyleLight16",
            ErrorCaption = "(preserved)"
        };
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        workbook.PivotCaches.Add(cache);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            true,
            true,
            false,
            PivotSubtotalPlacement.Bottom,
            false,
            false,
            "PivotStyleLight16",
            true,
            true,
            false,
            false,
            PivotReportLayout.Tabular,
            null,
            false,
            true,
            false,
            false);

        command.Apply(ctx).Success.Should().BeTrue();

        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeFalse();
        pivot.ErrorCaption.Should().Be("(preserved)");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesModeledAdvancedOptionsWhenCallerOmitsThem()
    {
        var workbook = new Workbook("PivotCompactIndentCompatibilityTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 5,
            MergeAndCenterLabels = true,
            PrintTitles = true,
            PrintExpandCollapseButtons = true,
            ShowExpandCollapseButtons = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            PageOverThenDown = true,
            PageWrap = 3,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            AltTextTitle = "Existing title",
            AltTextDescription = "Existing description"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            reportLayout: PivotReportLayout.Compact);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.CompactRowLabelIndent.Should().Be(5);
        pivot.MergeAndCenterLabels.Should().BeTrue();
        pivot.PrintTitles.Should().BeTrue();
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.ShowExpandCollapseButtons.Should().BeFalse();
        pivot.ShowContextualTooltips.Should().BeFalse();
        pivot.ShowPropertiesInTooltips.Should().BeFalse();
        pivot.ShowClassicLayout.Should().BeTrue();
        pivot.PageOverThenDown.Should().BeTrue();
        pivot.PageWrap.Should().Be(3);
        pivot.AutofitColumnsOnUpdate.Should().BeFalse();
        pivot.PreserveFormattingOnUpdate.Should().BeFalse();
        pivot.AltTextTitle.Should().Be("Existing title");
        pivot.AltTextDescription.Should().Be("Existing description");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "D4"))!.StyleId).IndentLevel.Should().Be(5);
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesShowExpandCollapseButtonsAndUndoRestores()
    {
        var workbook = new Workbook("PivotShowDrillOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ShowFieldHeaders = true,
            ShowExpandCollapseButtons = true,
            ShowContextualTooltips = true,
            ShowPropertiesInTooltips = true,
            ShowClassicLayout = false,
            MergeAndCenterLabels = false,
            PageOverThenDown = false,
            PageWrap = 0,
            PrintExpandCollapseButtons = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            mergeAndCenterLabels: true,
            showExpandCollapseButtons: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            pageOverThenDown: true,
            pageWrap: 4,
            printExpandCollapseButtons: false,
            showFieldHeaders: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.MergeAndCenterLabels.Should().BeTrue();
        pivot.ShowExpandCollapseButtons.Should().BeFalse();
        pivot.ShowContextualTooltips.Should().BeFalse();
        pivot.ShowPropertiesInTooltips.Should().BeFalse();
        pivot.ShowClassicLayout.Should().BeTrue();
        pivot.PageOverThenDown.Should().BeTrue();
        pivot.PageWrap.Should().Be(4);
        pivot.PrintExpandCollapseButtons.Should().BeFalse();
        pivot.ShowFieldHeaders.Should().BeFalse();

        command.Revert(ctx);

        pivot.MergeAndCenterLabels.Should().BeFalse();
        pivot.ShowExpandCollapseButtons.Should().BeTrue();
        pivot.ShowContextualTooltips.Should().BeTrue();
        pivot.ShowPropertiesInTooltips.Should().BeTrue();
        pivot.ShowClassicLayout.Should().BeFalse();
        pivot.PageOverThenDown.Should().BeFalse();
        pivot.PageWrap.Should().Be(0);
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.ShowFieldHeaders.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesShowItemsWithNoDataAndUndoRestores()
    {
        var workbook = new Workbook("PivotShowItemsWithNoDataCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ShowItemsWithNoDataOnRows = false,
            ShowItemsWithNoDataOnColumns = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showItemsWithNoDataOnRows: true,
            showItemsWithNoDataOnColumns: true);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.ShowItemsWithNoDataOnRows.Should().BeTrue();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeTrue();

        command.Revert(ctx);

        pivot.ShowItemsWithNoDataOnRows.Should().BeFalse();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeFalse();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesFormatOptionsAndUndoRestores()
    {
        var workbook = new Workbook("PivotFormatOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            AutofitColumnsOnUpdate = true,
            PreserveFormattingOnUpdate = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.AutofitColumnsOnUpdate.Should().BeFalse();
        pivot.PreserveFormattingOnUpdate.Should().BeFalse();

        command.Revert(ctx);

        pivot.AutofitColumnsOnUpdate.Should().BeTrue();
        pivot.PreserveFormattingOnUpdate.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesPivotCacheDataOptionsAndUndoRestores()
    {
        var workbook = new Workbook("PivotCacheOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        var ctx = new SimpleCtx(workbook);
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2",
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true,
            PreserveSourceSortFilter = true,
            MissingItemsLimit = null
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D2", "F5"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 0,
            updateMissingItemsLimit: true);

        command.Apply(ctx).Success.Should().BeTrue();

        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeFalse();
        cache.PreserveSourceSortFilter.Should().BeFalse();
        cache.MissingItemsLimit.Should().Be(0);

        command.Revert(ctx);

        cache.RefreshOnLoad.Should().BeFalse();
        cache.SaveData.Should().BeTrue();
        cache.EnableRefresh.Should().BeTrue();
        cache.PreserveSourceSortFilter.Should().BeTrue();
        cache.MissingItemsLimit.Should().BeNull();
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesCompactRowLabelIndentAndUndoRestores()
    {
        var workbook = new Workbook("PivotCompactIndentCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new SimpleCtx(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 1
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            reportLayout: PivotReportLayout.Compact,
            compactRowLabelIndent: 4);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.CompactRowLabelIndent.Should().Be(4);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "D4"))!.StyleId).IndentLevel.Should().Be(4);

        command.Revert(ctx);

        pivot.CompactRowLabelIndent.Should().Be(1);
    }
}
