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
}
