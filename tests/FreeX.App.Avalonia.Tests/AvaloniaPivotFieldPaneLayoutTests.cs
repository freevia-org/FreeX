using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins the docked PivotTable Fields pane to the layout of the WPF host's <c>PivotFieldListPane</c>
/// (MainWindow.xaml): 320 wide, a titled header with a close button, the field search box, the
/// checked field list, the five move buttons and the two-by-two area lists.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPivotFieldPaneLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task FieldPane_FollowsTheWpfPaneLayout() => Session.Dispatch(() =>
    {
        var window = CreatePivotWindow(out var pivot);
        try
        {
            var pane = window.PivotFieldPaneHostForTest;
            pane.IsVisible.Should().BeTrue();
            pane.Width.Should().Be(320);
            pane.BorderThickness.Should().Be(new global::Avalonia.Thickness(1, 0, 0, 0));

            var layout = pane.Child.Should().BeOfType<Grid>().Subject;
            layout.Margin.Should().Be(new global::Avalonia.Thickness(10));
            layout.RowDefinitions.Should().HaveCount(6);
            layout.RowDefinitions[3].Height.Should().Be(new GridLength(0.85, GridUnitType.Star));
            layout.RowDefinitions[5].Height.Should().Be(new GridLength(1.25, GridUnitType.Star));

            var header = ChildAtRow<DockPanel>(layout, 0);
            var close = header.Children.OfType<Button>().Should().ContainSingle().Subject;
            AutomationProperties.GetAutomationId(close).Should().Be("PivotFieldListCloseBtn");
            close.Width.Should().Be(22);
            close.Height.Should().Be(22);
            header.Children.OfType<TextBlock>().Should().ContainSingle().Which.FontSize.Should().Be(14);

            var search = ChildAtRow<TextBox>(layout, 2);
            AutomationProperties.GetAutomationId(search).Should().Be("PivotFieldListSearchBox");
            search.Height.Should().Be(24);

            // Every source field is listed with a check box that mirrors its placement.
            var available = ChildAtRow<Border>(layout, 3);
            AutomationProperties.GetAutomationId(available).Should().Be("PivotAvailableFieldsList");
            available.MinHeight.Should().Be(96);
            var checks = available.GetLogicalDescendants().OfType<CheckBox>().ToList();
            var placed = pivot.RowFields.Select(field => field.SourceFieldIndex)
                .Concat(pivot.ColumnFields.Select(field => field.SourceFieldIndex))
                .Concat(pivot.PageFields.Select(field => field.SourceFieldIndex))
                .Concat(pivot.DataFields.Select(field => field.SourceFieldIndex))
                .Distinct()
                .Count();
            checks.Should().NotBeEmpty();
            checks.Count(check => check.IsChecked == true).Should().Be(placed);

            var moveButtons = ChildAtRow<UniformGrid>(layout, 4);
            moveButtons.Columns.Should().Be(5);
            moveButtons.Children.OfType<Button>().Should().HaveCount(5)
                .And.OnlyContain(button => button.MinHeight == 24);

            var areas = ChildAtRow<Grid>(layout, 5);
            areas.ColumnDefinitions.Should().HaveCount(3);
            areas.ColumnDefinitions[1].Width.Should().Be(new GridLength(8));
            areas.Children.OfType<Border>()
                .Select(AutomationProperties.GetAutomationId)
                .Should()
                .Equal("PivotFiltersList", "PivotColumnsList", "PivotRowsList", "PivotValuesList");
            areas.Children.OfType<Border>().Should().OnlyContain(list => list.MinHeight == 80);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }, CancellationToken.None);

    [Fact]
    public Task CloseButton_HidesThePane() => Session.Dispatch(() =>
    {
        var window = CreatePivotWindow(out _);
        try
        {
            var pane = window.PivotFieldPaneHostForTest;
            var layout = (Grid)pane.Child!;
            var close = ChildAtRow<DockPanel>(layout, 0).Children.OfType<Button>().Single();

            close.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            window.PivotFieldPaneVisibleForTest.Should().BeFalse();
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }, CancellationToken.None);

    private static T ChildAtRow<T>(Grid grid, int row)
        where T : Control =>
        grid.Children.OfType<T>().Single(child => Grid.GetRow(child) == row);

    private static MainWindow CreatePivotWindow(out PivotTableModel pivot)
    {
        var fixturePath = TestWorkspaceFileLocator.Find(
            "tests",
            "FreeX.App.Avalonia.Tests",
            "Fixtures",
            "FreeX_wave50_pivot_fields.xlsx");
        var startup = new StartupWorkbookLoader().Load([fixturePath]);
        var session = new WorkbookSessionFactory().Create(
            startup,
            viewportHeight: 720,
            viewportWidth: 1120,
            includeObjects: true);
        var sheet = session.ActiveSheet;
        pivot = sheet.PivotTables.Single();
        var target = new CellAddress(sheet.Id, pivot.TargetRange.Start.Row, pivot.TargetRange.Start.Col);
        session.SelectCell(target);

        var window = new MainWindow([], session);
        window.SelectClickedCell(target, global::Avalonia.Input.KeyModifiers.None);
        return window;
    }
}
