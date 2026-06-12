using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Mechanism tests for the declarative container-element normalizer
/// (<see cref="XlsxWorkbookContainerElementNormalizer"/> driven by
/// <see cref="XlsxWorkbookContainerElementSchema"/>).
///
/// These verify the generic engine, not any specific workbook element.
/// </summary>
public sealed class XlsxWorkbookContainerElementNormalizerTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static XlsxWorkbookContainerElementSchema SimpleContainerSchema(
        string containerLocalName,
        string childLocalName,
        IReadOnlySet<string>? requiredAttrs = null,
        IReadOnlyList<Func<XElement, string?>>? dedupSelectors = null,
        bool removeSelfIfEmpty = true,
        IReadOnlyDictionary<string, Func<string?, string?>>? attributeRules = null)
        => new()
        {
            LocalName = containerLocalName,
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = childLocalName,
                AllowedAttributes = requiredAttrs ?? new HashSet<string>(StringComparer.Ordinal) { "name" },
                RequiredAttributes = requiredAttrs ?? new HashSet<string>(StringComparer.Ordinal) { "name" },
                AttributeRules = attributeRules ?? new Dictionary<string, Func<string?, string?>>()
            },
            DedupKeySelectors = dedupSelectors ?? [],
            RemoveSelfIfEmpty = removeSelfIfEmpty
        };

    private static XElement MakeContainer(string localName, params XElement[] children)
    {
        var container = new XElement(Ns + localName);
        foreach (var child in children)
            container.Add(child);
        return container;
    }

    private static XElement MakeChild(string localName, params (string name, string value)[] attrs)
    {
        var element = new XElement(Ns + localName);
        foreach (var (name, value) in attrs)
            element.SetAttributeValue(name, value);
        return element;
    }

    private static XElement MakeChildWithNamespacedAttr(string localName, XName nsAttr, string nsValue)
    {
        var element = new XElement(Ns + localName);
        element.SetAttributeValue(nsAttr, nsValue);
        return element;
    }

    // ── 1. child missing required attribute is pruned ────────────────────────────────────────

    [Fact]
    public void Normalize_PrunesChild_WhenRequiredAttributeMissing()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" });
        var container = MakeContainer("items",
            MakeChild("item", ("name", "good")),
            MakeChild("item")); // missing required "name"

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        container.Elements(Ns + "item").Should().HaveCount(1);
        container.Elements(Ns + "item").Single().Attribute("name")!.Value.Should().Be("good");
    }

    // ── 2. child attribute rule nulls value → child pruned via required check ────────────────

    [Fact]
    public void Normalize_PrunesChild_WhenRequiredAttributeNulledByRule()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" },
            attributeRules: new Dictionary<string, Func<string?, string?>>
            {
                // rule that always nulls the name
                ["name"] = _ => null
            });
        var container = MakeContainer("items",
            MakeChild("item", ("name", "something")));

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        container.Elements(Ns + "item").Should().BeEmpty("item whose required attr was nulled must be pruned");
    }

    // ── 3. dedup: first child kept, duplicate key pruned ────────────────────────────────────

    [Fact]
    public void Normalize_DeduplicatesChildren_ByKey_KeepsFirst()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" },
            dedupSelectors: [el => el.Attribute("name")?.Value]);
        var container = MakeContainer("items",
            MakeChild("item", ("name", "alpha")),
            MakeChild("item", ("name", "beta")),
            MakeChild("item", ("name", "alpha"))); // duplicate

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        var remaining = container.Elements(Ns + "item").ToList();
        remaining.Should().HaveCount(2);
        remaining[0].Attribute("name")!.Value.Should().Be("alpha");
        remaining[1].Attribute("name")!.Value.Should().Be("beta");
    }

    // ── 4. remove container when all children pruned and RemoveSelfIfEmpty = true ────────────

    [Fact]
    public void NormalizeWorkbookRoot_RemovesContainer_WhenAllChildrenPrunedAndRemoveSelfIfEmpty()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" },
            removeSelfIfEmpty: true);
        var workbookRoot = new XElement(Ns + "workbook",
            MakeContainer("items",
                MakeChild("item"))); // no name → will be pruned

        var changed = XlsxWorkbookContainerElementNormalizer.NormalizeWorkbookRoot(workbookRoot, schema, Ns);

        changed.Should().BeTrue();
        workbookRoot.Element(Ns + "items").Should().BeNull("empty container must be removed");
    }

    // ── 5. container kept when RemoveSelfIfEmpty = false and empty ────────────────────────────

    [Fact]
    public void NormalizeWorkbookRoot_KeepsContainer_WhenRemoveSelfIfEmpty_IsFalse()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" },
            removeSelfIfEmpty: false);
        var workbookRoot = new XElement(Ns + "workbook",
            MakeContainer("items",
                MakeChild("item"))); // no name → will be pruned

        var changed = XlsxWorkbookContainerElementNormalizer.NormalizeWorkbookRoot(workbookRoot, schema, Ns);

        changed.Should().BeTrue("pruning the invalid child is a change");
        workbookRoot.Element(Ns + "items").Should().NotBeNull("container must be kept even when empty");
    }

    // ── 6. duplicate container elements: only first kept ────────────────────────────────────

    [Fact]
    public void NormalizeWorkbookRoot_RemovesDuplicateContainerElements()
    {
        var schema = SimpleContainerSchema("items", "item");
        var workbookRoot = new XElement(Ns + "workbook",
            MakeContainer("items", MakeChild("item", ("name", "a"))),
            MakeContainer("items", MakeChild("item", ("name", "b")))); // second container

        var changed = XlsxWorkbookContainerElementNormalizer.NormalizeWorkbookRoot(workbookRoot, schema, Ns);

        changed.Should().BeTrue();
        workbookRoot.Elements(Ns + "items").Should().HaveCount(1, "duplicate containers must be collapsed to one");
    }

    // ── 7. unknown attributes on container stripped ──────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesUnknownAttributes_OnContainer()
    {
        var schema = new XlsxWorkbookContainerElementSchema
        {
            LocalName = "items",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "count" },
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "item",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "name" }
            }
        };
        var container = MakeContainer("items");
        container.SetAttributeValue("count", "0");
        container.SetAttributeValue("unknownAttr", "gone");

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        container.Attribute("count").Should().NotBeNull("allowed attribute must be kept");
        container.Attribute("unknownAttr").Should().BeNull("unknown attribute must be removed");
    }

    // ── 8. unknown attributes on child stripped ──────────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesUnknownAttributes_OnChild()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "name" });
        var child = MakeChild("item", ("name", "ok"), ("unknownExtra", "gone"));
        var container = MakeContainer("items", child);

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        var kept = container.Elements(Ns + "item").Single();
        kept.Attribute("name").Should().NotBeNull();
        kept.Attribute("unknownExtra").Should().BeNull("unknown child attribute must be removed");
    }

    // ── 9. PostProcess is called after child normalization ───────────────────────────────────

    [Fact]
    public void Normalize_InvokesPostProcess_AfterChildNormalization()
    {
        var postProcessCalled = false;
        var schema = new XlsxWorkbookContainerElementSchema
        {
            LocalName = "items",
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "item",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "name" }
            },
            PostProcess = (_, _) => postProcessCalled = true
        };
        var container = MakeContainer("items", MakeChild("item", ("name", "x")));

        XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        postProcessCalled.Should().BeTrue("PostProcess must be invoked");
    }

    // ── 10. namespaced required attribute missing → child pruned ────────────────────────────

    [Fact]
    public void Normalize_PrunesChild_WhenRequiredNamespacedAttributeMissing()
    {
        var ridName = RelNs + "id";
        var schema = new XlsxWorkbookContainerElementSchema
        {
            LocalName = "refs",
            ChildSchema = new XlsxWorkbookChildElementSchema
            {
                LocalName = "ref",
                AllowedAttributes = new HashSet<string>(StringComparer.Ordinal),
                AllowedNamespacedAttributes = [ridName],
                RequiredNamespacedAttributes = [ridName]
            },
            RemoveSelfIfEmpty = true
        };
        var childWithId = new XElement(Ns + "ref");
        childWithId.SetAttributeValue(ridName, "rId1");
        var childWithoutId = new XElement(Ns + "ref"); // missing r:id

        var container = MakeContainer("refs", childWithId, childWithoutId);

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        container.Elements(Ns + "ref").Should().HaveCount(1);
        container.Elements(Ns + "ref").Single().Attribute(ridName)!.Value.Should().Be("rId1");
    }

    // ── 11. XlsxWorkbookLeafElementNormalizer.ShouldRemove — remove-self-if-invalid ─────────

    [Fact]
    public void LeafShouldRemove_ReturnsFalse_WhenNoRequiredAttributes()
    {
        var schema = new XlsxWorkbookLeafElementSchema
        {
            LocalName = "elem",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "x" }
        };
        var element = new XElement("elem"); // no attrs

        XlsxWorkbookLeafElementNormalizer.ShouldRemove(element, schema).Should().BeFalse(
            "no RequiredAttributes → never remove");
    }

    [Fact]
    public void LeafShouldRemove_ReturnsTrue_WhenRequiredAttributeAbsent()
    {
        var schema = new XlsxWorkbookLeafElementSchema
        {
            LocalName = "elem",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" },
            RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" }
        };
        var element = new XElement("elem"); // missing "ref"

        XlsxWorkbookLeafElementNormalizer.ShouldRemove(element, schema).Should().BeTrue(
            "required attribute missing → element should be removed");
    }

    [Fact]
    public void LeafShouldRemove_ReturnsFalse_WhenRequiredAttributePresent()
    {
        var schema = new XlsxWorkbookLeafElementSchema
        {
            LocalName = "elem",
            AllowedAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" },
            RequiredAttributes = new HashSet<string>(StringComparer.Ordinal) { "ref" }
        };
        var element = new XElement("elem");
        element.SetAttributeValue("ref", "A1:B2");

        XlsxWorkbookLeafElementNormalizer.ShouldRemove(element, schema).Should().BeFalse();
    }

    // ── 12. dual-key dedup: each key tracked independently ──────────────────────────────────

    [Fact]
    public void Normalize_DualKeyDedup_PrunesChildWithEitherDuplicateKey()
    {
        var schema = SimpleContainerSchema("items", "item",
            requiredAttrs: new HashSet<string>(StringComparer.Ordinal) { "id", "rid" },
            dedupSelectors:
            [
                el => el.Attribute("id")?.Value,
                el => el.Attribute("rid")?.Value
            ]);
        var container = MakeContainer("items",
            MakeChild("item", ("id", "1"), ("rid", "rId1")),
            MakeChild("item", ("id", "2"), ("rid", "rId2")),
            MakeChild("item", ("id", "1"), ("rid", "rId3")), // duplicate id
            MakeChild("item", ("id", "3"), ("rid", "rId2"))  // duplicate rid
        );

        var changed = XlsxWorkbookContainerElementNormalizer.Normalize(container, schema, Ns);

        changed.Should().BeTrue();
        container.Elements(Ns + "item").Should().HaveCount(2,
            "only the first two items have unique id AND rid");
    }
}
