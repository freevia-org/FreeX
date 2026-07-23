using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionCsvPersistenceTests
{
    [Fact]
    public async Task CommitCellTextBeyondLoadedCsvUsedRange_SavesAndReloadsTheNewCell()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "beyond-used-range.csv");
        var adapter = new CsvFileAdapter();
        var sourceWorkbook = LoadCsv(adapter, CreateRows(11));
        var source = new StartupWorkbookLoadResult(
            sourceWorkbook,
            Path.GetFileName(path),
            "Opened CSV.",
            IsFallback: false,
            SourcePath: path);
        var session = new WorkbookSessionFactory().Create(
            source,
            viewportHeight: 240,
            viewportWidth: 320);
        var target = new CellAddress(session.ActiveSheet.Id, 12, 7);

        // Exercise the cache state produced by the grid before the first edit.
        session.ActiveSheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(session.ActiveSheet.Id, 1, 1),
            new CellAddress(session.ActiveSheet.Id, 11, 3)));
        session.SelectCell(target);
        session.CommitCellText("X11ContextClear").Success.Should().BeTrue();
        session.ActiveSheet.GetUsedRange()!.Value.End.Should().Be(target);

        await new WorkbookSaveService().SaveAsync(path, adapter, session.Workbook);

        var savedText = await File.ReadAllTextAsync(path, Encoding.Default);
        var savedRows = savedText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        savedRows.Should().HaveCount(12);
        savedRows[11].Split(',').Should().ContainInOrder("", "", "", "", "", "", "X11ContextClear");
        using var savedStream = File.OpenRead(path);
        var reloaded = adapter.Load(savedStream);
        var reloadedCell = reloaded.Sheets.Single().GetCell(12, 7);
        reloadedCell.Should().NotBeNull();
        reloadedCell!.Value.Should().Be(new TextValue("X11ContextClear"));
    }

    private static Workbook LoadCsv(CsvFileAdapter adapter, string csv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return adapter.Load(stream);
    }

    private static string CreateRows(int count) =>
        string.Join("\r\n", Enumerable.Range(1, count).Select(row => $"r{row},c{row},v{row}")) + "\r\n";
}
