using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r309: the last copy from r307's survey that neither guard reaches.
///
/// <para>Every member of <see cref="PreservedParts"/> is reference-typed, so the scalar helper finds
/// nothing to vary and would pass vacuously; and <c>CopyFrom</c> is a statement body rather than an
/// object initializer, so the shape contract does not see it either. It is also the copy with the
/// most to lose: these are the parts of an opened package FreeW does not model, carried forward so a
/// derived document saves without dropping what it never understood. A member forgotten here is not
/// a wrong value in the copy -- it is a part of the user's original file that quietly stops being
/// written.</para>
/// </summary>
public sealed class R309_PreservedPartsCopyFromCompletenessTests
{
    [Fact]
    public void CopyFromCarriesEveryPreservedMember()
    {
        var source = new PreservedParts
        {
            OriginalCoreProperties = new XElement("core", new XAttribute("id", "r309-core")),
            OriginalSettings = new XElement("settings", new XAttribute("id", "r309-settings")),
            OriginalNumbering = new XElement("numbering", new XAttribute("id", "r309-numbering")),
            OriginalCustomProperties = new XElement("custom", new XAttribute("id", "r309-custom")),
            WebExtensions = new PreservedWebExtensions(
                "<we id=\"r309-we\" />",
                [new PreservedDocumentReference("rId309", "/word/webextensions/we1.xml")]),
        };
        source.Parts.Add(new PreservedPart(
            "/word/r309.bin", [3, 0, 9], "application/octet-stream", "http://r309/rel", "http://r309/pkg"));
        source.ContentTypeDefaults["r309"] = "application/x-r309";

        var copy = new PreservedParts();
        copy.CopyFrom(source);

        copy.OriginalCoreProperties?.Attribute("id")!.Value.Should().Be("r309-core");
        copy.OriginalSettings?.Attribute("id")!.Value.Should().Be("r309-settings");
        copy.OriginalNumbering?.Attribute("id")!.Value.Should().Be("r309-numbering");
        copy.OriginalCustomProperties?.Attribute("id")!.Value.Should().Be("r309-custom");
        copy.WebExtensions.Should().NotBeNull();
        copy.WebExtensions!.Xml.Should().Be("<we id=\"r309-we\" />");
        copy.WebExtensions.References.Should().ContainSingle()
            .Which.OriginalRelId.Should().Be("rId309");
        copy.Parts.Should().ContainSingle().Which.PartName.Should().Be("/word/r309.bin");
        copy.Parts[0].Bytes.Should().Equal([(byte)3, (byte)0, (byte)9]);
        copy.ContentTypeDefaults.Should().ContainKey("r309").WhoseValue.Should().Be("application/x-r309");
    }

    /// <summary>
    /// The copy must be deep: sharing a buffer or a list with the source means an edit to the derived
    /// document reaches back into the one it came from.
    /// </summary>
    [Fact]
    public void CopyFromSharesNoMutableStateWithItsSource()
    {
        var source = new PreservedParts();
        source.Parts.Add(new PreservedPart("/word/r309.bin", [1, 2], null, null, null));

        var copy = new PreservedParts();
        copy.CopyFrom(source);
        copy.Parts.Should().NotBeSameAs(source.Parts);
        copy.Parts[0].Bytes.Should().NotBeSameAs(source.Parts[0].Bytes);

        copy.Parts[0].Bytes[0] = 99;
        source.Parts[0].Bytes[0].Should().Be(1, "the source package snapshot must be unaffected");
    }

    /// <summary>
    /// Pins the member census. The assertions above name each member explicitly, so a member added to
    /// the type would be copied or not with nothing to say so; this fails until someone looks.
    /// </summary>
    [Fact]
    public void EveryMemberOfThePreservedSnapshotIsAccountedForAbove()
    {
        string[] covered =
        [
            nameof(PreservedParts.OriginalCoreProperties),
            nameof(PreservedParts.OriginalSettings),
            nameof(PreservedParts.OriginalNumbering),
            nameof(PreservedParts.OriginalCustomProperties),
            nameof(PreservedParts.WebExtensions),
            nameof(PreservedParts.Parts),
            nameof(PreservedParts.ContentTypeDefaults),
            nameof(PreservedParts.IsEmpty),
        ];

        typeof(PreservedParts)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().BeEquivalentTo(covered,
                "a member added to the preserved-package snapshot must be copied by CopyFrom and "
                + "checked here, or a derived document silently stops carrying it");
    }
}
