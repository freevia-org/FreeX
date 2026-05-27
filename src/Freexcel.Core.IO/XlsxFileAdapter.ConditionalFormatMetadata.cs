using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyNativeConditionalFormatRuleMetadata(
        ConditionalFormat format,
        XElement rule,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormatRuleAttributes(rule);
        if (nativeAttributes.Count > 0)
            format.NativeAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormatRuleChildXmls(rule, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativeChildXmls = nativeChildren;
    }

    private static void ApplyNativeConditionalFormattingContainerMetadata(
        ConditionalFormat format,
        XElement conditionalFormatting,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormattingContainerAttributes(conditionalFormatting);
        if (nativeAttributes.Count > 0)
            format.NativeContainerAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormattingContainerChildXmls(conditionalFormatting, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativeContainerChildXmls = nativeChildren;
    }

    private static void ApplyNativeConditionalFormatPayloadMetadata(
        ConditionalFormat format,
        XElement payload,
        XNamespace worksheetNs)
    {
        var nativeAttributes = ReadNativeConditionalFormatPayloadAttributes(format.RuleType, payload);
        if (nativeAttributes.Count > 0)
            format.NativePayloadAttributes = nativeAttributes;

        var nativeChildren = ReadNativeConditionalFormatPayloadChildXmls(format.RuleType, payload, worksheetNs);
        if (nativeChildren.Count > 0)
            format.NativePayloadChildXmls = nativeChildren;
    }

    private static Dictionary<string, string> ReadNativeConditionalFormatRuleAttributes(XElement rule)
    {
        string[] modeledAttributes = ["type", "priority", "dxfId", "stopIfTrue"];
        return rule.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormatRuleChildXmls(XElement rule, XNamespace worksheetNs)
    {
        XName[] modeledChildren =
        [
            worksheetNs + "colorScale",
            worksheetNs + "dataBar",
            worksheetNs + "iconSet",
            worksheetNs + "formula"
        ];
        return rule.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadNativeConditionalFormattingContainerAttributes(XElement conditionalFormatting)
    {
        string[] modeledAttributes = ["sqref"];
        return conditionalFormatting.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormattingContainerChildXmls(
        XElement conditionalFormatting,
        XNamespace worksheetNs) =>
        conditionalFormatting.Elements()
            .Where(element => element.Name != worksheetNs + "cfRule")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static Dictionary<string, string> ReadNativeConditionalFormatPayloadAttributes(
        CfRuleType ruleType,
        XElement payload)
    {
        string[] modeledAttributes = ruleType switch
        {
            CfRuleType.DataBar => ["showValue", "minLength", "maxLength", "border", "axisPosition"],
            CfRuleType.IconSet => ["iconSet", "showValue", "reverse"],
            _ => []
        };
        return payload.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && !modeledAttributes.Contains(attribute.Name.LocalName))
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
    }

    private static List<string> ReadNativeConditionalFormatPayloadChildXmls(
        CfRuleType ruleType,
        XElement payload,
        XNamespace worksheetNs)
    {
        XName[] modeledChildren = ruleType switch
        {
            CfRuleType.ColorScale => [worksheetNs + "cfvo", worksheetNs + "color"],
            CfRuleType.DataBar => [],
            CfRuleType.IconSet => [worksheetNs + "cfvo", worksheetNs + "cfIcon"],
            _ => []
        };
        return payload.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Where(element => ruleType != CfRuleType.DataBar || !IsModeledDataBarPayloadChild(element, worksheetNs))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static bool IsModeledDataBarPayloadChild(XElement element, XNamespace worksheetNs) =>
        element.Name == worksheetNs + "cfvo" ||
        element.Name == worksheetNs + "color" ||
        (IsAdvancedDataBarColorChild(element, worksheetNs) && XlsxColorReader.TryReadRgbColor(element, out _));

    private static List<string> ReadNativeX14DataBarPayloadChildXmls(XElement x14DataBar, XNamespace x14Ns) =>
        x14DataBar.Elements()
            .Where(element =>
                IsNativeOnlyX14DataBarColorChild(element, x14Ns) ||
                (IsAdvancedDataBarColorChild(element, x14Ns) && !XlsxColorReader.TryReadRgbColor(element, out _)))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

    private static bool IsAdvancedDataBarColorChild(XElement element, XNamespace dataBarNs) =>
        element.Name == dataBarNs + "axisColor" ||
        element.Name == dataBarNs + "negativeFillColor" ||
        element.Name == dataBarNs + "negativeBorderColor";

    private static bool IsNativeOnlyX14DataBarColorChild(XElement element, XNamespace x14Ns) =>
        element.Name == x14Ns + "fillColor" ||
        element.Name == x14Ns + "borderColor";

    private static IReadOnlyList<string> AppendNativePayloadChildXmls(
        IReadOnlyList<string>? existing,
        IReadOnlyList<string> additions)
    {
        var result = existing?.ToList() ?? [];
        result.AddRange(additions);
        return result;
    }
}
