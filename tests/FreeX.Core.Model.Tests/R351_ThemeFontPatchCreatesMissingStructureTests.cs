using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r351: patching a native <c>a:fontScheme</c> must APPLY the caller's typeface, including when the
/// source omits the element the patch would have written into.
///
/// <para>The patch was a <c>?.</c> chain: <c>majorFont?.latin?.SetAttributeValue(...)</c>. A source
/// missing either element silently swallowed the edit and returned the XML unchanged, so the theme
/// font the user picked was dropped with no error -- and the result also failed schema validation,
/// because <c>CT_FontCollection</c> requires <c>latin</c>, <c>ea</c> and <c>cs</c> in that order.
/// Both apps patch through this one helper, so both lost the edit.</para>
///
/// <para>The reason this is worth a test rather than a one-line fix is the failure mode: a silent
/// no-op leaves a correct-looking file, so nothing downstream can detect it. That is the shape this
/// codebase has a standing rule about.</para>
/// </summary>
public sealed class R351_ThemeFontPatchCreatesMissingStructureTests
{
    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static string Scheme(string body) =>
        $"<a:fontScheme xmlns:a=\"{A}\" name=\"X\">{body}</a:fontScheme>";

    private static XElement Patch(string xml) =>
        DrawingMlThemeXml.TryPatchNativeFontScheme(xml, "Major New", "Minor New")!;

    private static string? Typeface(XElement scheme, string collection, string child) =>
        scheme.Element(A + collection)?.Element(A + child)?.Attribute("typeface")?.Value;

    [Fact]
    public void ACollectionMissingItsLatinStillReceivesTheTypeface()
    {
        var patched = Patch(Scheme(
            "<a:majorFont><a:ea typeface=\"\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Old\"/></a:minorFont>"));

        Typeface(patched, "majorFont", "latin").Should().Be("Major New");
        Typeface(patched, "minorFont", "latin").Should().Be("Minor New");
    }

    [Fact]
    public void AMissingCollectionIsCreatedInSchemaOrder()
    {
        var patched = Patch(Scheme("<a:minorFont><a:latin typeface=\"Old\"/></a:minorFont>"));

        Typeface(patched, "majorFont", "latin").Should().Be("Major New");
        patched.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("majorFont", "minorFont");
    }

    [Fact]
    public void TheRequiredChildrenAreWrittenInSchemaOrder()
    {
        var patched = Patch(Scheme(
            "<a:majorFont><a:cs typeface=\"C\"/></a:majorFont>" +
            "<a:minorFont/>"));

        foreach (var collection in new[] { "majorFont", "minorFont" })
        {
            patched.Element(A + collection)!.Elements().Select(element => element.Name.LocalName)
                .Should().StartWith(new[] { "latin", "ea", "cs" }, because: collection);
        }
    }

    [Fact]
    public void ScriptSpecificFontsAreKeptAfterTheRequiredChildren()
    {
        // a:font entries follow latin/ea/cs in CT_FontCollection. Re-seating the required three must
        // not drop them or leave them ahead of the elements the schema wants first.
        var patched = Patch(Scheme(
            "<a:majorFont><a:latin typeface=\"Old\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/>" +
            "<a:font script=\"Jpan\" typeface=\"Yu Gothic\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Old\"/></a:minorFont>"));

        var major = patched.Element(A + "majorFont")!;
        major.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("latin", "ea", "cs", "font");
        major.Element(A + "font")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic");
    }

    [Fact]
    public void ACompleteSchemeKeepsItsOtherDetail()
    {
        // The helper exists to preserve what the model does not carry (here the East-Asian
        // typeface). Creating missing elements must not turn into rewriting present ones.
        var patched = Patch(Scheme(
            "<a:majorFont><a:latin typeface=\"Old\"/><a:ea typeface=\"Meiryo\"/><a:cs typeface=\"Arial\"/></a:majorFont>" +
            "<a:minorFont><a:latin typeface=\"Old\"/><a:ea typeface=\"Meiryo\"/><a:cs typeface=\"Arial\"/></a:minorFont>"));

        Typeface(patched, "majorFont", "ea").Should().Be("Meiryo");
        Typeface(patched, "majorFont", "cs").Should().Be("Arial");
        Typeface(patched, "majorFont", "latin").Should().Be("Major New");
    }

    [Fact]
    public void FreeXThemeFontChangesSurviveAPartialNativeScheme()
    {
        // The same defect through FreeX's own consumer: WithFonts returns a theme whose native XML
        // is what gets saved, so a swallowed patch means the user's font choice never reaches disk.
        var theme = WorkbookTheme.Office with
        {
            NativeFontSchemeXml = Scheme(
                "<a:majorFont><a:ea typeface=\"\"/></a:majorFont>" +
                "<a:minorFont><a:latin typeface=\"Old\"/></a:minorFont>"),
        };

        var updated = theme.WithFonts("Cambria", "Calibri");

        updated.NativeFontSchemeXml.Should().Contain("Cambria");
        XElement.Parse(updated.NativeFontSchemeXml!)
            .Element(A + "majorFont")!.Element(A + "latin")!.Attribute("typeface")!.Value
            .Should().Be("Cambria");
    }
}
