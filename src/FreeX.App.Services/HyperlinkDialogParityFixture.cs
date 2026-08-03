using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>Shared deterministic content for paired Insert Hyperlink captures.</summary>
public static class HyperlinkDialogParityFixture
{
    public const string Target = "https://freex.example/insert-objects";
    public const string DisplayText = "FreeX visual evidence";

    public static void Seed(Sheet sheet, CellAddress address)
    {
        sheet.SetCell(address, Cell.FromValue(new TextValue(DisplayText)));
        sheet.Hyperlinks[address] = Target;
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage);
    }
}
