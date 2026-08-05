using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisOperationExecutorTests
{
    [Theory]
    [InlineData("format.databars", "conditional-format-dialog")]
    [InlineData("format.clear", "clear-conditional-formatting")]
    [InlineData("chart.clusteredcolumn", "insert-chart")]
    [InlineData("chart.more", "chart-picker")]
    [InlineData("total.sum", "total")]
    [InlineData("table.table", "table")]
    [InlineData("table.pivottable", "pivot-table")]
    [InlineData("sparkline.line", "sparkline")]
    public async Task ExecuteAsync_DispatchesOperationKindsThroughPortableHandlerMap(
        string itemId,
        string expectedHandler)
    {
        var invoked = new List<string>();
        var operation = Plan(itemId, QuickAnalysisShellCapabilities.DialogBacked);

        await QuickAnalysisOperationExecutor.ExecuteAsync(operation, Handlers(invoked));

        invoked.Should().Equal(expectedHandler);
    }

    [Fact]
    public async Task ExecuteSelectionAsync_DoesNotDispatchDisabledItem()
    {
        var invoked = new List<string>();
        var item = BuildItem("total.sum", QuickAnalysisShellCapabilities.DialogBacked) with
        {
            Action = new QuickAnalysisShellAction(
                QuickAnalysisShellActionKind.Deferred,
                BuildItem("total.sum", QuickAnalysisShellCapabilities.DialogBacked).Action.Route)
        };

        var handled = await new QuickAnalysisShellSession().ExecuteSelectionAsync(item, Handlers(invoked));

        handled.Should().BeFalse();
        invoked.Should().BeEmpty();
    }

    private static QuickAnalysisOperationHandlers Handlers(ICollection<string> invoked)
    {
        Task Record(string name)
        {
            invoked.Add(name);
            return Task.CompletedTask;
        }

        return new QuickAnalysisOperationHandlers(
            OpenConditionalFormatDialogAsync: (_, _) => Record("conditional-format-dialog"),
            ApplyConditionalFormatAsync: _ => Record("apply-conditional-format"),
            ClearConditionalFormattingAsync: () => Record("clear-conditional-formatting"),
            InsertChartAsync: _ => Record("insert-chart"),
            OpenChartPickerAsync: () => Record("chart-picker"),
            ExecuteTotalAsync: _ => Record("total"),
            CreateTableAsync: () => Record("table"),
            CreatePivotTableAsync: () => Record("pivot-table"),
            InsertSparklineAsync: _ => Record("sparkline"),
            ShowDeferredAsync: _ => Record("deferred"));
    }

    private static QuickAnalysisHostOperation Plan(
        string itemId,
        QuickAnalysisShellCapabilities capabilities) =>
        QuickAnalysisHostOperationPlanner.Plan(BuildItem(itemId, capabilities));

    private static QuickAnalysisShellItemPlan BuildItem(
        string itemId,
        QuickAnalysisShellCapabilities capabilities)
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));
        return QuickAnalysisShellPlanner.BuildMenuPlan(
                QuickAnalysisPlanner.BuildDisplayModel(selection),
                capabilities,
                selection)
            .AllItems()
            .Single(item => item.Id == itemId);
    }
}
