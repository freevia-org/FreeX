using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum ProtectionDialogMode
{
    Protect,
    Unprotect
}

public sealed record ProtectionDialogResult(ProtectionDialogMode Mode, string? Password);

public static class ProtectionDialogPlanner
{
    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password) =>
        sheet.IsProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password);

    public static ProtectionDialogResult CreateWorkbookResult(Workbook workbook, string? password) =>
        workbook.IsStructureProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password);

    public static bool TryParseAllowEditRange(string text, SheetId sheetId, out GridRange range)
    {
        try
        {
            range = GridRange.Parse(text.Trim(), sheetId);
            return true;
        }
        catch
        {
            range = default;
            return false;
        }
    }
}

public sealed class PasswordProtectionDialog : Window
{
    private readonly PasswordBox _passwordBox = new();

    public string? Password { get; private set; }

    public PasswordProtectionDialog(string title, string prompt)
    {
        Title = title;
        Width = 340;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(_passwordBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(buttons);
        var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            Password = _passwordBox.Password;
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 72, IsCancel = true });

        Content = root;
    }
}

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();

    public GridRange Range { get; private set; }

    public AllowEditRangeDialog(SheetId sheetId, string defaultRange)
    {
        _sheetId = sheetId;
        Title = "Allow Users to Edit Ranges";
        Width = 360;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Range:", Margin = new Thickness(0, 0, 0, 6) });
        _rangeBox.Text = defaultRange;
        root.Children.Add(_rangeBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(buttons);
        var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "Cancel", Width = 72, IsCancel = true });

        Content = root;
    }

    private void Accept()
    {
        if (!ProtectionDialogPlanner.TryParseAllowEditRange(_rangeBox.Text, _sheetId, out var range))
        {
            MessageBox.Show(this, "Enter a valid range.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Range = range;
        DialogResult = true;
    }
}
