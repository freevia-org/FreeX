using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class CrashAnalyticsArtifactConfigurationTests
{
    [Fact]
    public void Validator_AcceptsEmbeddedConfigurationWithoutPrintingEndpoint()
    {
        using var directory = new TestTemporaryDirectory();
        var endpoint = "https://public-key@example.invalid/42";
        var variable = "FREEX_TEST_SENTRY_DSN_" + Guid.NewGuid().ToString("N");
        var binary = Path.Combine(directory.Path, "FreeX.App.Host.dll");
        File.WriteAllText(binary, $"prefix\0{endpoint}\0tester-release\0suffix");
        Environment.SetEnvironmentVariable(variable, endpoint);

        try
        {
            var result = PowerShellScriptRunner.RunToolScript(
                "Test-CrashAnalyticsArtifactConfiguration.ps1",
                directory.Path,
                $"-ArtifactPath \"{binary}\" -DsnEnvironmentVariable \"{variable}\" -ExpectedEnvironment \"tester-release\"");

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.Output.Should().Contain("\"endpointConfigured\"").And.Contain("true");
            result.CombinedOutput.Should().NotContain(endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Validator_RejectsArtifactWithoutEmbeddedConfiguration()
    {
        using var directory = new TestTemporaryDirectory();
        var endpoint = "https://public-key@example.invalid/43";
        var variable = "FREEX_TEST_SENTRY_DSN_" + Guid.NewGuid().ToString("N");
        var binary = Path.Combine(directory.Path, "FreeX.App.Host.dll");
        File.WriteAllText(binary, "ordinary unsigned artifact");
        Environment.SetEnvironmentVariable(variable, endpoint);

        try
        {
            var result = PowerShellScriptRunner.RunToolScript(
                "Test-CrashAnalyticsArtifactConfiguration.ps1",
                directory.Path,
                $"-ArtifactPath \"{binary}\" -DsnEnvironmentVariable \"{variable}\" -ExpectedEnvironment \"tester-release\"");

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("does not contain the configured crash endpoint and environment");
            result.CombinedOutput.Should().NotContain(endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
