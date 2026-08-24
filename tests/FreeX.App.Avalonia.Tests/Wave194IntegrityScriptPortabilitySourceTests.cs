using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave194IntegrityScriptPortabilitySourceTests
{
    [Fact]
    public void IntegrityVerifier_UsesPowerShell5CompatibleSha256Apis()
    {
        var script = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "docs", "parity", "evidence", "wave194-freex-autofilter-mixed-type-20260823", "Test-Integrity.ps1");

        script.Should().Contain("[Security.Cryptography.SHA256]::Create()");
        script.Should().Contain("$hasher.ComputeHash($Bytes)");
        script.Should().Contain("$hasher.Dispose()");
        script.Should().Contain("[BitConverter]::ToString");
        script.Should().Contain(".ToLowerInvariant()");
        script.Should().Contain("$start.Arguments =");
        script.Should().NotContain("[Security.Cryptography.SHA256]::HashData");
        script.Should().NotContain("[Convert]::ToHexString");
        script.Should().NotContain("$start.ArgumentList");
    }
}
