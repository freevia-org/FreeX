using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R45-roundtrip-not-consumed-sweep-4: <see cref="PivotTableModel.AutofitColumnsOnUpdate"/>
/// round-tripped through PivotTable Options (<see cref="ConfigurePivotTableOptionsCommand"/>) and
/// XLSX I/O (reader/writer/NativeJsonAdapter), but no pivot refresh path ever consulted it -- toggling
/// "Autofit column widths on update" in Layout &amp; Format had zero effect on FreeX's actual refresh
/// behavior either way, unlike real Excel where it governs whether Refresh() resizes the pivot's
/// columns. <see cref="ConfigurePivotTableOptionsCommand.Apply"/> now honors the flag immediately
/// after its own <see cref="PivotTableRefreshService.Refresh"/> call: when true (Excel's default), the
/// pivot's freshly-rendered range is autofit to its content; when false, any manually-set column width
/// is left completely untouched, matching Excel.
/// </summary>
public sealed class R45_PivotAutofitColumnsOnUpdateConsumedTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateLongLabelPivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("SuperLongCategoryNameForWidthTest"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 4), new CellAddress(sheet.Id, 9, 6)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (workbook, sheet, pivot);
    }

    private static ConfigurePivotTableOptionsCommand CreateOptionsCommand(
        SheetId sheetId,
        string pivotTableName,
        bool autofitColumnsOnUpdate) =>
        new(
            sheetId,
            pivotTableName,
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleMedium9",
            autofitColumnsOnUpdate: autofitColumnsOnUpdate);

    // The bug case: AutofitColumnsOnUpdate=true (Excel's default) must actually resize the pivot's
    // row-label column to fit its freshly-refreshed content. Before the fix, ConfigurePivotTableOptionsCommand
    // never consulted this flag at all, so the column width was never touched regardless of its value.
    [Fact]
    public void ConfigureOptions_AutofitColumnsOnUpdateTrue_ResizesRowLabelColumnToFitContent()
    {
        var (workbook, sheet, pivot) = CreateLongLabelPivot("PivotAutofitOn");
        var ctx = new TestCommandContext(workbook);
        var labelColumn = pivot.TargetRange.Start.Col;
        sheet.ColumnWidths.Remove(labelColumn);

        var command = CreateOptionsCommand(sheet.Id, pivot.Name, autofitColumnsOnUpdate: true);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths.Should().ContainKey(labelColumn);
        sheet.ColumnWidths[labelColumn].Should().BeGreaterThan(sheet.DefaultColumnWidth,
            "the row-label column holds a long category name and AutofitColumnsOnUpdate=true must widen it on refresh, matching Excel");
    }

    // Sibling no-regression case: AutofitColumnsOnUpdate=false must leave a manually-set column width
    // completely untouched across a refresh triggered by the Options dialog, matching Excel's behavior
    // of not resizing columns on update when the checkbox is off.
    [Fact]
    public void ConfigureOptions_AutofitColumnsOnUpdateFalse_LeavesManualColumnWidthUntouched()
    {
        var (workbook, sheet, pivot) = CreateLongLabelPivot("PivotAutofitOff");
        var ctx = new TestCommandContext(workbook);
        var labelColumn = pivot.TargetRange.Start.Col;
        const double manualWidth = 5.0;
        sheet.ColumnWidths[labelColumn] = manualWidth;

        var command = CreateOptionsCommand(sheet.Id, pivot.Name, autofitColumnsOnUpdate: false);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths[labelColumn].Should().Be(manualWidth,
            "AutofitColumnsOnUpdate=false must leave the user's manually-set column width untouched, matching Excel");
    }

    // Undo must restore whatever column width existed before the autofit-on-update resize, not leave
    // the autofit width in place.
    [Fact]
    public void ConfigureOptions_AutofitColumnsOnUpdateTrue_RevertRestoresPreviousColumnWidth()
    {
        var (workbook, sheet, pivot) = CreateLongLabelPivot("PivotAutofitRevert");
        var ctx = new TestCommandContext(workbook);
        var labelColumn = pivot.TargetRange.Start.Col;
        const double manualWidth = 12.0;
        sheet.ColumnWidths[labelColumn] = manualWidth;

        var command = CreateOptionsCommand(sheet.Id, pivot.Name, autofitColumnsOnUpdate: true);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ColumnWidths[labelColumn].Should().NotBe(manualWidth);

        command.Revert(ctx);

        sheet.ColumnWidths[labelColumn].Should().Be(manualWidth,
            "reverting the Options command must restore the column width that existed before the autofit-on-update resize");
    }
}
