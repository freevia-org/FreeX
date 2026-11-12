using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>The three Word 97-2003 ("legacy") form-field kinds, keyed by their field instruction.</summary>
public enum LegacyFormFieldKind
{
    /// <summary><c>FORMTEXT</c> — a fill-in text box (<c>w:ffData/w:textInput</c>).</summary>
    Text,

    /// <summary><c>FORMCHECKBOX</c> — a check box (<c>w:ffData/w:checkBox</c>).</summary>
    CheckBox,

    /// <summary><c>FORMDROPDOWN</c> — a pick list (<c>w:ffData/w:ddList</c>).</summary>
    DropDown
}

/// <summary>Word's <c>ST_FFTextType</c>: what a text form field accepts and how it fills itself.</summary>
public enum LegacyFormTextType
{
    Regular,
    Number,
    Date,
    CurrentDate,
    CurrentTime,
    Calculated
}

/// <summary>
/// A form field's help (F1) or status-bar text (<c>w:helpText</c>/<c>w:statusText</c>). When
/// <see cref="IsAutoText"/> is true, <see cref="Value"/> names an AutoText entry rather than holding the text.
/// </summary>
public sealed record LegacyFormFieldHelpText(bool IsAutoText, string Value);

/// <summary>
/// The full <c>w:ffData</c> of a legacy form field — every element of Word's <c>CT_FFData</c> is modelled
/// (name, label, tab index, enabled, calculate-on-exit, entry/exit macros, help and status text, and the
/// kind-specific check-box / drop-down / text-input data), so a form round-trips without a raw XML fallback.
/// It hangs off <see cref="ComplexField.FormField"/>: the field stays an ordinary complex field
/// (instruction <c>FORMTEXT</c>/<c>FORMCHECKBOX</c>/<c>FORMDROPDOWN</c>), so field code display, locking and
/// unknown-field preservation keep working unchanged. The owning run's <see cref="Run.Text"/> is what the
/// field shows: the typed result for a text field, the box glyph for a check box and the chosen entry for a
/// drop-down.
/// </summary>
public sealed record LegacyFormField
{
    /// <summary>Word's longest form-field bookmark name.</summary>
    public const int MaxNameLength = 20;

    /// <summary>Word's limit on drop-down entries.</summary>
    public const int MaxDropDownEntries = 25;

    /// <summary>Word's limit on the length of one drop-down entry.</summary>
    public const int MaxDropDownEntryLength = 50;

    /// <summary>The largest maximum length Word accepts for a text form field.</summary>
    public const int MaxTextLength = 32767;

    /// <summary>
    /// What an empty text form field shows: five EN SPACEs, exactly the result Word writes for a blank field
    /// so the shaded box stays visible and clickable.
    /// </summary>
    public static readonly string BlankTextResult = new(' ', 5);

    public LegacyFormFieldKind Kind { get; init; }

    /// <summary>The field's bookmark name (<c>w:name</c>), e.g. <c>Text1</c>.</summary>
    public string? Name { get; init; }

    /// <summary><c>w:label</c>: the field's label id, when Word recorded one.</summary>
    public int? Label { get; init; }

    /// <summary><c>w:tabIndex</c>: the field's explicit tab order, when Word recorded one.</summary>
    public int? TabIndex { get; init; }

    /// <summary><c>w:enabled</c>: whether the field may be filled in ("Fill-in enabled" / "Check box enabled").</summary>
    public bool Enabled { get; init; } = true;

    /// <summary><c>w:calcOnExit</c>: recalculate the document's fields when leaving this one.</summary>
    public bool CalculateOnExit { get; init; }

    /// <summary><c>w:entryMacro</c>: preserved; FreeW does not run macros.</summary>
    public string? EntryMacro { get; init; }

    /// <summary><c>w:exitMacro</c>: preserved; FreeW does not run macros.</summary>
    public string? ExitMacro { get; init; }

    public LegacyFormFieldHelpText? HelpText { get; init; }

    public LegacyFormFieldHelpText? StatusText { get; init; }

    // ----- Text (w:textInput) -----

    public LegacyFormTextType TextType { get; init; }

    /// <summary><c>w:textInput/w:default</c>: the text a blank / reset field shows.</summary>
    public string? DefaultText { get; init; }

    /// <summary><c>w:maxLength</c>: null means unlimited.</summary>
    public int? MaxLength { get; init; }

    /// <summary><c>w:format</c>: a case rule (<c>UPPERCASE</c>...), number picture or date picture.</summary>
    public string? TextFormat { get; init; }

    // ----- Check box (w:checkBox) -----

    /// <summary>True for <c>w:sizeAuto</c> (or no size at all); false when <see cref="CheckBoxSizeHalfPoints"/> applies.</summary>
    public bool CheckBoxAutoSize { get; init; } = true;

    /// <summary><c>w:size</c> in half-points ("Exactly" size), or null for auto size.</summary>
    public int? CheckBoxSizeHalfPoints { get; init; }

    /// <summary><c>w:checkBox/w:default</c>: the reset state.</summary>
    public bool DefaultChecked { get; init; }

    /// <summary><c>w:checked</c>: the current state when Word recorded one; null means "the default".</summary>
    public bool? Checked { get; init; }

    // ----- Drop-down (w:ddList) -----

    public IReadOnlyList<string> DropDownEntries { get; init; } = [];

    /// <summary><c>w:ddList/w:result</c>: the chosen entry, or null for "the default".</summary>
    public int? DropDownResult { get; init; }

    /// <summary><c>w:ddList/w:default</c>: the entry a reset field shows.</summary>
    public int? DropDownDefault { get; init; }

    /// <summary>The check box state the field shows.</summary>
    public bool IsChecked => Checked ?? DefaultChecked;

    /// <summary>The drop-down entry index the field shows, clamped into the entry list (-1 when it is empty).</summary>
    public int SelectedIndex
    {
        get
        {
            if (DropDownEntries.Count == 0)
                return -1;
            var index = DropDownResult ?? DropDownDefault ?? 0;
            return index >= 0 && index < DropDownEntries.Count ? index : 0;
        }
    }

    /// <summary>The field instruction keyword for <see cref="Kind"/>.</summary>
    public string Keyword => KeywordFor(Kind);

    /// <summary>Whether the user can type into / toggle / pick this field (Word greys out computed text types).</summary>
    public bool IsFillable =>
        Enabled
        && !(Kind == LegacyFormFieldKind.Text
             && TextType is LegacyFormTextType.CurrentDate
                 or LegacyFormTextType.CurrentTime
                 or LegacyFormTextType.Calculated);

    public static string KeywordFor(LegacyFormFieldKind kind) => kind switch
    {
        LegacyFormFieldKind.CheckBox => "FORMCHECKBOX",
        LegacyFormFieldKind.DropDown => "FORMDROPDOWN",
        _ => "FORMTEXT"
    };

    /// <summary>The field instruction Word writes for <paramref name="kind"/>, e.g. <c> FORMTEXT </c>.</summary>
    public static string InstructionFor(LegacyFormFieldKind kind) => " " + KeywordFor(kind) + " ";

    /// <summary>Maps a field keyword to its form-field kind, or null for any other field.</summary>
    public static LegacyFormFieldKind? KindForKeyword(string? keyword) => keyword?.ToUpperInvariant() switch
    {
        "FORMTEXT" => LegacyFormFieldKind.Text,
        "FORMCHECKBOX" => LegacyFormFieldKind.CheckBox,
        "FORMDROPDOWN" => LegacyFormFieldKind.DropDown,
        _ => null
    };

    /// <summary>
    /// What the field shows for a stored result <paramref name="resultText"/>: the box glyph for a check box,
    /// the chosen entry for a drop-down (blank when it has none), and for a text field the result itself, or
    /// the blank placeholder when the result is empty.
    /// </summary>
    public string DisplayText(string? resultText) => Kind switch
    {
        LegacyFormFieldKind.CheckBox => IsChecked ? ContentControl.CheckedGlyph : ContentControl.UncheckedGlyph,
        LegacyFormFieldKind.DropDown => SelectedIndex >= 0 ? DropDownEntries[SelectedIndex] : BlankTextResult,
        _ => string.IsNullOrEmpty(resultText) ? BlankTextResult : resultText!
    };

    /// <summary>The text a reset (or newly inserted) text field shows: its default text, else the blank placeholder.</summary>
    public string DefaultDisplayText => Kind == LegacyFormFieldKind.Text
        ? (string.IsNullOrEmpty(DefaultText) ? BlankTextResult : DefaultText!)
        : DisplayText(null);

    /// <summary>Whether <paramref name="text"/> is the blank placeholder (only EN SPACEs, or nothing).</summary>
    public static bool IsBlankText(string? text) =>
        string.IsNullOrEmpty(text) || text.All(character => character == ' ');

    /// <summary>This field returned to its default state (Word's Reset Form Fields).</summary>
    public LegacyFormField Reset() => Kind switch
    {
        LegacyFormFieldKind.CheckBox => this with { Checked = null },
        LegacyFormFieldKind.DropDown => this with { DropDownResult = null },
        _ => this
    };

    /// <summary>A new field of <paramref name="kind"/> with Word's insertion defaults.</summary>
    public static LegacyFormField CreateDefault(LegacyFormFieldKind kind, string? name = null) => new()
    {
        Kind = kind,
        Name = name,
        Enabled = true,
        CheckBoxAutoSize = true
    };

    /// <summary>
    /// The first unused default name for <paramref name="kind"/> — <c>Text1</c>, <c>Check1</c>,
    /// <c>Dropdown1</c> — the bookmark name Word gives a newly inserted form field.
    /// </summary>
    public static string NextDefaultName(LegacyFormFieldKind kind, IEnumerable<string?> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        var prefix = kind switch
        {
            LegacyFormFieldKind.CheckBox => "Check",
            LegacyFormFieldKind.DropDown => "Dropdown",
            _ => "Text"
        };
        var used = new HashSet<string>(
            existingNames.Where(name => !string.IsNullOrEmpty(name)).Select(name => name!),
            StringComparer.OrdinalIgnoreCase);
        for (var number = 1; ; number++)
        {
            var candidate = prefix + number.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    public bool Equals(LegacyFormField? other) =>
        other is not null
        && Kind == other.Kind
        && Name == other.Name
        && Label == other.Label
        && TabIndex == other.TabIndex
        && Enabled == other.Enabled
        && CalculateOnExit == other.CalculateOnExit
        && EntryMacro == other.EntryMacro
        && ExitMacro == other.ExitMacro
        && Equals(HelpText, other.HelpText)
        && Equals(StatusText, other.StatusText)
        && TextType == other.TextType
        && DefaultText == other.DefaultText
        && MaxLength == other.MaxLength
        && TextFormat == other.TextFormat
        && CheckBoxAutoSize == other.CheckBoxAutoSize
        && CheckBoxSizeHalfPoints == other.CheckBoxSizeHalfPoints
        && DefaultChecked == other.DefaultChecked
        && Checked == other.Checked
        && DropDownEntries.SequenceEqual(other.DropDownEntries)
        && DropDownResult == other.DropDownResult
        && DropDownDefault == other.DropDownDefault;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Name);
        hash.Add(Enabled);
        hash.Add(TextType);
        hash.Add(DefaultText);
        hash.Add(MaxLength);
        hash.Add(Checked);
        hash.Add(DefaultChecked);
        hash.Add(DropDownResult);
        foreach (var entry in DropDownEntries)
            hash.Add(entry);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Every form field in the document's stories, in story order (body first, then headers/footers,
    /// notes and comments), paired with the run that carries it.
    /// </summary>
    public static IEnumerable<(Run Run, ComplexField Field, LegacyFormField FormField)> Enumerate(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var story in DocumentFieldStories.Enumerate(document))
        {
            foreach (var run in story.Paragraph.Runs)
            {
                if (run.ComplexField is { FormField: { } formField } field)
                    yield return (run, field, formField);
            }
        }
    }
}

/// <summary>
/// Fills in, toggles, picks or re-configures one legacy form field as a single undoable form-field edit.
/// The field is found by the identity of its <see cref="ComplexField"/> (which survives the editor's
/// render/commit cycle), in any story, so the same command works on a field in the body, a table cell, a
/// header or a text box. Apply captures the run's previous text and field; Revert puts exactly those back.
/// The <see cref="DocumentCommandMutationKind.FormField"/> classification keeps it — and its undo — permitted
/// under Word's "Filling in forms" protection.
/// </summary>
public sealed class SetLegacyFormFieldCommand(ComplexField target, string text, LegacyFormField formField)
    : IDocumentCommand
{
    private Run? _run;
    private string? _previousText;
    private ComplexField? _previousField;
    private ComplexField? _appliedField;

    public string Label => "Edit Form Field";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    /// <summary>The field object the document carries after Apply (null before).</summary>
    public ComplexField? AppliedField => _appliedField;

    public bool HasEffect(IDocumentCommandContext context) =>
        Find(context.Document, target) is { } run
        && (run.Text != text || !Equals(run.ComplexField!.FormField, formField));

    public void Apply(IDocumentCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(formField);
        var run = Find(context.Document, target);
        if (run is null)
            return;

        _run = run;
        _previousText = run.Text;
        _previousField = run.ComplexField;
        _appliedField = target with { FormField = formField };
        run.Text = text ?? string.Empty;
        run.ComplexField = _appliedField;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_appliedField is null || _previousField is null)
            return;

        var run = Find(context.Document, _appliedField) ?? _run;
        if (run is null || !ReferenceEquals(run.ComplexField, _appliedField))
            return;

        run.Text = _previousText ?? string.Empty;
        run.ComplexField = _previousField;
        _appliedField = null;
        _previousField = null;
        _previousText = null;
        _run = null;
    }

    private static Run? Find(TextDocument document, ComplexField field)
    {
        foreach (var story in DocumentFieldStories.Enumerate(document))
        {
            foreach (var run in story.Paragraph.Runs)
            {
                if (ReferenceEquals(run.ComplexField, field))
                    return run;
            }
        }

        return null;
    }
}

/// <summary>
/// Word's Developer &gt; Legacy Tools &gt; Reset Form Fields: every check box and drop-down returns to its
/// default and every fillable text field to its default text. One undoable form-field edit; Revert restores
/// each touched run's exact previous text and field.
/// </summary>
public sealed class ResetLegacyFormFieldsCommand : IDocumentCommand
{
    private List<(Run Run, string Text, ComplexField Field, ComplexField Applied)>? _changes;

    public string Label => "Reset Form Fields";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    public bool HasEffect(IDocumentCommandContext context) =>
        LegacyFormField.Enumerate(context.Document).Any(item => NeedsReset(item.Run, item.FormField));

    public void Apply(IDocumentCommandContext context)
    {
        var changes = new List<(Run, string, ComplexField, ComplexField)>();
        foreach (var (run, field, formField) in LegacyFormField.Enumerate(context.Document).ToList())
        {
            if (!NeedsReset(run, formField))
                continue;

            var reset = formField.Reset();
            var applied = field with { FormField = reset };
            changes.Add((run, run.Text, field, applied));
            run.Text = reset.DefaultDisplayText;
            run.ComplexField = applied;
        }

        _changes = changes;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_changes is null)
            return;

        for (var index = _changes.Count - 1; index >= 0; index--)
        {
            var (run, text, field, applied) = _changes[index];
            if (!ReferenceEquals(run.ComplexField, applied))
                continue;
            run.Text = text;
            run.ComplexField = field;
        }

        _changes = null;
    }

    private static bool NeedsReset(Run run, LegacyFormField formField)
    {
        if (formField.Kind == LegacyFormFieldKind.Text && !formField.IsFillable)
            return false;
        var reset = formField.Reset();
        return !Equals(reset, formField) || run.Text != reset.DefaultDisplayText;
    }
}
