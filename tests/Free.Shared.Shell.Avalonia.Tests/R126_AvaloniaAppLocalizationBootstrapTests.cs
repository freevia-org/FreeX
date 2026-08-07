namespace Free.Shared.Shell.Avalonia.Tests;

/// <summary>
/// R126: <see cref="AvaloniaAppLocalizationBootstrap"/> is the shared seam every Avalonia sister
/// app (FreeX, FreeW, FreeP) is now expected to call once at startup so
/// <see cref="ShellStrings.Current"/>/<see cref="BackstageStrings.Current"/> — read directly by
/// <c>AvaloniaDialogButtonRowFactory.CreateOkCancel</c> and <c>AvaloniaUserMessageDialog</c> — route
/// through the app's own localized resource catalog instead of staying pinned at the neutral-English
/// <see cref="DefaultShellStrings"/>/<see cref="DefaultBackstageStrings"/> fallback forever.
/// </summary>
public sealed class R126_AvaloniaAppLocalizationBootstrapTests : IDisposable
{
    private readonly IShellStrings _originalShellStrings = ShellStrings.Current;
    private readonly IBackstageStrings _originalBackstageStrings = BackstageStrings.Current;

    public void Dispose()
    {
        ShellStrings.Current = _originalShellStrings;
        BackstageStrings.Current = _originalBackstageStrings;
    }

    [Fact]
    public void InstallSharedSeams_RoutesShellStringsThroughProvidedDelegates()
    {
        var resources = new Dictionary<string, string>
        {
            ["Common_Ok"] = "Fake-Ok",
            ["Common_Cancel"] = "Fake-Cancel",
            ["Common_ErrorTitle"] = "Fake-Error",
            ["Common_WarningTitle"] = "Fake-Warning",
            ["Common_InformationTitle"] = "Fake-Information",
            ["Common_ConfirmTitle"] = "Fake-Confirm",
        };

        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
            key => resources[key],
            (key, args) => resources[key]);

        ShellStrings.Current.Ok.Should().Be("Fake-Ok");
        ShellStrings.Current.Cancel.Should().Be("Fake-Cancel");
        ShellStrings.Current.ErrorTitle.Should().Be("Fake-Error");
        ShellStrings.Current.WarningTitle.Should().Be("Fake-Warning");
        ShellStrings.Current.InformationTitle.Should().Be("Fake-Information");
        ShellStrings.Current.ConfirmTitle.Should().Be("Fake-Confirm");
    }

    [Fact]
    public void InstallSharedSeams_RoutesBackstageStringsThroughProvidedDelegates()
    {
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
            key => $"get:{key}",
            (key, args) => $"format:{key}:{args.Length}");

        BackstageStrings.Current.Get("Backstage_GreetingMorning").Should().Be("get:Backstage_GreetingMorning");
        BackstageStrings.Current.Format("Backstage_Recent_OpenRecentFileAutomationName", "Roadmap.docx")
            .Should()
            .Be("format:Backstage_Recent_OpenRecentFileAutomationName:1");
    }

    [Fact]
    public void InstallSharedSeams_UsesProvidedAutomationNameDelegate_WhenGiven()
    {
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(
            key => key,
            (key, args) => key,
            createAutomationName: text => $"automation:{text}");

        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("automation:_Open _File");
    }

    [Fact]
    public void InstallSharedSeams_FallsBackToDefaultAutomationName_WhenNoneGiven()
    {
        AvaloniaAppLocalizationBootstrap.InstallSharedSeams(key => key, (key, args) => key);

        // ResourceShellStrings' default automation-name resolver strips WPF/Avalonia access-key
        // markers — mirrors ShellStringText.CreateAutomationName, the same behavior the WPF host's
        // WpfAppLocalizationBootstrap.InstallSharedSeams() gets when it omits the delegate too.
        ShellStrings.Current.CreateAutomationName("_Open _File").Should().Be("Open File");
    }

    [Fact]
    public void InstallSharedSeams_ThrowsOnNullDelegates()
    {
        var act1 = () => AvaloniaAppLocalizationBootstrap.InstallSharedSeams(null!, (key, args) => key);
        var act2 = () => AvaloniaAppLocalizationBootstrap.InstallSharedSeams(key => key, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }
}
