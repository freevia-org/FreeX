using System.Text.Json;

using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FreeXTextBoxInlinePhysicalValidationToolTests
{
    [Fact]
    public void RunnerStartsFreeXWithScopedEvidenceAndStopsOnlyItsOwnContainer()
    {
        var runner = ReadRepoFile("tools", "Run-FreeXTextBoxInlineEditPhysicalLinuxValidation.ps1");
        var dockerfile = ReadRepoFile("tools", "LinuxInteractiveDocker", "Dockerfile");
        var validator = ReadRepoFile(
            "tools", "LinuxInteractiveDocker", "validate-freex-textbox-inline-edit-physical.py");

        runner.Should().Contain("Run-LinuxInteractiveDocker.ps1");
        runner.Should().Contain("FREEX_TEXTBOX_INLINE_PHYSICAL_RESULT=/work/freex-textbox-inline-physical.json");
        runner.Should().Contain("docker cp $probePath");
        runner.Should().Contain("docker cp $schemaPath \"${container}:/work/freex-textbox-inline-edit-physical.schema.json\"");
        runner.Should().Contain("docker cp $validatorPath \"${container}:/work/validate-freex-textbox-inline-edit-physical.py\"");
        runner.Should().Contain("docker exec --env DISPLAY=:99");
        runner.Should().Contain(
            "docker exec $container python3 /work/validate-freex-textbox-inline-edit-physical.py " +
            "/work/freex-textbox-inline-edit-physical.schema.json " +
            "/work/freex-textbox-inline-edit-physical/results.json");
        runner.Should().Contain("/work/freex-textbox-inline-edit-physical.schema.json");
        runner.Should().Contain("/work/freex-textbox-inline-edit-physical/results.json");
        runner.Should().NotContain("python3 -c");
        runner.Should().NotContain("$schemaValidationCode");
        runner.Should().Contain("$runnerPath -Action Stop -App FreeX -Port $Port");
        runner.Should().NotContain("build-server shutdown");
        dockerfile.Should().Contain("python3-jsonschema");
        validator.Should().Contain("from jsonschema import validate");
        validator.Should().Contain("schema = load_json(Path(arguments[1]))");
        validator.Should().Contain("manifest = load_json(Path(arguments[2]))");
        validator.Should().Contain("validate(instance=manifest, schema=schema)");
        validator.Should().Contain("raise SystemExit(main(sys.argv))");

        runner.IndexOf(
                "docker exec $container python3 /work/validate-freex-textbox-inline-edit-physical.py",
                StringComparison.Ordinal)
            .Should().BeLessThan(runner.IndexOf(
                "docker cp \"${container}:/work/freex-textbox-inline-edit-physical/results.json\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ProbeUsesOnlyPhysicalInputAndExactTextBoxPostconditions()
    {
        var probe = ReadRepoFile(
            "tools", "LinuxInteractiveDocker", "run-freex-textbox-inline-edit-physical.sh");

        probe.Should().Contain("xdotool click --clearmodifiers --repeat 2 --delay 180 1");
        probe.Should().Contain("send_key ctrl+Return");
        probe.Should().Contain("send_key Tab");
        probe.Should().Contain("send_key Escape");
        probe.Should().Contain("TextBoxInlineEditor");
        probe.Should().Contain("read_textbox_text");
        probe.Should().Contain("'provenance': 'xlsx-package-readback-before-interaction'");
        probe.Should().Contain("e.get(\"modelText\") == \"Wave93 committed\\nsecond line\"");
        probe.Should().Contain("if wait_for_runtime reopen; then reopen_observed=true; fi");
        probe.Should().Contain("if wait_for_runtime cancel-input; then cancel_input_observed=true; fi");
        probe.Should().Contain("identify -format '%w %h'");
        probe.Should().Contain("results.json");
        probe.Should().NotContain("send_key ctrl+s");
        probe.Should().NotContain("wait_for_package_text");
        probe.Should().NotContain("package-committed.txt");
        probe.Should().NotContain("package-canceled.txt");
        probe.Should().NotContain("BeginTextBoxInlineEditForTest");
        probe.Should().NotContain("RaiseTextBoxInlineEditorKeyDownForTest");
    }

    [Fact]
    public void FixtureGeneratorMarksTheLoadedDrawingObjectAsATextBoxAndSeedsStableText()
    {
        var generator = ReadRepoFile(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave93TextBoxFixture.ps1");

        generator.Should().Contain("txBox");
        generator.Should().Contain("Wave93 Physical TextBox");
        generator.Should().Contain("Wave93 initial text");
        generator.Should().Contain("xl/drawings/drawing1.xml");
    }

    [Fact]
    public void PhysicalManifestSchemaHasStrictEvidenceCountsAndAutomationContract()
    {
        using var document = JsonDocument.Parse(ReadRepoFile(
            "tools", "LinuxInteractiveDocker", "freex-textbox-inline-edit-physical.schema.json"));
        var root = document.RootElement;
        var properties = root.GetProperty("properties");

        properties.GetProperty("schemaVersion").GetProperty("const").GetInt32().Should().Be(1);
        properties.GetProperty("suite").GetProperty("const").GetString()
            .Should().Be("freex-linux-textbox-inline-edit-physical");
        properties.GetProperty("screenshots").GetProperty("maxItems").GetInt32().Should().Be(5);
        properties.GetProperty("fixture").GetProperty("properties")
            .GetProperty("packageText").GetProperty("const").GetString()
            .Should().Be("Wave93 initial text");
        properties.TryGetProperty("package", out _).Should().BeFalse();
        properties.GetProperty("results").GetProperty("maxItems").GetInt32().Should().Be(6);
        properties.GetProperty("summary").GetProperty("properties")
            .GetProperty("passed").GetProperty("const").GetInt32().Should().Be(6);
        var runtime = properties.GetProperty("runtime");
        var runtimeRequired = runtime.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        runtimeRequired.Should().Contain("platform");
        runtimeRequired.Should().Contain("shell");
        runtimeRequired.Should().Contain("app");
        runtime.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

        var runtimeContractProperties = runtime.GetProperty("properties");
        runtimeContractProperties.GetProperty("platform").GetProperty("const").GetString()
            .Should().Be("linux");
        runtimeContractProperties.GetProperty("shell").GetProperty("const").GetString()
            .Should().Be("avalonia");
        runtimeContractProperties.GetProperty("app").GetProperty("const").GetString()
            .Should().Be("FreeX");
        runtimeContractProperties.GetProperty("events").GetProperty("items").GetProperty("properties")
            .GetProperty("editorAutomationId").GetProperty("const").GetString()
            .Should().Be("TextBoxInlineEditor");

        var runtimeProperties = runtimeContractProperties
            .GetProperty("events").GetProperty("items").GetProperty("properties");
        runtimeProperties.GetProperty("editorVisible").GetProperty("type").GetString().Should().Be("boolean");
        runtimeProperties.GetProperty("nonZeroBounds").GetProperty("const").GetBoolean().Should().BeTrue();
        runtimeProperties.GetProperty("editorWidth").GetProperty("exclusiveMinimum").GetDouble().Should().Be(0);
        runtimeProperties.GetProperty("editorHeight").GetProperty("exclusiveMinimum").GetDouble().Should().Be(0);

        // The real commit/cancel observations hide the editor after routing the command, but retain its
        // last nonzero layout bounds. The schema intentionally has no visibility-dependent condition that
        // would reject that truthful runtime observation.
        runtimeProperties.TryGetProperty("if", out _).Should().BeFalse();
    }

    [Fact]
    public void AvaloniaPhysicalObserverIsOptInAndDoesNotOwnEditorInput()
    {
        var observer = ReadRepoFile(
            "src", "FreeX.App.Avalonia", "MainWindow.TextBoxInlinePhysicalEvidence.cs");
        var editor = ReadRepoFile(
            "src", "FreeX.App.Avalonia", "MainWindow.TextBoxInlineEditing.cs");

        observer.Should().Contain("FREEX_TEXTBOX_INLINE_PHYSICAL_RESULT");
        observer.Should().Contain("EditorAutomationId");
        observer.Should().Contain("EditorWidth");
        observer.Should().Contain("ModelText");
        observer.Should().Contain("_textBoxInlinePhysicalLayoutObservationPending = false;");
        observer.Should().Contain("editor.Bounds.Width <= 0 || editor.Bounds.Height <= 0");
        observer.Should().Contain("File.Move(temporaryPath, path, overwrite: true)");
        editor.Should().Contain("RequestTextBoxInlinePhysicalLayoutObservation();");
        editor.Should().Contain("_textBoxInlineEditor.LayoutUpdated += TextBoxInlineEditor_LayoutUpdated;");
        editor.Should().Contain("RecordTextBoxInlinePhysicalEvidence(\"committed\"");
        editor.Should().Contain("RecordTextBoxInlinePhysicalEvidence(\"canceled\"");
        editor.Should().NotContain("RecordTextBoxInlinePhysicalEvidence(\"editing\", activeTextBoxId)");
        editor.Should().NotContain("Environment.GetEnvironmentVariable(\"FREEX_TEXTBOX_INLINE_PHYSICAL_RESULT\")");
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
