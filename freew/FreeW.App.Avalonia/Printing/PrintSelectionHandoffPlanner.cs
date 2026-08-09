using Free.Shared.AppServices.Printing;

namespace FreeW.App.Avalonia.Printing;

internal sealed record PrintSelectionHandoffPlan(
    PrintSelection PdfSelection,
    PrintSelection SubmissionSelection);

internal static class PrintSelectionHandoffPlanner
{
    public static PrintSelectionHandoffPlan Build(
        PrintSelection selection,
        PrintRangeAndOrientationHandling handling)
    {
        var plan = FreeW.App.Presentation.Shell.FreeWPrintSelectionHandoffPlanner.Build(selection, handling);
        return new PrintSelectionHandoffPlan(plan.PdfSelection, plan.SubmissionSelection);
    }
}
