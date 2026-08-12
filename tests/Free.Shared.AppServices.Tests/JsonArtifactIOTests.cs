using System.Text.Json;

namespace Free.Shared.AppServices.Tests;

public sealed class JsonArtifactIOTests
{
    [Fact]
    public async Task Write_read_and_atomic_write_create_parent_directories_without_a_utf8_bom()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("json-artifact-");
        var path = Path.Combine(temporaryDirectory.Path, "nested", "artifact.json");
        var options = JsonArtifactIO.CreateSerializerOptions(
            propertyNameCaseInsensitive: true,
            stringEnums: true,
            ignoreNullValues: true);

        JsonArtifactIO.Write(path, new Artifact("first", ArtifactState.Ready, null), options);

        File.ReadAllBytes(path).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        JsonArtifactIO.Read<Artifact>(path, options)
            .Should().Be(new Artifact("first", ArtifactState.Ready, null));

        await JsonArtifactIO.WriteAtomicAsync(
            path,
            new Artifact("second", ArtifactState.Complete, "done"),
            options);

        JsonArtifactIO.Read<Artifact>(path, options)
            .Should().Be(new Artifact("second", ArtifactState.Complete, "done"));
        Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp")
            .Should().BeEmpty();
    }

    [Fact]
    public void AppendLine_writes_independent_json_values_and_read_if_exists_is_optional()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("json-artifact-");
        var path = Path.Combine(temporaryDirectory.Path, "nested", "events.jsonl");

        JsonArtifactIO.AppendLine(path, new { stage = "opened" });
        JsonArtifactIO.AppendLine(path, new { stage = "updated" });

        File.ReadAllLines(path)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("stage").GetString())
            .Should().Equal("opened", "updated");
        JsonArtifactIO.ReadIfExists<Artifact>(Path.Combine(temporaryDirectory.Path, "missing.json"))
            .Should().BeNull();
    }

    private sealed record Artifact(string Name, ArtifactState State, string? Detail);

    private enum ArtifactState
    {
        Ready,
        Complete,
    }
}
