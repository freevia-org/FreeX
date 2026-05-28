using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

/// <summary>
/// Verifies that <see cref="XlsxFileAdapter.LoadWithWarnings"/> surfaces non-fatal
/// feature-loading errors instead of silently swallowing them via Debug.WriteLine.
/// </summary>
public sealed class XlsxLoadWarningsTests
{
    // XlsxLoadResult record

    [Fact]
    public void XlsxLoadResult_HasWarnings_IsFalse_WhenWarningsEmpty()
    {
        var workbook = new Workbook("Test");
        var result = new XlsxLoadResult(workbook, []);

        result.HasWarnings.Should().BeFalse();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void XlsxLoadResult_HasWarnings_IsTrue_WhenWarningsPresent()
    {
        var workbook = new Workbook("Test");
        var result = new XlsxLoadResult(workbook, ["[merged-regions] Sheet 'Sheet1': something went wrong"]);

        result.HasWarnings.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void XlsxLoadResult_Workbook_IsAlwaysReturned()
    {
        var workbook = new Workbook("Test");
        var resultWithWarnings = new XlsxLoadResult(workbook, ["warn"]);
        var resultNoWarnings = new XlsxLoadResult(workbook, []);

        resultWithWarnings.Workbook.Should().BeSameAs(workbook);
        resultNoWarnings.Workbook.Should().BeSameAs(workbook);
    }

    // LoadWithWarnings round-trip: clean file produces no warnings

    [Fact]
    public void LoadWithWarnings_CleanFile_ReturnsNoWarnings()
    {
        var adapter = new XlsxFileAdapter();
        var bytes = SaveWorkbookToBytes(adapter, CreateSimpleWorkbook());

        var result = adapter.LoadWithWarnings(new MemoryStream(bytes, writable: false));

        result.Workbook.Should().NotBeNull();
        result.Warnings.Should().BeEmpty("a cleanly saved XLSX should produce no load warnings");
    }

    [Fact]
    public void LoadWithWarnings_CleanFile_WorkbookMatchesLoad()
    {
        var adapter = new XlsxFileAdapter();
        var bytes = SaveWorkbookToBytes(adapter, CreateSimpleWorkbook());

        var resultViaLoadWithWarnings = adapter.LoadWithWarnings(new MemoryStream(bytes, writable: false));
        var resultViaLoad = adapter.Load(new MemoryStream(bytes, writable: false));

        // Both paths must yield workbooks with the same sheet count and cell value.
        resultViaLoadWithWarnings.Workbook.Sheets.Count.Should().Be(resultViaLoad.Sheets.Count);
        var cell1 = resultViaLoadWithWarnings.Workbook.GetSheetAt(0)!.GetCell(1, 1)?.Value;
        var cell2 = resultViaLoad.GetSheetAt(0)!.GetCell(1, 1)?.Value;
        cell1.Should().Be(cell2);
    }

    // IFileAdapter.Load() delegates to LoadWithWarnings

    [Fact]
    public void Load_IsConsistentWithLoadWithWarnings_Workbook()
    {
        var adapter = new XlsxFileAdapter();
        var bytes = SaveWorkbookToBytes(adapter, CreateSimpleWorkbook());

        // Load() must still work and return the same logical content.
        var workbook = adapter.Load(new MemoryStream(bytes, writable: false));
        workbook.Should().NotBeNull();
        workbook.Sheets.Should().HaveCount(1);
    }

    // Source-level regression: no more Debug.WriteLine in catch blocks

    [Fact]
    public void XlsxFileAdapterSource_DoesNotContainDebugWriteLineInCatchBlocks()
    {
        // This is a source-code guard that prevents re-introducing silent swallowing.
        var adapterSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().NotContain(
            "System.Diagnostics.Debug.WriteLine",
            "catch blocks in XlsxFileAdapter must use warnings.Add() instead of Debug.WriteLine (stripped in Release)");

        adapterSource.Should().NotContain(
            "Debug.WriteLine",
            "catch blocks in XlsxFileAdapter must use warnings.Add() instead of Debug.WriteLine (stripped in Release)");
    }

    [Fact]
    public void XlsxFileAdapterSource_ContainsWarningsAddInCatchBlocks()
    {
        var adapterSource = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.Core.IO", "XlsxFileAdapter.cs"));

        // All 5 feature-loading catch blocks must use warnings.Add.
        adapterSource.Should().Contain("warnings.Add(");
    }

    // Helpers

    private static byte[] SaveWorkbookToBytes(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return stream.ToArray();
    }

    private static Workbook CreateSimpleWorkbook()
    {
        var workbook = new Workbook("TestBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        return workbook;
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
    }
}
