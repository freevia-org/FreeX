using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxAutoFilterXmlCodec
{
    public static XElement WriteColorFilter(
        WorksheetAutoFilterColorFilterModel colorFilter,
        XNamespace spreadsheetNs,
        int? allocatedDxfId = null)
    {
        var element = new XElement(spreadsheetNs + "colorFilter");
        if (colorFilter.DifferentialFormatIdRaw is not null)
            element.SetAttributeValue("dxfId", colorFilter.DifferentialFormatIdRaw);
        else if (colorFilter.DifferentialFormatId is not null)
            element.SetAttributeValue("dxfId", colorFilter.DifferentialFormatId.Value.ToString(CultureInfo.InvariantCulture));
        else if (allocatedDxfId is not null)
            element.SetAttributeValue("dxfId", allocatedDxfId.Value.ToString(CultureInfo.InvariantCulture));

        if (colorFilter.CellColorRaw is not null)
            element.SetAttributeValue("cellColor", colorFilter.CellColorRaw);
        else if (!colorFilter.CellColor)
            element.SetAttributeValue("cellColor", "0");

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, colorFilter.NativeAttributes);
        return element;
    }

    public static WorksheetAutoFilterColorFilterModel? ReadColorFilter(
        XElement? colorFilter,
        IReadOnlyList<CellStyle>? differentialStyles = null)
    {
        if (colorFilter is null)
            return null;

        var nativeAttributes = colorFilter.Attributes()
            .Where(attribute =>
                !IsModeledAttribute(attribute, "dxfId") &&
                !IsModeledAttribute(attribute, "cellColor"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);
        var dxfId = XlsxXmlAttributeReader.ReadIntAttribute(colorFilter, "dxfId");
        var cellColor = XlsxXmlAttributeReader.ReadBoolAttribute(colorFilter, "cellColor", defaultValue: true);

        CellColor? resolvedColor = null;
        if (dxfId is { } dxfIndex && differentialStyles is not null && dxfIndex >= 0 && dxfIndex < differentialStyles.Count)
        {
            var dxfStyle = differentialStyles[dxfIndex];
            resolvedColor = cellColor ? dxfStyle.FillColor : (CellColor?)dxfStyle.FontColor;
        }

        return new WorksheetAutoFilterColorFilterModel(
            DifferentialFormatId: dxfId,
            CellColor: cellColor,
            DifferentialFormatIdRaw: colorFilter.Attribute("dxfId")?.Value,
            CellColorRaw: colorFilter.Attribute("cellColor")?.Value,
            NativeAttributes: nativeAttributes.Count == 0 ? null : nativeAttributes,
            Color: resolvedColor);
    }

    public static XElement WriteDateGroupItem(
        WorksheetAutoFilterDateGroupItemModel dateGroup,
        XNamespace spreadsheetNs)
    {
        var element = new XElement(spreadsheetNs + "dateGroupItem");
        SetRawOrIntAttribute(element, "year", dateGroup.YearRaw, dateGroup.Year);
        SetRawOrIntAttribute(element, "month", dateGroup.MonthRaw, dateGroup.Month);
        SetRawOrIntAttribute(element, "day", dateGroup.DayRaw, dateGroup.Day);
        SetRawOrIntAttribute(element, "hour", dateGroup.HourRaw, dateGroup.Hour);
        SetRawOrIntAttribute(element, "minute", dateGroup.MinuteRaw, dateGroup.Minute);
        SetRawOrIntAttribute(element, "second", dateGroup.SecondRaw, dateGroup.Second);
        if (!string.IsNullOrWhiteSpace(dateGroup.DateTimeGrouping))
            element.SetAttributeValue("dateTimeGrouping", dateGroup.DateTimeGrouping);

        XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributesIfMissing(element, dateGroup.NativeAttributes);
        return element;
    }

    public static WorksheetAutoFilterDateGroupItemModel ReadDateGroupItem(XElement dateGroup)
    {
        var nativeAttributes = dateGroup.Attributes()
            .Where(attribute =>
                !IsModeledAttribute(attribute, "year") &&
                !IsModeledAttribute(attribute, "month") &&
                !IsModeledAttribute(attribute, "day") &&
                !IsModeledAttribute(attribute, "hour") &&
                !IsModeledAttribute(attribute, "minute") &&
                !IsModeledAttribute(attribute, "second") &&
                !IsModeledAttribute(attribute, "dateTimeGrouping"))
            .ToDictionary(attribute => attribute.Name.ToString(), attribute => attribute.Value, StringComparer.Ordinal);

        return new WorksheetAutoFilterDateGroupItemModel(
            Year: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "year"),
            Month: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "month"),
            Day: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "day"),
            Hour: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "hour"),
            Minute: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "minute"),
            Second: XlsxXmlAttributeReader.ReadIntAttribute(dateGroup, "second"),
            DateTimeGrouping: dateGroup.Attribute("dateTimeGrouping")?.Value,
            YearRaw: dateGroup.Attribute("year")?.Value,
            MonthRaw: dateGroup.Attribute("month")?.Value,
            DayRaw: dateGroup.Attribute("day")?.Value,
            HourRaw: dateGroup.Attribute("hour")?.Value,
            MinuteRaw: dateGroup.Attribute("minute")?.Value,
            SecondRaw: dateGroup.Attribute("second")?.Value,
            NativeAttributes: nativeAttributes.Count == 0 ? null : nativeAttributes);
    }

    private static void SetRawOrIntAttribute(XElement element, string name, string? rawValue, int? value)
    {
        if (rawValue is not null)
            element.SetAttributeValue(name, rawValue);
        else if (value is not null)
            element.SetAttributeValue(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsModeledAttribute(XAttribute attribute, string localName) =>
        attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == localName;
}
