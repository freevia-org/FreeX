using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Control? _ribbonControl;
    private IRibbonCommandRegistry? _ribbonCommandRegistry;
    private bool _ribbonKeyTipsVisible;
    internal bool RibbonKeyTipsVisibleForTest => _ribbonKeyTipsVisible;
    internal bool HasWindowIconForTest => Icon is not null;
    internal IRibbonCommandRegistry? RibbonCommandRegistryForTest => _ribbonCommandRegistry;
    internal Control? RibbonControlForTest => _ribbonControl;
    internal Thickness CellAddressPaddingForTest => _cellAddressText.Padding;

    internal void ShowBackstageOverlayForTest() => ShowBackstageOverlay();

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "FreeX.ico");
        if (!File.Exists(iconPath))
            return;

        try
        {
            using var stream = File.OpenRead(iconPath);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // A missing or unsupported desktop icon must not prevent the workbook from opening.
        }
    }

    private bool TryHandleRibbonKeyTips(KeyEventArgs args)
    {
        // Keep the generic Alt handler from re-opening the Data ribbon while the WPF-equivalent
        // worksheet-editing or Backstage exclusion is active. The legacy sequence dispatcher has
        // already reset any partial input; returning true here stops the fallback route without
        // marking the key handled.
        if (IsDataRibbonKeyTipAttempt(args) && !CanHandleLegacyDataFilterSequence(args))
        {
            ResetRibbonKeyTipSequence();
            args.Handled = false;
            return true;
        }

        if (args.Key is Key.LeftAlt or Key.RightAlt ||
            args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None)
        {
            SetRibbonKeyTipsVisible(!_ribbonKeyTipsVisible);
            args.Handled = true;
            return true;
        }

        var directAltToken = args.KeyModifiers == KeyModifiers.Alt
            ? ToRibbonKeyTipToken(args.Key)
            : null;
        if (!_ribbonKeyTipsVisible && directAltToken is null)
            return false;

        if (args.Key == Key.Escape)
        {
            SetRibbonKeyTipsVisible(false);
            args.Handled = true;
            return true;
        }

        var token = directAltToken ?? ToRibbonKeyTipToken(args.Key);
        if (token is null || _ribbonControl is null)
            return false;

        var activated = AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, token);
        SetRibbonKeyTipsVisible(false);
        args.Handled = activated;
        return activated;
    }

    private void SetRibbonKeyTipsVisible(bool visible)
    {
        _ribbonKeyTipsVisible = visible;
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(_ribbonControl, visible);
    }

    private static string? ToRibbonKeyTipToken(Key key)
    {
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return name.ToUpperInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            return name[1].ToString();
        return null;
    }
}
