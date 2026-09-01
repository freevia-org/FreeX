using System.Globalization;
using System.Threading;
using FluentAssertions;

namespace Free.Shared.Shell.Avalonia.Tests;

/// <summary>
/// r189 (backlog item 5): the Avalonia shells never applied the user's chosen application language.
/// The Options dialog on that platform offers the field, validates it, persists it and shows a
/// restart notice, so the app was telling the user a restart would change a setting nothing read.
/// Only the WPF-specific FrameworkElement.Language metadata step is toolkit-bound; setting the UI
/// culture is plain BCL, so the Avalonia bootstrap can do exactly what the WPF one does.
/// </summary>
public sealed class R189_AvaloniaApplyAppLanguageTests : IDisposable
{
    private readonly CultureInfo _previousThreadCulture = Thread.CurrentThread.CurrentUICulture;
    private readonly CultureInfo? _previousDefaultCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        Thread.CurrentThread.CurrentUICulture = _previousThreadCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _previousDefaultCulture;
    }

    [Fact]
    public void ApplyAppLanguage_SetsBothTheThreadAndTheProcessDefaultUiCulture()
    {
        // Both matter: the thread culture serves the startup thread, and the process default is what
        // every thread created afterwards inherits. Setting only one leaves half the app in the old
        // language -- which is why the WPF bootstrap sets both.
        var german = new CultureInfo("de-DE");

        AvaloniaAppLocalizationBootstrap.ApplyAppLanguage(
            "de-DE",
            _ => german,
            CultureInfo.InvariantCulture);

        Thread.CurrentThread.CurrentUICulture.Name.Should().Be("de-DE");
        CultureInfo.DefaultThreadCurrentUICulture!.Name.Should().Be("de-DE");
    }

    [Fact]
    public void ApplyAppLanguage_WithAnUnrecognisedName_FallsBackInsteadOfThrowing()
    {
        // A settings file naming a culture this build no longer ships must not stop the app
        // starting; the resolver reports the fallback and startup continues.
        var fallback = new CultureInfo("en-US");

        var act = () => AvaloniaAppLocalizationBootstrap.ApplyAppLanguage(
            "zz-ZZ-not-a-culture",
            _ => fallback,
            fallback);

        act.Should().NotThrow();
        Thread.CurrentThread.CurrentUICulture.Name.Should().Be("en-US");
    }

    [Fact]
    public void ApplyAppLanguage_WhenTheResolverReturnsNull_UsesTheFallback()
    {
        // Defence against a resolver that breaks its own contract: the WPF sibling throws here, but
        // throwing during startup would mean the app does not launch at all. Falling back is the
        // behaviour a user can recover from.
        var fallback = new CultureInfo("en-GB");

        AvaloniaAppLocalizationBootstrap.ApplyAppLanguage("anything", _ => null!, fallback);

        Thread.CurrentThread.CurrentUICulture.Name.Should().Be("en-GB");
    }

    [Fact]
    public void ApplyAppLanguage_RejectsANullResolverOrFallback()
    {
        var act = () => AvaloniaAppLocalizationBootstrap.ApplyAppLanguage("en-US", null!, CultureInfo.InvariantCulture);
        act.Should().Throw<ArgumentNullException>();

        var act2 = () => AvaloniaAppLocalizationBootstrap.ApplyAppLanguage("en-US", _ => CultureInfo.InvariantCulture, null!);
        act2.Should().Throw<ArgumentNullException>();
    }
}
