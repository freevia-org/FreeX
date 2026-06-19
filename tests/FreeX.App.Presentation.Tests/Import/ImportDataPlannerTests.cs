using System.Text;

using FreeX.App.Presentation.Import;

using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Import;

public sealed class ImportDataPlannerTests
{
    [Fact]
    public void ResolveDelimiter_WellKnownKinds_MapToCharacters()
    {
        ImportDataPlanner.ResolveDelimiter(new ImportDataOptions { Delimiter = ImportDelimiterKind.Comma }, null).Should().Be(',');
        ImportDataPlanner.ResolveDelimiter(new ImportDataOptions { Delimiter = ImportDelimiterKind.Tab }, null).Should().Be('\t');
        ImportDataPlanner.ResolveDelimiter(new ImportDataOptions { Delimiter = ImportDelimiterKind.Semicolon }, null).Should().Be(';');
        ImportDataPlanner.ResolveDelimiter(new ImportDataOptions { Delimiter = ImportDelimiterKind.Space }, null).Should().Be(' ');
        ImportDataPlanner.ResolveDelimiter(new ImportDataOptions { Delimiter = ImportDelimiterKind.Pipe }, null).Should().Be('|');
    }

    [Fact]
    public void ResolveDelimiter_Custom_UsesCharacterAndRejectsForbidden()
    {
        ImportDataPlanner.ResolveDelimiter(
            new ImportDataOptions { Delimiter = ImportDelimiterKind.Custom, CustomDelimiter = '#' }, null).Should().Be('#');

        // A null or forbidden custom delimiter falls back to comma so the split never crashes.
        ImportDataPlanner.ResolveDelimiter(
            new ImportDataOptions { Delimiter = ImportDelimiterKind.Custom, CustomDelimiter = null }, null).Should().Be(',');
        ImportDataPlanner.ResolveDelimiter(
            new ImportDataOptions { Delimiter = ImportDelimiterKind.Custom, CustomDelimiter = '"' }, null).Should().Be(',');
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6", ',')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a;b;c\n1;2;3", ';')]
    [InlineData("a|b|c\n1|2|3", '|')]
    public void DetectDelimiter_FindsConsistentSeparator(string text, char expected)
    {
        ImportDataPlanner.DetectDelimiter(text).Should().Be(expected);
    }

    [Fact]
    public void DetectDelimiter_PrefersConsistentColumnarShape_OverErraticSpaces()
    {
        // Commas appear once per line consistently; spaces appear erratically inside fields.
        var text = "first name,age\nJohn Smith,42\nMary Jane,37";
        ImportDataPlanner.DetectDelimiter(text).Should().Be(',');
    }

    [Fact]
    public void DetectDelimiter_EmptyOrNoSeparator_FallsBackToComma()
    {
        ImportDataPlanner.DetectDelimiter("").Should().Be(',');
        ImportDataPlanner.DetectDelimiter("singlecolumn\nnothinghere").Should().Be(',');
    }

    [Fact]
    public void DetectDelimiter_IgnoresDelimitersInsideQuotedFields()
    {
        // The semicolon is the real delimiter; the comma only appears inside a quoted field.
        var text = "name;city\n\"Smith, John\";Paris\n\"Doe, Jane\";Lyon";
        ImportDataPlanner.DetectDelimiter(text).Should().Be(';');
    }

    [Fact]
    public void DecodeBytes_HonoursUtf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("héllo")).ToArray();
        ImportDataPlanner.DecodeBytes(bytes, ImportEncodingKind.Detect).Should().Be("héllo");
    }

    [Fact]
    public void DecodeBytes_FallsBackToWindows1252_ForInvalidUtf8()
    {
        // 0xE9 is 'é' in Windows-1252 but an invalid lone lead byte in UTF-8.
        var bytes = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 };
        ImportDataPlanner.DecodeBytes(bytes, ImportEncodingKind.Detect).Should().Be("café");
    }

    [Fact]
    public void DecodeBytes_ExplicitUtf16Le_StripsBomAndDecodes()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("ok")).ToArray();
        ImportDataPlanner.DecodeBytes(bytes, ImportEncodingKind.Utf16Le).Should().Be("ok");
    }

    [Fact]
    public void DecodeBytes_ExplicitWindows1252_DecodesHighBytes()
    {
        var bytes = new byte[] { (byte)'a', 0xE9 };
        ImportDataPlanner.DecodeBytes(bytes, ImportEncodingKind.Windows1252).Should().Be("aé");
    }

    [Fact]
    public void SplitLines_HandlesAllLineEndingsAndDropsTrailingNewline()
    {
        ImportDataPlanner.SplitLines("a\nb\r\nc\rd").Should().Equal("a", "b", "c", "d");
        ImportDataPlanner.SplitLines("a\nb\n").Should().Equal("a", "b");
        ImportDataPlanner.SplitLines("").Should().BeEmpty();
    }

    [Fact]
    public void PreviewText_DelimitedComma_SplitsRowsAndReportsShape()
    {
        var text = "Name,Age,City\nAlice,30,NYC\nBob,25,LA";
        var options = new ImportDataOptions { Delimiter = ImportDelimiterKind.Comma };

        var preview = ImportDataPlanner.PreviewText(text, options);

        preview.ColumnCount.Should().Be(3);
        preview.TotalRowCount.Should().Be(3);
        preview.Delimiter.Should().Be(',');
        preview.SampleRows[0].Should().Equal("Name", "Age", "City");
        preview.SampleRows[1].Should().Equal("Alice", "30", "NYC");
    }

    [Fact]
    public void PreviewText_RespectsTextQualifier_KeepingEmbeddedDelimiters()
    {
        var text = "\"Smith, John\",42";
        var options = new ImportDataOptions { Delimiter = ImportDelimiterKind.Comma };

        var preview = ImportDataPlanner.PreviewText(text, options);

        preview.ColumnCount.Should().Be(2);
        preview.SampleRows[0].Should().Equal("Smith, John", "42");
    }

    [Fact]
    public void PreviewText_TreatConsecutiveDelimitersAsOne_CollapsesRuns()
    {
        var text = "a   b   c";
        var options = new ImportDataOptions
        {
            Delimiter = ImportDelimiterKind.Space,
            TreatConsecutiveDelimitersAsOne = true,
        };

        var preview = ImportDataPlanner.PreviewText(text, options);

        preview.SampleRows[0].Should().Equal("a", "b", "c");
    }

    [Fact]
    public void PreviewText_RespectsSampleRowLimit_ButCountsAllRows()
    {
        var text = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"row{i},{i}"));
        var options = new ImportDataOptions { Delimiter = ImportDelimiterKind.Comma };

        var preview = ImportDataPlanner.PreviewText(text, options, sampleRowLimit: 5);

        preview.SampleRows.Should().HaveCount(5);
        preview.TotalRowCount.Should().Be(100);
    }

    [Fact]
    public void PreviewText_EmptyText_IsEmptyPreview()
    {
        ImportDataPlanner.PreviewText("", new ImportDataOptions()).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void PreviewText_DetectMode_ChoosesTab_ForTsv()
    {
        var text = "a\tb\n1\t2";
        var options = new ImportDataOptions { Delimiter = ImportDelimiterKind.Detect };

        ImportDataPlanner.PreviewText(text, options).Delimiter.Should().Be('\t');
    }
}
