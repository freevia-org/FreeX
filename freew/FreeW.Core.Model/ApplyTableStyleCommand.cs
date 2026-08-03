namespace FreeW.Core.Model;

/// <summary>Applies a catalog table style and its border intent as one reversible document edit.</summary>
public sealed class ApplyTableStyleCommand(int blockIndex, DocumentTableStyle style) : IDocumentCommand
{
    private string? _previousStyleId;
    private TableFormatting? _previousFormatting;
    private bool _captured;
    private bool _applied;

    public string Label => "Apply Table Style";

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (blockIndex < 0
            || blockIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[blockIndex] is not Table table)
        {
            return;
        }

        if (!_captured)
        {
            _previousStyleId = table.TableStyleId;
            _previousFormatting = table.Formatting;
            _captured = true;
        }

        table.TableStyleId = style.WordStyleId;
        table.Formatting = table.Formatting with { Borders = style.Borders };
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied
            || _previousFormatting is null
            || blockIndex < 0
            || blockIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[blockIndex] is not Table table)
        {
            return;
        }

        table.TableStyleId = _previousStyleId;
        table.Formatting = _previousFormatting;
        _applied = false;
    }
}
