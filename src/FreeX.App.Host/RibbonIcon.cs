namespace FreeX.App.Host;

/// <summary>
/// FreeX XAML compatibility type over the shared ribbon icon renderer. FreeX contributes only its
/// command-artwork resolver; dependency properties and fallback vector rendering stay shared.
/// </summary>
public sealed class RibbonIcon : Free.Shared.Ribbon.Wpf.RibbonIcon
{
    public RibbonIcon()
    {
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CommandIconElementResolver =
            RibbonIconFactory.TryCreateCommandIconElement;
    }
}
