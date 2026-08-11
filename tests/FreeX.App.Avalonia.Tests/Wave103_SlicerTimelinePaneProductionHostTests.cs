using System.Threading;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave103_SlicerTimelinePaneProductionHostTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Pane_UsesRealControlsForSlicerAndTimelineMutations_UndoRefreshCloseAndKeyboardTraversal()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                SeedFixture(window.Session.Workbook, window.Session.ActiveSheet);
                window.RefreshSlicerTimelinePaneForTest();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                window.SlicerTimelinePaneVisibleForTest.Should().BeTrue();
                var initialBuildCount = window.SlicerTimelinePaneBuildCountForTest;
                var host = window.SlicerTimelinePaneHostForTest;
                Find<Button>(host, "SlicerPaneTile_Region Slicer_West").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var slicer = window.Session.Workbook.Slicers.Single();
                slicer.SelectedItems.Should().Equal("West");
                window.SlicerTimelinePaneBuildCountForTest.Should().BeGreaterThan(initialBuildCount);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Z,
                    KeyModifiers = KeyModifiers.Control,
                    Source = window,
                });
                slicer.SelectedItems.Should().BeEmpty();
                window.SlicerTimelinePaneVisibleForTest.Should().BeTrue();

                host = window.SlicerTimelinePaneHostForTest;
                var start = Find<TextBox>(host, "TimelinePaneStart_Order Date Timeline");
                var end = Find<TextBox>(host, "TimelinePaneEnd_Order Date Timeline");
                start.Text = "2026-01-01";
                end.Text = "2026-01-31";
                Find<Button>(host, "TimelinePaneApply_Order Date Timeline").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var timeline = window.Session.Workbook.Timelines.Single();
                timeline.SelectedStartDate.Should().Be("2026-01-01");
                timeline.SelectedEndDate.Should().Be("2026-01-31");

                host = window.SlicerTimelinePaneHostForTest;
                Find<Button>(host, "TimelinePaneClear_Order Date Timeline").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                timeline.SelectedStartDate.Should().BeNull();
                timeline.SelectedEndDate.Should().BeNull();

                host = window.SlicerTimelinePaneHostForTest;
                var close = Find<Button>(host, "SlicerTimelinePaneCloseButton");
                close.Focus().Should().BeTrue();
                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Tab,
                    KeyModifiers = KeyModifiers.None,
                    Source = close,
                });
                window.SlicerTimelinePaneHostForTest.GetVisualDescendants().OfType<Control>()
                    .Should().Contain(control => control.IsFocused);

                await window.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Escape,
                    KeyModifiers = KeyModifiers.None,
                    Source = close,
                });
                window.SlicerTimelinePaneVisibleForTest.Should().BeFalse();

                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));
                window.SlicerTimelinePaneVisibleForTest.Should().BeFalse();
                window.RefreshSlicerTimelinePaneForTest();
                window.SlicerTimelinePaneVisibleForTest.Should().BeFalse("a dismissed pane remains closed after refresh");

                var replacement = window.Session.CreateSiblingView(720, 1120);
                window.ReplaceSession(replacement);
                window.RefreshSlicerTimelinePaneForTest();
                window.SlicerTimelinePaneVisibleForTest.Should().BeTrue(
                    "opening or creating another workbook must reset the previous session's dismissal state");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Pane_UsesSharedSourceSessionForTableAndBoundPivotCacheItems()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();
                SeedSourceResolutionFixture(window.Session.Workbook, window.Session.ActiveSheet);
                window.RefreshSlicerTimelinePaneForTest();

                var host = window.SlicerTimelinePaneHostForTest;
                FindLogical<Button>(host, "SlicerPaneTile_Team Slicer_Admin").Should().NotBeNull();
                FindLogical<Button>(host, "SlicerPaneTile_Team Slicer_Sales").Should().NotBeNull();
                FindLogical<Button>(host, "SlicerPaneTile_Market Slicer_East").Should().NotBeNull();
                FindLogical<Button>(host, "SlicerPaneTile_Market Slicer_West").Should().NotBeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string automationId)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static T FindLogical<T>(Control root, string automationId)
        where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static void SeedFixture(Workbook workbook, Sheet sheet)
    {
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Order Date");
        Set(sheet, 1, 3, "Sales");
        Set(sheet, 2, 1, "West");
        Set(sheet, 2, 2, DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        Set(sheet, 2, 3, 100);
        Set(sheet, 3, 1, "East");
        Set(sheet, 3, 2, DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        Set(sheet, 3, 3, 200);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 3, 3),
            TargetRange = Range(sheet, 5, 1, 8, 3),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 1, 0),
            new DrawingAnchorPoint(9, 0, 8, 0));
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            DrawingAnchor = anchor,
            SourceSheetName = sheet.Name,
        });
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Order Date Timeline",
            CacheName = "Timeline_OrderDate",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Order Date",
            StartDate = "2026-01-01",
            EndDate = "2026-02-02",
            DrawingAnchor = anchor,
            SourceSheetName = sheet.Name,
        });
    }

    private static void SeedSourceResolutionFixture(Workbook workbook, Sheet sheet)
    {
        Set(sheet, 1, 1, "Team");
        Set(sheet, 2, 1, "Sales");
        Set(sheet, 3, 1, "Admin");
        var table = new StructuredTableModel
        {
            Id = 5,
            Name = "Teams",
            Range = Range(sheet, 1, 1, 3, 1),
        };
        table.Columns.Add(new StructuredTableColumnModel(9, "Team"));
        sheet.StructuredTables.Add(table);

        var decoy = new PivotCacheModel { CacheId = 11 };
        decoy.Fields.Add(new PivotCacheFieldModel("Market", SharedItems: ["Wrong"]));
        workbook.PivotCaches.Add(decoy);
        var bound = new PivotCacheModel { CacheId = 12 };
        bound.Fields.Add(new PivotCacheFieldModel("Market", SharedItems: ["West", "East"]));
        workbook.PivotCaches.Add(bound);
        sheet.PivotTables.Add(new PivotTableModel { Name = "PivotTable1", CacheId = 12 });

        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(6, 0, 1, 0),
            new DrawingAnchorPoint(9, 0, 8, 0));
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Team Slicer",
            SourceTableId = 5,
            SourceTableColumnId = 9,
            SourceSheetName = sheet.Name,
            DrawingAnchor = anchor,
        });
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Market Slicer",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Market",
            SourceSheetName = sheet.Name,
            DrawingAnchor = anchor,
        });
    }

    private static void Set(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static void Set(Sheet sheet, uint row, uint col, DateTimeValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private static void Set(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
