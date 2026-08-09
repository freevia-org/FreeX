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
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        return handling == PrintRangeAndOrientationHandling.PreparedPdf
            ? new PrintSelectionHandoffPlan(
                selection,
                selection with
                {
                    PageRange = PrintPageRange.All,
                    Orientation = PrintOrientation.Document,
                })
            : new PrintSelectionHandoffPlan(new PrintSelection(), selection);
    }
}
