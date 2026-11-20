using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>Rows or columns that were just inserted.</summary>
public sealed record InsertedBand(SheetId SheetId, bool Rows, uint Start, uint Count);

/// <summary>One choice on the Insert Options button's menu.</summary>
public sealed record InsertOptionsMenuItem(string Header, InsertedBandFormat Format);

/// <summary>
/// Excel's Insert Options button: after inserting rows or columns, a paintbrush button offers
/// Format Same As Above / Below (Left / Right for columns) or Clear Formatting for the new band.
/// </summary>
public static class InsertOptionsPlanner
{
    public const string ButtonAutomationId = "InsertOptionsButton";
    public const string ButtonToolTip = "Insert Options";
    public const double ButtonHeight = 20;

    public static IReadOnlyList<InsertOptionsMenuItem> Items(bool rows) =>
    [
        new(rows ? "Format Same As Above" : "Format Same As Left", InsertedBandFormat.SameAsBefore),
        new(rows ? "Format Same As Below" : "Format Same As Right", InsertedBandFormat.SameAsAfter),
        new("Clear Formatting", InsertedBandFormat.Clear),
    ];

    /// <summary>The band a just-run command inserted (or re-formatted), if it is an insert of rows or columns.</summary>
    public static InsertedBand? FromCommand(IWorkbookCommand? command) => command switch
    {
        InsertRowsCommand rows => new InsertedBand(rows.SheetId, true, rows.BeforeRow, rows.Count),
        InsertColumnsCommand columns => new InsertedBand(columns.SheetId, false, columns.BeforeCol, columns.Count),
        InsertedBandFormatCommand format => new InsertedBand(format.SheetId, format.Rows, format.Start, format.Count),
        CompositeWorkbookCommand composite => composite.Commands.Select(FromCommand).FirstOrDefault(band => band is not null),
        _ => null,
    };
}

/// <summary>
/// Follows the undo history like <see cref="PasteOptionsTracker"/>: the button shows right after an
/// insert of rows or columns (and after re-formatting it from the button), until anything else runs.
/// </summary>
public sealed class InsertOptionsTracker
{
    private int _seenUndoDepth = -1;
    private long _seenUndoVersion = -1;

    public InsertedBand? Band { get; private set; }

    public InsertedBand? Update(WorkbookSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var depth = session.UndoStackDepth;
        var version = session.UndoStackVersion;
        if (depth == _seenUndoDepth && version == _seenUndoVersion)
            return Band;

        var pushed = _seenUndoDepth >= 0 && version != _seenUndoVersion && depth >= _seenUndoDepth && depth > 0;
        _seenUndoDepth = depth;
        _seenUndoVersion = version;
        Band = pushed ? InsertOptionsPlanner.FromCommand(session.PeekUndoCommand()) : null;
        return Band;
    }

    public void Dismiss() => Band = null;
}
