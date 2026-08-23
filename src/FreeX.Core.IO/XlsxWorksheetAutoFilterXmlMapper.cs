using System.Xml.Linq;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetAutoFilterXmlMapper
{
    public static WorksheetAutoFilterModel? Read(
        XElement? autoFilter,
        IReadOnlyList<CellStyle>? differentialStyles = null)
    {
        if (autoFilter is null)
            return null;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var model = new WorksheetAutoFilterModel(
            autoFilter.Attribute("ref")?.Value,
            autoFilter.ToString(SaveOptions.DisableFormatting))
        {
            NativeAttributes = ReadNativeAttributes(autoFilter),
            NativeChildXmls = autoFilter
                .Elements()
                .Where(element => element.Name != worksheetNs + "filterColumn")
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToArray()
        };
        model.FilterColumns.AddRange(ReadFilterColumns(autoFilter, worksheetNs, differentialStyles));
        return model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(
        XlsxWorksheetXmlEditSession session,
        Workbook workbook,
        IReadOnlyDictionary<(SheetId, int), int>? colorFilterDxfIds = null)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.AutoFilter is not null))
        {
            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            root.Element(worksheetNs + "autoFilter")?.Remove();
            if (ToAutoFilterXml(sheet.AutoFilter, worksheetNs, sheet.Id, colorFilterDxfIds) is { } autoFilter)
        XlsxWorksheetElementOrder.Insert(root, autoFilter);

            session.MarkDirty(worksheetEdit);
        }
    }

    /// <summary>
    /// Returns the effective AutoFilter range reference -- the exact "ref" text <see cref="ToAutoFilterXml"/>
    /// would emit into the worksheet's own &lt;autoFilter ref=...&gt; element for this model -- or null
    /// when no &lt;autoFilter&gt; element would be written at all (e.g. no reference and no usable native
    /// XML). Used by <see cref="XlsxNamedRangeMapper"/> (R49-io-defined-name-scope-3-2) to keep the
    /// built-in <c>_xlnm._FilterDatabase</c> sheet-scoped defined name's refersTo range in sync with the
    /// live autoFilter range, without duplicating the native-vs-modeled reference resolution this mapper
    /// already performs.
    /// </summary>
    internal static string? GetEffectiveReference(WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return null;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return ToAutoFilterXml(autoFilter, worksheetNs, default, null)?.Attribute("ref")?.Value;
    }

    private static XElement? ToAutoFilterXml(
        WorksheetAutoFilterModel? autoFilter,
        XNamespace worksheetNs,
        SheetId sheetId,
        IReadOnlyDictionary<(SheetId, int), int>? colorFilterDxfIds)
    {
        if (autoFilter is null)
            return null;

        var hasModeledMetadata =
            autoFilter.FilterColumns.Count > 0 ||
            autoFilter.NativeAttributes?.Count > 0 ||
            autoFilter.NativeChildXmls?.Count > 0;
        if (!hasModeledMetadata && TryCreateNativeAutoFilter(autoFilter.NativeXml, worksheetNs, out var nativeAutoFilter))
            return nativeAutoFilter;

        return string.IsNullOrWhiteSpace(autoFilter.Reference)
            ? null
            : ToModeledAutoFilterXml(autoFilter, worksheetNs, sheetId, colorFilterDxfIds);
    }

    private static bool TryCreateNativeAutoFilter(string? nativeXml, XNamespace worksheetNs, out XElement? autoFilter)
    {
        autoFilter = null;
        if (string.IsNullOrWhiteSpace(nativeXml))
            return false;

        try
        {
            var element = XElement.Parse(nativeXml);
            if (element.Name != worksheetNs + "autoFilter")
                return true;

            XlsxWorksheetAutoFilterNormalizer.NormalizeElement(element);
            autoFilter = element;
            return true;
        }
        catch
        {
            // Fall back to a range-only AutoFilter when legacy native JSON contains malformed XML.
            return false;
        }
    }

    private static XElement ToModeledAutoFilterXml(
        WorksheetAutoFilterModel autoFilter,
        XNamespace worksheetNs,
        SheetId sheetId,
        IReadOnlyDictionary<(SheetId, int), int>? colorFilterDxfIds)
    {
        var element = new XElement(
            worksheetNs + "autoFilter",
            new XAttribute("ref", autoFilter.Reference!),
            autoFilter.FilterColumns
                .Where(filterColumn => filterColumn.ColumnId >= 0)
                .Select(filterColumn => ToFilterColumnXml(
                    filterColumn,
                    worksheetNs,
                    colorFilterDxfIds is not null && colorFilterDxfIds.TryGetValue((sheetId, filterColumn.ColumnId), out var allocatedDxfId)
                        ? allocatedDxfId
                        : null)));
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, autoFilter.NativeAttributes);

        foreach (var nativeChildXml in autoFilter.NativeChildXmls ?? [])
        {
            if (TryParseNativeWorksheetChild(nativeChildXml, worksheetNs, "filterColumn") is { } nativeChild)
                element.Add(nativeChild);
        }

        XlsxWorksheetAutoFilterNormalizer.NormalizeElement(element);
        return element;
    }

    private static XElement ToFilterColumnXml(WorksheetAutoFilterColumnModel filterColumn, XNamespace worksheetNs, int? allocatedColorFilterDxfId)
    {
        var element = new XElement(
            worksheetNs + "filterColumn",
            new XAttribute("colId", filterColumn.ColumnId.ToString(CultureInfo.InvariantCulture)));
        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, filterColumn.NativeAttributes);

        var hasCustomFilters = filterColumn.CustomFilters.Count > 0;
        var hasTop10 = filterColumn.Top10 is not null;
        var hasDynamicFilter = filterColumn.DynamicFilter is not null;
        var hasColorFilter = filterColumn.ColorFilter is not null;
        var hasIconFilter = filterColumn.IconFilter is not null;
        if (!hasCustomFilters && !hasTop10 && !hasDynamicFilter && !hasColorFilter && !hasIconFilter &&
            (filterColumn.Values.Count > 0 ||
             filterColumn.IncludeBlank ||
             filterColumn.DateGroups.Count > 0 ||
             filterColumn.NativeFiltersAttributes?.Count > 0))
        {
            var filters = new XElement(
                worksheetNs + "filters",
                filterColumn.IncludeBlank ? new XAttribute("blank", "1") : null,
                filterColumn.Values.Select(value => new XElement(worksheetNs + "filter", new XAttribute("val", value))),
                filterColumn.DateGroups.Select(dateGroup => XlsxAutoFilterXmlCodec.WriteDateGroupItem(dateGroup, worksheetNs)));
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(filters, filterColumn.NativeFiltersAttributes);

            element.Add(filters);
        }

        if (hasCustomFilters)
        {
            var customFilters = new XElement(
                worksheetNs + "customFilters",
                filterColumn.CustomFilters.Select(customFilter => ToCustomFilterXml(customFilter, worksheetNs)));
            if (filterColumn.CustomFiltersAndRaw is not null)
                customFilters.SetAttributeValue("and", filterColumn.CustomFiltersAndRaw);
            else if (filterColumn.CustomFiltersAnd)
                customFilters.SetAttributeValue("and", "1");

            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(customFilters, filterColumn.NativeCustomFiltersAttributes);

            element.Add(customFilters);
        }

        if (!hasCustomFilters && filterColumn.Top10 is { } top10)
            element.Add(ToTop10Xml(top10, worksheetNs));
        else if (!hasCustomFilters && filterColumn.DynamicFilter is { } dynamicFilter)
            element.Add(ToDynamicFilterXml(dynamicFilter, worksheetNs));
        else if (!hasCustomFilters && filterColumn.ColorFilter is { } colorFilter)
            element.Add(XlsxAutoFilterXmlCodec.WriteColorFilter(colorFilter, worksheetNs, allocatedColorFilterDxfId));
        else if (!hasCustomFilters && filterColumn.IconFilter is { } iconFilter)
            element.Add(ToIconFilterXml(iconFilter, worksheetNs));

        foreach (var nativeFilterXml in filterColumn.NativeFilterXmls)
        {
            if (TryParseNativeWorksheetChild(nativeFilterXml, worksheetNs, "filters", "customFilters", "top10", "dynamicFilter", "colorFilter", "iconFilter") is { } nativeFilter)
                element.Add(nativeFilter);
        }

        return element;
    }

    private static XElement ToIconFilterXml(WorksheetAutoFilterIconFilterModel iconFilter, XNamespace worksheetNs)
    {
        var element = new XElement(worksheetNs + "iconFilter");
        if (!string.IsNullOrWhiteSpace(iconFilter.IconSet))
            element.SetAttributeValue("iconSet", iconFilter.IconSet);
        if (iconFilter.IconIdRaw is not null)
            element.SetAttributeValue("iconId", iconFilter.IconIdRaw);
        else if (iconFilter.IconId is not null)
            element.SetAttributeValue("iconId", iconFilter.IconId.Value.ToString(CultureInfo.InvariantCulture));

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, iconFilter.NativeAttributes);

        return element;
    }

    private static XElement ToDynamicFilterXml(WorksheetAutoFilterDynamicFilterModel dynamicFilter, XNamespace worksheetNs)
    {
        var element = new XElement(worksheetNs + "dynamicFilter");
        element.SetAttributeValue("type", string.IsNullOrWhiteSpace(dynamicFilter.Type) ? "aboveAverage" : dynamicFilter.Type);
        if (dynamicFilter.ValueRaw is not null)
            element.SetAttributeValue("val", dynamicFilter.ValueRaw);
        else if (dynamicFilter.Value is not null)
            element.SetAttributeValue("val", dynamicFilter.Value.Value.ToString(CultureInfo.InvariantCulture));
        if (dynamicFilter.MaxValueRaw is not null)
            element.SetAttributeValue("maxVal", dynamicFilter.MaxValueRaw);
        else if (dynamicFilter.MaxValue is not null)
            element.SetAttributeValue("maxVal", dynamicFilter.MaxValue.Value.ToString(CultureInfo.InvariantCulture));

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, dynamicFilter.NativeAttributes);

        return element;
    }

    private static XElement ToTop10Xml(WorksheetAutoFilterTop10Model top10, XNamespace worksheetNs)
    {
        var element = new XElement(worksheetNs + "top10");
        if (top10.TopRaw is not null)
            element.SetAttributeValue("top", top10.TopRaw);
        else if (!top10.Top)
            element.SetAttributeValue("top", "0");

        if (top10.PercentRaw is not null)
            element.SetAttributeValue("percent", top10.PercentRaw);
        else if (top10.Percent)
            element.SetAttributeValue("percent", "1");

        if (top10.ValueRaw is not null)
            element.SetAttributeValue("val", top10.ValueRaw);
        else if (top10.Value is not null)
            element.SetAttributeValue("val", top10.Value.Value.ToString(CultureInfo.InvariantCulture));
        else
            element.SetAttributeValue("val", "10");

        if (top10.FilterValueRaw is not null)
            element.SetAttributeValue("filterVal", top10.FilterValueRaw);
        else if (top10.FilterValue is not null)
            element.SetAttributeValue("filterVal", top10.FilterValue.Value.ToString(CultureInfo.InvariantCulture));

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, top10.NativeAttributes);

        return element;
    }

    private static XElement ToCustomFilterXml(WorksheetAutoFilterCustomFilterModel customFilter, XNamespace worksheetNs)
    {
        var element = new XElement(worksheetNs + "customFilter");
        if (customFilter.Operator is not null)
            element.SetAttributeValue("operator", customFilter.Operator);
        if (customFilter.Value is not null)
            element.SetAttributeValue("val", customFilter.Value);

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, customFilter.NativeAttributes);

        return element;
    }

    private static XElement? TryParseNativeWorksheetChild(string? nativeXml, XNamespace worksheetNs, params string[] modeledLocalNames)
    {
        if (string.IsNullOrWhiteSpace(nativeXml))
            return null;

        try
        {
            var nativeChild = XElement.Parse(nativeXml);
            return nativeChild.Name.Namespace == worksheetNs && !modeledLocalNames.Contains(nativeChild.Name.LocalName, StringComparer.Ordinal)
                ? nativeChild
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string>? ReadNativeAttributes(XElement autoFilter)
    {
        var attributes = autoFilter.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "ref")
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static IEnumerable<WorksheetAutoFilterColumnModel> ReadFilterColumns(
        XElement autoFilter,
        XNamespace worksheetNs,
        IReadOnlyList<CellStyle>? differentialStyles)
    {
        foreach (var column in autoFilter.Elements(worksheetNs + "filterColumn"))
        {
            var filters = column.Element(worksheetNs + "filters");
            var customFilters = column.Element(worksheetNs + "customFilters");
            var top10 = column.Element(worksheetNs + "top10");
            var dynamicFilter = column.Element(worksheetNs + "dynamicFilter");
            var colorFilter = column.Element(worksheetNs + "colorFilter");
            var iconFilter = column.Element(worksheetNs + "iconFilter");
            var nativeFilters = column.Elements()
                .Where(element => element.Name != worksheetNs + "filters" && element.Name != worksheetNs + "customFilters" && element.Name != worksheetNs + "top10" && element.Name != worksheetNs + "dynamicFilter" && element.Name != worksheetNs + "colorFilter" && element.Name != worksheetNs + "iconFilter")
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToArray();
            var nativeAttributes = column.Attributes()
                .Where(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName != "colId")
                .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);
            var nativeFiltersAttributes = filters?
                .Attributes()
                .Where(attribute => !IsWorksheetAutoFilterModeledAttribute(attribute, "blank"))
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
            var customFiltersAnd = XlsxXmlAttributeReader.ReadBoolAttribute(customFilters, "and");
            var nativeCustomFiltersAttributes = customFilters?
                .Attributes()
                .Where(attribute => !IsWorksheetAutoFilterModeledAttribute(attribute, "and"))
                .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
            var filterColumn = new WorksheetAutoFilterColumnModel(
                XlsxXmlAttributeReader.ReadIntAttribute(column, "colId") ?? -1,
                filters?
                    .Elements(worksheetNs + "filter")
                    .Select(filter => filter.Attribute("val")?.Value)
                    // Keep any present (non-null) val, including whitespace-only strings: Excel emits
                    // a legal ST_Xstring like <filter val=" "/> for a space-valued cell, and dropping it
                    // here silently changes the filter on round-trip. Only a genuinely absent val
                    // attribute (null) is meaningless and should be discarded; blanks are separately
                    // handled via the filters/@blank attribute.
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray() ?? [],
                XlsxXmlAttributeReader.ReadBoolAttribute(filters, "blank"),
                customFilters?
                    .Elements(worksheetNs + "customFilter")
                    .Select(filter => new WorksheetAutoFilterCustomFilterModel(
                        filter.Attribute("operator")?.Value,
                        filter.Attribute("val")?.Value,
                        ReadCustomFilterNativeAttributes(filter)))
                    .ToArray() ?? [],
                customFiltersAnd,
                customFilters?.Attribute("and")?.Value,
                nativeCustomFiltersAttributes?.Count > 0 ? nativeCustomFiltersAttributes : null,
                ReadTop10(top10),
                ReadDynamicFilter(dynamicFilter),
                XlsxAutoFilterXmlCodec.ReadColorFilter(colorFilter, differentialStyles),
                ReadIconFilter(iconFilter),
                filters?
                    .Elements(worksheetNs + "dateGroupItem")
                    .Select(XlsxAutoFilterXmlCodec.ReadDateGroupItem)
                    .ToArray() ?? [],
                nativeFiltersAttributes?.Count > 0 ? nativeFiltersAttributes : null,
                nativeFilters,
                nativeAttributes.Count == 0 ? null : nativeAttributes);
            if (filterColumn.ColumnId >= 0 &&
                (filterColumn.Values.Count > 0 ||
                 filterColumn.IncludeBlank ||
                 filterColumn.DateGroups.Count > 0 ||
                 filterColumn.NativeFiltersAttributes?.Count > 0 ||
                 filterColumn.CustomFilters.Count > 0 ||
                 filterColumn.CustomFiltersAndRaw is not null ||
                 filterColumn.NativeCustomFiltersAttributes?.Count > 0 ||
                 filterColumn.Top10 is not null ||
                 filterColumn.DynamicFilter is not null ||
                 filterColumn.ColorFilter is not null ||
                 filterColumn.IconFilter is not null ||
                 filterColumn.NativeFilterXmls.Count > 0 ||
                 filterColumn.NativeAttributes?.Count > 0))
            {
                yield return filterColumn;
            }
        }
    }

    private static WorksheetAutoFilterIconFilterModel? ReadIconFilter(XElement? iconFilter)
    {
        if (iconFilter is null)
            return null;

        var nativeAttributes = iconFilter.Attributes()
            .Where(attribute =>
                !IsWorksheetAutoFilterModeledAttribute(attribute, "iconSet") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "iconId"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return new WorksheetAutoFilterIconFilterModel(
            IconSet: iconFilter.Attribute("iconSet")?.Value,
            IconId: XlsxXmlAttributeReader.ReadIntAttribute(iconFilter, "iconId"),
            IconIdRaw: iconFilter.Attribute("iconId")?.Value,
            NativeAttributes: nativeAttributes.Count == 0 ? null : nativeAttributes);
    }

    private static WorksheetAutoFilterDynamicFilterModel? ReadDynamicFilter(XElement? dynamicFilter)
    {
        if (dynamicFilter is null)
            return null;

        var nativeAttributes = dynamicFilter.Attributes()
            .Where(attribute =>
                !IsWorksheetAutoFilterModeledAttribute(attribute, "type") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "val") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "maxVal"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return new WorksheetAutoFilterDynamicFilterModel(
            Type: dynamicFilter.Attribute("type")?.Value,
            Value: XlsxXmlAttributeReader.ReadDoubleAttribute(dynamicFilter, "val"),
            MaxValue: XlsxXmlAttributeReader.ReadDoubleAttribute(dynamicFilter, "maxVal"),
            ValueRaw: dynamicFilter.Attribute("val")?.Value,
            MaxValueRaw: dynamicFilter.Attribute("maxVal")?.Value,
            NativeAttributes: nativeAttributes.Count == 0 ? null : nativeAttributes);
    }

    private static WorksheetAutoFilterTop10Model? ReadTop10(XElement? top10)
    {
        if (top10 is null)
            return null;

        var nativeAttributes = top10.Attributes()
            .Where(attribute =>
                !IsWorksheetAutoFilterModeledAttribute(attribute, "top") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "percent") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "val") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "filterVal"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return new WorksheetAutoFilterTop10Model(
            Top: XlsxXmlAttributeReader.ReadBoolAttribute(top10, "top", defaultValue: true),
            Percent: XlsxXmlAttributeReader.ReadBoolAttribute(top10, "percent"),
            Value: XlsxXmlAttributeReader.ReadDoubleAttribute(top10, "val"),
            FilterValue: XlsxXmlAttributeReader.ReadDoubleAttribute(top10, "filterVal"),
            TopRaw: top10.Attribute("top")?.Value,
            PercentRaw: top10.Attribute("percent")?.Value,
            ValueRaw: top10.Attribute("val")?.Value,
            FilterValueRaw: top10.Attribute("filterVal")?.Value,
            NativeAttributes: nativeAttributes.Count == 0 ? null : nativeAttributes);
    }

    private static IReadOnlyDictionary<string, string>? ReadCustomFilterNativeAttributes(XElement filter)
    {
        var attributes = filter.Attributes()
            .Where(attribute =>
                !IsWorksheetAutoFilterModeledAttribute(attribute, "operator") &&
                !IsWorksheetAutoFilterModeledAttribute(attribute, "val"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        return attributes.Count == 0 ? null : attributes;
    }

    private static bool IsWorksheetAutoFilterModeledAttribute(XAttribute attribute, string localName) =>
        attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == localName;

}
