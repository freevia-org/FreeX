using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxNamedRangeMapperTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsWorkbookAndSheetScopedDefinedNames()
    {
        var workbook = new Workbook("DefinedNames");
        var alpha = workbook.AddSheet("Alpha");
        var beta = workbook.AddSheet("Beta");

        workbook.DefineNamedRange("GlobalRange", Range(alpha, 1, 1, 2, 1));
        workbook.NamedFormulas["GlobalFormula"] = "SUM(Alpha!$A$1:$A$2)";
        workbook.DefineNamedRange("LocalRange", Range(beta, 3, 2, 4, 2), metadata: null, alpha.Id);
        workbook.DefineNamedFormula("LocalFormula", "GlobalFormula*2", alpha.Id);

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedAlpha = loaded.GetSheet("Alpha")!;
        var loadedBeta = loaded.GetSheet("Beta")!;

        loaded.NamedRanges.Should().ContainKey("GlobalRange");
        loaded.NamedRanges["GlobalRange"].Should().Be(Range(loadedAlpha, 1, 1, 2, 1));
        loaded.NamedFormulas.Should().ContainKey("GlobalFormula")
            .WhoseValue.Should().Be("SUM(Alpha!$A$1:$A$2)");

        loaded.ScopedNamedRanges.Should().ContainKey(("LocalRange", loadedAlpha.Id));
        loaded.ScopedNamedRanges[("LocalRange", loadedAlpha.Id)]
            .Should().Be(Range(loadedBeta, 3, 2, 4, 2));
        loaded.ScopedNamedFormulas.Should().ContainKey(("LocalFormula", loadedAlpha.Id));
        loaded.ScopedNamedFormulas[("LocalFormula", loadedAlpha.Id)]
            .Should().Be("GlobalFormula*2");
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
