using Free.Shared.Shell;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's shared-tier composition step (the equivalent of FreeX's App.xaml.cs startup): installs FreeP's
/// shell/backstage string implementations, registers the shared dialog-sizing behaviour, and points the
/// shared ribbon-icon factory at FreeP's command-id → glyph resolver so the shared chrome (BackstageFrame
/// rail, QAT, ribbon) draws meaningful icons. Idempotent and safe to call once at startup.
/// </summary>
internal static class AppComposition
{
    public static void InstallSharedSeams()
    {
        AppLocalization.Bootstrap.InstallSharedSeams();
        DialogSizing.RegisterAppDialogSizing();

        // Resolve freep.* command ids to shared glyphs in the shared WPF renderer (BackstageFrame rail, QAT,
        // ribbon). Without this every shared-chrome icon falls back to the generic glyph.
        FreePRibbonIcons.Install();
    }
}
