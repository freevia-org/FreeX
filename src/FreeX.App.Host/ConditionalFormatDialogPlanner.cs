using FreeX.Core.Model;
using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatDialogPlanner;

namespace FreeX.App.Host;

public static class ConditionalFormatDialogPlanner
{
    public static ConditionalFormat CloneRule(ConditionalFormat source) =>
        PresentationPlanner.CloneRule(source);

    public static string RuleTypeLabel(ConditionalFormat cf) =>
        PresentationPlanner.RuleTypeLabel(cf);
}
