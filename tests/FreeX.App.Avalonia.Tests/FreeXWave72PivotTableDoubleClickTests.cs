using Avalonia;
using Avalonia.Headless;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class FreeXWave72PivotTableDoubleClickTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PivotValueDoubleClick_DrillsToDetailsBeforeInlineEditing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.ActiveSheet;
            SeedPivot(window.Session.Workbook, sheet);
            window.Session.SelectCell(new CellAddress(sheet.Id, 4, 6));

            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));

            try
            {
                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 6));
                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 4, 6),
                    new CellAddress(sheet.Id, 4, 6)));
                PivotUiPlanner.ResolveShowDetailsTarget(sheet, window.Session.SelectedRange)
                    .Should().NotBeNull();
                window.TryShowPivotTableDetailsFromDoubleClickForTest().Should().BeTrue();

                window.Session.Workbook.Sheets.Should().HaveCount(2);
                window.Session.ActiveSheet.Name.Should().StartWith("Detail");
                window.Session.ActiveSheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Category"));
                window.Session.ActiveSheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("A"));
                window.Session.ActiveSheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("Q1"));
                window.Session.ActiveSheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(10));
                window.InlineCellEditorTextForTest.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void DoubleClickSourceContract_PreservesWpfPivotPrecedenceAndSingleDispatch()
    {
        var avaloniaGridSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var avaloniaPivotSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotAnalyzeActions.cs"));
        var wpfSelectionSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        wpfSelectionSource.Should().Contain("if (!TryShowPivotTableDetails(showMessage: false))");
        avaloniaPivotSource.Should().Contain(
            "PivotUiPlanner.ResolveShowDetailsTarget(_session.ActiveSheet, _session.SelectedRange)");
        avaloniaPivotSource.Should().Contain(
            "new DrillDownPivotTableCommand(_session.ActiveSheet.Id, target.PivotTableName, target.PivotCell)");
        avaloniaGridSource.Should().Contain("if (!TryShowPivotTableDetailsFromDoubleClick(address))");
        avaloniaGridSource.Should().Contain("ConsumePivotDetailsDoubleClickSuppression(address)");

        var pointerDoubleClickStart = avaloniaGridSource.IndexOf(
            "if (point.Properties.IsLeftButtonPressed && IsCellDoubleClick(address, args.ClickCount))",
            StringComparison.Ordinal);
        var pointerDoubleClickEnd = avaloniaGridSource.IndexOf(
            "SelectClickedCell(address, args.KeyModifiers);",
            pointerDoubleClickStart,
            StringComparison.Ordinal);
        avaloniaGridSource[pointerDoubleClickStart..pointerDoubleClickEnd]
            .IndexOf("TryShowPivotTableDetailsFromDoubleClick(address)", StringComparison.Ordinal)
            .Should().BeGreaterThanOrEqualTo(0);

        var doubleTappedStart = avaloniaGridSource.IndexOf("border.DoubleTapped +=", StringComparison.Ordinal);
        var doubleTappedEnd = avaloniaGridSource.IndexOf(
            "if (Equals(_inlineCellEditAddress, address))",
            doubleTappedStart,
            StringComparison.Ordinal);
        avaloniaGridSource[doubleTappedStart..doubleTappedEnd]
            .IndexOf("TryShowPivotTableDetailsFromDoubleClick(address)", StringComparison.Ordinal)
            .Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void LinuxPhysicalContract_UsesRealDoubleClickAndExactDetailReadback()
    {
        var probe = File.ReadAllText(
            RepoFile("tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var runner = File.ReadAllText(
            RepoFile("tools", "Run-FreeXLinuxInteractionValidation.ps1"));

        runner.Should().Contain("\"pivot-table-details-double-click\"");
        runner.Should().Contain("\"pivot-table-details-double-click-physical\"");
        runner.Should().Contain("FreeX_wave50_pivot_fields.xlsx");
        runner.Should().Contain(
            "$PhysicalProbeSelector -in @(\"pivot-field-list\", \"pivot-table-details-double-click\")");
        probe.Should().Contain("probe_pivot_table_details_double_click()");
        probe.Should().Contain("xdotool click --repeat 2 --delay 180 1");
        probe.Should().Contain("pivot_detail_package_signature");
        probe.Should().Contain("clipboard readback Region|Category|Amount and North|Hardware|100");
    }

    private static void SeedPivot(Workbook workbook, Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 3)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 3, 5),
                new CellAddress(sheet.Id, 8, 8))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
