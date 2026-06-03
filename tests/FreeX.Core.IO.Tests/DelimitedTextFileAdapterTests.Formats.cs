using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public void Formats_CanOpenAndSave(string extension)
    {
        var adapter = new DelimitedTextFileAdapter(extension, "Text (Tab delimited)", '\t');

        adapter.Formats.Should().ContainSingle(format =>
            format.Extension == extension &&
            format.CanOpen &&
            format.CanSave);
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    public void Constructor_RejectsLineBreakDelimiters(char delimiter)
    {
        var create = () => new DelimitedTextFileAdapter(".txt", "Delimited text", delimiter);

        create.Should().Throw<ArgumentException>()
            .WithMessage("Delimited text field delimiter cannot be a line break.*");
    }

    [Fact]
    public void Constructor_RejectsQuoteDelimiter()
    {
        var create = () => new DelimitedTextFileAdapter(".txt", "Delimited text", '"');

        create.Should().Throw<ArgumentException>()
            .WithMessage("Delimited text field delimiter cannot be the quote character.*");
    }

}
