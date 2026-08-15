using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Owns the renderer-neutral transaction behind Table Design &gt; Table Styles live preview. The first
/// hover freezes the target table and its complete style/formatting baseline; subsequent hovers restore
/// that baseline before applying another temporary style. Cancel never enters undo history, while commit
/// restores the baseline and delegates one reversible edit to <see cref="DocumentTableEditingCoordinator"/>.
/// </summary>
public sealed class DocumentTableStylePreviewSession
{
    private readonly DocumentEditingSession _session;
    private DocumentTableCellAddress? _target;
    private string? _styleIdBaseline;
    private TableFormatting? _formattingBaseline;

    internal DocumentTableStylePreviewSession(DocumentEditingSession session) => _session = session;

    public bool HasActivePreview => _formattingBaseline is not null;

    public DocumentTableCellAddress? ActiveTarget => _target;

    public bool Preview(DocumentTableCellAddress target, DocumentTableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (_formattingBaseline is null)
        {
            if (!TryGetTable(target.BlockIndex, out var initialTable))
                return false;

            _target = target;
            _styleIdBaseline = initialTable.TableStyleId;
            _formattingBaseline = initialTable.Formatting;
        }
        else
        {
            RestoreBaseline();
        }

        if (_target is not { } captured || !TryGetTable(captured.BlockIndex, out var table))
        {
            Clear();
            return false;
        }

        table.TableStyleId = style.WordStyleId;
        table.Formatting = table.Formatting with { Borders = style.Borders };
        return true;
    }

    /// <summary>Restores the exact pre-hover table state and returns the frozen target.</summary>
    public DocumentTableCellAddress? Cancel()
    {
        if (_formattingBaseline is null)
            return null;

        var target = _target;
        RestoreBaseline();
        Clear();
        return target;
    }

    /// <summary>
    /// Commits to the first-hover target when preview is active, or to <paramref name="currentTarget"/>
    /// for keyboard/direct execution. The result always comes from the shared undoable table coordinator.
    /// </summary>
    public DocumentTableEditResult Commit(
        DocumentTableCellAddress currentTarget,
        DocumentTableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var target = _target ?? currentTarget;
        if (_formattingBaseline is not null)
            RestoreBaseline();
        Clear();
        return _session.Tables.ApplyStyle(target, style);
    }

    private void RestoreBaseline()
    {
        if (_target is not { } target
            || _formattingBaseline is not { } formatting
            || !TryGetTable(target.BlockIndex, out var table))
        {
            return;
        }

        table.TableStyleId = _styleIdBaseline;
        table.Formatting = formatting;
    }

    private bool TryGetTable(int blockIndex, out Table table)
    {
        if (blockIndex >= 0
            && blockIndex < _session.Document.Blocks.Count
            && _session.Document.Blocks[blockIndex] is Table candidate)
        {
            table = candidate;
            return true;
        }

        table = null!;
        return false;
    }

    private void Clear()
    {
        _target = null;
        _styleIdBaseline = null;
        _formattingBaseline = null;
    }
}
