using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class TableEditingRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersEveryOwnedActionAndPreparesEachExecution()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));

        var commands = builder.Build().Commands;
        TableEditingRibbonWorkflow.Actions.Should().OnlyHaveUniqueItems().And.HaveCount(35);
        TableEditingRibbonWorkflow.Actions.Should().OnlyContain(
            action => FreeWRibbonEditorExecutionProfile.TableActions.Contains(action));

        foreach (var action in TableEditingRibbonWorkflow.Actions)
        {
            commands.Should().ContainKey(action);
            commands[action].Execute(RibbonCommandContext.Empty);
        }

        events.Count(entry => entry == "prepare").Should().Be(TableEditingRibbonWorkflow.Actions.Count);
    }

    [Fact]
    public void SharedWorkflowOwnsAutoFitAlignmentAndTextDirectionMappings()
    {
        var events = new List<string>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();
        TableEditingRibbonWorkflow.Register(builder, CreatePorts(events));
        var commands = builder.Build().Commands;

        Execute(commands, FreeWRibbonCommandAction.TableAutofitContents);
        Execute(commands, FreeWRibbonCommandAction.TableAutofitWindow);
        Execute(commands, FreeWRibbonCommandAction.TableAutofitFixed);
        Execute(commands, FreeWRibbonCommandAction.CellAlignTopLeft);
        Execute(commands, FreeWRibbonCommandAction.CellAlignMiddleCenter);
        Execute(commands, FreeWRibbonCommandAction.CellAlignBottomRight);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionHorizontal);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionRotate90);
        Execute(commands, FreeWRibbonCommandAction.CellTextDirectionRotate270);

        events.Where(entry => entry != "prepare").Should().Equal(
            "autofit:Contents",
            "autofit:Window",
            "autofit:Fixed",
            "align:Top:Left",
            "align:Center:Center",
            "align:Bottom:Right",
            "direction:Horizontal",
            "direction:Rotate90",
            "direction:Rotate270");
    }

    [Fact]
    public void BothRenderersDelegateTableEditingPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableEditingRibbonWorkflow.Register(");
            source.Should().Contain("CreateTableEditingPorts(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableHeaderRow");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableSelectTable");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.TableAutofitContents");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CellAlignTopLeft");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.CellTextDirectionHorizontal");
        }
    }

    private static void Execute(
        IReadOnlyDictionary<FreeWRibbonCommandAction, IRibbonCommand> commands,
        FreeWRibbonCommandAction action) =>
        commands[action].Execute(RibbonCommandContext.Empty);

    private static TableEditingRibbonPorts CreatePorts(ICollection<string> events) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            ToggleHeaderRow: () => events.Add("toggle-header-row"),
            ToggleBandedRows: () => events.Add("toggle-banded-rows"),
            ToggleLastRow: () => events.Add("toggle-last-row"),
            ToggleFirstColumn: () => events.Add("toggle-first-column"),
            ToggleLastColumn: () => events.Add("toggle-last-column"),
            ToggleBandedColumns: () => events.Add("toggle-banded-columns"),
            ToggleGridlines: () => events.Add("toggle-gridlines"),
            SelectTable: () => events.Add("select-table"),
            SelectRow: () => events.Add("select-row"),
            SelectColumn: () => events.Add("select-column"),
            SelectCell: () => events.Add("select-cell"),
            InsertRowAbove: () => events.Add("insert-row-above"),
            InsertColumnLeft: () => events.Add("insert-column-left"),
            DeleteRow: () => events.Add("delete-row"),
            DeleteColumn: () => events.Add("delete-column"),
            DeleteTable: () => events.Add("delete-table"),
            SplitTable: () => events.Add("split-table"),
            DistributeRows: () => events.Add("distribute-rows"),
            DistributeColumns: () => events.Add("distribute-columns"),
            SetAutoFit: mode => events.Add($"autofit:{mode}"),
            SetCellAlignment: (vertical, horizontal) => events.Add($"align:{vertical}:{horizontal}"),
            SetCellTextDirection: direction => events.Add($"direction:{direction}"),
            ToggleRepeatHeaderRow: () => events.Add("toggle-repeat-header"));
}
