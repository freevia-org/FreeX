using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxStyleOnlyRoundTripTests
{
    // Distinct style-only (empty, formatted) cells must each keep their own style across a source-package
    // rebuild — a plain bordered cell next to a bold/centred/filled one must not inherit the latter's style.
    [Fact]
    public void XlsxAdapter_RoundTrip_PreservesDistinctStyleOnlyCellStylesThroughSourceRebuild()
    {
        var workbook = new Workbook("StyleOnlyRoundTrip");
        var sheet = workbook.AddSheet("S1");

        var boldWhite = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FillColor = new CellColor(255, 255, 255),
            FillPatternStyle = CellFillPatternStyle.Solid,
        });
        var plain = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
        });

        sheet.SetStyleOnly(2, 1, boldWhite);
        sheet.SetStyleOnly(3, 1, plain);
        sheet.SetStyleOnly(5, 1, plain);
        sheet.SetStyleOnly(6, 1, plain);

        var adapter = new XlsxFileAdapter();
        // Save -> load captures a source package; an edit then forces a rebuild on the second save.
        using var first = new MemoryStream();
        adapter.Save(workbook, first);
        first.Position = 0;
        var sourceLoaded = adapter.Load(first);
        sourceLoaded.GetSheetAt(0).SetCell(new CellAddress(sourceLoaded.GetSheetAt(0).Id, 100, 1), new NumberValue(1));
        using var ms = new MemoryStream();
        adapter.Save(sourceLoaded, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);

        var a3 = loadedSheet.GetStyleOnly(3, 1);
        a3.Should().NotBeNull();
        var a3Style = loaded.GetStyle(a3!.Value);
        a3Style.Bold.Should().BeFalse("A3's plain style must not pick up A2's bold style");
        a3Style.FillColor.Should().BeNull("A3 must not pick up A2's white fill");
        a3Style.HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }
}
