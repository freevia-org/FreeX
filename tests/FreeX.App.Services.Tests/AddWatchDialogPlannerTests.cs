using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class AddWatchDialogPlannerTests
{
    [Fact]
    public void DialogContract_UsesStableWindowsSizedSurfaceAndAutomationIds()
    {
        AddWatchDialogPlanner.TitleKey.Should().Be("AddWatch_Title");
        AddWatchDialogPlanner.SelectedRangeLabelKey.Should().Be("AddWatch_SelectedRangeLabel");
        AddWatchDialogPlanner.BodyTextKey.Should().Be("AddWatch_BodyText");
        AddWatchDialogPlanner.CancelButtonKey.Should().Be("Common_Cancel");
        AddWatchDialogPlanner.Width.Should().Be(360);
        AddWatchDialogPlanner.Height.Should().Be(170);
        AddWatchDialogPlanner.ButtonWidth.Should().Be(76);
        AddWatchDialogPlanner.RootMargin.Should().Be(12);
        AddWatchDialogPlanner.RangeBottomMargin.Should().Be(8);
        AddWatchDialogPlanner.ActionRowTopMargin.Should().Be(12);
        AddWatchDialogPlanner.ButtonMinWidth.Should().Be(84);
        AddWatchDialogPlanner.AvaloniaRangeBottomMargin.Should().Be(11);
        AddWatchDialogPlanner.AvaloniaActionRowTopMargin.Should().Be(14);
        AddWatchDialogPlanner.AvaloniaWpfClientRightInset.Should().Be(16);
        AddWatchDialogPlanner.ParitySelectedRangeText.Should().Be("Sheet1!$B$2");
        AddWatchDialogPlanner.DialogAutomationId.Should().Be("AddWatchDialog");
        AddWatchDialogPlanner.SelectedRangeAutomationId.Should().Be("AddWatchSelectedRangeBox");
        AddWatchDialogPlanner.AddButtonAutomationId.Should().Be("AddWatchAddButton");
        AddWatchDialogPlanner.CancelButtonAutomationId.Should().Be("AddWatchCancelButton");
    }
}

public sealed class WatchWindowDialogPlannerTests
{
    [Fact]
    public void DialogContract_UsesStableWindowsSizedSurfaceAndColumns()
    {
        WatchWindowDialogPlanner.TitleKey.Should().Be("WatchWindow_WatchWindow");
        WatchWindowDialogPlanner.DialogAutomationId.Should().Be("WatchWindowDialog");
        WatchWindowDialogPlanner.BookColumnWidth.Should().Be(90);
        WatchWindowDialogPlanner.SheetColumnWidth.Should().Be(110);
        WatchWindowDialogPlanner.NameColumnWidth.Should().Be(80);
        WatchWindowDialogPlanner.CellColumnWidth.Should().Be(70);
        WatchWindowDialogPlanner.ValueColumnWidth.Should().Be(120);
        WatchWindowDialogPlanner.FormulaColumnWidth.Should().Be(170);
        WatchWindowDialogPlanner.ColumnsWidth.Should().Be(640);
        WatchWindowDialogPlanner.ChromeAndPaddingWidth.Should().Be(120);
        WatchWindowDialogPlanner.Width.Should().Be(760);
        WatchWindowDialogPlanner.Height.Should().Be(320);
        WatchWindowDialogPlanner.MinWidth.Should().Be(720);
        WatchWindowDialogPlanner.MinHeight.Should().Be(220);
    }

    [Fact]
    public void CreateRows_ProjectsDisplayFieldsAndPreservesEntryOrder()
    {
        var sheetId = SheetId.New();
        var laterAddress = new CellAddress(sheetId, 4, 2);
        var earlierAddress = new CellAddress(sheetId, 1, 1);
        var entries = new[]
        {
            new WatchWindowEntry(sheetId, "Totals", laterAddress, "120", "=SUM(A1:A3)"),
            new WatchWindowEntry(sheetId, "Input", earlierAddress, "Ready", null),
        };

        WatchWindowDialogPlanner.CreateRows(entries, "This Workbook")
            .Should()
            .Equal(
                new WatchWindowRowPlan(
                    "This Workbook",
                    "Totals",
                    string.Empty,
                    "B4",
                    "120",
                    "=SUM(A1:A3)",
                    laterAddress),
                new WatchWindowRowPlan(
                    "This Workbook",
                    "Input",
                    string.Empty,
                    "A1",
                    "Ready",
                    string.Empty,
                    earlierAddress));
    }
}
