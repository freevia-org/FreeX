using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotSourceHeaderResolverTests
{
    // A pivot loaded from xlsx that draws on a pivot cache arrives with an empty SourceRange, so the
    // source header read yields no usable names. The resolver must fall back to the cache field names
    // so field captions / header dropdowns show real names instead of "Column N" (Issue 123).
    [Fact]
    public void Resolve_FallsBackToCacheFieldNames_WhenSourceHeadersAreEmpty()
    {
        var workbook = BuildWorkbookWithCache("OrderDate", "Customer", "Category", "Product");
        var pivot = new PivotTableModel { Name = "PivotTable1", CacheId = 7 };

        var headers = PivotSourceHeaderResolver.Resolve(workbook, pivot, []);

        headers.Should().Equal("OrderDate", "Customer", "Category", "Product");
    }

    [Fact]
    public void Resolve_FallsBackToCacheFieldNames_WhenSourceHeadersAreGenericPlaceholders()
    {
        var workbook = BuildWorkbookWithCache("OrderDate", "Customer", "Category", "Product");
        var pivot = new PivotTableModel { Name = "PivotTable1", CacheId = 7 };

        // Simulate the empty-SourceRange read collapsing to a single generic "Column 1" entry.
        var headers = PivotSourceHeaderResolver.Resolve(workbook, pivot, ["Column 1"]);

        headers.Should().Equal("OrderDate", "Customer", "Category", "Product");
    }

    [Fact]
    public void Resolve_PrefersRealSourceHeaders_WhenTheyResolved()
    {
        var workbook = BuildWorkbookWithCache("CacheA", "CacheB", "CacheC");
        var pivot = new PivotTableModel { Name = "PivotTable1", CacheId = 7 };

        var sourceHeaders = new List<string> { "Region", "Quarter", "Amount" };
        var headers = PivotSourceHeaderResolver.Resolve(workbook, pivot, sourceHeaders);

        headers.Should().Equal("Region", "Quarter", "Amount");
    }

    [Fact]
    public void Resolve_ReturnsSourceHeadersUnchanged_WhenNoMatchingCache()
    {
        var workbook = new Workbook("NoCache");
        var pivot = new PivotTableModel { Name = "PivotTable1", CacheId = 99 };

        var headers = PivotSourceHeaderResolver.Resolve(workbook, pivot, ["Column 1"]);

        headers.Should().Equal("Column 1");
    }

    private static Workbook BuildWorkbookWithCache(params string[] fieldNames)
    {
        var workbook = new Workbook("PivotCacheResolver");
        var cache = new PivotCacheModel { CacheId = 7 };
        foreach (var name in fieldNames)
            cache.Fields.Add(new PivotCacheFieldModel(name));
        workbook.PivotCaches.Add(cache);
        return workbook;
    }
}
