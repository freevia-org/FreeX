using System.Reflection;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r414: every simple run-formatting property must survive a .docx save and reload.
///
/// <para>The FreeP counterpart (r413) generalised a real bug: an edit the user makes, sees applied,
/// and loses on reopen with no error. Character formatting is the same exposure in a word processor
/// -- a lost bold is invisible until someone reads the printed page.</para>
///
/// <para>Reflection-driven so properties added later are covered when they appear. Colours are
/// compared with the leading <c>#</c> normalised, because the reader canonicalises it -- that is a
/// representation difference, not a loss, and treating it as a failure would have made this sweep
/// report three false positives on its first run.</para>
/// </summary>
public sealed class R414_EveryRunFormatPropertyReachesTheFileTests
{
    private static RunFormatting? RoundTrip(RunFormatting formatting)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("sample", formatting));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream).Paragraphs.First().Runs.First().Formatting;
    }

    private static string? Normalize(object? value) =>
        value is string text ? text.TrimStart('#').ToUpperInvariant() : value?.ToString();

    [Fact]
    public void EverySimpleRunFormatPropertySurvivesADocxRoundTrip()
    {
        var properties = typeof(RunFormatting).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool) || property.PropertyType == typeof(double) ||
                               property.PropertyType == typeof(double?) || property.PropertyType == typeof(string) ||
                               property.PropertyType == typeof(int?))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            20, "the query must still reach the formatting model rather than silently matching little");

        var lost = new List<string>();

        foreach (var property in properties)
        {
            // CharacterShadingHex with the DEFAULT (Clear) pattern is deliberately excluded: w:shd
            // val="clear" is indistinguishable from a legacy highlight in CT_RPr's single shading
            // slot, so the colour comes back as HighlightColorHex. The colour is preserved either
            // way -- only the field it lands in differs -- and Word has the same ambiguity. Its
            // patterned behaviour is pinned by the test below rather than skipped.
            if (property.Name == nameof(RunFormatting.CharacterShadingHex))
                continue;

            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => true,
                var type when type == typeof(double) || type == typeof(double?) => 13.5d,
                var type when type == typeof(int?) => 3,
                var type when type == typeof(string) =>
                    property.Name.Contains("Hex", StringComparison.Ordinal) ? "#FF0000"
                    : property.Name.Contains("LanguageTag", StringComparison.Ordinal) ? "fr-FR"
                    : "Verdana",
                _ => null,
            };

            if (value is null)
                continue;

            var formatting = new RunFormatting();
            property.SetValue(formatting, value);

            var reloaded = RoundTrip(formatting);
            if (reloaded is null || Normalize(property.GetValue(reloaded)) != Normalize(value))
            {
                lost.Add($"{property.Name}: wrote {value}, read {property.GetValue(reloaded!) ?? "(null)"}");
            }
        }

        lost.Should().BeEmpty(
            "character formatting the writer drops is applied on screen and gone on reopen, with no " +
            "error to notice:\n" + string.Join("\n", lost));
    }

    [Theory]
    [InlineData(ShadingPattern.Solid)]
    [InlineData(ShadingPattern.Pct25)]
    [InlineData(ShadingPattern.Pct50)]
    public void PatternedCharacterShadingRoundTripsAsShading(ShadingPattern pattern)
    {
        var reloaded = RoundTrip(new RunFormatting
        {
            CharacterShadingHex = "#00FF00",
            CharacterShadingPattern = pattern,
        });

        reloaded!.CharacterShadingHex.Should().NotBeNull("a patterned shading is unambiguous in w:shd");
        Normalize(reloaded.CharacterShadingHex).Should().Be("00FF00");
        reloaded.CharacterShadingPattern.Should().Be(pattern, "the pattern is what distinguishes it from a highlight");
    }

    [Fact]
    public void ClearCharacterShadingKeepsItsColourEvenThoughItSurfacesAsAHighlight()
    {
        // Pins the ambiguity as understood behaviour rather than leaving the exclusion above
        // unexplained: the colour must not be LOST, whichever field carries it back.
        var reloaded = RoundTrip(new RunFormatting { CharacterShadingHex = "#FF0000" });

        Normalize(reloaded!.HighlightColorHex).Should().Be(
            "FF0000",
            "w:shd val=\"clear\" cannot be told apart from a legacy highlight, so the colour returns " +
            "in that field -- what matters is that it survives at all");
    }
}
