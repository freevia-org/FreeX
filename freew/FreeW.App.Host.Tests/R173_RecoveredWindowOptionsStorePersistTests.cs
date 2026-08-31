using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// round-173 F1: <see cref="MainWindow"/>'s <c>optionsStore</c> constructor parameter defaults to a
/// private, non-persisting <see cref="InMemoryApplicationOptionsStore{T}"/> when a caller omits it.
/// <c>OpenNewWindowWithRecoveredSnapshot</c> (crash-recovery's "extra window per pending snapshot"
/// path) and <c>OpenAdditionalStartupFiles</c> (multi-file-startup's "extra window per argument"
/// path) both omitted it, so a window opened either way silently got a private in-memory store: File
/// &gt; Options in that window reported success (the in-memory store's <c>Save</c> always returns
/// true) but never touched %APPDATA%\FreeW\settings.json, and the change was gone on the next
/// launch. The fix passes this window's own <c>_optionsStore</c> (the real disk-backed store
/// <c>Program.cs</c> resolved for the process) to every sibling window it constructs, exactly as the
/// already-correct <c>OpenNewWindow</c> (Feature 5 "New Window") does.
///
/// <para>
/// This test drives the REAL <c>OpenNewWindowWithRecoveredSnapshot</c> production method (via
/// reflection, since it is private and only ever invoked as an <see cref="AutosaveCoordinator"/>
/// callback) end to end: it captures the actual recovered window WPF constructs and shows (via an
/// <see cref="EventManager"/> class handler on <see cref="FrameworkElement.LoadedEvent"/>, since the
/// method returns only a bool and exposes no reference to its new window), changes an option
/// THROUGH THAT WINDOW's own runtime/store fields exactly as its <c>OpenOptions</c> would, and then
/// re-loads the settings file from a FRESH <see cref="ApplicationOptionsStore{T}"/> instance to prove
/// the change actually reached disk -- not merely that a store's <c>StorePath</c> string looks right.
/// </para>
/// </summary>
public sealed class R173_RecoveredWindowOptionsStorePersistTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.R173OptionsStoreTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [StaFact]
    public void OpenNewWindowWithRecoveredSnapshot_OptionChangedInTheNewWindow_PersistsToTheCanonicalSettingsFile()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var canonicalStore = ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath);
        Assert.True(canonicalStore.Save(new FreeWOptions { RecentFilesCap = FreeWOptions.DefaultRecentFilesCap }));

        var primaryWindow = new MainWindow(
            new FreeWOptions(),
            canonicalStore,
            messageService: new RecordingUserMessageService());

        var recoveredWindow = CaptureNextLoadedWindow(primaryWindow, () =>
        {
            var snapshotPath = WriteDocx("Recovered.docx", "Recovered content");
            var candidate = new AutosaveRecoveryCandidate(
                snapshotPath,
                snapshotPath + ".sidecar.json",
                new AutosaveSidecar { OriginalFilePath = null });

            InvokePrivate(primaryWindow, "OpenNewWindowWithRecoveredSnapshot", candidate);
        });

        Assert.NotNull(recoveredWindow);
        Assert.NotSame(primaryWindow, recoveredWindow);

        // Confirm the wiring: the recovered window must resolve to the SAME real store instance the
        // primary window uses, not a fresh in-memory fallback.
        var recoveredStore = GetOptionsStore(recoveredWindow!);
        Assert.Same(canonicalStore, recoveredStore);

        // THE ACTUAL PROOF: change an option through the recovered window's own runtime/store fields
        // (exactly what its File > Options would do) and verify the canonical settings FILE changed --
        // read back through a brand-new store instance, so nothing but disk content can satisfy this.
        var edited = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 9,
            format: null,
            uiLanguage: null,
            autoCorrectEnabled: true,
            autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);

        InvokePrivate(recoveredWindow!, "ApplyOptionsEditAndNotify", edited);

        var reloaded = ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath).Load();
        Assert.Equal(9, reloaded.RecentFilesCap);
    }

    /// <summary>
    /// Sibling no-regression: Feature 5's "New Window" (<c>OpenNewWindow</c>) was already correct
    /// before this fix -- it already passed <c>_optionsStore</c> explicitly. Touching this file must
    /// not have disturbed that: a window opened via New Window must still resolve to the SAME store
    /// instance as its opener and must still persist an option change to the canonical file.
    /// </summary>
    [StaFact]
    public void OpenNewWindow_OptionChangedInTheNewWindow_StillPersistsToTheCanonicalSettingsFile()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var canonicalStore = ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath);
        Assert.True(canonicalStore.Save(new FreeWOptions { RecentFilesCap = FreeWOptions.DefaultRecentFilesCap }));

        var primaryWindow = new MainWindow(
            new FreeWOptions(),
            canonicalStore,
            messageService: new RecordingUserMessageService());

        var secondWindow = CaptureNextLoadedWindow(primaryWindow, () =>
            InvokePrivate(primaryWindow, "OpenNewWindow"));

        Assert.NotNull(secondWindow);
        Assert.Same(canonicalStore, GetOptionsStore(secondWindow!));

        var edited = OptionsDialogPlanner.BuildResult(
            recentFilesCap: 11,
            format: null,
            uiLanguage: null,
            autoCorrectEnabled: true,
            autoFormat: AutoFormatOptions.Default,
            autoCorrect: AutoCorrectOptions.Default);

        InvokePrivate(secondWindow!, "ApplyOptionsEditAndNotify", edited);

        var reloaded = ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath).Load();
        Assert.Equal(11, reloaded.RecentFilesCap);
    }

    /// <summary>
    /// Registers a one-shot class handler for <see cref="FrameworkElement.LoadedEvent"/> across every
    /// <see cref="MainWindow"/> instance in the process, runs <paramref name="action"/> (which is
    /// expected to construct and <c>Show()</c> exactly one new sibling window), and returns whichever
    /// window (other than <paramref name="excluding"/>) raised Loaded. This is the only way to observe
    /// the window <c>OpenNewWindowWithRecoveredSnapshot</c>/<c>OpenNewWindow</c> construct internally --
    /// both are void/bool-returning and expose no reference to it.
    /// </summary>
    private static MainWindow? CaptureNextLoadedWindow(MainWindow excluding, Action action)
    {
        MainWindow? captured = null;
        RoutedEventHandler handler = (sender, _) =>
        {
            if (captured is null && sender is MainWindow candidate && !ReferenceEquals(candidate, excluding))
                captured = candidate;
        };
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, handler);
        action();
        return captured;
    }

    private static IApplicationOptionsStore<FreeWOptions> GetOptionsStore(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_optionsStore", BindingFlags.Instance | BindingFlags.NonPublic);
        return (IApplicationOptionsStore<FreeWOptions>)field!.GetValue(window)!;
    }

    private static object? InvokePrivate(MainWindow window, string methodName, params object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(window, args);
    }

    private string WriteDocx(string name, string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        var path = Path.Combine(_tempDir, name);
        DocxWriter.Write(doc, path);
        return path;
    }
}
