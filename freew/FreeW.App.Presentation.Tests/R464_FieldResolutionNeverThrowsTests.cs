using FluentAssertions;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r464: resolving a field must never throw, whatever state the document is in.
///
/// <para>The FreeW analogue of r463. FreeW has no formula engine -- checked rather than assumed --
/// but it does evaluate Word fields, and a field is resolved every time a header or footer is drawn.
/// An exception here does not spoil one field: it takes down the render pass for the page.</para>
///
/// <para>Every field kind against three document states and four display contexts, including the
/// awkward ones a resolver actually meets -- a document with no blocks at all, properties that are
/// empty strings rather than null, and a page count of zero or negative. 132 resolutions on
/// introduction, none throwing.</para>
/// </summary>
public sealed class R464_FieldResolutionNeverThrowsTests
{
    [Fact]
    public void ResolvingAnyFieldKindNeverThrows()
    {
        var threw = new List<string>();
        var resolved = 0;

        var kinds = Enum.GetValues<RunFieldKind>();

        // Documents in states a field resolver can genuinely meet: brand new, one with no blocks at
        // all, and one whose properties are empty strings rather than null.
        var documents = new (string Label, Func<TextDocument> Make)[]
        {
            ("default", () => new TextDocument()),
            ("no blocks", () =>
            {
                var document = new TextDocument();
                document.Blocks.Clear();
                return document;
            }),
            ("empty properties", () =>
            {
                var document = new TextDocument();
                document.Properties.Author = string.Empty;
                document.Properties.Title = string.Empty;
                document.Properties.Subject = string.Empty;
                document.Properties.Keywords = string.Empty;
                document.Properties.Comments = string.Empty;
                return document;
            }),
        };

        var contexts = new (string Label, DocumentFieldDisplayContext Context)[]
        {
            ("default", new DocumentFieldDisplayContext()),
            ("zero page count", new DocumentFieldDisplayContext { PageCount = 0 }),
            ("negative page count", new DocumentFieldDisplayContext { PageCount = -5 }),
            ("empty page text", new DocumentFieldDisplayContext { PageNumberText = string.Empty }),
        };

        foreach (var kind in kinds)
        {
            foreach (var (documentLabel, makeDocument) in documents)
            {
                foreach (var (contextLabel, context) in contexts)
                {
                    try
                    {
                        _ = DocumentFieldDisplayPlanner.Resolve(kind, "fallback", makeDocument(), context);
                        resolved++;
                    }
                    catch (Exception ex)
                    {
                        threw.Add($"{kind} / {documentLabel} / {contextLabel} :: {ex.GetType().Name}");
                    }
                }
            }
        }

        var census = $"kinds={kinds.Length} resolved={resolved} threw={threw.Count}";

        threw.Should().BeEmpty(
            "a field is resolved every time a header or footer is drawn, so an exception here takes " +
            "down the render pass for the page rather than spoiling one field. " + census + "\n" +
            string.Join("\n", threw.Take(20)),
            Array.Empty<object>());

        resolved.Should().BeGreaterThanOrEqualTo(
            100,
            "the sweep must still be resolving -- if this falls it has quietly stopped testing rather " +
            "than the resolver having changed. " + census);
    }
}
