namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// The paste formats FreeW can currently honor for text clipboard content.
/// </summary>
public enum PasteSpecialOption
{
    KeepSourceFormatting,
    MergeFormatting,
    KeepTextOnly,
}

/// <summary>
/// Shared order and user-facing copy for the backed Paste Special choices.
/// </summary>
public readonly record struct PasteSpecialOptionChoice(
    string Label,
    string Description,
    PasteSpecialOption Option)
{
    public override string ToString() => Label;
}

public static class PasteSpecialOptionCatalog
{
    public static IReadOnlyList<PasteSpecialOptionChoice> Options { get; } =
    [
        new(
            "Keep Source Formatting",
            "Paste with the source's character and paragraph formatting.",
            PasteSpecialOption.KeepSourceFormatting),
        new(
            "Merge Formatting",
            "Paste text with the destination's formatting.",
            PasteSpecialOption.MergeFormatting),
        new(
            "Keep Text Only",
            "Paste as unformatted plain text.",
            PasteSpecialOption.KeepTextOnly),
    ];
}

/// <summary>
/// The metadata categories selected for removal by Document Inspector.
/// </summary>
public sealed record InspectorRemovalChoice(
    bool Comments,
    bool Revisions,
    bool Properties,
    bool Bookmarks)
{
    public bool Any => Comments || Revisions || Properties || Bookmarks;
}
