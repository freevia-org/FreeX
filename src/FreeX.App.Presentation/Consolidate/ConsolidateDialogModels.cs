using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Consolidate;

public sealed record ConsolidateDialogResult(
    IReadOnlyList<GridRange> SourceRanges,
    CellAddress DestinationCell,
    ConsolidateFunction Function,
    bool UseTopRowLabels = false,
    bool UseLeftColumnLabels = false,
    bool CreateLinksToSourceData = false);

public sealed record ConsolidateDialogInitialState(
    string SourceReference,
    string DestinationReference);

public enum ConsolidateRangeSelectionTarget
{
    Reference,
    DestinationCell
}

public sealed record ConsolidateRangeSelectionRequest(
    ConsolidateRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public enum ConsolidateDialogIssueKind
{
    None,
    NoSourceRanges,
    InvalidSourceRange,
    MismatchedSourceSizes,
    InvalidDestinationCell,
    DuplicateSourceReference,
    NoOutput,
    OutsideWorksheetBounds
}

public readonly record struct ConsolidateDialogIssue(
    ConsolidateDialogIssueKind Kind,
    string? InvalidPart = null)
{
    public static ConsolidateDialogIssue None { get; } = new(ConsolidateDialogIssueKind.None);

    public bool HasIssue => Kind != ConsolidateDialogIssueKind.None;
}

public enum ConsolidateDialogMessageContext
{
    AddReference,
    FinalValidation
}

public enum ConsolidateDialogTextProfile
{
    Wpf,
    Avalonia
}

public enum ConsolidateDialogFocusTarget
{
    Reference,
    Destination
}

public sealed record ConsolidateApplyPlan(
    IReadOnlyList<GridRange> SourceRanges,
    CellAddress DestinationCell,
    ConsolidateOptions Options,
    ConsolidateResult Result,
    IReadOnlyList<(CellAddress Address, Cell NewCell)> Edits,
    IReadOnlyList<CellAddress> OverwriteTargets);
