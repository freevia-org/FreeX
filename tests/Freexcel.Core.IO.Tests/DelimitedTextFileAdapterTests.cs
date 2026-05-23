using System.Text;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class DelimitedTextFileAdapterTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public void Formats_AreOpenOnly(string extension)
    {
        var adapter = new DelimitedTextFileAdapter(extension, "Text (Tab delimited)", '\t');

        adapter.Formats.Should().ContainSingle(format =>
            format.Extension == extension &&
            format.CanOpen &&
            !format.CanSave);
    }

    [Fact]
    public void Load_ReadsTabDelimitedValuesAndQuotedTabs()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name\tAmount\tNote\r\nAlice\t3.5\t\"a\tb\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("a\tb"));
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');

        var act = () => adapter.Save(new Workbook("Book1"), new MemoryStream());

        act.Should().Throw<NotSupportedException>();
    }
}
