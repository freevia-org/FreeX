namespace FreeX.App.Presentation.TextToColumns;

/// <summary>
/// The dialog state captured from a Text-to-Columns dialog, in a form the portable planner can consume
/// without any UI types. Mirrors the controls the dialog exposes: the split mode, the delimiter
/// checkboxes (plus the "Other" character), treat-consecutive, the text qualifier, the fixed-width break
/// positions, and the per-output-column format hints. Each shell reads its own controls into this record
/// and then defers to <see cref="TextToColumnsDialogPlanner"/> for the app-neutral mapping.
/// </summary>
public sealed record TextToColumnsDialogState(
    TextToColumnsSplitMode SplitMode,
    bool Tab,
    bool Semicolon,
    bool Comma,
    bool Space,
    bool Other,
    // r200: string, not char -- see TextToColumnsDelimiters.CharacterFor.
    string? OtherDelimiter,
    bool TreatConsecutiveDelimitersAsOne,
    TextToColumnsTextQualifier TextQualifier,
    IReadOnlyList<int> FixedWidthBreakPositions,
    IReadOnlyList<TextToColumnsColumnFormat> ColumnFormats);
