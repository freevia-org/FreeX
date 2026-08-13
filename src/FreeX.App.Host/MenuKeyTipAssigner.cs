using System.Windows.Controls;
using Free.Shared.Ribbon.KeyTips;

namespace FreeX.App.Host;

public static class MenuKeyTipAssigner
{
    public static void AssignUniqueKeyTips(IEnumerable<MenuItem> menuItems)
    {
        var items = menuItems.ToList();
        var assignments = MenuKeyTipAssignmentPlanner.AssignUnique(
            items
                .Select(item => new MenuKeyTipAssignmentCandidate(
                    ExtractHeaderText(item.Header),
                    RibbonTooltip.GetKeyTip(item)))
                .ToArray());
        for (var index = 0; index < items.Count; index++)
            RibbonTooltip.SetKeyTip(items[index], assignments[index]);

        foreach (var item in items)
            AssignUniqueKeyTips(item.Items.OfType<MenuItem>());
    }

    private static string ExtractHeaderText(object? header) =>
        header switch
        {
            null => "",
            string text => text,
            AccessText accessText => accessText.Text,
            TextBlock textBlock => WpfTextContentExtractor.ExtractText(textBlock),
            ContentControl contentControl => ExtractHeaderText(contentControl.Content),
            _ => header.ToString() ?? ""
    };
}
