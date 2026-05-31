using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static class ChartAuthoringPlanner
{
    public const string DeferredAuthoringMessage =
        "This chart family is recognized for XLSX preservation but cannot be authored yet.";

    public static bool CanAuthor(ChartType chartType) =>
        ChartTypeSupport.IsAuthorable(chartType);

    public static CommandOutcome? RejectIfUnsupported(ChartType chartType) =>
        CanAuthor(chartType)
            ? null
            : new CommandOutcome(false, DeferredAuthoringMessage);
}
