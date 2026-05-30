using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class HighlightCellsRuleDialog : ConditionalFormatDialog
{
    public HighlightCellsRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = UiText.Format("ConditionalFormatDialog_HighlightCellsRuleTitleFormat", RuleTypeDisplayName(ruleType));
    }
}

public sealed class TopBottomRuleDialog : ConditionalFormatDialog
{
    public TopBottomRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = UiText.Format("ConditionalFormatDialog_TopBottomRuleTitleFormat", RuleTypeDisplayName(ruleType));
    }
}

public sealed class DataBarRuleDialog : ConditionalFormatDialog
{
    public DataBarRuleDialog(GridRange range)
        : base("Data Bar", range)
    {
        Title = UiText.Get("ConditionalFormatDialog_DataBarRuleTitle");
    }
}

public sealed class ColorScaleRuleDialog : ConditionalFormatDialog
{
    public ColorScaleRuleDialog(GridRange range)
        : base("Color Scale", range)
    {
        Title = UiText.Get("ConditionalFormatDialog_ColorScaleRuleTitle");
    }
}

public sealed class IconSetRuleDialog : ConditionalFormatDialog
{
    public IconSetRuleDialog(GridRange range)
        : base("Icon Set", range)
    {
        Title = UiText.Get("ConditionalFormatDialog_IconSetRuleTitle");
    }
}

public sealed class NewConditionalFormatRuleDialog : ConditionalFormatDialog
{
    public NewConditionalFormatRuleDialog(string ruleType, GridRange range)
        : base(ruleType, range)
    {
        Title = UiText.Get("ConditionalFormatDialog_NewTitle");
    }
}

public static class ConditionalFormatDialogFactory
{
    public static ConditionalFormatDialog Create(string ruleType, GridRange range) =>
        ruleType switch
        {
            "Greater Than" or "Less Than" or "Equal To" or "Between" or "Text Contains" or "Date Occurring" or "Duplicate Values" or
            "Blanks" or "No Blanks" or "Errors" or "No Errors" =>
                new HighlightCellsRuleDialog(ruleType, range),
            "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" or "Above Average" or "Below Average" =>
                new TopBottomRuleDialog(ruleType, range),
            "Data Bar" => new DataBarRuleDialog(range),
            "Color Scale" => new ColorScaleRuleDialog(range),
            "Icon Set" => new IconSetRuleDialog(range),
            _ => new NewConditionalFormatRuleDialog(ruleType, range)
        };
}
