using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// Home &gt; Text Effects and Typography: applies a pure run-formatting edit from the shared
    /// catalog to the selection through the normal undoable character-formatting path. The Avalonia
    /// renderer stores and round-trips the w14 effects but does not paint glow, shadow, reflection or
    /// outline yet; the OpenType entries (number styles, ligatures, stylistic sets) render as usual.
    /// </summary>
    public void ApplyTextEffectFormatting(Func<RunFormatting, RunFormatting> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ApplyRunFormatting(edit);
    }
}
