using System.Windows.Controls;
using Free.Shared.Ribbon.KeyTips;

namespace FreeX.App.Host;

public static class MenuKeyTipAssigner
{
    public static void AssignUniqueKeyTips(IEnumerable<MenuItem> menuItems)
    {
        var items = menuItems.ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
            PreserveExistingKeyTip(item, used);

        foreach (var item in items)
            AssignMissingKeyTip(item, used);

        foreach (var item in items)
            AssignUniqueKeyTips(item.Items.OfType<MenuItem>());
    }

    private static void PreserveExistingKeyTip(MenuItem item, HashSet<string> used)
    {
        var existing = RibbonKeyTipText.NormalizeOrEmpty(RibbonTooltip.GetKeyTip(item));
        if (string.IsNullOrWhiteSpace(existing))
            return;

        if (RibbonKeyTipText.IsTypeableKeyTip(existing) && RibbonKeyTipText.IsAvailable(existing, used))
        {
            RibbonTooltip.SetKeyTip(item, existing);
            used.Add(existing);
            return;
        }

        RibbonTooltip.SetKeyTip(item, "");
    }

    private static void AssignMissingKeyTip(MenuItem item, HashSet<string> used)
    {
        if (!string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item)))
            return;

        var keyTip = RibbonKeyTipText.CreateUniqueKeyTip(ExtractHeaderText(item.Header), used);
        RibbonTooltip.SetKeyTip(item, keyTip);
        used.Add(keyTip);
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
