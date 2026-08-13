using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Mechanism tests for the declarative leaf-element normalizer
/// (<see cref="XlsxWorkbookLeafElementNormalizer"/> driven by
/// <see cref="XlsxWorkbookLeafElementSchema"/>).
///
/// These verify the generic engine, not any specific workbook element. They are
/// deliberately schema-independent so that the mechanism can be tested in isolation.
/// </summary>
public sealed class XlsxWorkbookLeafElementNormalizerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static XlsxWorkbookLeafElementSchema SimpleSchema(
        string localName,
        IReadOnlySet<string>? allowed = null,
        Dictionary<string, Func<string?, string?>>? rules = null,
        bool removeChildNodes = true) =>
        new()
        {
            LocalName = localName,
            AllowedAttributes = allowed ?? new HashSet<string>(StringComparer.Ordinal) { "keep" },
            AttributeRules = rules ?? new Dictionary<string, Func<string?, string?>>(),
            RemoveAllChildNodes = removeChildNodes
        };

    private static XElement MakeElement(string localName, params (string name, string value)[] attrs)
    {
        var element = new XElement(localName);
        foreach (var (name, value) in attrs)
            element.SetAttributeValue(name, value);
        return element;
    }

    // ── 1. unknown attributes are removed ────────────────────────────────────────────────────

    [Fact]
    public void Normalize_RemovesUnknownAttributes()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "keep1", "keep2" });
        var element = MakeElement("elem",
            ("keep1", "v1"),
            ("keep2", "v2"),
            ("unknown", "gone"));

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeTrue();
        element.Attribute("keep1")!.Value.Should().Be("v1");
        element.Attribute("keep2")!.Value.Should().Be("v2");
        element.Attribute("unknown").Should().BeNull("unknown attribute must be removed");
    }

    // ── 2. allowed attributes are preserved verbatim when no rule is registered ─────────────

    [Fact]
    public void Normalize_PreservesAllowedAttributeVerbatim_WhenNoRuleRegistered()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "passthrough" });
        var element = MakeElement("elem", ("passthrough", "  some text  "));

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        // No child nodes removed (element has none), no attribute changes — should be no-op
        changed.Should().BeFalse();
        element.Attribute("passthrough")!.Value.Should().Be("  some text  ");
    }

    // ── 3. attribute rule fires and canonicalizes value ────────────────────────────────────

    [Fact]
    public void Normalize_AppliesAttributeRule_CanonicalizesBooleanValue()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "flag" },
            rules: new Dictionary<string, Func<string?, string?>>
            {
                ["flag"] = XlsxXmlNormalizationHelpers.NormalizeBoolean
            });
        var element = MakeElement("elem", ("flag", "true"));   // "true" is valid; stays

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeFalse("'true' is already canonical");
        element.Attribute("flag")!.Value.Should().Be("true");
    }

    [Fact]
    public void Normalize_AttributeRule_RemovesInvalidValue()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "flag" },
            rules: new Dictionary<string, Func<string?, string?>>
            {
                ["flag"] = XlsxXmlNormalizationHelpers.NormalizeBoolean
            });
        var element = MakeElement("elem", ("flag", "yes"));    // "yes" is not valid bool

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeTrue();
        element.Attribute("flag").Should().BeNull("invalid boolean value must be removed");
    }

    // ── 4. child nodes are removed when RemoveAllChildNodes = true ───────────────────────

    [Fact]
    public void Normalize_RemovesChildNodes_WhenSchemaRequiresIt()
    {
        var schema = SimpleSchema("elem", removeChildNodes: true);
        var element = MakeElement("elem");
        element.Add(new XElement("child", "text"));
        element.Add(new XText("text content"));

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeTrue();
        element.Nodes().Should().BeEmpty("all child nodes must be removed");
    }

    [Fact]
    public void Normalize_LeavesChildNodes_WhenSchemaDoesNotRequireRemoval()
    {
        var schema = SimpleSchema("elem", removeChildNodes: false);
        var element = MakeElement("elem");
        element.Add(new XElement("child"));

        XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        element.HasElements.Should().BeTrue("child nodes must not be touched");
    }

    // ── 5. no-op: valid content → no changes, changed = false ──────────────────────────────

    [Fact]
    public void Normalize_IsNoOp_WhenElementAlreadyCanonical()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "flag", "count" },
            rules: new Dictionary<string, Func<string?, string?>>
            {
                ["flag"]  = XlsxXmlNormalizationHelpers.NormalizeBoolean,
                ["count"] = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull
            });

        var element = MakeElement("elem",
            ("flag", "1"),    // canonical boolean
            ("count", "42")); // canonical uint

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeFalse("no changes needed when content is already canonical");
    }

    // ── 6. namespace declarations are preserved (not treated as unknown attributes) ─────────

    [Fact]
    public void Normalize_PreservesNamespaceDeclarations()
    {
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal));

        XNamespace ns = "http://example.com/test";
        var element = new XElement(ns + "elem");
        element.SetAttributeValue(XNamespace.Xmlns + "x", ns.NamespaceName);

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        // Namespace declarations must survive even though AllowedAttributes is empty
        element.Attributes().Where(a => a.IsNamespaceDeclaration).Should().NotBeEmpty();
        // No ordinary attributes were present or added → no "real" change from attribute removal
        // (the namespace decl is not considered a change by RemoveUnknownAttributes)
        _ = changed; // value not asserted; what matters is no crash and decl preserved
    }

    // ── 7. token-validating rule ──────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_TokenRule_KeepsValidToken()
    {
        var validSet = new HashSet<string>(StringComparer.Ordinal) { "auto", "manual" };
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "mode" },
            rules: new Dictionary<string, Func<string?, string?>>
            {
                ["mode"] = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, validSet)
            });
        var element = MakeElement("elem", ("mode", "auto"));

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeFalse();
        element.Attribute("mode")!.Value.Should().Be("auto");
    }

    [Fact]
    public void Normalize_TokenRule_RemovesInvalidToken()
    {
        var validSet = new HashSet<string>(StringComparer.Ordinal) { "auto", "manual" };
        var schema = SimpleSchema("elem",
            allowed: new HashSet<string>(StringComparer.Ordinal) { "mode" },
            rules: new Dictionary<string, Func<string?, string?>>
            {
                ["mode"] = v => XlsxXmlNormalizationHelpers.NormalizeToken(v, validSet)
            });
        var element = MakeElement("elem", ("mode", "bogus"));

        var changed = XlsxWorkbookLeafElementNormalizer.Normalize(element, schema);

        changed.Should().BeTrue();
        element.Attribute("mode").Should().BeNull();
    }

    // ── 8. schema table coverage: all registered schemas are self-consistent ─────────────

    [Fact]
    public void Normalize_KnownElement_ResolvesSchemaByLocalName()
    {
        var element = MakeElement("calcPr", ("calcMode", "auto"), ("unknown", "drop"));

        XlsxWorkbookLeafElementNormalizer.Normalize(element).Should().BeTrue();

        element.Attribute("calcMode")!.Value.Should().Be("auto");
        element.Attribute("unknown").Should().BeNull();
    }

    [Fact]
    public void Normalize_UnknownElement_ReportsMissingSchema()
    {
        var act = () => XlsxWorkbookLeafElementNormalizer.Normalize(new XElement("unknownLeaf"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknownLeaf*");
    }

    [Theory]
    [MemberData(nameof(AllRegisteredSchemaLocalNames))]
    public void RegisteredSchema_AttributeRuleKeys_AreSubsetOfAllowedAttributes(string localName)
    {
        var schema = XlsxWorkbookLeafElementSchemas.ByLocalName[localName];
        foreach (var key in schema.AttributeRules.Keys)
        {
            schema.AllowedAttributes.Should().Contain(key,
                because: $"AttributeRule key '{key}' must be in AllowedAttributes for schema '{schema.LocalName}'");
        }
    }

    [Theory]
    [MemberData(nameof(AllRegisteredSchemaLocalNames))]
    public void RegisteredSchema_LocalName_MatchesDictionaryKey(string localName)
    {
        var schema = XlsxWorkbookLeafElementSchemas.ByLocalName[localName];
        schema.LocalName.Should().Be(localName,
            because: "ByLocalName dictionary key must match schema.LocalName");
    }

    public static IEnumerable<object[]> AllRegisteredSchemaLocalNames() =>
        XlsxWorkbookLeafElementSchemas.ByLocalName.Keys
            .Select(k => new object[] { k });
}
