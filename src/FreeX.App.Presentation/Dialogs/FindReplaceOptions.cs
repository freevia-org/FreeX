using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Dialogs;

/// <summary>
/// Portable dialog-backing model for the Find / Replace dialog's option row.
/// </summary>
/// <remarks>
/// The shared search service already owns the scope/order/look-in enums and a
/// <see cref="FreeX.Core.Commands.FindOptions"/> record that it consumes directly; this DTO does NOT
/// duplicate them. Instead it groups every choice the dialog surfaces — the service's three enums
/// (<see cref="FindLookIn"/> / <see cref="FindWithin"/> / <see cref="FindSearchOrder"/>) plus the two
/// match toggles (<see cref="MatchCase"/> / <see cref="MatchEntireCell"/>) that the service takes as
/// separate parameters, plus the required-format placeholder — into one round-trippable value the
/// desktop hosts and renderers can bind to. <see cref="ToFindOptions"/> projects it back onto the
/// service record so nothing is reinvented.
/// </remarks>
public sealed record FindReplaceOptions(
    FindLookIn LookIn = FindLookIn.Values,
    FindWithin Within = FindWithin.Sheet,
    FindSearchOrder Search = FindSearchOrder.ByRows,
    bool MatchCase = false,
    bool MatchEntireCell = false,
    SheetId? CurrentSheetId = null,
    StyleDiff? RequiredFormat = null)
{
    /// <summary>True when a cell-format constraint is attached to the search.</summary>
    public bool HasFormatConstraint => RequiredFormat is not null;

    /// <summary>
    /// Projects this DTO onto the search service's <see cref="FreeX.Core.Commands.FindOptions"/> record.
    /// The match toggles are not part of that record — the service takes them as separate arguments — so
    /// callers pass <see cref="MatchCase"/> / <see cref="MatchEntireCell"/> alongside the result.
    /// </summary>
    public FindOptions ToFindOptions() => new(
        Within: Within,
        CurrentSheetId: CurrentSheetId,
        SearchOrder: Search,
        LookIn: LookIn,
        RequiredFormat: RequiredFormat);

    /// <summary>
    /// Rebuilds the dialog DTO from a service <see cref="FreeX.Core.Commands.FindOptions"/> record plus the
    /// two match toggles that live outside it, so a host can restore the option row from persisted state.
    /// </summary>
    public static FindReplaceOptions FromFindOptions(
        FindOptions options,
        bool matchCase = false,
        bool matchEntireCell = false) => new(
        LookIn: options.LookIn,
        Within: options.Within,
        Search: options.SearchOrder,
        MatchCase: matchCase,
        MatchEntireCell: matchEntireCell,
        CurrentSheetId: options.CurrentSheetId,
        RequiredFormat: options.RequiredFormat);
}
