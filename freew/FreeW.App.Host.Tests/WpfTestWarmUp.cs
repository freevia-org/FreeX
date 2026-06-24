using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit.Sdk;

namespace FreeW.App.Host.Tests;

// ────────────────────────────────────────────────────────────────────────────
// Root-cause analysis
// ────────────────────────────────────────────────────────────────────────────
//
// Two cold-start failures occur when the full FreeW.App.Host.Tests suite is run
// with a fresh test-runner process:
//
// (A) SharedBackstageFrameTests.ArrowDown_OnRail_MovesFocusToNextNavButton
//     System.InvalidOperationException:
//       'System.Windows.ResourceReferenceExpression' is not a valid value for
//       property 'Foreground'.
//
//     Cause: BackstageFrame.ctor() self-merges SharedChromeResources.xaml into its
//     own Resources.  That XAML was defining its neutral-color Chrome*Brush entries
//     as DynamicResource aliases (e.g. <DynamicResource x:Key="ChromeWhiteBrush"
//     ResourceKey="ThemeNeutralWhiteBrush"/>).  Styles in the same XAML bound to
//     those keys with {StaticResource ChromeWhiteBrush}, which at XAML-parse time
//     yielded the DynamicResource indirection object (a ResourceReferenceExpression)
//     rather than a concrete Brush.  WPF resolves the indirection at layout time by
//     walking the element's resource chain up to Application.Current.Resources.  In
//     a test host where no Application exists (or exists on a different STA thread),
//     the chain terminates unresolved and GetEffectiveValue() throws when
//     TextBlock.MeasureOverride() evaluates the Foreground property.
//
//     Fix: SharedChromeResources.xaml (shared/Free.Shared.Ribbon.Wpf) now defines
//     the four neutral Chrome*Brush keys as concrete SolidColorBrush values whose
//     colors are byte-identical across FreeX/FreeW/FreeP.  {StaticResource
//     ChromeWhiteBrush} in style setters now resolves to a real Brush at XAML-parse
//     time, so no Application is needed and no runtime exception occurs.
//
// (B) FreeWRibbonParityTests.LayoutPageSetup_Columns... (and related)
//     System.Runtime.InteropServices.InvalidComObjectException:
//       COM object that has been separated from its underlying RCW cannot be used.
//     (stack: SpellCheckerFactory.RegisterUserDictionaryPrivate → WinRTSpellerInterop →
//       Speller.SetCustomDictionaries → RichTextBox.set_Document → DocumentView.Render)
//
//     Cause: Xunit.StaFact creates a new STA thread per test class.  The WinRT
//     ISpellCheckerFactory COM object is a per-STA-apartment singleton — once created it
//     is tied to the thread that created it.  When multiple test classes run concurrently
//     on different STA threads, the first thread to call RichTextBox.set_Document
//     initialises the COM factory on its apartment.  When that thread exits (after the
//     class's tests finish), the factory's STA thread is gone.  A second test class on
//     a different STA thread then calls set_Document → SetCustomDictionaries → tries to
//     call the factory COM object → RCW is disconnected → InvalidComObjectException.
//
//     Fix: pre-create a DocumentView and call LoadModel() on a long-lived STA keeper
//     thread BEFORE any test-class STA thread does so.  The keeper thread's Dispatcher
//     pumps for the entire process lifetime, keeping the WinRT COM apartment alive and
//     the RCW reference count positive.  Subsequent test-class calls to set_Document
//     re-use the already-initialised factory through COM's inter-apartment proxy and
//     succeed.
//
// ────────────────────────────────────────────────────────────────────────────
// Design
// ────────────────────────────────────────────────────────────────────────────
//
// WpfTestWarmUp.StartWarmUp() — [ModuleInitializer]
//   Fires once when the test assembly is loaded, before xUnit discovers tests.
//   Starts a background STA keeper thread FIRE-AND-FORGET (no blocking here —
//   blocking in a [ModuleInitializer] while the background thread JITs/loads
//   types from the same module can deadlock on the CLR's type-init guard).
//
// WpfWarmUpGateAttribute — [assembly: WpfWarmUpGate] in AppProductTestDefaults.cs
//   An assembly-level Xunit.Sdk.BeforeAfterTestAttribute.  xUnit calls
//   Before(MethodInfo) before EVERY test in the assembly.  The first call blocks
//   until _ready is set (max 60 s); all subsequent calls are instant no-ops.
//   Running on each test's own thread avoids CLR lock contention with the module
//   initializer.
//
// KeeperThreadProc / DoWarmUp
//   The STA keeper thread grabs its Dispatcher, posts DoWarmUp via BeginInvoke
//   (so Dispatcher.Run() is pumping before DocumentView is touched — required for
//   WinRT COM STA re-entrancy), and calls Dispatcher.Run().
//   DoWarmUp() creates a DocumentView, calls LoadModel() to trigger WinRT COM
//   initialisation, and holds the reference alive so the COM apartment never exits.
//   The keeper thread intentionally does NOT create Application.Current — doing so
//   would make other tests that create their own Application fail with cross-thread
//   Dispatcher-affinity exceptions.
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Assembly-level gate that ensures the WPF keeper-thread warm-up has completed before
/// any test body executes. Applied via <c>[assembly: WpfWarmUpGate]</c> in
/// <see cref="AppProductTestDefaults"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
internal sealed class WpfWarmUpGateAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) =>
        WpfTestWarmUp.EnsureReady();
}

/// <summary>
/// One-time WPF warm-up: pre-warms the WinRT spell-checker COM object on a long-lived STA keeper
/// thread so that subsequent test-class STA threads can access it through the COM inter-apartment
/// proxy without hitting a disconnected-RCW exception.
/// </summary>
internal static class WpfTestWarmUp
{
    // Keep the pre-warmed DocumentView alive for the process lifetime so the WinRT spell-checker
    // COM RCW reference count never hits zero and the COM apartment on the keeper thread never exits.
#pragma warning disable IDE0052
    private static DocumentView? _keepAliveDocumentView;
#pragma warning restore IDE0052

    private static readonly ManualResetEventSlim _ready = new(initialState: false);
    private static Exception? _warmUpException;

    /// <summary>
    /// Starts the STA keeper thread fire-and-forget.  Called by [ModuleInitializer] — must NOT
    /// block, because the CLR's type-init guard may be held on the calling thread while the
    /// keeper thread tries to JIT-compile methods in this assembly.
    /// </summary>
    [ModuleInitializer]
    public static void StartWarmUp()
    {
        var keeperThread = new Thread(KeeperThreadProc);
        keeperThread.SetApartmentState(ApartmentState.STA);
        keeperThread.IsBackground = true;           // does not prevent process exit
        keeperThread.Name = "WpfTestWarmUpKeeperThread";
        keeperThread.Start();
    }

    /// <summary>
    /// Blocks until the warm-up completes (called from each test's Before hook).
    /// After the first successful call this is a fast no-op.
    /// </summary>
    internal static void EnsureReady()
    {
        if (!_ready.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException(
                "WpfTestWarmUp: timed out (60 s) waiting for the WPF keeper thread to initialise. " +
                "The DocumentView construction or WinRT spell-checker warm-up may be deadlocked.");

        if (_warmUpException is not null)
            throw new InvalidOperationException(
                "WpfTestWarmUp: the WPF keeper thread threw during initialisation.", _warmUpException);
    }

    private static void KeeperThreadProc()
    {
        // Grab this thread's Dispatcher BEFORE calling Run() so we can post work to it.
        // DoWarmUp() is posted via BeginInvoke so that Dispatcher.Run() is already pumping
        // messages when DocumentView is first created — required for COM STA re-entrancy in
        // WinRT's ISpellCheckerFactory initialisation path.
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)DoWarmUp);
        Dispatcher.Run();   // blocks forever; keeper thread is a background thread
    }

    private static void DoWarmUp()
    {
        try
        {
            // Pre-initialise the WinRT ISpellCheckerFactory COM object.
            // DocumentView.LoadModel() → internal Render() → RichTextBox.set_Document
            // → SetCustomDictionaries → WinRTSpellerInterop.LoadDictionaryImpl
            // → SpellCheckerFactory.RegisterUserDictionaryPrivate  (first-time COM init).
            // Keeping _keepAliveDocumentView alive prevents the RCW reference count from
            // ever reaching zero, so the COM apartment on this keeper thread stays alive for
            // the duration of the test process.  Subsequent test-class STA threads that call
            // set_Document reach the same COM factory through COM's inter-apartment proxy.
            var dv = new DocumentView();
            dv.LoadModel(TextDocument.CreateEmpty());
            _keepAliveDocumentView = dv;
        }
        catch (Exception ex)
        {
            _warmUpException = ex;
        }
        finally
        {
            _ready.Set();
        }
    }
}
