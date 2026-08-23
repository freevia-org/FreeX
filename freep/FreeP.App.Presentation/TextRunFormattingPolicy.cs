using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

internal static class TextRunFormattingPolicy
{
    internal static bool Get(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        TableCellTextFormatKind.Strikethrough => run.Strikethrough,
        TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
        TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
        _ => false,
    };

    internal static void Set(Run run, TableCellTextFormatKind kind, bool value)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.Bold = value;
                run.BoldSet = true;
                break;
            case TableCellTextFormatKind.Italic:
                run.Italic = value;
                run.ItalicSet = true;
                break;
            case TableCellTextFormatKind.Underline:
                run.Underline = value;
                run.UnderlineStyleToken = value ? "sng" : null;
                break;
            case TableCellTextFormatKind.Strikethrough:
                run.Strikethrough = value;
                run.StrikeStyleToken = value ? "sngStrike" : null;
                break;
            case TableCellTextFormatKind.Superscript:
                run.BaselineOffset = value ? 10000 : null;
                break;
            case TableCellTextFormatKind.Subscript:
                run.BaselineOffset = value ? -10000 : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    internal static void SetValue(Run run, TableCellTextValueFormatKind kind, object? value)
    {
        switch (kind)
        {
            case TableCellTextValueFormatKind.FontFamily:
                run.FontFamily = (string?)value;
                break;
            case TableCellTextValueFormatKind.FontSize:
                run.FontSizePt = (double?)value;
                break;
            case TableCellTextValueFormatKind.Color:
                run.Color = (ThemeAwareColor?)value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
