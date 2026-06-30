using System.Globalization;
using System.Xml.Linq;

namespace Free.Shared.Opc;

/// <summary>
/// App-neutral model for OPC <c>docProps/custom.xml</c> name/value properties.
/// </summary>
public sealed class OpcCustomDocumentProperties
{
    public static readonly XNamespace CustomPropertiesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    public static readonly XNamespace VariantTypesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public const string DefaultFormatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

    private static readonly XName PropertiesElementName = CustomPropertiesNamespace + "Properties";
    private static readonly XName PropertyElementName = CustomPropertiesNamespace + "property";

    private readonly XElement _root;

    private OpcCustomDocumentProperties(XElement root)
    {
        _root = new XElement(root);
        _root.SetAttributeValue(XNamespace.Xmlns + "vt", VariantTypesNamespace.NamespaceName);
    }

    public static OpcCustomDocumentProperties Create() =>
        new(new XElement(
            PropertiesElementName,
            new XAttribute(XNamespace.Xmlns + "vt", VariantTypesNamespace.NamespaceName)));

    public static OpcCustomDocumentProperties FromDocument(XDocument? document) =>
        FromRoot(document?.Root);

    public static OpcCustomDocumentProperties FromRoot(XElement? root) =>
        root is null ? Create() : new OpcCustomDocumentProperties(root);

    public IReadOnlyList<XElement> PropertyElements =>
        _root.Elements(PropertyElementName).Select(property => new XElement(property)).ToList();

    public bool Contains(string name) => FindByName(name) is not null;

    public string? GetString(string name)
    {
        var property = FindByName(name);
        return property?.Element(VariantTypesNamespace + "lpwstr")?.Value
            ?? property?.Element(VariantTypesNamespace + "lpstr")?.Value
            ?? property?.Element(VariantTypesNamespace + "bstr")?.Value;
    }

    public bool? GetBoolean(string name)
    {
        var value = FindByName(name)?.Element(VariantTypesNamespace + "bool")?.Value;
        if (value is null)
            return null;

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1")
            return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0")
            return false;
        return null;
    }

    public double? GetDouble(string name)
    {
        var property = FindByName(name);
        var value = property?.Element(VariantTypesNamespace + "r8")?.Value
            ?? property?.Element(VariantTypesNamespace + "r4")?.Value
            ?? property?.Element(VariantTypesNamespace + "lpwstr")?.Value
            ?? property?.Element(VariantTypesNamespace + "lpstr")?.Value;

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    public void Remove(string name)
    {
        foreach (var property in PropertiesNamed(name).ToList())
            property.Remove();
    }

    public void RemoveRange(IEnumerable<string> names)
    {
        foreach (var name in names)
            Remove(name);
    }

    public void SetString(string name, string value) =>
        SetValue(name, new XElement(VariantTypesNamespace + "lpwstr", value));

    public void SetBoolean(string name, bool value) =>
        SetValue(name, new XElement(VariantTypesNamespace + "bool", value ? "true" : "false"));

    public void SetDouble(string name, double value) =>
        SetValue(name, new XElement(
            VariantTypesNamespace + "r8",
            value.ToString("G", CultureInfo.InvariantCulture)));

    public XElement ToXElement() => new(_root);

    public XDocument ToXDocument(bool includeXmlDeclaration = false)
    {
        var document = new XDocument(ToXElement());
        if (includeXmlDeclaration)
            document.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
        return document;
    }

    private XElement? FindByName(string name) =>
        PropertiesNamed(name).FirstOrDefault();

    private IEnumerable<XElement> PropertiesNamed(string name) =>
        _root.Elements(PropertyElementName)
            .Where(property => string.Equals(
                property.Attribute("name")?.Value,
                name,
                StringComparison.Ordinal));

    private void SetValue(string name, XElement valueElement)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Custom document property name must be non-empty.", nameof(name));

        var matches = PropertiesNamed(name).ToList();
        var reusablePid = matches
            .Select(ParsePid)
            .FirstOrDefault(pid => pid >= 2);

        foreach (var property in matches)
            property.Remove();

        var usedPids = UsedPids();
        var pid = reusablePid >= 2 && !usedPids.Contains(reusablePid)
            ? reusablePid
            : AllocatePid(usedPids);

        _root.Add(new XElement(
            PropertyElementName,
            new XAttribute("fmtid", DefaultFormatId),
            new XAttribute("pid", pid.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("name", name),
            valueElement));
    }

    private HashSet<int> UsedPids() =>
        _root.Elements(PropertyElementName)
            .Select(ParsePid)
            .Where(pid => pid >= 2)
            .ToHashSet();

    private static int AllocatePid(IReadOnlySet<int> usedPids)
    {
        for (var pid = 2; ; pid++)
        {
            if (!usedPids.Contains(pid))
                return pid;
        }
    }

    private static int ParsePid(XElement property) =>
        int.TryParse(
            property.Attribute("pid")?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var pid)
            ? pid
            : -1;
}
