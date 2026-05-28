using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    // ── NativeXmlPreserveBag helpers ──────────────────────────────────────────────

    private static NativeXmlPreserveBag? ToNativeBag(
        Dictionary<string, string>? rawAttributes,
        List<string>? rawChildren,
        string key)
    {
        var nativeAttributes = CleanNativeAttributes(rawAttributes);
        var nativeChildXmls = (rawChildren ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();

        var serialized = XmlNativeBagSerializer.Serialize(nativeAttributes, nativeChildXmls);
        if (serialized is null)
            return null;

        var bag = new NativeXmlPreserveBag();
        bag.Set(key, serialized);
        return bag;
    }

    private static (Dictionary<string, string> Attributes, List<string> Children) FromNativeBag(
        NativeXmlPreserveBag? bag,
        string key)
    {
        if (bag is null)
            return (new Dictionary<string, string>(StringComparer.Ordinal), []);

        var (attrs, children) = XmlNativeBagSerializer.Deserialize(bag.Get(key));
        return (CleanNativeAttributesForSave(attrs), children.Where(xml => !string.IsNullOrWhiteSpace(xml)).ToList());
    }

    // ── Worksheet metadata ────────────────────────────────────────────────────────

    private static NativeXmlPreserveBag? ToWorksheetProtectionMetadata(WorksheetProtectionMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "sheetProtection");

    private static WorksheetProtectionMetadataDto? FromWorksheetProtectionMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "sheetProtection");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetProtectionMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetPageSetupMetadata(WorksheetPageSetupMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "pageSetup");

    private static WorksheetPageSetupMetadataDto? FromWorksheetPageSetupMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "pageSetup");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetPageSetupMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetPrintOptionsMetadata(WorksheetPrintOptionsMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "printOptions");

    private static WorksheetPrintOptionsMetadataDto? FromWorksheetPrintOptionsMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "printOptions");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetPrintOptionsMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetSheetFormatMetadata(WorksheetSheetFormatMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "sheetFormatPr");

    private static WorksheetSheetFormatMetadataDto? FromWorksheetSheetFormatMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "sheetFormatPr");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetSheetFormatMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetDimensionMetadata(WorksheetDimensionMetadataDto? dto)
    {
        if (dto is null) return null;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var serialized = XmlNativeBagSerializer.Serialize(nativeAttributes);
        if (serialized is null) return null;
        var bag = new NativeXmlPreserveBag();
        bag.Set("dimension", serialized);
        return bag;
    }

    private static WorksheetDimensionMetadataDto? FromWorksheetDimensionMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, _) = FromNativeBag(bag, "dimension");
        if (attrs.Count == 0) return null;
        return new WorksheetDimensionMetadataDto { NativeAttributes = attrs };
    }

    private static NativeXmlPreserveBag? ToWorksheetSheetPropertiesMetadata(WorksheetSheetPropertiesMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "sheetPr");

    private static WorksheetSheetPropertiesMetadataDto? FromWorksheetSheetPropertiesMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "sheetPr");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetSheetPropertiesMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetPrimaryViewMetadata(WorksheetPrimaryViewMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "sheetView");

    private static WorksheetPrimaryViewMetadataDto? FromWorksheetPrimaryViewMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "sheetView");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetPrimaryViewMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static WorksheetPageBreaksMetadataModel? ToWorksheetPageBreaksMetadata(WorksheetPageBreaksMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var breakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var pair in dto.BreakNativeAttributes ?? [])
        {
            var attributes = CleanNativeAttributes(pair.Value);
            if (attributes.Count > 0)
                breakNativeAttributes[pair.Key] = attributes;
        }

        if (nativeAttributes.Count == 0 && breakNativeAttributes.Count == 0)
            return null;

        return new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = nativeAttributes,
            BreakNativeAttributes = breakNativeAttributes
        };
    }

    private static WorksheetPageBreaksMetadataDto? FromWorksheetPageBreaksMetadata(WorksheetPageBreaksMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var breakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var pair in model.BreakNativeAttributes)
        {
            var attributes = CleanNativeAttributesForSave(pair.Value);
            if (attributes.Count > 0)
                breakNativeAttributes[pair.Key] = attributes;
        }

        if (nativeAttributes.Count == 0 && breakNativeAttributes.Count == 0)
            return null;

        return new WorksheetPageBreaksMetadataDto
        {
            NativeAttributes = nativeAttributes,
            BreakNativeAttributes = breakNativeAttributes
        };
    }

    private static NativeXmlPreserveBag? ToWorksheetPageMarginsMetadata(WorksheetPageMarginsMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "pageMargins");

    private static WorksheetPageMarginsMetadataDto? FromWorksheetPageMarginsMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "pageMargins");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetPageMarginsMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorksheetHeaderFooterMetadata(WorksheetHeaderFooterMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "headerFooter");

    private static WorksheetHeaderFooterMetadataDto? FromWorksheetHeaderFooterMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "headerFooter");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetHeaderFooterMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    // ── Workbook metadata ─────────────────────────────────────────────────────────

    private static NativeXmlPreserveBag? ToWorkbookProperties(WorkbookPropertiesDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "workbookPr");

    private static WorkbookPropertiesDto? FromWorkbookProperties(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "workbookPr");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorkbookPropertiesDto { NativeAttributes = attrs, NativeChildXmls = children };
    }

    private static NativeXmlPreserveBag? ToWorkbookProtectionMetadata(WorkbookProtectionMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "workbookProtection");

    private static WorkbookProtectionMetadataDto? FromWorkbookProtectionMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "workbookProtection");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorkbookProtectionMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }
}
