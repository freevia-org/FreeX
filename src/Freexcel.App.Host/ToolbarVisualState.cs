using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public sealed record ToolbarVisualState(
    bool CanUndo,
    bool CanRedo,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    CellVAlign VerticalAlignment,
    CellHAlign HorizontalAlignment,
    bool WrapText,
    string FontName,
    string FontSizeText)
{
    public static ToolbarVisualState From(CellStyle style, bool canUndo, bool canRedo) =>
        new(
            canUndo,
            canRedo,
            style.Bold,
            style.Italic,
            style.Underline && !style.Strikethrough,
            style.Strikethrough,
            style.VerticalAlignment,
            style.HorizontalAlignment,
            style.WrapText,
            style.FontName,
            style.FontSize.ToString("0.#"));
}
