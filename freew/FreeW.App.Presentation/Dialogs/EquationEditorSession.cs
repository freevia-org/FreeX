using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>A text-box edit planned by the equation editor: the new text and the selection to show.</summary>
public sealed record EquationEditorTextEdit(string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// What committing the equation editor does: insert <see cref="Equation"/> at the caret when
/// <see cref="Original"/> is null, otherwise replace <see cref="Original"/> with it.
/// </summary>
public sealed record EquationEditorCommit(Equation? Original, Equation Equation);

/// <summary>
/// Renderer-neutral state and rules of the in-place equation editor: the linear source being typed, the
/// live built-up equation it stands for, the inline/display layout, and what OK commits. The host only
/// supplies a text box and a preview surface.
/// </summary>
public sealed class EquationEditorSession
{
    private readonly IReadOnlyList<MathRun> _opaqueParts;

    private EquationEditorSession(Equation? original, EquationLinearText source, bool isDisplay, MathJustification justification)
    {
        Original = original;
        InitialText = source.Text;
        Text = source.Text;
        HasOpaqueParts = source.HasOpaqueParts;
        _opaqueParts = source.OpaqueParts;
        IsDisplay = isDisplay;
        Justification = justification;
    }

    /// <summary>The equation being edited, or null when a new equation is being inserted.</summary>
    public Equation? Original { get; }

    /// <summary>The linear source the editor opened with.</summary>
    public string InitialText { get; }

    /// <summary>The linear source as currently typed.</summary>
    public string Text { get; private set; }

    /// <summary>
    /// True when part of the equation cannot be written as linear text. Such a part shows as a token
    /// (⟦0⟧) that can be moved or deleted but not edited, and is put back untouched on commit.
    /// </summary>
    public bool HasOpaqueParts { get; }

    /// <summary>Whether the equation is a display equation (on its own line) rather than inline.</summary>
    public bool IsDisplay { get; set; }

    /// <summary>Justification of the display equation; only meaningful while <see cref="IsDisplay"/>.</summary>
    public MathJustification Justification { get; set; }

    /// <summary>Starts editing a new equation.</summary>
    public static EquationEditorSession ForNew(bool isDisplay = false) =>
        new(null, new EquationLinearText(string.Empty, [], HasOpaqueParts: false), isDisplay, MathJustification.CenterGroup);

    /// <summary>Starts editing an equation that is in the document.</summary>
    public static EquationEditorSession ForExisting(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        return new(equation, EquationLinearFormat.ToLinear(equation), equation.IsDisplayMath, equation.Justification);
    }

    /// <summary>Takes the text box's current content.</summary>
    public void SetText(string? text) => Text = text ?? string.Empty;

    /// <summary>The equation the current text and layout stand for — what the live preview shows.</summary>
    public Equation BuildEquation()
    {
        var equation = EquationLinearFormat.Parse(Text, _opaqueParts);
        equation.IsDisplayMath = IsDisplay;
        equation.Justification = IsDisplay ? Justification : MathJustification.CenterGroup;
        return equation;
    }

    /// <summary>True when the text builds no equation at all (there is nothing to insert or keep).</summary>
    public bool IsEmpty => EquationLinearFormat.Parse(Text, _opaqueParts).Runs.Count == 0;

    /// <summary>True when committing would change the document.</summary>
    public bool HasChanges =>
        Original is null
            ? !IsEmpty
            : !EquationStructure.AreEquivalent(BuildEquation(), Original, compareLayout: true);

    /// <summary>
    /// The edit OK commits, or null when there is nothing to do: an empty equation is never written to
    /// the document, and an unchanged one is left alone so it does not add an undo step.
    /// </summary>
    public EquationEditorCommit? Commit() =>
        IsEmpty || !HasChanges ? null : new EquationEditorCommit(Original, BuildEquation());

    /// <summary>
    /// Inserts a symbol or a structure template over the selection. The first placeholder (⬚) of the
    /// inserted text is selected so the next keystroke fills it; without one the caret goes after it.
    /// </summary>
    public static EquationEditorTextEdit Insert(string? text, int selectionStart, int selectionLength, string? snippet)
    {
        text ??= string.Empty;
        snippet ??= string.Empty;
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        var result = string.Concat(text.AsSpan(0, start), snippet, text.AsSpan(start + length));
        var placeholder = snippet.IndexOf(EquationLinearSymbols.Placeholder, StringComparison.Ordinal);
        return placeholder >= 0
            ? new EquationEditorTextEdit(result, start + placeholder, EquationLinearSymbols.Placeholder.Length)
            : new EquationEditorTextEdit(result, start + snippet.Length, 0);
    }

    /// <summary>
    /// Word's math autocorrect: when a space has just been typed after a known <c>\name</c>, the name and
    /// the space become the character it stands for. Returns null when there is nothing to correct.
    /// </summary>
    /// <param name="text">The text after the keystroke.</param>
    /// <param name="caret">The caret position after the keystroke.</param>
    public static EquationEditorTextEdit? AutoCorrect(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret < 3 || caret > text.Length || text[caret - 1] != ' ')
            return null;

        var nameEnd = caret - 1;
        var nameStart = nameEnd;
        while (nameStart > 0 && char.IsAsciiLetter(text[nameStart - 1]))
            nameStart--;

        if (nameStart == nameEnd || nameStart == 0 || text[nameStart - 1] != '\\')
            return null;

        // An escaped backslash (\\alpha) is a literal backslash followed by ordinary letters.
        var backslashes = 0;
        for (var index = nameStart - 1; index >= 0 && text[index] == '\\'; index--)
            backslashes++;
        if (backslashes % 2 == 0)
            return null;

        if (!EquationLinearSymbols.Names.TryGetValue(text[nameStart..nameEnd], out var value))
            return null;

        var result = string.Concat(text.AsSpan(0, nameStart - 1), value, text.AsSpan(caret));
        return new EquationEditorTextEdit(result, nameStart - 1 + value.Length, 0);
    }
}
