namespace Free.Shared.AppServices.Tests;

public sealed class ValidationInfrastructureOwnershipSourceTests
{
    [Fact]
    public void Visual_evidence_compatibility_helpers_delegate_to_generic_owners()
    {
        var source = Read(
            "shared", "Free.Shared.AppServices", "VisualEvidence", "VisualEvidenceProtocol.cs");

        source.Should().Contain("CommandLineValueOptionParser.Parse(");
        source.Should().Contain("CommandLineValueOptionParser.ReadFirst(");
        source.Should().Contain("JsonArtifactIO.CreateSerializerOptions(");
        source.Should().Contain("JsonArtifactIO.Read<T>(");
        source.Should().Contain("JsonArtifactIO.Write(path, value, options);");
        source.Should().NotContain("private static (VisualEvidenceArgumentSpec Spec");
    }

    [Theory]
    [InlineData("tools", "FreeX.Validation.Avalonia", "PivotRuntimeEvidence.cs")]
    [InlineData("freew", "TestSupport", "Validation.Avalonia", "TablePropertiesX11Validation.cs")]
    [InlineData("freep", "TestSupport", "Validation.Avalonia", "AccessibilityValidation.cs")]
    [InlineData("freep", "TestSupport", "Validation.Avalonia", "PhysicalValidation.cs")]
    public void Matching_validation_options_use_the_shared_value_parser(params string[] path)
    {
        var source = Read(path);

        source.Should().Contain("CommandLineValueOptionParser.Parse(");
        source.Should().NotContain("for (var index = 0; index < args.Count;");
        source.Should().NotContain("for (var index = 0; index < arguments.Count;");
    }

    [Fact]
    public void Validation_json_artifacts_use_shared_persistence()
    {
        var pivot = Read("tools", "FreeX.Validation.Avalonia", "PivotRuntimeEvidence.cs");
        var table = Read("freew", "TestSupport", "Validation.Avalonia", "TablePropertiesX11Validation.cs");
        var accessibility = Read("freep", "TestSupport", "Validation.Avalonia", "AccessibilityValidation.cs");
        var physical = Read("freep", "TestSupport", "Validation.Avalonia", "PhysicalValidation.cs");
        var startup = Read("freep", "TestSupport", "Validation.Avalonia", "StartupDirtyTraceValidation.cs");

        pivot.Should().Contain("JsonArtifactIO.AppendLine(path, payload);")
            .And.NotContain("JsonSerializer.Serialize(payload)")
            .And.NotContain("File.AppendAllText(path");
        table.Should().Contain("JsonArtifactIO.Write(")
            .And.NotContain("JsonSerializer.Serialize(result");
        accessibility.Should().Contain("JsonArtifactIO.WriteAtomicAsync(")
            .And.NotContain("temporaryManifestPath")
            .And.NotContain("JsonSerializer.Serialize(manifest");
        physical.Should().Contain("JsonArtifactIO.Write(")
            .And.NotContain("JsonSerializer.Serialize(manifest");
        startup.Should().Contain("JsonArtifactIO.Write(")
            .And.NotContain("JsonSerializer.Serialize(report");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
