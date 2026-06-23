using System.Globalization;

namespace FreeX.App.Presentation.PageLayout;

public enum PrintPreviewSidesMode
{
    OneSided,
    TwoSidedLongEdge,
    TwoSidedShortEdge
}

public static class PrintPreviewToolbarStatePlanner
{
    public static PrintPreviewNavigationState CreateNavigationState(int currentPage, int totalPages) =>
        PrintPreviewNavigationState.Create(currentPage, totalPages);

    public static PrintPreviewSidesMode SidesIndexToMode(int selectedIndex) =>
        selectedIndex switch
        {
            1 => PrintPreviewSidesMode.TwoSidedLongEdge,
            2 => PrintPreviewSidesMode.TwoSidedShortEdge,
            _ => PrintPreviewSidesMode.OneSided
        };

    public static int SidesModeToIndex(PrintPreviewSidesMode mode) =>
        mode switch
        {
            PrintPreviewSidesMode.TwoSidedLongEdge => 1,
            PrintPreviewSidesMode.TwoSidedShortEdge => 2,
            _ => 0
        };

    public static string CreateStatusText(string? printerName, int? copies, int totalPages)
    {
        var copyText = copies is { } count
            ? count == 1 ? "1 copy" : $"{count.ToString(CultureInfo.InvariantCulture)} copies"
            : "invalid copies";
        var pages = totalPages == 1
            ? "1 page"
            : $"{totalPages.ToString(CultureInfo.InvariantCulture)} pages";
        var name = string.IsNullOrWhiteSpace(printerName)
            ? "Windows print dialog"
            : printerName;

        return $"Ready: {name}; {copyText}; {pages}";
    }
}
