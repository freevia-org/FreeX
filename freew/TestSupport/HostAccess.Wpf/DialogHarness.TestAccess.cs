using FreeW.App.Host.Backstage;

namespace FreeW.App.Host;

public sealed partial class MainWindow
{
    internal BackstageView BackstageForVisualHarness => _backstage;
}
