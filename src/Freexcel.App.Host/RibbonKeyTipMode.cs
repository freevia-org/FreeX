using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed class RibbonKeyTipMode
{
    public bool IsActive { get; private set; }

    public void Enter()
    {
        IsActive = true;
    }

    public void Cancel()
    {
        IsActive = false;
    }

    public RibbonKeyTipModeResult HandleTopLevelKey(Key key)
    {
        if (!IsActive)
            return RibbonKeyTipModeResult.Ignored;

        if (key == Key.Escape)
        {
            Cancel();
            return RibbonKeyTipModeResult.Cancel();
        }

        var keyTip = ToKeyTipToken(key);
        if (keyTip is null)
            return RibbonKeyTipModeResult.Ignored;

        Cancel();
        return RibbonKeyTipModeResult.Handle(keyTip);
    }

    public static string? ToKeyTipToken(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();

        if (key >= Key.D0 && key <= Key.D9)
            return ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)(key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return null;
    }
}

public readonly record struct RibbonKeyTipModeResult(bool Handled, string? KeyTip, bool Canceled)
{
    public static RibbonKeyTipModeResult Ignored { get; } = new(false, null, false);

    public static RibbonKeyTipModeResult Handle(string keyTip) => new(true, keyTip, false);

    public static RibbonKeyTipModeResult Cancel() => new(true, null, true);
}
