using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class LegacyXlsFileAdapterTests
{
    [Fact]
    public void Formats_AreOpenOnly()
    {
        var adapter = new LegacyXlsFileAdapter();

        adapter.Formats.Should().ContainSingle(format =>
            format.Extension == ".xls" &&
            format.CanOpen &&
            !format.CanSave);
    }

    [Fact]
    public void Load_ReadsLegacyBinaryWorkbookSheetsAndCells()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simple.xls");
        using var stream = File.OpenRead(path);
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().NotBeEmpty();
        var firstSheet = workbook.Sheets[0];
        firstSheet.Name.Should().NotBeNullOrWhiteSpace();
        firstSheet.GetUsedRange().Should().NotBeNull();
        firstSheet.EnumerateCells()
            .Any(cell => cell.Cell.Value is TextValue or NumberValue or BoolValue)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new LegacyXlsFileAdapter();

        var act = () => adapter.Save(new Workbook("Book1"), new MemoryStream());

        act.Should().Throw<NotSupportedException>();
    }
}
