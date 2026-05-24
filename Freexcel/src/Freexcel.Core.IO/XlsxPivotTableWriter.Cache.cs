using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private static XDocument ToPivotCacheDefinitionXml(PivotCacheModel cache, XNamespace workbookNs, XNamespace relNs)
    {
        var source = new XElement(workbookNs + "worksheetSource");
        if (!string.IsNullOrWhiteSpace(cache.SourceTableName))
            source.SetAttributeValue("name", cache.SourceTableName);
        if (!string.IsNullOrWhiteSpace(cache.SourceSheetName))
            source.SetAttributeValue("sheet", cache.SourceSheetName);
        if (!string.IsNullOrWhiteSpace(cache.SourceReference))
            source.SetAttributeValue("ref", cache.SourceReference);
        var cacheSource = new XElement(
            workbookNs + "cacheSource",
            new XAttribute("type", cache.SourceType == PivotCacheSourceType.External ? "external" : "worksheet"),
            cache.ConnectionId is { } connectionId ? new XAttribute("connectionId", connectionId.ToString(CultureInfo.InvariantCulture)) : null);
        if (cache.SourceType != PivotCacheSourceType.External)
            cacheSource.Add(source);

        return new XDocument(new XElement(
            workbookNs + "pivotCacheDefinition",
            new XAttribute(XNamespace.Xmlns + "r", relNs),
            cache.IsOlap ? new XAttribute("olap", "1") : null,
            new XAttribute("refreshOnLoad", cache.RefreshOnLoad ? "1" : "0"),
            new XAttribute("saveData", cache.SaveData ? "1" : "0"),
            new XAttribute("enableRefresh", cache.EnableRefresh ? "1" : "0"),
            new XAttribute("preserveSourceSortFilter", cache.PreserveSourceSortFilter ? "1" : "0"),
            cache.MissingItemsLimit is { } missingItemsLimit ? new XAttribute("missingItemsLimit", missingItemsLimit.ToString(CultureInfo.InvariantCulture)) : null,
            cache.CreatedVersion is { } createdVersion ? new XAttribute("createdVersion", createdVersion.ToString(CultureInfo.InvariantCulture)) : null,
            cache.MinRefreshableVersion is { } minRefreshableVersion ? new XAttribute("minRefreshableVersion", minRefreshableVersion.ToString(CultureInfo.InvariantCulture)) : null,
            cache.RefreshedVersion is { } refreshedVersion ? new XAttribute("refreshedVersion", refreshedVersion.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(cache.RefreshedBy) ? new XAttribute("refreshedBy", cache.RefreshedBy) : null,
            !string.IsNullOrWhiteSpace(cache.RefreshedDateIso) ? new XAttribute("refreshedDateIso", cache.RefreshedDateIso) : null,
            new XAttribute("recordCount", (cache.RecordCount ?? 0).ToString(CultureInfo.InvariantCulture)),
            cacheSource,
            new XElement(
                workbookNs + "cacheFields",
                new XAttribute("count", cache.Fields.Count.ToString(CultureInfo.InvariantCulture)),
                cache.Fields.Select(field => new XElement(
                    workbookNs + "cacheField",
                    new XAttribute("name", string.IsNullOrWhiteSpace(field.Name) ? "Field" : field.Name),
                    field.NumberFormatId is { } numFmtId ? new XAttribute("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture)) : null,
                    ToPivotCacheSharedItemsXml(field, workbookNs))))));
    }

    private static XElement ToPivotCacheSharedItemsXml(PivotCacheFieldModel field, XNamespace workbookNs) =>
        new(
            workbookNs + "sharedItems",
            field.SharedItemCount is { } count ? new XAttribute("count", count.ToString(CultureInfo.InvariantCulture)) : null,
            field.ContainsBlank ? new XAttribute("containsBlank", "1") : null,
            field.ContainsString ? new XAttribute("containsString", "1") : null,
            field.ContainsNumber ? new XAttribute("containsNumber", "1") : null,
            field.ContainsDate ? new XAttribute("containsDate", "1") : null,
            field.ContainsMixedTypes ? new XAttribute("containsMixedTypes", "1") : null,
            field.ContainsSemiMixedTypes ? new XAttribute("containsSemiMixedTypes", "1") : null,
            field.ContainsNonDate ? new XAttribute("containsNonDate", "1") : null,
            field.ContainsInteger ? new XAttribute("containsInteger", "1") : null,
            field.ContainsLongText ? new XAttribute("longText", "1") : null,
            field.MinValue is { } minValue ? new XAttribute("minValue", minValue.ToString(CultureInfo.InvariantCulture)) : null,
            field.MaxValue is { } maxValue ? new XAttribute("maxValue", maxValue.ToString(CultureInfo.InvariantCulture)) : null,
            !string.IsNullOrWhiteSpace(field.MinDate) ? new XAttribute("minDate", field.MinDate) : null,
            !string.IsNullOrWhiteSpace(field.MaxDate) ? new XAttribute("maxDate", field.MaxDate) : null,
            (field.SharedItems ?? []).Select(item => ToPivotCacheSharedItemXml(item, workbookNs)));

    private static XElement ToPivotCacheSharedItemXml(string item, XNamespace workbookNs)
    {
        if (double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return new XElement(workbookNs + "n", new XAttribute("v", item));
        if (bool.TryParse(item, out var boolean))
            return new XElement(workbookNs + "b", new XAttribute("v", boolean ? "1" : "0"));
        if (DateTime.TryParse(item, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return new XElement(workbookNs + "d", new XAttribute("v", item));
        return new XElement(workbookNs + "s", new XAttribute("v", item));
    }

    private static XDocument ToPivotCacheDefinitionRelsXml(XNamespace packageRelNs) =>
        new(new XElement(packageRelNs + "Relationships"));
}
