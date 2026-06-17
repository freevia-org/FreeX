using System.Runtime.CompilerServices;
using Free.Shared.Shell;
using FreeX.App.Host;

namespace FreeX.App.Host.Logic.Tests;

// The neutral backstage planners (greeting, recent-file list) now live in the shared shell
// and resolve strings via BackstageStrings.Current. Install FreeX's UiText-backed catalog
// once on assembly load so planner output matches UiText, exactly as App.xaml.cs does at
// runtime. Runs before any test in this assembly.
internal static class BackstageStringsTestDefaults
{
    [ModuleInitializer]
    public static void Initialize() =>
        BackstageStrings.Current = new FreeXBackstageStrings();
}
