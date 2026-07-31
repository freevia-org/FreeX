namespace FreeP.Core.Model;

/// <summary>
/// A table carried by a rich-text object-replacement run. Unlike a slide table, this table
/// has no slide geometry of its own; its placement is owned by the containing paragraph.
/// The cell text bodies may themselves contain inline table runs.
/// </summary>
public sealed class InlineTableInfo
{
    public TableShape Table { get; set; } = new();

    /// <summary>Creates a detached copy suitable for clipboard or edit-buffer ownership.</summary>
    public InlineTableInfo Clone() => new()
    {
        Table = PresentationModelCloneHelper.CloneTable(Table),
    };
}
