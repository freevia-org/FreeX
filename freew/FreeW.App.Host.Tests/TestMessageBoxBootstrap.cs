using System.Runtime.CompilerServices;
using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Installs a non-interactive message-box responder for the whole FreeW host test assembly.
///
/// Several host tests construct a real <c>MainWindow</c> or <c>FileCommands</c>, trigger error paths
/// (corrupt autosave snapshots, failed opens) or close windows with dirty documents. Without this hook
/// those code paths show a blocking modal <c>MessageBox</c> on the STA test thread — there is no
/// message loop to dismiss it, so the dialog appears on the user's desktop during test runs.
///
/// The handler answers every prompt non-interactively: "Don't Save" for the Yes/No/Cancel save prompt
/// (so the window closes and discards throwaway test edits) and OK for everything else. Production
/// never sets <see cref="HeadlessMessageBox.Handler"/>, so real dialogs are unaffected.
/// </summary>
internal static class TestMessageBoxBootstrap
{
    [ModuleInitializer]
    internal static void Install()
    {
        HeadlessMessageBox.Handler = static (_, buttons) => buttons switch
        {
            UserMessageButtons.YesNoCancel => UserMessageResult.No,   // discard unsaved changes on close
            UserMessageButtons.YesNo       => UserMessageResult.No,
            UserMessageButtons.OkCancel    => UserMessageResult.Ok,
            _                              => UserMessageResult.Ok,
        };
    }
}
