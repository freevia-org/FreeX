using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Presentation.Ribbon;

public sealed record ToolbarVisualState(
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
    public static ToolbarVisualState From(CellStyle style) =>
        new(
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
