using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Covers the outline-gutter level-number buttons ("1 2 3..." boxes above/left of the outline
/// brackets) -- distinct from <see cref="AvaloniaWorksheetOutlineTests"/>, which covers the
/// individual group +/- toggle buttons. Before the fix, <c>AddOutlineLevelButton</c> built a
/// non-interactive <c>Border</c> (<c>IsHitTestVisible = false</c>, no click handler), so these
/// buttons rendered but could never be clicked (outline-subtotal F1).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaWorksheetOutlineLevelButtonTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task RowOutlineLevelButton_IsAClickableButton_NotAnInertBorder() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 3, 5, 2, preserveExistingHierarchy: true)).Success);

        var built = window.RebuildSheetGridForTest();

        // This is the crux of the defect: the level button must be a real Button control that
        // participates in hit-testing, not a Border painted on top with clicks disabled.
        var levelButton = FindByAutomationId<Button>(built, "WorksheetRowOutlineLevel-1");
        Assert.NotNull(levelButton);
        Assert.True(levelButton!.IsHitTestVisible);
        Assert.Equal("Show outline level 1", AutomationProperties.GetName(levelButton));

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task RowOutlineLevelButton_Click_ShowsThroughClickedDepthAndCollapsesDeeper() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        // Outer group rows 2-6 at level 1; inner nested group rows 3-5 at level 2 -- mirrors a
        // 2-level Subtotal-style outline. Rows 2 and 6 are level-1-only; rows 3-5 are level 2.
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 3, 5, 2, preserveExistingHierarchy: true)).Success);

        var built = window.RebuildSheetGridForTest();
        var level1Button = FindByAutomationId<Button>(built, "WorksheetRowOutlineLevel-1");
        Assert.NotNull(level1Button);

        // Excel semantics: clicking level button N shows summary rows through depth N and
        // collapses everything nested deeper. Clicking "1" must hide the level-2 detail (3,4,5)
        // while leaving the level-1-only rows (2,6) visible.
        level1Button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.All(new uint[] { 3, 4, 5 }, row => Assert.Contains(row, sheet.GroupHiddenRows));
        Assert.DoesNotContain(2u, sheet.GroupHiddenRows);
        Assert.DoesNotContain(6u, sheet.GroupHiddenRows);

        // Clicking the deepest level button ("2") must reveal everything again.
        var level2Button = FindByAutomationId<Button>(window.RebuildSheetGridForTest(), "WorksheetRowOutlineLevel-2");
        Assert.NotNull(level2Button);
        level2Button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(sheet.GroupHiddenRows);

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task ColumnOutlineLevelButton_Click_ShowsThroughClickedDepthAndCollapsesDeeper() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupColumnsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true)).Success);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupColumnsCommand(sheet.Id, 3, 5, 2, preserveExistingHierarchy: true)).Success);

        var built = window.RebuildSheetGridForTest();
        var level1Button = FindByAutomationId<Button>(built, "WorksheetColumnOutlineLevel-1");
        Assert.NotNull(level1Button);

        level1Button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.All(new uint[] { 3, 4, 5 }, col => Assert.Contains(col, sheet.GroupHiddenCols));
        Assert.DoesNotContain(2u, sheet.GroupHiddenCols);
        Assert.DoesNotContain(6u, sheet.GroupHiddenCols);

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }, CancellationToken.None);

    /// <summary>
    /// No-regression sibling: the individual group +/- toggle button (a different affordance,
    /// wired up before this fix) must keep working unchanged alongside the newly-interactive
    /// level buttons in the same outline overlay.
    /// </summary>
    [Fact]
    public Task RowOutlineGroupToggle_StillTogglesIndependentlyOfLevelButtons() => Session.Dispatch(() =>
    {
        var window = CreateCleanWindow(out var sheet);
        Assert.True(window.Session.ExecuteReviewCommand(
            new GroupRowsCommand(sheet.Id, 2, 4, 1)).Success);

        var built = window.RebuildSheetGridForTest();
        var toggle = FindByAutomationId<Button>(built, "WorksheetRowOutlineToggle-L1-2-4");
        Assert.NotNull(toggle);
        Assert.Equal("Collapse outline group", AutomationProperties.GetName(toggle));

        toggle!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.All(new uint[] { 2, 3, 4 }, row => Assert.Contains(row, sheet.GroupHiddenRows));

        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }, CancellationToken.None);

    private static MainWindow CreateCleanWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("OutlineLevelButtonFixture");
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
