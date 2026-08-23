using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxPivotFilterKindCodec
{
    internal static PivotValueFilterKind? DecodeValue(XElement filter, XNamespace workbookNs)
    {
        var token = filter.Attribute("type")?.Value;
        var topFilterIsBottom = token?.Trim().ToLowerInvariant() is "count" or "percent" or "sum" or "topcount" or "top" &&
            ReadTopFilterIsBottom(filter, workbookNs);
        return DecodeValue(token, topFilterIsBottom);
    }

    internal static PivotValueFilterKind? DecodeValue(string? token, bool topFilterIsBottom = false) =>
        token?.Trim().ToLowerInvariant() switch
        {
            "count" or "percent" or "sum" or "topcount" or "top" => topFilterIsBottom
                ? PivotValueFilterKind.Bottom
                : PivotValueFilterKind.Top,
            "bottomcount" or "bottom" => PivotValueFilterKind.Bottom,
            "valueequal" or "valueequals" => PivotValueFilterKind.Equals,
            "valuenotequal" or "valuedoesnotequal" => PivotValueFilterKind.DoesNotEqual,
            "valuegreaterthan" => PivotValueFilterKind.GreaterThan,
            "valuegreaterthanorequal" => PivotValueFilterKind.GreaterThanOrEqual,
            "valuelessthan" => PivotValueFilterKind.LessThan,
            "valuelessthanorequal" => PivotValueFilterKind.LessThanOrEqual,
            "valuebetween" => PivotValueFilterKind.Between,
            "valuenotbetween" => PivotValueFilterKind.NotBetween,
            _ => null
        };

    internal static PivotLabelFilterKind? DecodeLabel(string? token) =>
        token?.Trim().ToLowerInvariant() switch
        {
            "captionequal" or "captionequals" => PivotLabelFilterKind.Equals,
            "captionnotequal" or "captiondoesnotequal" => PivotLabelFilterKind.DoesNotEqual,
            "captionbeginswith" => PivotLabelFilterKind.BeginsWith,
            "captionendswith" => PivotLabelFilterKind.EndsWith,
            "captioncontains" => PivotLabelFilterKind.Contains,
            "captionnotcontains" or "captiondoesnotcontain" => PivotLabelFilterKind.DoesNotContain,
            "captiongreaterthan" => PivotLabelFilterKind.GreaterThan,
            "captiongreaterthanorequal" => PivotLabelFilterKind.GreaterThanOrEqual,
            "captionlessthan" => PivotLabelFilterKind.LessThan,
            "captionlessthanorequal" => PivotLabelFilterKind.LessThanOrEqual,
            "captionbetween" => PivotLabelFilterKind.Between,
            "dateequal" => PivotLabelFilterKind.DateEqual,
            "datenotequal" => PivotLabelFilterKind.DateNotEqual,
            "dateolderthan" => PivotLabelFilterKind.DateOlderThan,
            "dateolderthanorequal" => PivotLabelFilterKind.DateOlderThanOrEqual,
            "datenewerthan" => PivotLabelFilterKind.DateNewerThan,
            "datenewerthanorequal" => PivotLabelFilterKind.DateNewerThanOrEqual,
            "datebetween" => PivotLabelFilterKind.DateBetween,
            "datenotbetween" => PivotLabelFilterKind.DateNotBetween,
            "yesterday" => PivotLabelFilterKind.Yesterday,
            "today" => PivotLabelFilterKind.Today,
            "tomorrow" => PivotLabelFilterKind.Tomorrow,
            "lastweek" => PivotLabelFilterKind.LastWeek,
            "thisweek" => PivotLabelFilterKind.ThisWeek,
            "nextweek" => PivotLabelFilterKind.NextWeek,
            "lastmonth" => PivotLabelFilterKind.LastMonth,
            "thismonth" => PivotLabelFilterKind.ThisMonth,
            "nextmonth" => PivotLabelFilterKind.NextMonth,
            "lastquarter" => PivotLabelFilterKind.LastQuarter,
            "thisquarter" => PivotLabelFilterKind.ThisQuarter,
            "nextquarter" => PivotLabelFilterKind.NextQuarter,
            "lastyear" => PivotLabelFilterKind.LastYear,
            "thisyear" => PivotLabelFilterKind.ThisYear,
            "nextyear" => PivotLabelFilterKind.NextYear,
            "yeartodate" => PivotLabelFilterKind.YearToDate,
            _ => null
        };

    // ST_PivotFilterType has no above/below-average value tokens. Those two kinds keep using the
    // legacy FreeX-only representation, while invalid enum values remain unsupported.
    internal static string? EncodeValue(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.Top or PivotValueFilterKind.Bottom => "count",
            PivotValueFilterKind.Equals => "valueEqual",
            PivotValueFilterKind.DoesNotEqual => "valueNotEqual",
            PivotValueFilterKind.GreaterThan => "valueGreaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "valueGreaterThanOrEqual",
            PivotValueFilterKind.LessThan => "valueLessThan",
            PivotValueFilterKind.LessThanOrEqual => "valueLessThanOrEqual",
            PivotValueFilterKind.Between => "valueBetween",
            PivotValueFilterKind.NotBetween => "valueNotBetween",
            _ => null
        };

    internal static string EncodeLabel(PivotLabelFilterKind kind) =>
        kind switch
        {
            PivotLabelFilterKind.DoesNotEqual => "captionNotEqual",
            PivotLabelFilterKind.BeginsWith => "captionBeginsWith",
            PivotLabelFilterKind.EndsWith => "captionEndsWith",
            PivotLabelFilterKind.Contains => "captionContains",
            PivotLabelFilterKind.DoesNotContain => "captionNotContains",
            PivotLabelFilterKind.GreaterThan => "captionGreaterThan",
            PivotLabelFilterKind.GreaterThanOrEqual => "captionGreaterThanOrEqual",
            PivotLabelFilterKind.LessThan => "captionLessThan",
            PivotLabelFilterKind.LessThanOrEqual => "captionLessThanOrEqual",
            PivotLabelFilterKind.Between => "captionBetween",
            PivotLabelFilterKind.DateEqual => "dateEqual",
            PivotLabelFilterKind.DateNotEqual => "dateNotEqual",
            PivotLabelFilterKind.DateOlderThan => "dateOlderThan",
            PivotLabelFilterKind.DateOlderThanOrEqual => "dateOlderThanOrEqual",
            PivotLabelFilterKind.DateNewerThan => "dateNewerThan",
            PivotLabelFilterKind.DateNewerThanOrEqual => "dateNewerThanOrEqual",
            PivotLabelFilterKind.DateBetween => "dateBetween",
            PivotLabelFilterKind.DateNotBetween => "dateNotBetween",
            PivotLabelFilterKind.Yesterday => "yesterday",
            PivotLabelFilterKind.Today => "today",
            PivotLabelFilterKind.Tomorrow => "tomorrow",
            PivotLabelFilterKind.LastWeek => "lastWeek",
            PivotLabelFilterKind.ThisWeek => "thisWeek",
            PivotLabelFilterKind.NextWeek => "nextWeek",
            PivotLabelFilterKind.LastMonth => "lastMonth",
            PivotLabelFilterKind.ThisMonth => "thisMonth",
            PivotLabelFilterKind.NextMonth => "nextMonth",
            PivotLabelFilterKind.LastQuarter => "lastQuarter",
            PivotLabelFilterKind.ThisQuarter => "thisQuarter",
            PivotLabelFilterKind.NextQuarter => "nextQuarter",
            PivotLabelFilterKind.LastYear => "lastYear",
            PivotLabelFilterKind.ThisYear => "thisYear",
            PivotLabelFilterKind.NextYear => "nextYear",
            PivotLabelFilterKind.YearToDate => "yearToDate",
            _ => "captionEqual"
        };

    internal static bool AllowsEmptyLabelValue(PivotLabelFilterKind kind) =>
        kind is PivotLabelFilterKind.Yesterday or
            PivotLabelFilterKind.Today or
            PivotLabelFilterKind.Tomorrow or
            PivotLabelFilterKind.LastWeek or
            PivotLabelFilterKind.ThisWeek or
            PivotLabelFilterKind.NextWeek or
            PivotLabelFilterKind.LastMonth or
            PivotLabelFilterKind.ThisMonth or
            PivotLabelFilterKind.NextMonth or
            PivotLabelFilterKind.LastQuarter or
            PivotLabelFilterKind.ThisQuarter or
            PivotLabelFilterKind.NextQuarter or
            PivotLabelFilterKind.LastYear or
            PivotLabelFilterKind.ThisYear or
            PivotLabelFilterKind.NextYear or
            PivotLabelFilterKind.YearToDate;

    private static bool ReadTopFilterIsBottom(XElement filter, XNamespace workbookNs)
    {
        var top10 = filter.Element(workbookNs + "autoFilter")?
            .Element(workbookNs + "filterColumn")?
            .Element(workbookNs + "top10");
        return top10 is not null && !XlsxXmlAttributeReader.ReadBoolAttribute(top10, "top", defaultValue: true);
    }
}
