using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetOutlineTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task GroupedRows_RenderOutlineToggle_AndToggleCollapsedState() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        var outcome = window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 2, 4, 1));
        Assert.True(outcome.Success, outcome.ErrorMessage);

        var built = window.RebuildSheetGridForTest();
        Assert.NotNull(FindByAutomationId<Canvas>(built, "WorksheetOutlineOverlay"));
        var toggle = FindByAutomationId<Button>(built, "WorksheetRowOutlineToggle-L1-2-4");
        Assert.NotNull(toggle);
        Assert.Equal("Collapse outline group", AutomationProperties.GetName(toggle));

        toggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.All(new uint[] { 2, 3, 4 }, row => Assert.Contains(row, sheet.GroupHiddenRows));
        Assert.Contains(5u, sheet.CollapsedAnchorRows);

        var expandToggle = FindByAutomationId<Button>(
            window.RebuildSheetGridForTest(),
            "WorksheetRowOutlineToggle-L1-2-4");
        Assert.NotNull(expandToggle);
        Assert.Equal("Expand outline group", AutomationProperties.GetName(expandToggle));
        expandToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(new uint[] { 2, 3, 4 }, row => sheet.GroupHiddenRows.Contains(row));
        Assert.DoesNotContain(5u, sheet.CollapsedAnchorRows);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task GroupedColumns_RenderOutlineToggle_AndToggleCollapsedState() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        var outcome = window.Session.ExecuteReviewCommand(new GroupColumnsCommand(sheet.Id, 2, 4, 1));
        Assert.True(outcome.Success, outcome.ErrorMessage);

        var built = window.RebuildSheetGridForTest();
        Assert.NotNull(FindByAutomationId<Canvas>(built, "WorksheetOutlineOverlay"));
        var toggle = FindByAutomationId<Button>(built, "WorksheetColumnOutlineToggle-L1-2-4");
        Assert.NotNull(toggle);
        Assert.Equal("Collapse outline group", AutomationProperties.GetName(toggle));

        toggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.All(new uint[] { 2, 3, 4 }, column => Assert.Contains(column, sheet.GroupHiddenCols));
        Assert.Contains(5u, sheet.CollapsedAnchorCols);

        var expandToggle = FindByAutomationId<Button>(
            window.RebuildSheetGridForTest(),
            "WorksheetColumnOutlineToggle-L1-2-4");
        Assert.NotNull(expandToggle);
        Assert.Equal("Expand outline group", AutomationProperties.GetName(expandToggle));
        expandToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(new uint[] { 2, 3, 4 }, column => sheet.GroupHiddenCols.Contains(column));
        Assert.DoesNotContain(5u, sheet.CollapsedAnchorCols);
        window.Close();
    }, CancellationToken.None);

    private static MainWindow CreateCleanWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("OutlineFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Session.UpdateViewportSize(880, 1440);
        return window;
    }

    private static T? FindByAutomationId<T>(Control root, string automationId)
        where T : Control
    {
        if (root is T own && AutomationProperties.GetAutomationId(own) == automationId)
            return own;

        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
    }
}
