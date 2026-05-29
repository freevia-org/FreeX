using System.Windows.Media;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private static readonly string[] FormatStyleLabels =
        ["Data Bar", "2-Color Scale", "3-Color Scale", "Icon Set"];

    private string CurrentFormatStyleLabel => _ruleType switch
    {
        "Icon Set"    => "Icon Set",
        "Color Scale" => _colorScaleUseThreeColorBox?.IsChecked == true ? "3-Color Scale" : "2-Color Scale",
        _             => "Data Bar"
    };

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

    private static readonly string[] ConditionKindLabels =
    [
        "Cell Value",
        "Specific Text",
        "Dates Occurring",
        "Blanks",
        "No Blanks",
        "Errors",
        "No Errors"
    ];

    private static readonly (string Label, string RuleType)[] CellValueOperatorLabels =
    [
        ("greater than", "Greater Than"),
        ("less than", "Less Than"),
        ("equal to", "Equal To"),
        ("between", "Between"),
        ("not equal to", "Not Equal To"),
        ("greater than or equal to", "Greater Than Or Equal To"),
        ("less than or equal to", "Less Than Or Equal To"),
        ("not between", "Not Between")
    ];

    private static readonly (string Label, string RuleType)[] SpecificTextOperatorLabels =
    [
        ("containing", "Text Contains"),
        ("not containing", "Text Does Not Contain"),
        ("beginning with", "Text Begins With"),
        ("ending with", "Text Ends With")
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

    private static string ConditionKindLabelForRuleType(string ruleType) => ruleType switch
    {
        "Text Contains" or "Text Does Not Contain" or "Text Begins With" or "Text Ends With" => "Specific Text",
        "Date Occurring" => "Dates Occurring",
        "Blanks" => "Blanks",
        "No Blanks" => "No Blanks",
        "Errors" => "Errors",
        "No Errors" => "No Errors",
        _ => "Cell Value"
    };

    private static string DefaultRuleTypeForConditionKind(string label) => label switch
    {
        "Specific Text" => "Text Contains",
        "Dates Occurring" => "Date Occurring",
        "Blanks" => "Blanks",
        "No Blanks" => "No Blanks",
        "Errors" => "Errors",
        "No Errors" => "No Errors",
        _ => "Greater Than"
    };

    private static string CellValueOperatorLabelForRuleType(string ruleType) =>
        CellValueOperatorLabels.FirstOrDefault(item => item.RuleType == ruleType).Label ?? "greater than";

    private static string SpecificTextOperatorLabelForRuleType(string ruleType) =>
        SpecificTextOperatorLabels.FirstOrDefault(item => item.RuleType == ruleType).Label ?? "containing";
}
