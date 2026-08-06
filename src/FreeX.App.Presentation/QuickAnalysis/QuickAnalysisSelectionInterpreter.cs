using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

public enum QuickAnalysisSelectionEligibility
{
    Eligible,
    SingleCell,
    WholeColumns,
    WholeRows,
    TooLarge
}

public sealed record QuickAnalysisSelectionInterpretation(
    QuickAnalysisSelectionEligibility Eligibility,
    QuickAnalysisSelectionDescription? Description)
{
    public bool IsEligible => Eligibility == QuickAnalysisSelectionEligibility.Eligible && Description is not null;
}

/// <summary>
/// Owns the practical selection boundary for Quick Analysis before any dense cell inspection occurs.
/// Whole-axis and unusually large selections remain valid worksheet selections, but are not suitable
/// for an interactive analysis popup.
/// </summary>
public static class QuickAnalysisSelectionInterpreter
{
    public const long MaximumAnalyzedCellCount = 1_000_000;

    public static QuickAnalysisSelectionInterpretation Interpret(Sheet sheet, GridRange selection)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var eligibility = ClassifyEligibility(selection);
        return eligibility == QuickAnalysisSelectionEligibility.Eligible
            ? new QuickAnalysisSelectionInterpretation(
                eligibility,
                QuickAnalysisSelectionReader.Describe(sheet, selection))
            : new QuickAnalysisSelectionInterpretation(eligibility, Description: null);
    }

    public static QuickAnalysisSelectionEligibility ClassifyEligibility(GridRange selection)
    {
        if (selection.CellCount <= 1)
            return QuickAnalysisSelectionEligibility.SingleCell;
        if (selection.Start.Row == 1 && selection.End.Row == CellAddress.MaxRow)
            return QuickAnalysisSelectionEligibility.WholeColumns;
        if (selection.Start.Col == 1 && selection.End.Col == CellAddress.MaxCol)
            return QuickAnalysisSelectionEligibility.WholeRows;
        if (selection.CellCount > MaximumAnalyzedCellCount)
            return QuickAnalysisSelectionEligibility.TooLarge;

        return QuickAnalysisSelectionEligibility.Eligible;
    }
}
