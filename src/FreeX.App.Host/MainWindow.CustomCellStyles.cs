using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Localization;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // Home > Styles > Cell Styles: the built-in presets come from the ribbon definition; the workbook's
    // own named styles, "New Cell Style..." and "Modify" are added to the dropdown each time it opens.
    private const string CustomCellStyleMenuTag = "FreeX.CustomCellStyleItem";

    private void AppendCustomCellStyleMenuItems(ContextMenu menu)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is FrameworkElement { Tag: CustomCellStyleMenuTag })
                menu.Items.RemoveAt(i);
        }

        var insertAt = 0;
        var newItem = new MenuItem { Header = UiText.Get("CellStyles_NewButton"), Tag = CustomCellStyleMenuTag };
        AutomationProperties.SetAutomationId(newItem, "CellStylesNewMenuItem");
        newItem.Click += (_, _) => ShowCustomCellStyleDialog(null);
        menu.Items.Insert(insertAt++, newItem);

        var styles = _workbook.CustomCellStyles.ToList();
        if (styles.Count > 0)
        {
            menu.Items.Insert(insertAt++, new Separator { Tag = CustomCellStyleMenuTag });
            menu.Items.Insert(insertAt++, new MenuItem
            {
                Header = UiText.Get("CellStyles_CustomHeader"),
                IsEnabled = false,
                Tag = CustomCellStyleMenuTag,
            });

            var modifyMenu = new MenuItem { Header = UiText.Get("CellStyles_ModifyButton"), Tag = CustomCellStyleMenuTag };
            foreach (var style in styles)
            {
                var name = style.Name;
                var apply = new MenuItem { Header = name, Tag = CustomCellStyleMenuTag };
                AutomationProperties.SetAutomationId(apply, "CellStyleCustom_" + name);
                apply.Click += (_, _) => ApplyCustomCellStyleByName(name);
                menu.Items.Insert(insertAt++, apply);

                var modify = new MenuItem { Header = name };
                AutomationProperties.SetAutomationId(modify, "CellStyleModify_" + name);
                modify.Click += (_, _) => ShowCustomCellStyleDialog(name);
                modifyMenu.Items.Add(modify);
            }

            menu.Items.Insert(insertAt++, modifyMenu);
        }

        menu.Items.Insert(insertAt, new Separator { Tag = CustomCellStyleMenuTag });
    }

    private void ApplyCustomCellStyleByName(string name)
    {
        var style = _workbook.CustomCellStyles.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (style is null)
            return;

        ApplyStyleDiff(CustomCellStylePlanner.BuildDiff(style));
    }

    /// <summary>Excel's Style dialog: a name plus the groups of the active cell's formatting the style captures.</summary>
    private void ShowCustomCellStyleDialog(string? existingName)
    {
        var existing = existingName is null
            ? null
            : _workbook.CustomCellStyles.FirstOrDefault(s => string.Equals(s.Name, existingName, StringComparison.OrdinalIgnoreCase));
        var parts = existing?.Parts ?? CustomCellStyleParts.All;

        var nameBox = new TextBox { Text = existing?.Name ?? "", Width = 280, IsEnabled = existing is null, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(nameBox, "CellStyleNameBox");
        AutomationProperties.SetName(nameBox, UiText.Get("CellStyles_NameLabel"));

        var checks = new List<(CustomCellStyleParts Part, CheckBox Box)>();
        var includePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (part, key) in new[]
        {
            (CustomCellStyleParts.NumberFormat, "CellStyles_PartNumber"),
            (CustomCellStyleParts.Alignment, "CellStyles_PartAlignment"),
            (CustomCellStyleParts.Font, "CellStyles_PartFont"),
            (CustomCellStyleParts.Border, "CellStyles_PartBorder"),
            (CustomCellStyleParts.Fill, "CellStyles_PartFill"),
            (CustomCellStyleParts.Protection, "CellStyles_PartProtection"),
        })
        {
            var box = new CheckBox { Content = UiText.Get(key), IsChecked = parts.HasFlag(part), Margin = new Thickness(0, 0, 0, 4) };
            AutomationProperties.SetAutomationId(box, "CellStylePart_" + part);
            checks.Add((part, box));
            includePanel.Children.Add(box);
        }

        var ok = new Button { Content = UiText.Get("Options_OK"), MinWidth = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(ok, "CellStyleOkButton");
        var cancel = new Button { Content = UiText.Get("Options_Cancel"), MinWidth = 80, IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = UiText.Get("CellStyles_NameLabel"), Margin = new Thickness(0, 0, 0, 4) });
        body.Children.Add(nameBox);
        body.Children.Add(new TextBlock
        {
            Text = UiText.Get("CellStyles_IncludeHeader"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0),
        });
        body.Children.Add(includePanel);
        body.Children.Add(buttons);

        var dialog = new Window
        {
            Title = UiText.Get("CellStyles_StyleDialogTitle"),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            Content = body,
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true)
            return;

        var chosen = checks
            .Where(c => c.Box.IsChecked == true)
            .Aggregate(CustomCellStyleParts.None, (acc, c) => acc | c.Part);
        DefineCustomCellStyle(existing?.Name ?? nameBox.Text, chosen);
    }

    private void DefineCustomCellStyle(string name, CustomCellStyleParts parts)
    {
        if (!CustomCellStylePlanner.IsValidNewName(name, out var error))
        {
            _messageService.ShowWarning(error ?? UiText.Get("CellStyles_DefineFailed"), UiText.Get("CellStyles_StyleDialogTitle"));
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var address = new CellAddress(_currentSheetId, sheet.ActiveRow ?? 1, sheet.ActiveCol ?? 1);
        var styleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        var command = new DefineCustomCellStyleCommand(new CustomCellStyle(name.Trim(), _workbook.GetStyle(styleId), parts));
        TryExecuteCommand(command, "Define Cell Style");
    }
}
