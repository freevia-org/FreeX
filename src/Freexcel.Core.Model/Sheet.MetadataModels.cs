namespace Freexcel.Core.Model;

public sealed record CommentReply(string Text, string Author = "Freexcel");

public sealed record ThreadedComment(string Text, string Author = "Freexcel")
{
    public IReadOnlyList<CommentReply> Replies { get; init; } = [];
    public bool IsResolved { get; init; } = false;
}

public enum HyperlinkTargetKind
{
    ExistingFileOrWebPage,
    CreateNewDocument,
    PlaceInThisDocument,
    EmailAddress
}

public sealed record HyperlinkMetadata(
    HyperlinkTargetKind LinkType = HyperlinkTargetKind.ExistingFileOrWebPage,
    string ScreenTip = "",
    string Bookmark = "");

public sealed record WorksheetAutoFilterModel(string? Reference, string? NativeXml);

public sealed class WorksheetProtectionMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPageSetupMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPrintOptionsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetSheetFormatMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetDimensionMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetSheetPropertiesMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPrimaryViewMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPageBreaksMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<uint, Dictionary<string, string>> BreakNativeAttributes { get; set; } = [];
}

public sealed class WorksheetCellWatchesMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> WatchNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorksheetIgnoredErrorsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> ErrorNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorksheetSingleXmlCellsModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetSingleXmlCellModel> Cells { get; set; } = [];
}

public sealed class WorksheetSingleXmlCellModel
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public int? XmlCellPropertyId { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetPageMarginsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetHeaderFooterMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetSmartTagsModel
{
    public string? NativeXml { get; set; }
    public List<WorksheetCellSmartTagsModel> Cells { get; set; } = [];
}

public sealed class WorksheetCellSmartTagsModel
{
    public string? Reference { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetCellSmartTagModel> Tags { get; set; } = [];
}

public sealed class WorksheetCellSmartTagModel
{
    public string? Type { get; set; }
    public bool? Deleted { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetCellSmartTagPropertyModel> Properties { get; set; } = [];
}

public sealed class WorksheetCellSmartTagPropertyModel
{
    public string? Key { get; set; }
    public string? Value { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetDataConsolidationModel
{
    public string? Function { get; set; }
    public bool? LeftLabels { get; set; }
    public bool? TopLabels { get; set; }
    public bool? Link { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetDataConsolidationReferenceModel> References { get; set; } = [];
}

public sealed class WorksheetDataConsolidationReferenceModel
{
    public string? Reference { get; set; }
    public string? Sheet { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetSortStateModel
{
    public string? Reference { get; set; }
    public bool? ColumnSort { get; set; }
    public bool? CaseSensitive { get; set; }
    public string? SortMethod { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetSortConditionModel> Conditions { get; set; } = [];
}

public sealed class WorksheetSortConditionModel
{
    public string? Reference { get; set; }
    public bool? Descending { get; set; }
    public string? SortBy { get; set; }
    public string? CustomList { get; set; }
    public string? DxfId { get; set; }
    public string? IconSet { get; set; }
    public string? IconId { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetAdditionalViewsModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetAdditionalViewModel> Views { get; set; } = [];
}

public sealed class WorksheetAdditionalViewModel
{
    public string? WorkbookViewId { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetCustomPropertyMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed record WorksheetCustomProperty(
    string Name,
    int Id,
    WorksheetCustomPropertyMetadataModel? Metadata = null);

public sealed record WorksheetPhoneticProperties(string? FontId, string? Type, string? Alignment);

public enum WorksheetViewMode
{
    Normal,
    PageBreakPreview,
    PageLayout
}
