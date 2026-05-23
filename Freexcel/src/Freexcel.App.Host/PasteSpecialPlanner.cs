using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class PasteSpecialPlanner
{
    public static PasteSpecialPlan CreatePlan(PasteSpecialDialogSelection selection)
    {
        var options = new PasteSpecialOptions(
            Transpose: selection.Transpose,
            Operation: ParseOperation(selection.Operation),
            SkipBlanks: selection.SkipBlanks,
            ContentKind: selection.Mode switch
            {
                PasteSpecialDialogMode.AllExceptBorders => PasteSpecialContentKind.AllExceptBorders,
                PasteSpecialDialogMode.AllMergingConditionalFormats => PasteSpecialContentKind.AllMergingConditionalFormats,
                PasteSpecialDialogMode.FormulasAndNumberFormats => PasteSpecialContentKind.FormulasAndNumberFormats,
                PasteSpecialDialogMode.ValuesAndNumberFormats => PasteSpecialContentKind.ValuesAndNumberFormats,
                PasteSpecialDialogMode.ValuesAndSourceFormatting => PasteSpecialContentKind.ValuesAndSourceFormatting,
                _ => PasteSpecialContentKind.Default
            });

        if (selection.Mode == PasteSpecialDialogMode.ColumnWidths)
            return new PasteSpecialPlan(PasteSpecialAction.ColumnWidths, PasteMode.All, options, selection.KeepColumnWidths);

        if (selection.Mode == PasteSpecialDialogMode.Comments)
            return new PasteSpecialPlan(PasteSpecialAction.Comments, PasteMode.All, options, selection.KeepColumnWidths);

        if (selection.Mode == PasteSpecialDialogMode.Validation)
            return new PasteSpecialPlan(PasteSpecialAction.Validation, PasteMode.All, options, selection.KeepColumnWidths);

        if (selection.Mode == PasteSpecialDialogMode.LinkedPicture)
            return new PasteSpecialPlan(PasteSpecialAction.LinkedPicture, PasteMode.All, options, selection.KeepColumnWidths);

        if (selection.Mode == PasteSpecialDialogMode.Picture)
            return new PasteSpecialPlan(PasteSpecialAction.Picture, PasteMode.All, options, selection.KeepColumnWidths);

        if (selection.PasteLink)
            return new PasteSpecialPlan(PasteSpecialAction.Link, PasteMode.All, options, selection.KeepColumnWidths);

        var pasteMode = selection.Mode switch
        {
            PasteSpecialDialogMode.Values => PasteMode.Values,
            PasteSpecialDialogMode.Formulas => PasteMode.Formulas,
            PasteSpecialDialogMode.Formats => PasteMode.Formats,
            _ => PasteMode.All
        };
        return new PasteSpecialPlan(PasteSpecialAction.Paste, pasteMode, options, selection.KeepColumnWidths);
    }

    private static PasteSpecialOperation ParseOperation(string operation) =>
        operation.ToLowerInvariant() switch
        {
            "add" => PasteSpecialOperation.Add,
            "subtract" => PasteSpecialOperation.Subtract,
            "multiply" => PasteSpecialOperation.Multiply,
            "divide" => PasteSpecialOperation.Divide,
            _ => PasteSpecialOperation.None
        };
}

public sealed record PasteSpecialDialogSelection(
    PasteSpecialDialogMode Mode,
    string Operation,
    bool SkipBlanks = false,
    bool Transpose = false,
    bool KeepColumnWidths = false,
    bool PasteLink = false);

public sealed record PasteSpecialPlan(
    PasteSpecialAction Action,
    PasteMode PasteMode,
    PasteSpecialOptions Options,
    bool KeepColumnWidths);

public enum PasteSpecialAction
{
    Paste,
    ColumnWidths,
    Comments,
    Validation,
    Picture,
    LinkedPicture,
    Link
}
