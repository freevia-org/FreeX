using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// AV-MAIL: the Avalonia shell's mail-merge session state, mirroring the WPF host's
/// <c>FreeWRibbonCommands.MailMergeSession</c>. Holds the active recipient data source, the
/// role→column mapping (for Address Block / Greeting Line composition), the merge output mode, and
/// the live preview cursor. The session is a thin holder over the portable <see cref="MailMerge"/>
/// engine — all substitution / composition / record-iteration logic lives in
/// <see cref="FreeW.Core.Model"/>; this just remembers what the user picked.
///
/// <para>
/// Unlike the WPF host, the Avalonia <see cref="Editing.DocumentView"/> has no separate "model commit"
/// step — its <see cref="Editing.DocumentView.Document"/> is always the live model. So the merge
/// template captured on first Preview is simply the current document; <see cref="Template"/> stashes it
/// so leaving the preview can restore the user's editable template.
/// </para>
/// </summary>
internal sealed class MailMergeSession
{
    /// <summary>The active recipient data source (null until Select Recipients loads one).</summary>
    public MergeData? Data { get; set; }

    /// <summary>The merge output mode (Letters: page break per record; Directory: continuous).</summary>
    public MailMergeOutputMode Mode { get; set; } = MailMergeOutputMode.Letters;

    /// <summary>
    /// Non-null only while a preview is active: the editable template document captured when the user
    /// first entered Preview Results, so stepping records re-renders from it and leaving preview restores it.
    /// </summary>
    public TextDocument? Template { get; set; }

    /// <summary>The 0-based index of the record currently shown in Preview Results.</summary>
    public int CurrentIndex { get; set; }

    /// <summary>
    /// Role→column mapping used to compose Address Block / Greeting Line. Null until data is loaded
    /// (Select Recipients seeds it via <see cref="MailMerge.AutoMatchFields"/>).
    /// </summary>
    public FieldMapping? Mapping { get; set; }

    /// <summary>True while a live preview is showing merged values (a template is stashed).</summary>
    public bool IsPreviewing => Template is not null;

    /// <summary>Reset all session state (Select Recipients re-seeds it for a new data source).</summary>
    public void Clear()
    {
        Data = null;
        Template = null;
        CurrentIndex = 0;
        Mode = MailMergeOutputMode.Letters;
        Mapping = null;
    }

    /// <summary>
    /// Build an augmented row that adds synthetic <c>«AddressBlock»</c> and <c>«GreetingLine»</c> keys so
    /// the standard substitution path resolves both composite placeholders per record. When no mapping is
    /// set the synthetic keys resolve to empty strings.
    /// </summary>
    public IReadOnlyDictionary<string, string> AugmentRow(
        IReadOnlyDictionary<string, string> row,
        string greetingFormat = "Dear")
    {
        ArgumentNullException.ThrowIfNull(row);
        var augmented = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
        var mapping = Mapping ?? new FieldMapping();
        if (!augmented.ContainsKey("AddressBlock"))
            augmented["AddressBlock"] = MailMerge.ComposeAddressBlock(row, mapping);
        if (!augmented.ContainsKey("GreetingLine"))
            augmented["GreetingLine"] = MailMerge.ComposeGreetingLine(row, mapping, greetingFormat);
        return augmented;
    }
}
