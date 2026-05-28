using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static NativeXmlPreserveBag? ToWorksheetCustomPropertyMetadata(WorksheetCustomPropertyMetadataDto? dto)
        => dto is null ? null : ToNativeBag(dto.NativeAttributes, dto.NativeChildXmls, "customPr");

    private static WorksheetCustomPropertyMetadataDto? FromWorksheetCustomPropertyMetadata(NativeXmlPreserveBag? bag)
    {
        var (attrs, children) = FromNativeBag(bag, "customPr");
        if (attrs.Count == 0 && children.Count == 0) return null;
        return new WorksheetCustomPropertyMetadataDto { NativeAttributes = attrs, NativeChildXmls = children };
    }
}
