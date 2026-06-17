using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// UI-free factory that turns an anchor into the Core <see cref="AddTextBoxCommand"/> the shell runs to
/// place a text box. Kept portable so the placeholder and default size are unit testable; the Avalonia
/// drawing overlay already renders <see cref="TextBoxModel"/>s (and supports move/resize/rotate).
/// </summary>
internal static class InsertTextBoxCommandFactory
{
    public const double DefaultWidth = 180d;
    public const double DefaultHeight = 80d;

    /// <summary>The placeholder text a freshly-inserted text box shows until the user edits it.</summary>
    public const string Placeholder = "Text Box";

    /// <summary>
    /// Builds the <see cref="AddTextBoxCommand"/> placing a text box at <paramref name="anchor"/>. A blank
    /// <paramref name="text"/> falls back to <see cref="Placeholder"/> so the box is visible immediately.
    /// </summary>
    public static AddTextBoxCommand Build(SheetId sheetId, CellAddress anchor, string? text = null) =>
        new(sheetId,
            anchor,
            string.IsNullOrWhiteSpace(text) ? Placeholder : text.Trim(),
            DefaultWidth,
            DefaultHeight);
}
