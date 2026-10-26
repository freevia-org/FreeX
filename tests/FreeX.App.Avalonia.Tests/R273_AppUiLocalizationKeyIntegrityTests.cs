using FreeX.App.Localization;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r273: <c>src/FreeX.App.UI</c> was the one FreeX project with localization key call sites that no
/// integrity test scanned.
///
/// <para>Both existing tests are scoped to a single shell --
/// <c>FreeX.App.Host.Tests.LocalizationUsageTests</c> walks <c>FreeX.App.Host</c>,
/// <c>LocalizationKeyIntegrityTests</c> walks <c>FreeX.App.Avalonia</c> -- so a third UI project fell
/// between them. Its twenty keys all resolve today; nothing was checking that they still would.</para>
///
/// <para>A missing key is not an exception. <c>LocalizedTextCatalog.Get</c> returns
/// <c>CreateMissingText(key)</c>, so the user reads <c>[[Some_Key]]</c> where a label should be and
/// no test fails -- the same silent-failure shape as r270's discarded tasks, in the one place the
/// user is guaranteed to look.</para>
///
/// <para>This is the second perimeter gap this program has found in existing fences, after r272's
/// two shared shells. Both were found the same way: not by reviewing code, but by asking what the
/// checks actually cover and comparing it against the repository.</para>
/// </summary>
public sealed class R273_AppUiLocalizationKeyIntegrityTests
{
    [Fact]
    public void AppUiSourceLocalizationKeys_AllExistInNeutralResources() =>
        LocalizationKeyIntegrityTestSupport.AssertAllLiteralUiTextKeysExist(
            "FreeX.slnx",
            UiText.GetNeutralResourceKeys(),
            requireLiteralUses: true,
            "src",
            "FreeX.App.UI");
}
