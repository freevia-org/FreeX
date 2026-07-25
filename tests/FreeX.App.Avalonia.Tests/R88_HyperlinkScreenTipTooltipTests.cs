using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-88 regression test for finding R88-app-hyperlink-navigation-5-4 (MED): a hyperlink's
/// ScreenTip is captured (Insert Hyperlink dialog) and persisted (XLSX/JSON round-trip) but was never
/// surfaced anywhere in the Avalonia grid -- hovering a hyperlinked cell showed no tooltip at all. Real
/// Excel shows the custom ScreenTip, falling back to the raw link target when no custom ScreenTip was
/// set. Fixed by wiring <c>MainWindow.FormatHyperlinkTooltip</c> into the interactive cell border's
/// <c>ToolTip.SetTip</c> call alongside the existing comment tooltip.
/// </summary>
public sealed class R88_HyperlinkScreenTipTooltipTests
{
    private static (Sheet Sheet, CellAddress Address) CreateSheetWithHyperlink(string target, string screenTip = "")
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address] = target;
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(ScreenTip: screenTip);
        return (sheet, address);
    }

    [Fact]
    public void FormatHyperlinkTooltip_PrefersCustomScreenTipOverTarget()
    {
        var (sheet, address) = CreateSheetWithHyperlink("https://intranet.example.com/report", "Q3 report (internal)");

        var tooltip = MainWindow.FormatHyperlinkTooltipForTest(sheet, address);

        tooltip.Should().Be("Q3 report (internal)");
    }

    [Fact]
    public void FormatHyperlinkTooltip_FallsBackToTargetUrl_WhenNoCustomScreenTip_NoRegression()
    {
        // No-regression sibling: a hyperlink with no custom ScreenTip set must still surface
        // something useful -- the raw link target -- rather than silently going back to no tooltip.
        var (sheet, address) = CreateSheetWithHyperlink("https://intranet.example.com/report");

        var tooltip = MainWindow.FormatHyperlinkTooltipForTest(sheet, address);

        tooltip.Should().Be("https://intranet.example.com/report");
    }

    [Fact]
    public void FormatHyperlinkTooltip_ReturnsNull_ForPlainCellWithoutHyperlink()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var tooltip = MainWindow.FormatHyperlinkTooltipForTest(sheet, address);

        tooltip.Should().BeNull();
    }
}
