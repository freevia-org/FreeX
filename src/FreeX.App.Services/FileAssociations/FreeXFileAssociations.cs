using Free.Shared.AppServices;

namespace FreeX.App.Services.FileAssociations;

/// <summary>FreeX's file-association catalog, supplied to the shared registrar.</summary>
public static class FreeXFileAssociations
{
    /// <summary>
    /// The full association policy. Native FreeX files (.fxl) are owned outright; everything
    /// else is offered via "Open with" so we never steal Excel/Notepad defaults on install.
    /// </summary>
    public static IReadOnlyList<FileAssociationDefinition> All { get; } = new[]
    {
        new FileAssociationDefinition(".fxl",  "FreeX.Workbook.fxl",      "FreeX Workbook",          AssociationOwnership.Default),
        new FileAssociationDefinition(".csv",  "FreeX.Workbook.csv",      "CSV (FreeX)",             AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".tsv",  "FreeX.Workbook.tsv",      "Tab-Separated (FreeX)",   AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".tab",  "FreeX.Workbook.tab",      "Tab-Delimited (FreeX)",   AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".txt",  "FreeX.Workbook.txt",      "Text (FreeX)",            AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".xml",  "FreeX.Workbook.xml",      "SpreadsheetML (FreeX)",   AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".xlsx", "FreeX.Workbook.xlsx",     "XLSX Workbook (FreeX)",   AssociationOwnership.OpenWith),
        new FileAssociationDefinition(".xls",  "FreeX.Workbook.xls",      "Legacy XLS (FreeX)",      AssociationOwnership.OpenWith),
    };
}
