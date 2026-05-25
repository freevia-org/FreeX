using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private static readonly string[] FormatStyleLabels =
        ["Data Bar", "2-Color Scale", "3-Color Scale", "Icon Set"];

    private static readonly (string Label, Color FillColor, Color? FontColor, bool Bold)[] ColorOptions =
    [
        ("Light Red Fill with Dark Red Text", Color.FromRgb(255, 199, 206), Color.FromRgb(156, 0, 6), true),
        ("Yellow Fill with Dark Yellow Text", Color.FromRgb(255, 235, 132), Color.FromRgb(156, 101, 0), true),
        ("Green Fill with Dark Green Text", Color.FromRgb(198, 239, 206), Color.FromRgb(0, 97, 0), true),
        ("Light Red Fill",    Color.FromRgb(255, 199, 206), null, false),
        ("Yellow Fill",       Color.FromRgb(255, 235, 132), null, false),
        ("Green Fill",        Color.FromRgb(198, 239, 206), null, false),
        ("Light Blue Fill",   Color.FromRgb(189, 215, 238), null, false),
        ("Bold Red Text",     Color.FromRgb(255, 255, 255), Color.FromRgb(255, 0, 0), true),
        ("Bold Green Text",   Color.FromRgb(255, 255, 255), Color.FromRgb(0, 176, 80), true),
        ("Custom Format...",  Color.FromRgb(255, 255, 255), null, false),
    ];

    private static readonly string[] ExcelRuleShellTypes =
    [
        "Format all cells based on their values",
        "Format only cells that contain",
        "Format only top or bottom ranked values",
        "Format only values that are above or below average",
        "Format only unique or duplicate values",
        "Use a formula to determine which cells to format"
    ];

    private static readonly IReadOnlyList<string> IconSetStyles = ConditionalFormatIconSetPlanner.Styles;

    private static readonly (string Label, string Value)[] DateOccurringPeriods =
    [
        ("Yesterday", "yesterday"),
        ("Today", "today"),
        ("Tomorrow", "tomorrow"),
        ("Last 7 Days", "last7Days"),
        ("Last Week", "lastWeek"),
        ("This Week", "thisWeek"),
        ("Next Week", "nextWeek"),
        ("Last Month", "lastMonth"),
        ("This Month", "thisMonth"),
        ("Next Month", "nextMonth")
    ];
}
