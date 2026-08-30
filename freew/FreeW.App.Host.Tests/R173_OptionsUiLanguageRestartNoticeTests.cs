using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// round-173 F2: FreeW's UI-language option silently required a restart to take effect, with no
/// notice shown -- unlike FreeX (<c>MainWindow.Backstage.cs</c> <c>ShowOptionsDialog</c>), which both
/// re-applies the culture immediately AND tells the user a restart is needed. FreeW's
/// <c>MainWindow.OpenOptions</c> (now split into <c>ApplyOptionsEditAndNotify</c> for testability --
/// <c>OptionsDialog.ShowDialog()</c> is a real blocking modal with no headless seam) applied the
/// persisted settings and editor typing options but never compared the new UI language against the
/// old one and never told the user anything.
///
/// <para>
/// These drive the real <see cref="MainWindow"/> production method via reflection and observe the
/// message actually shown through the shared <see cref="HeadlessMessageBox"/> test seam (the same
/// seam <see cref="Free.Shared.Shell.DialogMessageHelper"/> routes through in production) -- not a
/// string that merely looks like the fix is present.
/// </para>
/// </summary>
public sealed class R173_OptionsUiLanguageRestartNoticeTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.R173LanguageRestartTests-");
    private string _tempDir => _temporaryDirectory.Path;

    // r173 remediation: these tests call ApplyAppLanguage, which sets the PROCESS-WIDE
    // CultureInfo.DefaultThreadCurrentUICulture. Without restoring it, every later test in the
    // assembly ran under fr-FR, which made unrelated ribbon-label assertions fail depending on
    // execution order -- a scope auditor reproduced two failures on one run and five on the next
    // from the same commit. Mirrors the save/restore FreeWLocalizationStartupTests already uses
    // for exactly this reason.
    private readonly CultureInfo _originalCurrentCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalCurrentUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _originalDefaultThreadCurrentUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        HeadlessMessageBox.Handler = null;
        CultureInfo.CurrentCulture = _originalCurrentCulture;
        CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultThreadCurrentUiCulture;
        _temporaryDirectory.Dispose();
    }

    [StaFact]
    public void ApplyOptionsEditAndNotify_UiLanguageChanged_ShowsARestartNotice()
    {
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(Path.Combine(_tempDir, "settings.json"));
        var window = new MainWindow(
            new FreeWOptions { UiLanguage = "" },
            store,
            messageService: new RecordingUserMessageService());

        var shownMessages = new List<string?>();
        HeadlessMessageBox.Handler = (message, _) =>
        {
            shownMessages.Add(message);
            return UserMessageResult.Ok;
        };

        var edited = OptionsDialogPlanner.BuildResult(
            recentFilesCap: FreeWOptions.DefaultRecentFilesCap,
            format: null,
            uiLanguage: "fr-FR",
            autoCorrectEnabled: true,
            autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);

        InvokePrivate(window, "ApplyOptionsEditAndNotify", edited);

        // The exact user gesture from the finding: change the UI language, click OK, and get told
        // (rather than silently finding out on the next launch) that a restart is needed.
        Assert.Contains(shownMessages, message => message is not null && message.Contains("restart", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sibling no-regression: an options edit that does NOT touch the UI language (the overwhelmingly
    /// common case -- changing the recent-files cap, AutoCorrect toggles, etc.) must not show any
    /// restart notice at all. The fix must be gated strictly on an actual language change.
    /// </summary>
    [StaFact]
    public void ApplyOptionsEditAndNotify_UiLanguageUnchanged_ShowsNoRestartNotice()
    {
        var store = ApplicationOptionsStore<FreeWOptions>.ForPath(Path.Combine(_tempDir, "settings.json"));
        var window = new MainWindow(
            new FreeWOptions { UiLanguage = "" },
            store,
            messageService: new RecordingUserMessageService());

        var shownMessages = new List<string?>();
        HeadlessMessageBox.Handler = (message, _) =>
        {
            shownMessages.Add(message);
            return UserMessageResult.Ok;
        };

        var edited = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 6,
            format: null,
            uiLanguage: null,
            autoCorrectEnabled: true,
            autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);

        InvokePrivate(window, "ApplyOptionsEditAndNotify", edited);

        Assert.Empty(shownMessages);
    }

    private static void InvokePrivate(MainWindow window, string methodName, params object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, args);
    }
}
