using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// The deterministic print-preview page used by the cross-shell visual capture. Both desktop
/// shells render this fixture so the evidence compares chrome and page rendering rather than
/// comparing different page-content inputs.
/// </summary>
public sealed record PrintPreviewParityTextRun(
    string Text,
    double Left,
    double Top,
    double FontSize,
    bool Bold,
    PresentationRgb Color);

public sealed record PrintPreviewParityPage(
    string Title,
    string Subtitle,
    int PageNumber,
    IReadOnlyList<PrintPreviewParityTextRun> TextRuns);

public static class PrintPreviewParityFixture
{
    public const double PageWidth = 696;
    public const double PageHeight = 768;
    public const double DocumentWidth = 1120 * 0.62;
    public const double DocumentHeight = 700 * 1.1;

    private static readonly PresentationRgb Black = new(0, 0, 0);
    private static readonly PresentationRgb DimGray = new(105, 105, 105);

    public static IReadOnlyList<PrintPreviewParityPage> Pages { get; } =
    [
        CreatePage("Parity Demo", "Revenue by region", 1),
        CreatePage("Parity Demo", "Pipeline by product", 2),
    ];

    private static PrintPreviewParityPage CreatePage(string title, string subtitle, int pageNumber)
    {
        var runs = new List<PrintPreviewParityTextRun>
        {
            new(title, 48, 44, 22, Bold: true, Black),
            new(subtitle, 48, 78, 14, Bold: false, DimGray),
        };

        var headers = new[] { "Region", "Product", "Units", "Revenue" };
        for (var column = 0; column < headers.Length; column++)
            runs.Add(new(headers[column], 48 + column * 132, 132, 12, Bold: true, Black));

        var rows = new[]
        {
            new[] { "North", "Widget", "120", "$12,480" },
            new[] { "South", "Gadget", "85", "$8,925" },
            new[] { "East", "Sprocket", "200", "$21,700" },
            new[] { "West", "Gizmo", "64", "$6,080" },
        };

        var top = 160d;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
                runs.Add(new(row[column], 48 + column * 132, top, 12, Bold: false, Black));
            top += 24;
        }

        runs.Add(new($"Page {pageNumber}", 48, 704, 11, Bold: false, DimGray));
        return new PrintPreviewParityPage(title, subtitle, pageNumber, runs);
    }
}
