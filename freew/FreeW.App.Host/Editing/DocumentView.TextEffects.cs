using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Home &gt; Text Effects and Typography: applies a pure run-formatting edit (a text-effect preset,
    /// an outline/shadow/reflection/glow pick, an OpenType choice or Clear Text Effects) to the
    /// selection. Text effects have no WPF property slot, so the edit goes through the model: a real
    /// selection is changed as one exact range command (undoable, tracked like any other character
    /// format), and a collapsed caret formats the paragraph's runs, matching the other model-only
    /// character formats (character border and shading).
    /// </summary>
    public void ApplyTextEffectFormatting(Func<RunFormatting, RunFormatting> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (TrySetSelectedRunFormatting(formatting => edit(formatting) == formatting, edit))
            return;

        FormatSelectedModelRuns(edit);
    }
}
