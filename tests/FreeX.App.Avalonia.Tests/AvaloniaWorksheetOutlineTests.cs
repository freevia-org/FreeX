using Avalonia;
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

        window.AllowCloseWithoutDirtyPromptForParityCapture();

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

        window.AllowCloseWithoutDirtyPromptForParityCapture();

        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task NestedGroups_ArrangeBothOutlineLevelsInsideRenderedGrid() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 9, 13, 1, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 10, 11, 2, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupColumnsCommand(sheet.Id, 7, 11, 1, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupColumnsCommand(sheet.Id, 8, 10, 2, preserveExistingHierarchy: true)).Success);

        var built = window.RebuildSheetGridForTest();
        built.Measure(new Size(1440, 880));
        built.Arrange(new Rect(0, 0, 1440, 880));

        AssertControlArrangedInside(built, "WorksheetRowOutlineToggle-L1-9-13");
        AssertControlArrangedInside(built, "WorksheetRowOutlineToggle-L2-10-11");
        AssertControlArrangedInside(built, "WorksheetColumnOutlineToggle-L1-7-11");
        AssertControlArrangedInside(built, "WorksheetColumnOutlineToggle-L2-8-10");

        var innerRowToggle = FindByAutomationId<Button>(built, "WorksheetRowOutlineToggle-L2-10-11");
        innerRowToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(new uint[] { 10, 11 }, row => Assert.Contains(row, sheet.GroupHiddenRows));
        Assert.DoesNotContain(9u, sheet.GroupHiddenRows);
        Assert.DoesNotContain(12u, sheet.GroupHiddenRows);

        var innerColumnToggle = FindByAutomationId<Button>(
            window.RebuildSheetGridForTest(),
            "WorksheetColumnOutlineToggle-L2-8-10");
        innerColumnToggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.All(new uint[] { 8, 9, 10 }, column => Assert.Contains(column, sheet.GroupHiddenCols));
        Assert.DoesNotContain(7u, sheet.GroupHiddenCols);
        Assert.DoesNotContain(11u, sheet.GroupHiddenCols);

        window.AllowCloseWithoutDirtyPromptForParityCapture();

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

    private static void AssertControlArrangedInside(Control root, string automationId)
    {
        var control = FindByAutomationId<Control>(root, automationId);
        Assert.NotNull(control);
        Assert.True(control!.Bounds.Width > 0, $"{automationId} was not arranged horizontally.");
        Assert.True(control.Bounds.Height > 0, $"{automationId} was not arranged vertically.");

        var origin = control.TranslatePoint(default, root);
        Assert.NotNull(origin);
        Assert.InRange(origin.Value.X, 0, root.Bounds.Width - control.Bounds.Width);
        Assert.InRange(origin.Value.Y, 0, root.Bounds.Height - control.Bounds.Height);
    }
}
