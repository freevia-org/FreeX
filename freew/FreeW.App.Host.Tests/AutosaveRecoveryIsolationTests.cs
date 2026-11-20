using Free.Shared.AppServices;
using FreeW.App.Host;
using Xunit;
using TextDocument = FreeW.Core.Model.TextDocument;

namespace FreeW.App.Host.Tests;

/// <summary>
/// A window a test opens must never see the per-user recovery folder: leftover autosave files of a killed
/// test host (or of the developer's own FreeW) would otherwise raise a modal recovery dialog on load and
/// hang the test.
/// </summary>
public sealed class AutosaveRecoveryIsolationTests
{
    [Fact]
    public void TestHost_GivesWindowsASessionWithAPrivateRecoveryFolder()
    {
        AutosaveCoordinator.DefaultSessionFactory.Should().NotBeNull(
            "the test host installs a private recovery folder before any window is created");

        var ports = new AutosaveDocumentPorts<TextDocument>(
            GetOriginalFilePath: () => null,
            GetDisplayName: () => "isolation",
            GetIsDirty: () => false,
            GetDirtyGeneration: () => 0,
            ExecuteWithDocument: write => write(TextDocument.CreateEmpty()));
        using var session = AutosaveCoordinator.DefaultSessionFactory!(ports);

        session.Should().NotBeNull();
    }
}
