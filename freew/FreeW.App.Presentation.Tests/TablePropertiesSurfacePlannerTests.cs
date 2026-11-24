using System.Reflection;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class TablePropertiesSurfacePlannerTests
{
    private static IEnumerable<string> FieldIds(IEnumerable<TablePropertiesSurfaceNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TablePropertiesFieldGridNode grid:
                    foreach (var row in grid.Rows)
                    {
                        if (row.ToggleAutomationId is { } toggle)
                            yield return toggle;
                        yield return row.FieldAutomationId;
                    }

                    break;
                case TablePropertiesControlNode control:
                    yield return control.AutomationId;
                    break;
                case TablePropertiesCheckStackNode checks:
                    foreach (var id in checks.AutomationIds)
                        yield return id;
                    break;
                case TablePropertiesExpanderNode expander:
                    foreach (var id in FieldIds(expander.Content))
                        yield return id;
                    break;
            }
        }
    }

    [Fact]
    public void Tabs_AreInTabKindOrder_WithThePlannerLabelsAndIds()
    {
        var tabs = TablePropertiesSurfacePlanner.Tabs;

        tabs.Select(tab => tab.Kind).Should().Equal(
            TablePropertiesDialogTabKind.Table,
            TablePropertiesDialogTabKind.Row,
            TablePropertiesDialogTabKind.Column,
            TablePropertiesDialogTabKind.Cell);
        tabs.Select(tab => (int)tab.Kind).Should().Equal(0, 1, 2, 3);
        tabs.Select(tab => tab.Label).Should().Equal(
            TablePropertiesDialogPlanner.TableTabLabel,
            TablePropertiesDialogPlanner.RowTabLabel,
            TablePropertiesDialogPlanner.ColumnTabLabel,
            TablePropertiesDialogPlanner.CellTabLabel);
        tabs.Select(tab => tab.AutomationId).Should().Equal(
            TablePropertiesDialogPlanner.TableTabAutomationId,
            TablePropertiesDialogPlanner.RowTabAutomationId,
            TablePropertiesDialogPlanner.ColumnTabAutomationId,
            TablePropertiesDialogPlanner.CellTabAutomationId);
    }

    [Fact]
    public void EveryFieldAndToggleIdIsPlacedExactlyOnce()
    {
        var placed = TablePropertiesSurfacePlanner.Tabs.SelectMany(tab => FieldIds(tab.Content)).ToList();
        var declared = typeof(TablePropertiesDialogPlanner)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, FieldType.Name: "String" })
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.EndsWith("Box", StringComparison.Ordinal))
            .ToList();

        placed.Should().OnlyHaveUniqueItems();
        placed.Should().BeEquivalentTo(declared);
        placed.Should().HaveCount(39);
    }

    [Fact]
    public void RowsCarryEitherALabelOrAToggle_AndOnlyTheCellPositionGridIsMarked()
    {
        var grids = new List<TablePropertiesFieldGridNode>();
        void Collect(IEnumerable<TablePropertiesSurfaceNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is TablePropertiesFieldGridNode grid)
                    grids.Add(grid);
                if (node is TablePropertiesExpanderNode expander)
                    Collect(expander.Content);
            }
        }

        foreach (var tab in TablePropertiesSurfacePlanner.Tabs)
            Collect(tab.Content);

        grids.SelectMany(grid => grid.Rows)
            .All(row => (row.Label == null) != (row.ToggleAutomationId == null))
            .Should().BeTrue();
        grids.Where(grid => grid.IsPositionGrid).Should().ContainSingle()
            .Which.Rows.Should().HaveCount(6);
    }

    [Theory]
    [InlineData(0, 0, 0, false, false, false)]
    [InlineData(1, 0, 0, true, true, true)]
    [InlineData(1, 1, 1, true, false, false)]
    public void ControlEnabledStates_FollowThePlannedFloatingState(
        int wrapping, int horizontalMode, int verticalMode, bool floating, bool horizontalOffset, bool verticalOffset)
    {
        var table = Table.Create(1, 1);
        var context = new ModelTableContext(table, table.Rows[0], table.Rows[0].Cells[0]);
        var session = new TablePropertiesDialogSession(context, System.Globalization.CultureInfo.InvariantCulture);

        var states = TablePropertiesSurfacePlanner
            .ControlEnabledStates(session.PlanEnabledState(wrapping, horizontalMode, verticalMode))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        states.Should().HaveCount(11);
        states[TablePropertiesDialogPlanner.AllowOverlapAutomationId].Should().Be(floating);
        states[TablePropertiesDialogPlanner.HorizontalAnchorAutomationId].Should().Be(floating);
        states[TablePropertiesDialogPlanner.DistanceRightAutomationId].Should().Be(floating);
        states[TablePropertiesDialogPlanner.HorizontalOffsetAutomationId].Should().Be(horizontalOffset);
        states[TablePropertiesDialogPlanner.VerticalOffsetAutomationId].Should().Be(verticalOffset);
    }

    [Theory]
    [InlineData("FreeW.App.Host", "TablePropertiesDialog.cs")]
    [InlineData("FreeW.App.Avalonia", "TableDialogs.cs")]
    public void Hosts_RenderTheSharedSurfaceSpecInsteadOfBuildingTabsByHand(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", project, fileName));
        var start = source.IndexOf("class TablePropertiesDialog", StringComparison.Ordinal);
        var dialog = source[start..];

        dialog.Should().Contain("TablePropertiesSurfacePlanner.Tabs");
        dialog.Should().Contain("TablePropertiesSurfacePlanner.ControlEnabledStates(");
        dialog.Should().NotContain("BuildTableTab");
        dialog.Should().NotContain("BuildRowTab");
        dialog.Should().NotContain("BuildColumnTab");
        dialog.Should().NotContain("BuildCellTab");
        dialog.Should().NotContain("BuildFloatingPositioningPanel");
        dialog.Should().NotContain("TablePropertiesDialogPlanner.TopLabel");
        dialog.Should().NotContain(".FloatingControlsEnabled");
    }
}