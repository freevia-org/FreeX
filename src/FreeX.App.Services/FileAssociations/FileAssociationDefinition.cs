namespace FreeX.App.Services.FileAssociations;

/// <summary>How aggressively FreeX claims a file extension.</summary>
public enum AssociationOwnership
{
    /// <summary>FreeX becomes the default handler (only for types nobody else owns).</summary>
    Default,
    /// <summary>FreeX is added to the "Open with" list but the existing default handler is preserved.</summary>
    OpenWith,
}

/// <summary>One file type FreeX can handle, and how it should be registered.</summary>
public sealed record FileAssociationDefinition(
    string Extension,
    string ProgId,
    string FriendlyName,
    AssociationOwnership Ownership)
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
