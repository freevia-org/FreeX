namespace Free.Shared.PageSetup;

/// <summary>
/// The union of the named paper sizes the sibling apps offer. Apps still choose which subset their
/// own dialog lists (FreeX exposes all of them; FreeW's dialogs list a smaller set plus a "Custom"
/// row that is not a named size and therefore not modelled here).
/// </summary>
public enum SharedPaperSize
{
    Letter,
    Legal,
    Tabloid,
    Ledger,
    Statement,
    Executive,
    A3,
    A4,
    A5,
    B4,
    B5,
    Folio,
}
