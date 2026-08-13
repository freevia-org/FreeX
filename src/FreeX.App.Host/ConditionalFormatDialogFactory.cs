using FreeX.App.Presentation.ConditionalFormatting;
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
        : base(ConditionalFormatDialogCatalog.DataBarRule, range)
    {
        Title = UiText.Get("ConditionalFormatDialog_DataBarRuleTitle");
    }
}

public sealed class ColorScaleRuleDialog : ConditionalFormatDialog
{
    public ColorScaleRuleDialog(GridRange range)
        : base(ConditionalFormatDialogCatalog.ColorScaleRule, range)
    {
        Title = UiText.Get("ConditionalFormatDialog_ColorScaleRuleTitle");
    }
}

public sealed class IconSetRuleDialog : ConditionalFormatDialog
{
    public IconSetRuleDialog(GridRange range)
        : base(ConditionalFormatDialogCatalog.IconSetRule, range)
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
        ConditionalFormatDialogCatalog.DialogFamilyForRuleType(ruleType) switch
        {
            ConditionalFormatDialogFamily.HighlightCells => new HighlightCellsRuleDialog(ruleType, range),
            ConditionalFormatDialogFamily.TopBottom => new TopBottomRuleDialog(ruleType, range),
            ConditionalFormatDialogFamily.DataBar => new DataBarRuleDialog(range),
            ConditionalFormatDialogFamily.ColorScale => new ColorScaleRuleDialog(range),
            ConditionalFormatDialogFamily.IconSet => new IconSetRuleDialog(range),
            _ => new NewConditionalFormatRuleDialog(ruleType, range)
        };
}
