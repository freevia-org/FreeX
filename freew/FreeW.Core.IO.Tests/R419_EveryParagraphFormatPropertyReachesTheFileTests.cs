using System.Reflection;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r419: every simple paragraph-formatting property must survive a .docx save and reload.
///
/// <para>The companion to r414's run-formatting sweep, and it fails differently: a lost bold is at
/// least visible on the word it was applied to, whereas a lost indent, keep-with-next or
/// page-break-before changes where the text SITS on the page. The user sees a reflowed document
/// rather than a missing setting, and looks for the cause in the wrong place.</para>
///
/// <para><b>Paragraph formatting is interdependent in a way run formatting is not</b>, and that is
/// what this test had to learn. Its first version reported six failures, ALL of them the fixture:
/// list-related fields are meaningless unless <c>ListKind</c> makes the paragraph a list;
/// <c>LineHeightPt</c> is only consumed when <c>LineRule</c> is Exact or AtLeast; a colour needs to
/// be a colour. Setting one field in isolation and calling the result a dropped value would have
/// filed six bugs that do not exist. Companions are supplied below, and the reason is recorded so
/// the next person does not re-derive it.</para>
/// </summary>
public sealed class R419_EveryParagraphFormatPropertyReachesTheFileTests
{
    /// <summary>
    /// Fields that must be set alongside the property under test for it to mean anything. Each entry
    /// is a measured dependency, not a guess: without the companion the writer legitimately emits
    /// nothing, because the value has no meaning to write.
    /// </summary>
    private static ParagraphFormatting WithCompanions(ParagraphFormatting formatting, string propertyName) =>
        propertyName switch
        {
            // A level, a start override or a marker glyph only exist for a paragraph that is in a list.
            nameof(ParagraphFormatting.ListLevel) or
            nameof(ParagraphFormatting.ListStartOverride) or
            nameof(ParagraphFormatting.ListMarkerText) =>
                formatting with { ListKind = ListKind.Number },

            // An exact line height is written only under an Exact/AtLeast rule; under the default
            // "auto" rule the writer emits the multiple in LineSpacing instead, and correctly ignores
            // the point height.
            nameof(ParagraphFormatting.LineHeightPt) =>
                formatting with { LineRule = LineSpacingRule.Exact, LineSpacingIsSet = true },

            // The "was this explicitly set" flag has nothing to record unless a value accompanies it.
            nameof(ParagraphFormatting.LineSpacingIsSet) =>
                formatting with { LineSpacing = 2.0 },

            _ => formatting,
        };

    private static ParagraphFormatting? RoundTrip(ParagraphFormatting formatting)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph { Formatting = formatting };
        paragraph.Runs.Add(new Run("sample"));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream).Paragraphs.First().Formatting;
    }

    [Fact]
    public void EverySimpleParagraphFormatPropertySurvivesADocxRoundTrip()
    {
        var properties = typeof(ParagraphFormatting).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property is { CanRead: true, CanWrite: true })
            .Where(property => property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?) ||
                               property.PropertyType == typeof(double) || property.PropertyType == typeof(int) ||
                               property.PropertyType == typeof(int?) || property.PropertyType == typeof(string))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        properties.Should().HaveCountGreaterThanOrEqualTo(
            15, "the query must still reach the paragraph model rather than quietly matching little");

        var defaults = new ParagraphFormatting();
        var lost = new List<string>();
        var exercised = 0;

        foreach (var property in properties)
        {
            var current = property.GetValue(defaults);

            // Chosen to differ from THIS property's own default, whatever it is: SpaceAfterPt
            // defaults to 8, LineSpacing to 1.15, WidowControl to true. A probe equal to the default
            // round-trips through a writer that emits nothing at all.
            object? value = property.PropertyType switch
            {
                var type when type == typeof(bool) => !(bool)(current ?? false),
                var type when type == typeof(bool?) => !(bool?)current ?? true,
                var type when type == typeof(double) => (double)(current ?? 0d) + 7.5d,
                var type when type == typeof(int) => (int)(current ?? 0) + 2,
                var type when type == typeof(int?) => ((int?)current ?? 0) + 2,
                var type when type == typeof(string) =>
                    property.Name.Contains("Hex", StringComparison.Ordinal) ? "#FF0000" : "*",
                _ => null,
            };

            if (value is null)
                continue;

            var formatting = new ParagraphFormatting();
            property.SetValue(formatting, value);
            formatting = WithCompanions(formatting, property.Name);

            var reloaded = RoundTrip(formatting);
            exercised++;

            if (reloaded is null || !Equals(property.GetValue(reloaded), value))
            {
                lost.Add($"{property.Name}: wrote {value}, read " +
                         (reloaded is null ? "(no formatting)" : property.GetValue(reloaded)?.ToString() ?? "(null)"));
            }
        }

        exercised.Should().BeGreaterThanOrEqualTo(
            15, "the sweep must actually be setting and comparing properties, not skipping them");

        lost.Should().BeEmpty(
            "paragraph formatting the writer drops changes where the text sits on the page, so the " +
            "user sees a reflowed document rather than a missing setting:\n" + string.Join("\n", lost));
    }

    [Fact]
    public void TheCompanionRequirementIsRealAndNotAWorkaround()
    {
        // Pins WHY the companions above exist. If a future writer learns to persist an exact line
        // height without a rule, this fails and the companion becomes removable -- rather than
        // sitting there forever as an unexplained special case that might be masking a defect.
        RoundTrip(new ParagraphFormatting { LineHeightPt = 21.5 })!.LineHeightPt
            .Should().Be(0, "with the default auto rule there is no exact height to write");

        RoundTrip(new ParagraphFormatting
        {
            LineHeightPt = 21.5,
            LineRule = LineSpacingRule.Exact,
            LineSpacingIsSet = true,
        })!.LineHeightPt.Should().Be(21.5, "under an Exact rule the height is meaningful and must persist");
    }
}
