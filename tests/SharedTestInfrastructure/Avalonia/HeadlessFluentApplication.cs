using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

/// <summary>
/// The one headless Avalonia <see cref="Application"/> every Avalonia test assembly derives from.
/// It installs the Fluent theme so styled controls get templates and can measure. A test assembly
/// declares a tiny public subclass (so <c>[assembly: AvaloniaTestApplication(typeof(...))]</c> and
/// <c>HeadlessUnitTestSession.GetOrStartForAssembly</c> keep a per-assembly type) whose
/// <c>BuildAvaloniaApp</c> either calls <see cref="ConfigureHeadless{TApplication}"/> (headless
/// drawing, no pixels) or chains <c>UseSkia()</c> itself when the test needs real rasterization --
/// this file cannot reference Avalonia.Skia because not every test project does.
/// </summary>
public abstract class HeadlessFluentApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    protected static AppBuilder ConfigureHeadless<TApplication>()
        where TApplication : Application, new() =>
        AppBuilder.Configure<TApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
